// Lays out the timeline from fixed caption durations (no TTS): scenes, line cues, shots, SRT.
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { buildScenes } from "./script.mjs";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const FPS = 30;
const config = JSON.parse(await readFile(path.join(root, "config.json"), "utf8"));
const sec = (s) => Math.round(s * FPS);

let cursor = 0;
const scenes = [];
const shots = [];
const subtitles = [];
for (const scene of buildScenes()) {
  const from = cursor;
  const lines = scene.lines.map((l) => {
    const placed = { sub: l.sub, shot: l.shot, from: cursor, frames: sec(l.seconds) };
    subtitles.push({ from: placed.from, to: placed.from + placed.frames, text: l.sub });
    const prev = shots.at(-1);
    if (prev && prev.id === l.shot) prev.frames += placed.frames;
    else shots.push({ id: l.shot, from: cursor, frames: placed.frames, lines: [] });
    shots.at(-1).lines.push(placed.from - shots.at(-1).from);
    cursor += placed.frames;
    return placed;
  });
  const entry = { id: scene.id, from, lines };
  if (scene.credits) {
    entry.blurAt = cursor + sec(0.2);
    entry.rollAt = entry.blurAt + sec(0.5);
    entry.rollFrames = sec(config.creditSeconds ?? 4);
    cursor = entry.rollAt + entry.rollFrames + sec(0.8);
    shots.at(-1).frames = cursor - shots.at(-1).from;
  }
  entry.frames = cursor - from;
  scenes.push(entry);
}

await mkdir(path.join(root, "public", "data"), { recursive: true });
await writeFile(path.join(root, "public", "data", "timeline.json"), JSON.stringify({ fps: FPS, totalFrames: cursor, scenes, shots }, null, 2));
const stamp = (frame) => {
  const ms = Math.round((frame / FPS) * 1000);
  const pad = (n, w = 2) => String(n).padStart(w, "0");
  return `${pad(Math.floor(ms / 3600000))}:${pad(Math.floor(ms / 60000) % 60)}:${pad(Math.floor(ms / 1000) % 60)},${pad(ms % 1000, 3)}`;
};
await mkdir(path.join(root, "out"), { recursive: true });
await writeFile(path.join(root, "out", `${config.slug}.srt`), subtitles.map((s, i) => `${i + 1}\n${stamp(s.from)} --> ${stamp(s.to)}\n${s.text}\n`).join("\n"));
console.log(`총 ${(cursor / FPS).toFixed(1)}초, 장면 ${scenes.length}개, 샷 ${shots.length}개, 자막 ${subtitles.length}줄`);
