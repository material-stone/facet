// Records every shot from the REAL skin page (registry/skins/catroom/world.html) with a virtual clock:
// Playwright's fake timers drive Date / performance.now / requestAnimationFrame, CSS animations are pinned to
// the same clock, and each frame is stepped by exactly 1/30 s and screenshotted. Frames -> public/clips/<shot>.mp4.
// The page's own sound calls (ding, purr, thud...) are logged with their frame -> public/data/sfx-events.json.
// Usage: node capture/capture.mjs [shotId ...]
import { chromium } from "playwright-core";
import { execFileSync } from "node:child_process";
import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import { fileURLToPath, pathToFileURL } from "node:url";
import path from "node:path";
import { SHOTS } from "./shots.mjs";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const config = JSON.parse(await readFile(path.join(root, "config.json"), "utf8"));
const timeline = JSON.parse(await readFile(path.join(root, "public", "data", "timeline.json"), "utf8"));
const FPS = timeline.fps;
const W = 1920, H = 1080;
const pageUrl = pathToFileURL(path.resolve(root, config.skinPage)).href;
const fontUrls = config.skinFonts.map((f) => pathToFileURL(path.resolve(root, f)).href);
const remotionCli = path.join(root, "node_modules", "@remotion", "cli", "remotion-cli.js");
const only = process.argv.slice(2);
const SOUNDS = ["ding", "thud", "thump", "purr", "chirp", "mew", "crunch", "lap", "scratch", "glass", "pop", "chime", "knock", "flick", "creak"];

const eventsPath = path.join(root, "public", "data", "sfx-events.json");
const allEvents = existsSync(eventsPath) ? JSON.parse(await readFile(eventsPath, "utf8")) : {};

// Seeded Math.random so a re-capture gives the same motion.
const initScript = `(() => { let a = 20261117; Math.random = () => { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; })();`;

// Page-side helpers, installed after the page has loaded.
const helpers = ([fonts, sounds]) => {
  const R = window.__catRoom;
  const st = document.createElement("style");
  st.textContent = `@font-face{font-family:"Jua";src:url("${fonts[0]}")}@font-face{font-family:"Gowun Dodum";src:url("${fonts[1]}")}`;
  document.head.appendChild(st);
  const log = [];
  const S = window.__catSound;
  for (const k of sounds) {
    const f = S[k];
    S[k] = function (...a) {
      log.push({ t: performance.now(), k, a: typeof a[0] === "number" || typeof a[0] === "boolean" ? a[0] : null });
      return f.apply(S, a);
    };
  }
  const center = (id, sel) => {
    const wg = [...document.querySelectorAll("#wl .wg")].find((w) => w.__id === id);
    const el = wg && wg.querySelector(sel);
    if (!el) return null;
    const r = el.getBoundingClientRect(), b = wg.getBoundingClientRect();
    return { x: r.left + r.width / 2 - b.left, y: r.top + r.height / 2 - b.top };
  };
  const scale = () => Math.min(1.5, window.devicePixelRatio || 1);
  window.__v = {
    R, log,
    place(x, y, acts) { const c = R.cat; c.x = x; c.y = y; c.q = acts.map((a) => ({ ...a })); c.act = null; c.lift = 0; c.z = 0; c.inBox = false; },
    queue(acts) { const c = R.cat; c.q = acts.map((a) => ({ ...a })); c.act = null; },
    widgets(list) {
      window.receive({ widgets: list });
      const els = [...document.querySelectorAll("#wl .wg")];
      els.forEach((el, i) => { if (!el.__id) el.__id = list[i] && list[i].id; });
    },
    pointer(id, sel, kind) { const p = center(id, sel); if (p) window.receive({ pointer: { id, kind, x: p.x, y: p.y, delta: 0, button: 0 } }); return p; },
    clickAt(nx, ny) { R.clickAt({ x: nx * innerWidth * scale(), y: ny * innerHeight * scale() }); },
    clickPom() {
      const s = scale(), Wp = innerWidth * s, Hp = innerHeight * s;
      for (let y = 0.6; y < 0.8; y += 0.004) for (let x = 0.55; x < 0.65; x += 0.002)
        if (R.hitAt({ x: x * Wp, y: y * Hp }) === "pom") { R.clickAt({ x: x * Wp, y: y * Hp }); return true; }
      return false;
    },
    syncCss() {
      const now = performance.now();
      for (const a of document.getAnimations()) { if (a.__b === undefined) { a.__b = now; a.pause(); } a.currentTime = now - a.__b; }
    },
  };
};

const browser = await chromium.launch({ channel: "chrome" });
await mkdir(path.join(root, "public", "clips"), { recursive: true });

for (const shot of timeline.shots) {
  if (only.length && !only.includes(shot.id)) continue;
  const spec = SHOTS[shot.id];
  if (!spec) throw new Error(`no shot spec: ${shot.id}`);
  const ctx = await browser.newContext({ viewport: { width: W, height: H }, deviceScaleFactor: 1, bypassCSP: true });
  await ctx.addInitScript(initScript);
  const page = await ctx.newPage();
  page.on("pageerror", (e) => console.error(`[${shot.id}] page error:`, e.message));
  const t0 = new Date(spec.time ?? "2026-11-17T14:05:00").getTime();
  await page.clock.install({ time: t0 - 60000 });
  await page.clock.pauseAt(t0);
  await page.goto(pageUrl);
  await page.evaluate(helpers, [fontUrls, SOUNDS]);
  await page.evaluate(async () => { await document.fonts.load("20px Jua"); await document.fonts.load("20px \"Gowun Dodum\""); });
  await page.evaluate(() => { receive({ scenes: ["room"] }); receive({ options: { sound: true } }); receive({ weather: "clear", visible: true }); });
  await page.clock.runFor(500);

  // v: Node-side handle; every call is chained so setup/events run in order.
  let chain = Promise.resolve();
  const ev = (fn, ...args) => { chain = chain.then(() => page.evaluate(fn, args)).then(() => {}); };
  const v = {
    place: (x, y, acts) => ev(([x, y, acts]) => __v.place(x, y, acts), x, y, acts),
    queue: (acts) => ev(([acts]) => __v.queue(acts), acts),
    js: (code) => ev(([code]) => Function("R", code)(__v.R), code),
    widgets: (list) => ev(([list]) => __v.widgets(list), list),
    hover: (id, sel) => ev(([id, sel]) => __v.pointer(id, sel, "move"), id, sel),
    press: (id, sel, kind) => ev(([id, sel, kind]) => __v.pointer(id, sel, kind), id, sel, kind),
    click: (x, y) => ev(([x, y]) => __v.clickAt(x, y), x, y),
    clickCat: () => ev(() => { const c = __v.R.cat; __v.clickAt(c.x, c.y - 0.06); }),
    clickPom: () => ev(() => __v.clickPom()),
  };
  spec.setup?.(v);
  await chain;
  await page.clock.runFor(Math.round((spec.warm ?? 0.5) * 1000));

  const startMs = await page.evaluate(() => { __v.log.length = 0; return performance.now(); });
  const frameDir = path.join(root, "capture", "frames", shot.id);
  await rm(frameDir, { recursive: true, force: true });
  await mkdir(frameDir, { recursive: true });
  const events = (spec.events ?? []).map((e) => ({ ...e, frame: Math.round(((e.line ? shot.lines[e.line] / FPS : 0) + e.at) * FPS) }));
  let elapsed = 0;
  for (let f = 0; f < shot.frames; f++) {
    for (const e of events.filter((e) => e.frame === f)) { e.run(v); await chain; }
    await page.evaluate(() => __v.syncCss());
    await page.screenshot({ path: path.join(frameDir, `${String(f).padStart(5, "0")}.jpg`), type: "jpeg", quality: 93 });
    const next = Math.round(((f + 1) * 1000) / FPS);
    await page.clock.runFor(next - elapsed);
    elapsed = next;
  }
  const log = await page.evaluate(() => __v.log);
  allEvents[shot.id] = log
    .map((l) => ({ frame: Math.max(0, Math.round(((l.t - startMs) / 1000) * FPS)), k: l.k, a: l.a }))
    .filter((l) => l.frame < shot.frames);
  await ctx.close();

  execFileSync(process.execPath, [remotionCli, "ffmpeg", "-y", "-loglevel", "error", "-framerate", String(FPS), "-i", path.join(frameDir, "%05d.jpg"),
    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "15", "-preset", "medium", path.join(root, "public", "clips", `${shot.id}.mp4`)], { stdio: "inherit" });
  await rm(frameDir, { recursive: true, force: true });
  console.log(`${shot.id}: ${shot.frames} frames, sounds ${allEvents[shot.id].length}`);
  await writeFile(eventsPath, JSON.stringify(allEvents, null, 2));
}
await browser.close();
