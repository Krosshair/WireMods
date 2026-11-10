import subprocess,numpy as np,os,sys,glob
sr=48000
def load(f): return np.frombuffer(subprocess.run(["ffmpeg","-v","quiet","-i",f,"-f","f32le","-ac","1","-"],capture_output=True).stdout,np.float32).astype(float)
def find(x,W=12,R=480,TH=float(sys.argv[1]) if len(sys.argv)>1 else 4.0):
  d2=np.zeros_like(x);d2[1:-1]=x[2:]-2*x[1:-1]+x[:-2]
  e=np.sqrt(np.convolve(d2*d2,np.ones(W)/W,'same'))
  pk=abs(x).max();hits=[]
  i=R
  while i<len(x)-R:
    loc=np.median(e[i-R:i+R])
    if e[i]>TH*loc and e[i]>pk*2e-3:
      j=i+np.argmax(e[i:i+W]);hits.append((j,e[j]/loc));i=j+W*2
    else:i+=1
  return hits
for f in sorted(glob.glob(os.path.expanduser("~/mnt/kajet/Downloads/Footstep_clicks/original/*.ogg"))):
  x=load(f);h=find(x)
  print(os.path.basename(f)[16:-4],len(h),[(round(j/sr,4),round(20*np.log10(r),1)) for j,r in h][:8])
