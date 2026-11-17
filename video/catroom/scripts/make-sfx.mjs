// Cute procedural sound effects, modelled on the cat room skin's own WebAudio sounds (world.html `__catSound`).
// The capture logs every sound the page plays (public/data/sfx-events.json) and the video plays these files at
// the same frames. Plus `tag` (caption paper tag landing) and a short `rain` bed. Seeded noise: same every run.
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const RATE = 44100;
const TAU = Math.PI * 2;

function wav(samples) {
  const data = Buffer.alloc(samples.length * 2);
  samples.forEach((v, i) => data.writeInt16LE(Math.round(Math.max(-1, Math.min(1, v)) * 32767), i * 2));
  const h = Buffer.alloc(44);
  h.write("RIFF", 0); h.writeUInt32LE(36 + data.length, 4); h.write("WAVE", 8);
  h.write("fmt ", 12); h.writeUInt32LE(16, 16); h.writeUInt16LE(1, 20); h.writeUInt16LE(1, 22);
  h.writeUInt32LE(RATE, 24); h.writeUInt32LE(RATE * 2, 28); h.writeUInt16LE(2, 32); h.writeUInt16LE(16, 34);
  h.write("data", 36); h.writeUInt32LE(data.length, 40);
  return Buffer.concat([h, data]);
}

let seed = 9;
const rand = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
const noise = () => rand() * 2 - 1;
const buf = (sec) => new Float32Array(Math.round(sec * RATE));

// Sine/triangle sweep with a fast attack and exponential-ish decay (like the page's tone()).
function tone(out, { type = "sine", f0, f1 = 0, dur, gain, delay = 0 }) {
  const s0 = Math.round(delay * RATE), n = Math.round(dur * RATE);
  let ph = 0;
  for (let i = 0; i < n && s0 + i < out.length; i++) {
    const t = i / RATE, k = t / dur;
    const f = f1 ? f0 * Math.pow(f1 / f0, k) : f0;
    ph += TAU * f / RATE;
    const w = type === "triangle" ? (2 / Math.PI) * Math.asin(Math.sin(ph)) : Math.sin(ph);
    const env = Math.min(1, t / 0.005) * Math.pow(1 - k, 2.2);
    out[s0 + i] += w * env * gain;
  }
  return out;
}
// Filtered noise burst: one-pole band around `f` (rough but soft).
function burst(out, { f, dur, gain, delay = 0, low = false }) {
  const s0 = Math.round(delay * RATE), n = Math.round(dur * RATE);
  const a = Math.min(0.99, (TAU * f) / RATE);
  let lp = 0, lp2 = 0;
  for (let i = 0; i < n && s0 + i < out.length; i++) {
    const t = i / RATE;
    lp += a * (noise() - lp); lp2 += a * (lp - lp2);
    const v = low ? lp2 : lp - lp2;
    out[s0 + i] += v * gain * 3 * Math.min(1, t / 0.003) * Math.pow(1 - t / dur, 2);
  }
  return out;
}

const S = {
  ding: () => tone(tone(buf(0.9), { f0: 1450, dur: 0.8, gain: 0.45 }), { f0: 1450 * 2.76, dur: 0.35, gain: 0.12 }),
  thud: () => burst(tone(buf(0.25), { f0: 110, f1: 55, dur: 0.16, gain: 0.8 }), { f: 400, dur: 0.07, gain: 0.5, low: true }),
  chirp: () => tone(tone(buf(0.3), { type: "triangle", f0: 680, f1: 980, dur: 0.13, gain: 0.35 }), { f0: 950, f1: 820, dur: 0.1, gain: 0.2, delay: 0.1 }),
  pop: () => tone(buf(0.15), { f0: 530, f1: 950, dur: 0.07, gain: 0.5 }),
  chime: () => tone(tone(buf(0.6), { f0: 1080, dur: 0.42, gain: 0.25, delay: 0.05 }), { f0: 1620, dur: 0.42, gain: 0.18, delay: 0.12 }),
  glass: () => tone(tone(buf(0.25), { f0: 2700, dur: 0.14, gain: 0.25 }), { f0: 5400, dur: 0.06, gain: 0.07 }),
  knock: () => burst(tone(buf(0.2), { type: "triangle", f0: 235, f1: 180, dur: 0.09, gain: 0.6 }), { f: 900, dur: 0.05, gain: 0.3 }),
  flick: () => burst(buf(0.08), { f: 4000, dur: 0.05, gain: 0.25 }),
  crunch: () => burst(burst(buf(0.15), { f: 3200, dur: 0.03, gain: 0.45 }), { f: 2800, dur: 0.03, gain: 0.4, delay: 0.05 }),
  lap: () => burst(tone(buf(0.1), { f0: 420, f1: 780, dur: 0.06, gain: 0.35 }), { f: 1500, dur: 0.04, gain: 0.15 }),
  scratch: () => burst(buf(0.14), { f: 2800, dur: 0.1, gain: 0.35 }),
  tag: () => burst(tone(buf(0.25), { f0: 180, f1: 90, dur: 0.12, gain: 0.35 }), { f: 2200, dur: 0.08, gain: 0.25 }),
  creak() {
    const out = buf(0.4);
    let ph = 0;
    for (let i = 0; i < out.length; i++) {
      const t = i / RATE, f = 150 + (t / 0.35) * 90;
      ph += TAU * f / RATE;
      const saw = ((ph / TAU) % 1) * 2 - 1;
      out[i] = saw * 0.06 * Math.sin(Math.PI * Math.min(1, t / 0.3)) * (t < 0.3 ? 1 : 0);
    }
    return out;
  },
  // Purr: low noise amplitude-modulated at ~25 Hz with a slow breath, 2 s.
  purr() {
    const out = buf(2.1);
    let lp = 0, lp2 = 0;
    for (let i = 0; i < out.length; i++) {
      const t = i / RATE;
      lp += (noise() - lp) * 0.08; lp2 += (lp - lp2) * 0.3;
      const am = Math.pow(0.5 + 0.5 * Math.sin(TAU * 25 * t), 2.4), breath = 0.6 + 0.4 * Math.sin(t * Math.PI * 1.1 - 1.2);
      const env = Math.min(1, t / 0.15) * Math.min(1, (2.1 - t) / 0.4);
      out[i] = lp2 * am * breath * env * 5;
    }
    return out;
  },
  // Mew: a short gliding triangle with vibrato.
  mew() {
    const out = buf(0.6);
    let ph = 0;
    for (let i = 0; i < out.length; i++) {
      const t = i / RATE, k = t / 0.5;
      const f = 700 + 350 * Math.sin(Math.PI * Math.min(1, k)) + 15 * Math.sin(TAU * 6 * t);
      ph += TAU * f / RATE;
      out[i] = (2 / Math.PI) * Math.asin(Math.sin(ph)) * 0.22 * Math.min(1, t / 0.03) * Math.max(0, 1 - k) ** 0.7;
    }
    return out;
  },
  // Rain on the window: soft low-passed noise with little drips, 3 s loopable-ish bed.
  rain() {
    const out = buf(3);
    let lp = 0;
    for (let i = 0; i < out.length; i++) { lp += (noise() - lp) * 0.12; out[i] = lp * 0.5; }
    for (let k = 0; k < 14; k++) tone(out, { f0: 1500 + rand() * 700, f1: 950, dur: 0.05, gain: 0.05, delay: rand() * 2.9 });
    return out;
  },
};
S.thump = S.thud;

await mkdir(path.join(root, "public", "sfx"), { recursive: true });
for (const [k, fn] of Object.entries(S)) await writeFile(path.join(root, "public", "sfx", `${k}.wav`), wav(fn()));
console.log(`효과음 ${Object.keys(S).length}개를 만들었습니다.`);
