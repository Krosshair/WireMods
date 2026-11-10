import numpy as np,os,wave
exec(open('ring3.py').read())
exec(open('fix5.py').read().split('out=os.path')[0].replace("sys.argv=['x','4'];exec(open('burst2.py').read().split('for f in')[0])",""))
O=os.path.expanduser("~/mnt/kajet/Downloads/Footstep_clicks/original/Player_Footstep_")
known={"Wood_Walk_M_03":[.1736,.1933],"Wood_Run_M_03":[.2025,.2726],"Wood_Run_M_05":[.1801],"Wood_Run_M_06":[.3202],"Wood_Run_M_07":[.183],"Wood_Walk_M_05":[.2824]}
def stick(z,t,sr=48000):
  j=int(t*sr);d=np.zeros_like(z);d[1:-1]=z[2:]-2*z[1:-1]+z[:-2];e=np.sqrt(np.convolve(d*d,np.ones(12)/12,'same'))
  return 20*np.log10(e[j-24:j+24].max()/np.median(e[j-480:j+480]))
def other(z,y):  # how much of the clip outside the click spots changed
  return 20*np.log10(np.sqrt(np.mean((z-y)**2))/np.sqrt(np.mean(z**2))+1e-12)
for n,ts in known.items():
  x=load(O+n+".ogg");yr,d=clean(x,48000);ys,_,_=fix(x)
  for t in ts: print(f"{n:15} {t:.4f}  sticks out: original {stick(x,t):5.1f} dB  ring-fit {stick(yr,t):5.1f}  smoothing {stick(ys,t):5.1f}")
  print(f"   whole-clip change: ring-fit {other(x,yr):6.1f} dB  smoothing {other(x,ys):6.1f} dB   fits subtracted {len(d)}")
