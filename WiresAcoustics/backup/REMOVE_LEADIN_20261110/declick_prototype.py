import sys,glob,os,subprocess,numpy as np,wave
S,OUT=sys.argv[1],sys.argv[2]
def load(f): return np.frombuffer(subprocess.run(['ffmpeg','-v','error','-i',f,'-f','f32le','-acodec','pcm_f32le','-'],capture_output=True).stdout,dtype=np.float32).astype(np.float64)
W=48; R=20; TH=10.0  # 1 ms windows, +-20 ms context, 20 dB
def detect(x):
    d2=np.zeros_like(x); d2[1:-1]=x[2:]-2*x[1:-1]+x[:-2]
    n=len(x)//W
    e=np.array([np.sqrt(np.mean(d2[i*W:(i+1)*W]**2)) for i in range(n)])
    pk=np.abs(x).max()
    hits=[]
    for i in range(1,n):
        ctx=np.concatenate([e[max(0,i-R):max(0,i-1)],e[i+2:i+R]])
        if len(ctx)<4: continue
        med=np.median(ctx)+1e-12
        if e[i]>TH*med and e[i]>pk*1e-3: hits.append(i)
    return hits
def fix(x,hits):
    y=x.copy()
    k=np.ones(17)/17
    sm=np.convolve(x,k,mode='same')
    for i in hits:
        a=max(0,i*W-W); b=min(len(x),(i+1)*W+W)
        L=b-a; g=np.ones(L); r=min(24,L//2)
        g[:r]=np.linspace(0,1,r); g[-r:]=np.linspace(1,0,r)
        y[a:b]=x[a:b]*(1-g)+sm[a:b]*g
    return y
os.makedirs(OUT,exist_ok=True)
for d in ['stone','wood','grass']:
    tot=0
    for f in sorted(glob.glob(os.path.join(S,d,'*.ogg'))):
        x=load(f); h=detect(x); tot+=len(h)
        if d!='grass': print(d,os.path.basename(f)[:-4],[ "%dms"%i for i in h])
        if h and d!='grass':
            y=fix(x,h); pcm=(np.clip(y,-1,1)*32767).astype('<i2')
            w=wave.open(os.path.join(OUT,os.path.basename(f)[:-4]+'_declicked.wav'),'wb'); w.setnchannels(1); w.setsampwidth(2); w.setframerate(48000); w.writeframes(pcm.tobytes()); w.close()
    print("==",d,"detections",tot)
