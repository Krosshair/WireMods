// excess delay without phase unwrapping: the delay whose straight phase line fits best at 200-1500 Hz
const L=require('./lib');
function robust(h,f0=200,f1=1500){const n=4096;const re=new Float64Array(n),im=new Float64Array(n);for(let i=0;i<h.length;i++)re[i]=h[i];L.fft(re,im);const mp=L.minPhase(L.mag(h,n));const a=Float64Array.from(mp),b=new Float64Array(n);L.fft(a,b);
 const ph=[],w=[];for(let k=Math.ceil(f0/48000*n);k<=f1/48000*n;k++){const x=re[k]*a[k]+im[k]*b[k],y=im[k]*a[k]-re[k]*b[k];ph.push(Math.atan2(y,x));w.push(2*Math.PI*k/n);}
 const score=d=>{let s=0;for(let i=0;i<w.length;i++)s+=Math.cos(ph[i]+w[i]*d);return s;};
 let best=0,bs=-1e9;for(let d=5;d<220;d+=.25){const s=score(d);if(s>bs){bs=s;best=d;}}
 let lo=best-.25,hi=best+.25;for(let i=0;i<40;i++){const m1=lo+(hi-lo)/3,m2=hi-(hi-lo)/3;if(score(m1)<score(m2))lo=m1;else hi=m2;}return [(lo+hi)/2,bs/w.length];}
module.exports={robust};
