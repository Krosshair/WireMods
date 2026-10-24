from PIL import Image
def deshadow(im):
    im=im.convert('RGBA'); W,H=im.size; p=im.load()
    out=im.copy(); o=out.load()
    def solid(x,y): return 0<=x<W and 0<=y<H and p[x,y][3]>200
    for y in range(H):
        for x in range(W):
            r,g,b,a=p[x,y]
            if a==0: continue
            border = a<=200 or any(not solid(x+dx,y+dy) for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)))
            if not border: continue
            # the body colour next to it: brightest solid neighbour within 2
            best=None
            for dy in range(-2,3):
                for dx in range(-2,3):
                    xx,yy=x+dx,y+dy
                    if (dx or dy) and solid(xx,yy):
                        q=p[xx,yy]
                        if best is None or q[0]>best[0]: best=q
            if best is None or best[0]<=0: o[x,y]=(0,0,0,0); continue
            # this pixel = body colour over black: how much body is in it
            cov=min(1.0,(r*a/255.0)/best[0])
            if cov<0.08: o[x,y]=(0,0,0,0); continue
            if r>=best[0]*0.97 and a>200: continue
            o[x,y]=(best[0],best[1],best[2],int(round(cov*255)))
    return out
if __name__=='__main__':
    import sys
    deshadow(Image.open(sys.argv[1])).save(sys.argv[2])
