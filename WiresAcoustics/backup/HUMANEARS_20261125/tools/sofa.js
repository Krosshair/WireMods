// reads the arrays of a SOFA file (HDF5: version 2 objects, chunks in a version 1 B-tree)
const fs = require('fs'), zlib = require('zlib');
module.exports = function (file) {
  const b = fs.readFileSync(file); const u = (o, n) => { let v = 0; for (let i = n - 1; i >= 0; i--) v = v * 256 + b[o + i]; return v; };
  const sets = [];
  const block = (o, end, fl, out) => { while (o + 4 <= end) { const t = b[o], s = u(o + 1, 2); o += 4 + (fl & 4 ? 2 : 0); if (o + s > end) break; out.push({ t, s, d: o }); if (t === 16) { const at = u(o, 8), len = u(o + 8, 8); if (b.toString('latin1', at, at + 4) === 'OCHK') block(at + 4, at + len - 4, fl, out); } o += s; } };
  for (let i = b.indexOf('OHDR'); i >= 0; i = b.indexOf('OHDR', i + 4)) {
    const fl = b[i + 5]; let o = i + 6 + (fl & 32 ? 16 : 0) + (fl & 16 ? 4 : 0); const n = 1 << (fl & 3), size = u(o, n); o += n; const m = []; block(o, o + size, fl, m);
    const sp = m.find(x => x.t === 1), ly = m.find(x => x.t === 8), pl = m.find(x => x.t === 11); if (!sp || !ly || b[ly.d + 1] !== 2) continue;
    const dims = Array.from({ length: b[sp.d + 1] }, (_, k) => u(sp.d + 4 + 8 * k, 8));
    sets.push({ dims, read() { // doubles
      const nd = b[ly.d + 2] - 1, tree = u(ly.d + 3, 8), ch = Array.from({ length: nd }, (_, k) => u(ly.d + 11 + 4 * k, 4));
      const filters = []; if (pl) { let p = pl.d + 2; for (let k = 0; k < b[pl.d + 1]; k++) { const id = u(p, 2); p += 2; if (id >= 256) p += 2; p += 2; const nv = u(p, 2); p += 2; filters.push(id); p += 4 * nv; } }
      const out = new Float64Array(dims.reduce((x, y) => x * y, 1));
      const walk = at => { if (b.toString('latin1', at, at + 4) !== 'TREE') throw 'tree'; const level = b[at + 5], cnt = u(at + 6, 2), ks = 8 + 8 * (nd + 1); let p = at + 24; for (let k = 0; k < cnt; k++) { const len = u(p, 4), mask = u(p + 4, 4), off = Array.from({ length: nd }, (_, q) => u(p + 8 + 8 * q, 8)), child = u(p + ks, 8); p += ks + 8; if (level) { walk(child); continue; }
        let d = b.subarray(child, child + len); for (let f = filters.length - 1; f >= 0; f--) { if (mask >> f & 1) continue; if (filters[f] === 1) d = zlib.inflateSync(d); else if (filters[f] === 2) { const n = d.length / 8, e = Buffer.alloc(d.length); for (let x = 0; x < n; x++) for (let y = 0; y < 8; y++) e[x * 8 + y] = d[y * n + x]; d = e; } else if (filters[f] === 3) d = d.subarray(0, d.length - 4); else throw 'filter ' + filters[f]; }
        const idx = new Array(nd).fill(0), total = ch.reduce((x, y) => x * y, 1); for (let x = 0; x < total; x++) { let q = x, ok = true, pos = 0; for (let y = nd - 1; y >= 0; y--) { idx[y] = off[y] + q % ch[y]; q = Math.floor(q / ch[y]); if (idx[y] >= dims[y]) ok = false; } if (!ok) continue; for (let y = 0; y < nd; y++) pos = pos * dims[y] + idx[y]; out[pos] = d.readDoubleLE(x * 8); } } };
      walk(tree); return out; } });
  }
  const M = sets.find(s => s.dims.length === 3 && s.dims[1] === 2 && s.dims[2] > 100), P = sets.find(s => s.dims.length === 2 && s.dims[0] === M.dims[0] && s.dims[1] === 3), R = sets.filter(s => s.dims.length === 1 && s.dims[0] === 1);
  const ir = M.read(), pos = P.read(), N = M.dims[2]; return { rate: R[0].read()[0], delay: sets.filter(s => s.dims.length === 2 && s.dims[1] === 2).map(s => [...s.read()]), set: Array.from({ length: M.dims[0] }, (_, i) => ({ az: ((pos[3 * i] % 360) + 360) % 360, el: pos[3 * i + 1], r: pos[3 * i + 2], L: ir.slice(2 * i * N, (2 * i + 1) * N), R: ir.slice((2 * i + 1) * N, (2 * i + 2) * N) })) };
};
