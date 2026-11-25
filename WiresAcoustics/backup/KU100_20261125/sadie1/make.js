// the KU100 recordings (SADIE subject 002, diffuse-field compensated, 48 kHz) -> ku100.bin for Wire's Acoustics
// node make.js <folder with the WAVs> <ku100.bin>
const L = require('./lib'), { robust } = require('./robust'), fs = require('fs');
const N = 64, ROWS = 19, COLS = 72;
const set = L.load(process.argv[2]), at = (az, el) => set.find(s => s.az === ((az % 360) + 360) % 360 && s.el === el);
// minimum-phase filter cut to N samples, faded out at the end
const cut = h => Float64Array.from({ length: N }, (_, i) => h[i] * (i < N - 16 ? 1 : .5 + .5 * Math.cos(Math.PI * (i - (N - 16) + 1) / 17)));
const one = h => ({ m: L.mag(h, 2048), d: robust(h)[0] });
const mix = (A, B, t) => ({ m: A.m.map((v, i) => Math.exp((1 - t) * Math.log(Math.max(1e-6, v)) + t * Math.log(Math.max(1e-6, B.m[i])))), d: A.d + (B.d - A.d) * t });
function grid(ear) {
  const rows = [];
  const tops = set.filter(s => s.el === 90).map(s => one(s[ear])); let top = tops[0]; for (let i = 1; i < tops.length; i++) top = mix(top, tops[i], 1 / (i + 1));
  for (let r = 0; r < ROWS; r++) {
    const e = -90 + r * 10, row = [];
    for (let c = 0; c < COLS; c++) row.push(e === -90 ? one(at(0, -90)[ear]) : e === 90 ? top : e === -80 ? null : one(at(c * 5, e)[ear]));
    // Two recordings in Google's copy are broken: 225/-50 has its ears swapped and 220/-50 is half way
    // to it. Filled from the good ones on both sides (215 and 230).
    if (e === -50) { row[44] = mix(row[43], row[46], 1 / 3); row[45] = mix(row[43], row[46], 2 / 3); }
    rows.push(row);
  }
  for (let c = 0; c < COLS; c++) rows[1][c] = mix(rows[0][c], rows[2][c], .5); // -80 wasn't recorded
  // each ear's average over all directions to 0 dB (recorded at -3 dB, the left microphone 0.4 dB louder)
  let p = 0, w = 0; for (let r = 2; r < ROWS - 1; r++) for (const g of rows[r]) { const a = Math.cos((-90 + r * 10) * Math.PI / 180); for (let i = Math.round(200 / 48000 * 2048); i < 16000 / 48000 * 2048; i++) { p += a * g.m[i] ** 2; w += a; } }
  const gain = 1 / Math.sqrt(p / w); console.log(ear, 'average', (-20 * Math.log10(gain)).toFixed(2), 'dB');
  return rows.flat().map(g => ({ d: g.d, f: cut(L.minPhase(g.m)).map(v => v * gain) }));
}
const all = [...grid('L'), ...grid('R')];
const mn = Math.min(...all.map(g => g.d)), peak = Math.max(...all.map(g => Math.max(...g.f.map(Math.abs))));
const b = Buffer.alloc(24 + all.length * 4 + all.length * N * 2); let o = 4; b.write('KU2F', 0, 'ascii'); for (const v of [48000, ROWS, COLS, N]) { b.writeInt32LE(v, o); o += 4; }
const scale = peak / 32767; b.writeFloatLE(scale, o); o += 4; for (const g of all) { b.writeFloatLE(g.d - mn, o); o += 4; } for (const g of all) for (const v of g.f) { b.writeInt16LE(Math.round(v / scale), o); o += 2; }
fs.writeFileSync(process.argv[3], b); console.log(b.length, 'bytes, delays', mn.toFixed(2), Math.max(...all.map(g => g.d)).toFixed(2), 'peak', peak.toFixed(3));
let st = 0; for (let e = 0; e < 2; e++) for (let r = 0; r < ROWS; r++) for (let c = 0; c < COLS; c++) st = Math.max(st, Math.abs(all[(e * ROWS + r) * COLS + c].d - all[(e * ROWS + r) * COLS + (c + 1) % COLS].d)); console.log('largest delay step', st.toFixed(2));
