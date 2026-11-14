// Lists voxel boxes of different colors whose same-facing faces share a plane and overlap: those flicker as z-fighting.
// Usage: node scripts/check-world-overlaps.js   (exit 1 when any overlap is found)
const fs = require('fs'), path = require('path');
const html = fs.readFileSync(path.join(__dirname, '../impls/Facet/Design/World/world.html'), 'utf8');
const js = html.slice(html.indexOf('/* ---------- 수학'), html.indexOf('/* ---------- 셰이더'));
const run = new Function('window', js + '\nreturn { M, buildStreet, buildNature, buildCave };');
const world = run({});
const hex = (c) => c.map((v) => v.toFixed(3)).join(',');
let failed = 0;
for (const [name, build] of [['market', world.buildStreet], ['lakeside', world.buildNature], ['cave', world.buildCave]]) {
  const planes = new Map();
  const original = world.M.prototype.box;
  world.M.prototype.box = function (x, y, z, w, h, d, c, flag = 0) {
    // Foliage, grass and glowing crystals intersect on purpose.
    if (this.hm && flag !== 1 && flag !== 2 && flag !== 3 && flag !== 4) {
      const add = (axis, at, a0, a1, b0, b1) => {
        const key = axis + ':' + at.toFixed(3);
        if (!planes.has(key)) planes.set(key, []);
        planes.get(key).push({ a0, a1, b0, b1, color: hex(c), x, y, z });
      };
      add('+x', x + w, y, y + h, z, z + d); add('-x', x, y, y + h, z, z + d);
      add('+y', y + h, x, x + w, z, z + d);
      add('+z', z + d, x, x + w, y, y + h); add('-z', z, x, x + w, y, y + h);
    }
    return original.call(this, x, y, z, w, h, d, c, flag);
  };
  build((m) => ({ vao: null, n: m.n / 28 * 6 }));
  world.M.prototype.box = original;
  const pairs = new Map();
  for (const faces of planes.values()) {
    faces.sort((p, q) => p.a0 - q.a0);
    for (let i = 0; i < faces.length; i++) for (let j = i + 1; j < faces.length && faces[j].a0 < faces[i].a1 - 1e-3; j++) {
      const p = faces[i], q = faces[j];
      // Specks such as fruit are too small to read as flicker.
      const across = Math.min(p.a1, q.a1) - Math.max(p.a0, q.a0), along = Math.min(p.b1, q.b1) - Math.max(p.b0, q.b0);
      if (p.color === q.color || along < 1e-3 || across * along < .02) continue;
      const key = [p.color, q.color].sort().join(' / ');
      const entry = pairs.get(key) || { count: 0, at: [p.x, p.y, p.z].map((v) => +v.toFixed(2)) };
      entry.count++; pairs.set(key, entry);
    }
  }
  const total = [...pairs.values()].reduce((sum, e) => sum + e.count, 0);
  console.log(name + ': ' + total + ' overlaps');
  [...pairs.entries()].sort((a, b) => b[1].count - a[1].count).slice(0, 14).forEach(([k, e]) => console.log('  ' + e.count + '  ' + k + '  near ' + e.at));
  failed += total;
}
process.exit(failed ? 1 : 0);
