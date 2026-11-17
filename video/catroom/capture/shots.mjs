// One recorded clip per shot id from scripts/script.mjs. Coordinates are the room's own normalized units
// (x 0..1 of width, y 0..1 of height; the floor is y .80..97). `setup` runs before the warm-up, `events`
// fire at seconds from the clip start (`line(i)` = start of the shot's i-th caption line).
import { widgetFrames } from "./widgets.mjs";

const hold = (pose, dur = 1e9) => ({ t: "hold", pose, dur });
const go = (x, y, spd) => ({ t: "go", x, y, spd });

export const SHOTS = {
  open: {
    setup: (v) => v.place(1.1, 0.9, [hold("stand")]),
    events: [{ at: 0.9, run: (v) => v.queue([go(0.66, 0.89, 0.11), hold("sit")]) }],
  },
  walk: { warm: 0.6, setup: (v) => v.place(0.3, 0.92, [go(0.6, 0.86, 0.1), hold("stand")]) },
  groom: { warm: 0.7, setup: (v) => { v.place(0.45, 0.88, []); v.js("R.BEH.groom()"); } },
  eat: { warm: 1.2, setup: (v) => { v.place(0.3, 0.81, []); v.js("R.BEH.eat()"); } },
  pom: { warm: 0.6, setup: (v) => { v.place(0.5, 0.85, []); v.js("R.BEH.pomPlay(true)"); } },
  hunt: {
    warm: 0.2,
    setup: (v) => { v.place(0.22, 0.9, [hold("sit")]); v.js("Object.assign(R.yarn,{ on: true, x: 0.5, y: 0.9, z: 0, vx: 0.05, vy: 0, vz: 0, hunted: true })"); },
    events: [{ at: 0.05, run: (v) => v.js("R.startHunt()") }],
  },
  nap: { warm: 2.6, setup: (v) => { v.place(0.25, 0.915, []); v.js("R.BEH.nap()"); } },
  rain: {
    warm: 0.5, setup: (v) => { v.place(0.5, 0.82, [hold("back")]); },
    events: [{ at: 0.25, run: (v) => v.js("R.setW('rain')") }],
  },
  snow: { warm: 0.8, setup: (v) => { v.js("R.setW('snow',true)"); v.place(0.5, 0.82, [hold("back")]); } },
  night: { time: "2026-11-17T21:00:00", warm: 0.3, setup: (v) => v.place(0.5, 0.82, [hold("back")]) },
  widgets: {
    warm: 0.3,
    setup: (v) => v.place(0.33, 0.88, [hold("loaf")]),
    events: [
      { at: 0.15, run: (v) => v.widgets(widgetFrames()) },
      { line: 1, at: 0.0, run: (v) => v.hover("w-clock", "[data-k=clock]") },
      { line: 1, at: 0.9, run: (v) => v.hover("w-weather", "[data-k=sign]") },
      { line: 2, at: 0.1, run: (v) => v.hover("w-mood", "[data-k='tok:good']") },
      { line: 2, at: 0.6, run: (v) => v.press("w-mood", "[data-k='tok:good']", "down") },
      { line: 2, at: 0.75, run: (v) => v.press("w-mood", "[data-k='tok:good']", "up") },
      { line: 2, at: 0.8, run: (v) => v.widgets(widgetFrames({ mood: "good" })) },
    ],
  },
  pet: {
    warm: 0.5, setup: (v) => v.place(0.45, 0.89, [hold("sit")]),
    events: [{ at: 0.25, run: (v) => v.clickCat() }, { at: 1.1, run: (v) => v.clickCat() }],
  },
  drop: {
    warm: 0.5, setup: (v) => v.place(0.22, 0.91, [hold("sit")]),
    events: [{ at: 0.2, run: (v) => v.click(0.5, 0.88) }],
  },
  call: {
    warm: 0.5, setup: (v) => v.place(0.16, 0.93, [hold("sit")]),
    events: [{ at: 0.2, run: (v) => v.clickPom() }],
  },
  end: {
    warm: 1.0,
    setup: (v) => { v.place(0.33, 0.88, [hold("loaf")]); v.widgets(widgetFrames({ mood: "good" })); },
  },
};
