import numpy as np
exec(open('ring.py').read().split('def clean(')[0])
def atom(f,tau,k,sr):
  t=np.arange(L).astype(float);env=(t/tau)*np.exp(1-t/tau)
  return k[0]*env*np.cos(2*np.pi*f/sr*t)+k[1]*env*np.sin(2*np.pi*f/sr*t)
def spot(h,j):  # click window energy vs its surroundings
  w=np.sqrt(np.mean(h[j-24:j+24]**2));around=np.concatenate([h[max(0,j-480):j-48],h[j+48:j+480]])
  return w,np.median(np.abs(around))*1.25+1e-12   # median |x| ~ 0.8 rms for noise-like
def clean(x,sr,TH=3.16,GATE=.6,ATOMS=8,log=None):
  B=bank(sr);y=x.copy();n=0
  for it in range(3):
    hits=find(y,TH=TH);changed=False
    for j,_ in hits:
      if j<600 or j>len(y)-600:continue
      h=hp(y);bst=fit(y,h,j,B)
      if bst is None or bst[0]<GATE:
        if log is not None and bst: log.append((j,bst[0],0))
        continue
      a=0
      while bst is not None and a<ATOMS:
        r2,t0,f,tau,k=bst;y[t0:t0+L]-=atom(f,tau,k,sr);a+=1
        h=hp(y);w,bg=spot(h,j)
        if w<=bg*1.4:break
        bst=fit(y,h,j,B)
        if bst is None or bst[0]<.2:break
      n+=a;changed=True
      if log is not None: log.append((j,r2,a))
    if not changed:break
  return y,n
