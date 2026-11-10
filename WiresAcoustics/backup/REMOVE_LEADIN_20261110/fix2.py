import numpy as np,os,glob,sys,subprocess,wave
sys.argv=['x','8'];exec(open('burst2.py').read().split('for f in')[0])
def smooth(x,n=9):
  k=np.ones(n)/n;return np.convolve(np.convolve(x,k,'same'),k,'same')
def fix(x,hits,H=72,F=24):
  y=x.copy();s=smooth(x);m=np.zeros_like(x)
  for j,_ in hits:
    a,b=max(0,j-H),min(len(x),j+H);w=np.ones(b-a)
    r=np.hanning(2*F);w[:F]=r[:F];w[-F:]=r[F:];m[a:b]=np.maximum(m[a:b],w)
  return x*(1-m)+s*m
out=os.path.expanduser("~/mnt/kajet/Downloads/Footstep_clicks/declicked2");os.makedirs(out,exist_ok=True)
tot=0
for f in sorted(glob.glob(os.path.expanduser("~/mnt/kajet/Downloads/Footstep_clicks/original/*.ogg"))):
  x=load(f);h=find(x);tot+=len(h)
  if not h: continue
  y=fix(x,h);n=os.path.basename(f)[:-4]
  with wave.open(f"{out}/{n}_declicked.wav","wb") as w:
    w.setnchannels(1);w.setsampwidth(2);w.setframerate(sr);w.writeframes((np.clip(y,-1,1)*32767).astype('<i2').tobytes())
  print(n[16:],[round(j/sr,4) for j,_ in h])
  if n.endswith("Wood_Walk_M_03"):
    d=np.diff(y[8300:8370]);print(" M03 max |diff| near 0.1735 before",round(abs(np.diff(x[8300:8370])).max(),4),"after",round(abs(d).max(),4))
print("total",tot)
