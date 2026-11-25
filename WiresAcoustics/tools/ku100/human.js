// human_average.bin -> ku100.bin for Wire's Acoustics: node human.js human_average.bin ku100.bin
// human_average.bin is the plain average of SADIE II's 18 people (H3 to H20), made with
// human_average_prompt.txt (same format, see Ears.Load). Here its colour is cut down to the broad shape.
const fs = require('fs'), zlib = require('zlib'), L = require('./lib');
const b = zlib.inflateRawSync(fs.readFileSync(process.argv[2]).subarray(4)), R = b.readInt32LE(4), C = b.readInt32LE(8), N = b.readInt32LE(12), G = 2 * R * C, from = 20 + 4 * R + 4 * G;
const T = Array.from({ length: G }, () => new Float64Array(N)); for (let i = 0, q = 0; i < N; i++) { let v = 0; for (let g = 0; g < G; g++, q++) { const u = b[from + q] << 8 | b[from + G * N + q]; v = ((v + ((u >> 1) ^ -(u & 1))) << 16) >> 16; T[g][i] = v * b.readFloatLE(16); } }
// What both ears hear together is the sound's colour. Of that only the broad shape stays (a Gaussian, 2 octaves
// wide, over log frequency; the width is picked); the narrow peaks and dips go, by the same curve for both ears,
// so the level difference between the ears and the delay stay as measured. The curve is taken over 1/3 octaves: a
// finer one doesn't fit the short filters and bends the level difference.
const OCTAVES = 2, NF = 2048, BIN = 48000 / NF;
function broad(mL, mR) {
  const pw = Float64Array.from(mL, (v, i) => (v * v + mR[i] * mR[i]) / 2), own = pw.map((_, i) => { const a = Math.max(0, Math.round(i / 2 ** (1 / 6))), z = Math.min(pw.length - 1, Math.round(i * 2 ** (1 / 6))); let s = 0; for (let k = a; k <= z; k++) s += pw[k]; return 10 * Math.log10(Math.max(1e-12, s / (z - a + 1))); });
  const n = Math.floor(24 * Math.log2(18000 / 100)) + 1, x = Array.from({ length: n }, (_, j) => { const f = 100 * 2 ** (j / 24) / BIN, i = Math.floor(f); return own[i] + (own[i + 1] - own[i]) * (f - i); });
  const sg = OCTAVES * 24 / 2.355, y = x.map((_, j) => { let s = 0, w = 0; for (let k = 0; k < n; k++) { const g = Math.exp(-.5 * ((j - k) / sg) ** 2); s += g * x[k]; w += g; } return s / w; });
  const on = i => { const j = Math.max(0, Math.min(n - 1.001, 24 * Math.log2(Math.max(1, i) * BIN / 100))), k = Math.floor(j); return y[k] + (y[k + 1] - y[k]) * (j - k); };
  return own.map((v, i) => Math.pow(10, (on(i) - v) / 20));
}
const cut = h => Float64Array.from({ length: N }, (_, i) => h[i] * (i < N - 16 ? 1 : .5 + .5 * Math.cos(Math.PI * (i - (N - 16) + 1) / 17)));
const mags = [];
for (let g = 0; g < R * C; g++) { const A = L.mag(T[g], NF), D = L.mag(T[g + R * C], NF), k = broad(A, D); mags[g] = A.map((v, i) => v * k[i]); mags[g + R * C] = D.map((v, i) => v * k[i]); }
// the lows (120 Hz to 1 kHz) average 0 dB over all directions
let p = 0, w = 0; const els = Array.from({ length: R }, (_, i) => b.readFloatLE(20 + 4 * i));
for (let r = 0; r < R; r++) { const a = Math.cos(els[r] * Math.PI / 180) * (els[Math.min(R - 1, r + 1)] - els[Math.max(0, r - 1)]); for (let c = 0; c < C; c++) for (const g of [r * C + c, (R + r) * C + c]) for (let i = 5; i < 43; i++) { p += a * mags[g][i] ** 2; w += a; } }
const gain = 1 / Math.sqrt(p / w), taps = mags.map(m => cut(L.minPhase(m)).map(v => v * gain)), scale = Math.max(...taps.map(t => Math.max(...t.map(Math.abs)))) / 32767, I = taps.map(t => Array.from(t, v => Math.round(v / scale)));
const out = Buffer.from(b); out.writeFloatLE(scale, 16); for (let i = 0, q = 0; i < N; i++) for (let g = 0; g < G; g++, q++) { const d = ((I[g][i] - (g ? I[g - 1][i] : 0)) << 16) >> 16, u = ((d << 1) ^ (d >> 31)) & 0xffff; out[from + q] = u >> 8; out[from + G * N + q] = u & 255; }
const file = Buffer.concat([Buffer.from('KU4F'), zlib.deflateRawSync(out, { level: 9, memLevel: 9 })]); fs.writeFileSync(process.argv[3], file); console.log(file.length, 'bytes');
