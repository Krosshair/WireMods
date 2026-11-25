// reading the KU100 set and the maths the rest uses
const fs = require('fs'), path = require('path');
const NF = 1024;
function fft(re, im, inv) { // in place, radix 2
  const n = re.length; for (let i = 1, j = 0; i < n; i++) { let b = n >> 1; for (; j & b; b >>= 1) j ^= b; j ^= b; if (i < j) { let t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; } }
  for (let len = 2; len <= n; len <<= 1) { const a = (inv ? 2 : -2) * Math.PI / len; for (let i = 0; i < n; i += len) for (let k = 0; k < len / 2; k++) { const c = Math.cos(a * k), s = Math.sin(a * k), x = i + k, y = x + len / 2; const r = re[y] * c - im[y] * s, m = re[y] * s + im[y] * c; re[y] = re[x] - r; im[y] = im[x] - m; re[x] += r; im[x] += m; } }
  if (inv) for (let i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
}
function mag(h, n = NF) { const re = new Float64Array(n), im = new Float64Array(n); for (let i = 0; i < h.length; i++) re[i] = h[i]; fft(re, im); const m = new Float64Array(n / 2 + 1); for (let i = 0; i <= n / 2; i++) m[i] = Math.hypot(re[i], im[i]); return m; }
function minPhase(m) { // from a magnitude (n/2+1 bins) to a minimum-phase impulse response, through the cepstrum
  const n = (m.length - 1) * 2, re = new Float64Array(n), im = new Float64Array(n);
  for (let i = 0; i < n; i++) re[i] = Math.log(Math.max(1e-6, m[i <= n / 2 ? i : n - i]));
  fft(re, im, true); for (let i = 1; i < n / 2; i++) { re[i] *= 2; im[i] = 0; } for (let i = n / 2 + 1; i < n; i++) { re[i] = 0; im[i] = 0; } im[0] = im[n / 2] = 0;
  fft(re, im); for (let i = 0; i < n; i++) { const e = Math.exp(re[i]), p = im[i]; re[i] = e * Math.cos(p); im[i] = e * Math.sin(p); } fft(re, im, true); return re;
}
function load(DIR) {
  const out = []; for (const f of fs.readdirSync(DIR)) { const m = f.match(/^azi_([-\d,.]+)_ele_([-\d,.]+)_DFC\.wav$/); if (!m) continue; const b = fs.readFileSync(path.join(DIR, f)); const n = b.readUInt32LE(40) / 6; const L = new Float64Array(n), R = new Float64Array(n); for (let i = 0; i < n; i++) { L[i] = b.readIntLE(44 + i * 6, 3) / 8388608; R[i] = b.readIntLE(47 + i * 6, 3) / 8388608; } out.push({ az: parseFloat(m[1].replace(',', '.')), el: parseFloat(m[2].replace(',', '.')), L, R }); } return out;
}
// delay of an impulse response against its own minimum-phase version, in samples (cross-correlation, parabola peak)
function delayOf(h) { const mp = minPhase(mag(h)); const n = NF, a = new Float64Array(n), ai = new Float64Array(n), b = new Float64Array(n), bi = new Float64Array(n); for (let i = 0; i < h.length; i++) a[i] = h[i]; for (let i = 0; i < n; i++) b[i] = mp[i]; fft(a, ai); fft(b, bi); const U = 8, re = new Float64Array(n * U), im = new Float64Array(n * U); for (let i = 0; i <= n / 2; i++) { const r = a[i] * b[i] + ai[i] * bi[i], q = ai[i] * b[i] - a[i] * bi[i]; re[i] = r; im[i] = q; if (i) { re[n * U - i] = r; im[n * U - i] = -q; } } fft(re, im, true); let best = 0; for (let i = 0; i < 200 * U; i++) if (re[i] > re[best]) best = i; const y0 = re[(best + n * U - 1) % (n * U)], y1 = re[best], y2 = re[best + 1]; return (best + .5 * (y0 - y2) / (y0 - 2 * y1 + y2)) / U; }
const dB = x => 20 * Math.log10(Math.max(1e-9, x));
// error between two magnitudes in dB after third-octave smoothing, 200 Hz to 16 kHz: [rms, worst]
function err(m1, m2, fs = 48000) { const n = (m1.length - 1) * 2; let s = 0, c = 0, w = 0; for (let f = 200; f <= 16000; f *= Math.pow(2, 1 / 12)) { const lo = f * Math.pow(2, -1 / 6), hi = f * Math.pow(2, 1 / 6); let a = 0, b = 0; for (let i = Math.max(1, Math.ceil(lo / fs * n)); i <= Math.max(Math.ceil(lo / fs * n), Math.floor(hi / fs * n)); i++) { a += m1[i] * m1[i]; b += m2[i] * m2[i]; } const d = 10 * Math.log10(a / b); s += d * d; c++; w = Math.max(w, Math.abs(d)); } return [Math.sqrt(s / c), w]; }
module.exports = { fft, mag, minPhase, load, delayOf, dB, err, NF };
