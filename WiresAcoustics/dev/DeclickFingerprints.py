import numpy as np,subprocess,os,sys
A=os.path.expanduser("~/mnt/kajet/WireMods/ValheimSOURCE/ValheimProject/ExportedProject/Assets/")
rows=[l.rstrip('\n').split('\t') for l in open('table.tsv')]
done={l.split('\t')[0] for l in open('fp.tsv')} if os.path.exists('fp.tsv') else set()
o=open('fp.tsv','a')
for name,frames,ch,rate,path,fx,_ in rows:
  if fx in('-','skip','unreadable') or path in done: continue
  b=subprocess.run(["ffmpeg","-v","quiet","-i",A+path,"-f","f32le","-"],capture_output=True).stdout
  x=np.frombuffer(b,np.float32).reshape(-1,int(ch))
  c,p=map(int,fx.split(';')[0].split(':')[:2])
  seg=x[p-8:p+8,c]
  o.write(path+'\t'+f"{c}:{p}:"+','.join(str(int(round(v*32767))) for v in seg)+'\n');o.flush()
