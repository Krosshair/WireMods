// The KU100 recordings (SADIE II subject D1, diffuse-field equalised, 48 kHz) -> ku100.bin for Wire's Acoustics
// node make.js sadie_d1.sofa ku100.bin
const L = require('./lib'), { robust } = require('./robust'), fs = require('fs');
const N = 64, COLS = 72, sofa = require('./sofa')(process.argv[2]), set = sofa.set; if (sofa.rate !== 48000) throw 'rate';
const key = (az, el) => Math.round((((az % 360) + 360) % 360) * 10) + '/' + el.toFixed(1), idx = new Map(set.map(s => [key(s.az, s.el), s]));
const els = [...new Set(set.map(s => s.el))].sort((a, b) => a - b), ROWS = els.length; // every recorded ring
const at = (az, el) => { const s = idx.get(key(Math.abs(el) === 90 ? 0 : az, el)); if (!s) throw az + ' ' + el; return s; };
// minimum-phase filter cut to N samples, faded out at the end
const cut = h => Float64Array.from({ length: N }, (_, i) => h[i] * (i < N - 16 ? 1 : .5 + .5 * Math.cos(Math.PI * (i - (N - 16) + 1) / 17)));
function grid(ear) {
  const rows = els.map(e => Array.from({ length: COLS }, (_, c) => { const h = at(c * 5, e)[ear]; return { m: L.mag(h, 2048), d: robust(h)[0] }; }));
  // the ear's average over all directions to 0 dB
  let p = 0, w = 0; rows.forEach((R, r) => { const a = Math.cos(els[r] * Math.PI / 180) * (els[Math.min(ROWS - 1, r + 1)] - els[Math.max(0, r - 1)]); for (const g of R) for (let i = Math.round(200 / 48000 * 2048); i < 16000 / 48000 * 2048; i++) { p += a * g.m[i] ** 2; w += a; } });
  const gain = 1 / Math.sqrt(p / w); console.log(ear, 'average', (-20 * Math.log10(gain)).toFixed(2), 'dB');
  return rows.flat().map(g => ({ d: g.d, f: cut(L.minPhase(g.m)).map(v => v * gain) }));
}
const all = [...grid('L'), ...grid('R')];
const mn = Math.min(...all.map(g => g.d)), peak = Math.max(...all.map(g => Math.max(...g.f.map(Math.abs)))), scale = peak / 32767;
const b = Buffer.alloc(24 + ROWS * 4 + all.length * (4 + N * 2)); let o = 4; b.write('KU3F', 0, 'ascii'); for (const v of [48000, ROWS, COLS, N]) o = b.writeInt32LE(v, o); o = b.writeFloatLE(scale, o); for (const e of els) o = b.writeFloatLE(e, o);
for (const g of all) o = b.writeFloatLE(g.d - mn, o); for (const g of all) for (const v of g.f) o = b.writeInt16LE(Math.round(v / scale), o);
fs.writeFileSync(process.argv[3], b); console.log(b.length, 'bytes,', ROWS, 'rings, delays', mn.toFixed(2), Math.max(...all.map(g => g.d)).toFixed(2), 'peak', peak.toFixed(3));
let st = 0; for (let e = 0; e < 2 * ROWS; e++) for (let c = 0; c < COLS; c++) st = Math.max(st, Math.abs(all[e * COLS + c].d - all[e * COLS + (c + 1) % COLS].d)); console.log('largest delay step', st.toFixed(2));
