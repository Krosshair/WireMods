// The KU100 recordings (SADIE II subject D1, diffuse-field equalised, 48 kHz) -> ku100.bin for Wire's Acoustics
// node make.js sadie_d1.sofa ku100.bin
const L = require('./lib'), { robust } = require('./robust'), fs = require('fs');
const N = 64, COLS = 72, sofa = require('./sofa')(process.argv[2]), set = sofa.set; if (sofa.rate !== 48000) throw 'rate';
const key = (az, el) => Math.round((((az % 360) + 360) % 360) * 10) + '/' + el.toFixed(1), idx = new Map(set.map(s => [key(s.az, s.el), s]));
const els = [...new Set(set.map(s => s.el))].sort((a, b) => a - b), ROWS = els.length; // every recorded ring
const at = (az, el) => { const s = idx.get(key(Math.abs(el) === 90 ? 0 : az, el)); if (!s) throw az + ' ' + el; return s; };
// minimum-phase filter cut to N samples, faded out at the end
const cut = h => Float64Array.from({ length: N }, (_, i) => h[i] * (i < N - 16 ? 1 : .5 + .5 * Math.cos(Math.PI * (i - (N - 16) + 1) / 17)));
// What both ears hear together is the sound's colour. Of that only the broad shape stays (a Gaussian, 2 octaves
// wide, over log frequency); the narrow peaks and dips go, by the same curve for both ears, so the level
// difference between the ears and the delay stay as measured. The curve is taken over 1/3 octaves: a finer one
// doesn't fit the short filters and bends the level difference. The width of 2 octaves is picked.
const OCTAVES = 2, NF = 2048, BIN = 48000 / NF, LOW = Math.round(100 / BIN), HIGH = Math.round(18000 / BIN);
function broad(mL, mR) {
  const pw = Float64Array.from(mL, (v, i) => (v * v + mR[i] * mR[i]) / 2), own = pw.map((_, i) => { const a = Math.max(0, Math.round(i / 2 ** (1 / (2 * 3)))), b = Math.min(pw.length - 1, Math.round(i * 2 ** (1 / (2 * 3)))); let s = 0; for (let k = a; k <= b; k++) s += pw[k]; return 10 * Math.log10(Math.max(1e-12, s / (b - a + 1))); });
  // on a log axis of 24 steps an octave, 100 Hz to 18 kHz
  const n = Math.floor(24 * Math.log2(18000 / 100)) + 1, x = Array.from({ length: n }, (_, j) => { const f = 100 * 2 ** (j / 24) / BIN, i = Math.floor(f); return own[i] + (own[i + 1] - own[i]) * (f - i); });
  const sg = OCTAVES * 24 / 2.355, y = x.map((_, j) => { let s = 0, w = 0; for (let k = 0; k < n; k++) { const g = Math.exp(-.5 * ((j - k) / sg) ** 2); s += g * x[k]; w += g; } return s / w; });
  const on = i => { const j = Math.max(0, Math.min(n - 1.001, 24 * Math.log2(Math.max(1, i) * BIN / 100))), k = Math.floor(j); return y[k] + (y[k + 1] - y[k]) * (j - k); };
  return own.map((v, i) => Math.pow(10, (on(i) - v) / 20));
}
const colour = new Map(); for (const e of els) for (let c = 0; c < COLS; c++) { const s = at(c * 5, e); colour.set(e + '/' + c, broad(L.mag(s.L, 2048), L.mag(s.R, 2048))); }
function grid(ear) {
  const rows = els.map(e => Array.from({ length: COLS }, (_, c) => { const h = at(c * 5, e)[ear]; return { k: colour.get(e + '/' + c), m: L.mag(h, 2048), d: robust(h)[0] }; }));
  // the ear's recorded average over all directions to 0 dB (the lows stay at 0, as both ears' level differences do)
  let p = 0, w = 0; rows.forEach((R, r) => { const a = Math.cos(els[r] * Math.PI / 180) * (els[Math.min(ROWS - 1, r + 1)] - els[Math.max(0, r - 1)]); for (const g of R) for (let i = Math.round(200 / 48000 * 2048); i < 16000 / 48000 * 2048; i++) { p += a * g.m[i] ** 2; w += a; } });
  const gain = 1 / Math.sqrt(p / w); console.log(ear, 'average', (-20 * Math.log10(gain)).toFixed(2), 'dB');
  return rows.flat().map(g => ({ d: g.d, f: cut(L.minPhase(g.m.map((v, i) => v * g.k[i]))).map(v => v * gain) }));
}
const all = [...grid('L'), ...grid('R')];
const mn = Math.min(...all.map(g => g.d)), peak = Math.max(...all.map(g => Math.max(...g.f.map(Math.abs)))), scale = peak / 32767;
// Packed without loss: the delays' bytes in four runs, then the filters sample by sample as the step from
// one direction to the next (which is small), high bytes and low bytes apart, all deflated.
const G = all.length, b = Buffer.alloc(20 + ROWS * 4 + G * 4 + G * N * 2); let o = 0; for (const v of [48000, ROWS, COLS, N]) o = b.writeInt32LE(v, o); o = b.writeFloatLE(scale, o); for (const e of els) o = b.writeFloatLE(e, o);
const f4 = Buffer.alloc(4); all.forEach((g, i) => { f4.writeFloatLE(g.d - mn); for (let k = 0; k < 4; k++) b[o + k * G + i] = f4[k]; }); o += 4 * G;
const taps = all.map(g => Array.from(g.f, v => Math.round(v / scale))); let q = 0; for (let i = 0; i < N; i++) for (let g = 0; g < G; g++, q++) { const d = ((taps[g][i] - (g ? taps[g - 1][i] : 0)) << 16) >> 16, u = ((d << 1) ^ (d >> 31)) & 0xffff; b[o + q] = u >> 8; b[o + G * N + q] = u & 255; }
const out = Buffer.concat([Buffer.from('KU4F', 'ascii'), require('zlib').deflateRawSync(b, { level: 9, memLevel: 9 })]);
fs.writeFileSync(process.argv[3], out); console.log(out.length, 'bytes (' + b.length + ' unpacked),', ROWS, 'rings, delays', mn.toFixed(2), Math.max(...all.map(g => g.d)).toFixed(2), 'peak', peak.toFixed(3));
let st = 0; for (let e = 0; e < 2 * ROWS; e++) for (let c = 0; c < COLS; c++) st = Math.max(st, Math.abs(all[e * COLS + c].d - all[e * COLS + (c + 1) % COLS].d)); console.log('largest delay step', st.toFixed(2));
