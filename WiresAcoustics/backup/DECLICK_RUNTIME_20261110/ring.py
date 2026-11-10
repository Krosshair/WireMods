import numpy as np,os,sys,glob,wave
sys.argv=['x','4'];exec(open('burst2.py').read().split('for f in')[0])
def ma(x,n):
  k=np.ones(n)/n;return np.convolve(np.convolve(x,k,'same'),k,'same')
def hp(x): return x-ma(x,9)
L=64;PRE=8
def bank(sr):
  B=[]
  for f in np.arange(4.5e3,9.51e3,250):
    for tau in (2,3,5,8,12):
      t=np.arange(L).astype(float);env=(t/tau)*np.exp(1-t/tau)
      c=env*np.cos(2*np.pi*f/sr*t);s=env*np.sin(2*np.pi*f/sr*t)
      pad=lambda v:hp(np.concatenate([np.zeros(16),v,np.zeros(16)]))
      B.append((f,tau,c,s,pad(c),pad(s)))   # filtered versions have 16+L+16 samples
  return B
def fit(x,h,j,B):
  best=None
  for t0 in range(j-16,j+6):
    a=t0-16;b=t0+L+16
    if a<0 or b>len(x):continue
    y=h[a:b];yy=(y*y).sum()
    if yy<=0:continue
    for f,tau,c,s,fc,fs in B:
      G=np.array([[fc@fc,fc@fs],[fc@fs,fs@fs]]);r=np.array([fc@y,fs@y])
      try:k=np.linalg.solve(G,r)
      except:continue
      res=y-k[0]*fc-k[1]*fs;r2=1-(res@res)/yy
      if best is None or r2>best[0]:best=(r2,t0,f,tau,k)
  return best
def clean(x,sr,TH=3.16,GATE=.6,log=None):
  B=bank(sr);y=x.copy();done=[]
  for it in range(3):
    h=hp(y);hits=find(y,TH=TH);changed=False
    for j,_ in hits:
      bst=fit(y,h,j,B)
      if bst is None:continue
      r2,t0,f,tau,k=bst
      if log is not None: log.append((j,r2,f,tau,t0))
      if r2<GATE:continue
      t=np.arange(L).astype(float);env=(t/tau)*np.exp(1-t/tau)
      m=k[0]*env*np.cos(2*np.pi*f/sr*t)+k[1]*env*np.sin(2*np.pi*f/sr*t)
      y[t0:t0+L]-=m;done.append((t0,f,tau,np.hypot(*k)));changed=True
    if not changed:break
  return y,done
if __name__=="__main__":
  O=os.path.expanduser("~/mnt/kajet/Downloads/Footstep_clicks/original/")
  for n in ["Wood_Walk_M_03","Wood_Run_M_03","Wood_Run_M_07"]:
    x=load(O+"Player_Footstep_"+n+".ogg");log=[]
    y,d=clean(x,48000,log=log)
    print(n,"candidates",len(log));
    for j,r2,f,tau,t0 in log: print(f"   {j/48000:.4f} R2 {r2:.2f} f {f/1000:.2f} tau {tau}")
