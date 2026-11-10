import numpy as np,os,glob,sys,wave
sys.argv=['x','4'];exec(open('burst2.py').read().split('for f in')[0])
def ma(x,n):
  k=np.ones(n)/n;return np.convolve(np.convolve(x,k,'same'),k,'same')
def env(x,n=480): return np.sqrt(np.convolve(x*x,np.ones(n)/n,'same'))
def patch(x,hits,n):
  s=ma(x,n);m=np.zeros_like(x)
  for j,_ in hits:
    a,b=max(0,j-96),min(len(x),j+96);w=np.ones(b-a);F=min(32,(b-a)//2);q=np.hanning(2*F);w[:F]=q[:F];w[-F:]=q[F:];m[a:b]=np.maximum(m[a:b],w)
  return x*(1-m)+s*m
def fix(x):
  s=ma(x,9);h=x-s
  e=env(x);p=int(np.argmax(e));t=p+int(np.argmax(e[p:]<e[p]*0.25))
  g=np.ones_like(x);g[t:t+480]=np.linspace(1,0,len(g[t:t+480]));g[t+480:]=0
  y=s+h*g
  # keep smoothing whatever still sticks out, harder each pass, until nothing does
  # the impact itself (3 ms either side of the loudest sample) is left alone
  pi=int(np.argmax(abs(x)));keep=lambda hs:[h for h in hs if abs(h[0]-pi)>144]
  for n in (9,13,19,27,39,55):
    hits=keep(find(y,TH=3.16))
    if not hits: break
    y=patch(y,hits,n)
  return y,t,keep(find(y,TH=3.16))
out=os.path.expanduser("~/mnt/kajet/Downloads/Footstep_clicks/declicked3");os.makedirs(out,exist_ok=True)
bad=0
for f in sorted(glob.glob(os.path.expanduser("~/mnt/kajet/Downloads/Footstep_clicks/original/*.ogg"))):
  x=load(f);y,t,left=fix(x);n=os.path.basename(f)[:-4]
  with wave.open(f"{out}/{n}_declicked.wav","wb") as w:
    w.setnchannels(1);w.setsampwidth(2);w.setframerate(sr);w.writeframes((np.clip(y,-1,1)*32767).astype('<i2').tobytes())
  pk=lambda z:20*np.log10(abs(z).max())
  if left: bad+=1
  print(f"{n[16:]:18} peak {pk(x):5.1f}->{pk(y):5.1f} dB  left>=10dB: {[round(j/sr,4) for j,_ in left]}")
print("files with anything left:",bad)
