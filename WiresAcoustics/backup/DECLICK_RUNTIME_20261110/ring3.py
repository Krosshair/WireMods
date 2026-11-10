import numpy as np
exec(open('ring2.py').read().split('\ndef clean(')[0])
def patch_at(x,spots,n):
  s=ma(x,n);m=np.zeros_like(x)
  for j in spots:
    a,b=max(0,j-96),min(len(x),j+96);w=np.ones(b-a);F=min(32,(b-a)//2);q=np.hanning(2*F);w[:F]=q[:F];w[-F:]=q[F:];m[a:b]=np.maximum(m[a:b],w)
  return x*(1-m)+s*m
def rclean(x,sr,TH=3.16,GATE=.6,log=None):
  B=bank(sr);y=x.copy();marked=[]
  for it in range(3):
    h=hp(y);new=[]
    for j,_ in find(y,TH=TH):
      if any(abs(j-m)<48 for m in marked):continue
      bst=fit(y,h,j,B)
      if log is not None and bst:log.append((j,round(bst[0],2)))
      if bst and bst[0]>=GATE:new.append(j)
    if not new:break
    marked+=new
    todo=new
    for n in (9,13,19,27,39):
      y=patch_at(y,todo,n);h=hp(y)
      todo=[j for j in todo if (lambda w,bg:w>bg*1.4)(*spot(h,j))]
      if not todo:break
  return y,len(marked)
