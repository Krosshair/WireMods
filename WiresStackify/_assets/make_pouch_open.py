from PIL import Image, ImageFilter
import math, sys
SRC='/mnt/user-data/uploads/kajet/WireMods/WiresStackify/_assets/pouch_icon.png'
from deshadow import deshadow
src=deshadow(Image.open(SRC)); W,H=src.size; S=8
KEEP=23            # original body kept from this row down
TOP=13.0           # mouth centre row
RX,RY=11.2,3.9     # mouth radii
WAIST_Y,WAIST=20.6,6.6   # where the loose cord sits, half width there
def span(y):
    xs=[x for x in range(W) if src.getpixel((x,int(y)))[3]>200]; return min(xs),max(xs)+1
L0,R0=span(KEEP); C0=(L0+R0)/2; HW0=(R0-L0)/2
CM=C0-0.6
def cx(y):
    if y>=WAIST_Y: t=(KEEP-y)/(KEEP-WAIST_Y); return C0+(CM-C0)*t*0.5
    t=(WAIST_Y-y)/(WAIST_Y-TOP); return C0+(CM-C0)*(0.5+0.5*t)
def hw(y):
    if y>=WAIST_Y:
        t=(KEEP-y)/(KEEP-WAIST_Y); return HW0+(WAIST-HW0)*(t**0.8)
    t=(WAIST_Y-y)/(WAIST_Y-TOP)
    return WAIST+(RX-WAIST)*(0.55*math.sin(t*math.pi/2)+0.45*t*t)        # cloth flares out, steepest at the lip
def toon(u):
    # the body's own shading: flat light left, quick ramp, flat shade right
    if u< -0.05: g=243
    elif u<0.3: g=243-(243-172)*((u+0.05)/0.35)
    else: g=172-12*((u-0.3)/0.7)
    if u>0.88: g-=35*(u-0.88)/0.12
    return g
def wave(a): return 1+0.10*math.sin(4*a+0.9)+0.05*math.sin(7*a+2.1)
KNOT=(28.6,23.6)
def rope(cg,t,x,y):
    # round cord: light on top, dark underneath, a strand twist
    cg=cg-30+34*max(0,-t)-46*max(0,t)
    if math.sin(x*2.3-y*2.3)>0.55: cg-=22
    return int(max(0,min(255,cg)))
def Cord(x,y):
    CR=1.25
    cu=(x-cx(WAIST_Y))/(hw(WAIST_Y)+1.1)
    if abs(cu)<=1:
        cyc=WAIST_Y+1.3*math.sqrt(max(0,1-cu*cu))   # the ring seen from a little above
        if abs(y-cyc)<=CR:
            return rope(toon(cu),(y-cyc)/CR,x,y)
    # the knot, slipped loose, and its two ends hanging down
    kx,ky=KNOT
    if math.hypot((x-kx)/1.5,(y-ky)/1.35)<=1:
        return rope(toon(0.25)+6,(y-ky)/1.35,x,y)
    for dx,ln,bend in ((-0.6,4.2,-0.35),(0.9,3.2,0.45)):
        if ky<y<=ky+ln:
            s=(y-ky)/ln
            ex=kx+dx+bend*s*s*2
            if abs(x-ex)<=0.62*(1-0.35*s):
                return rope(toon(0.25),(x-ex)/0.62*0.6,x,y)
    return None
big=Image.new('RGBA',(W*S,H*S))
px=big.load(); sp=src.load()
for Y in range(H*S):
    y=(Y+0.5)/S
    for X in range(W*S):
        x=(X+0.5)/S
        g=None
        if y>=KEEP+0.0:
            p=sp[int(x),int(y)]
            if p[3]>0: px[X,Y]=p
            cord=Cord(x,y)
            if cord is not None: px[X,Y]=(cord,cord,cord,255)
            continue
        # mouth ellipse
        ex=(x-CM)/RX; ey=(y-TOP)/RY
        a=math.atan2(ey,ex); r=math.hypot(ex,ey); wv=wave(a)
        inmouth=r<=wv
        insurf = y>=TOP and abs(x-cx(y))<=hw(y)
        if inmouth:
            inner=0.74*wv
            if r<=inner:
                # inside: the back wall, lit at the top, dark deep down
                d=(ey/ (RY*0+1) + 0.8)/1.6      # 0 at back, 1 at front
                d=max(0,min(1,(ey+inner)/(2*inner)))
                g=118-88*(d**0.6)
                if ex>0.15: g+=18*(1-d)          # the right wall's inside catches the light
            elif ey<0:
                # back rim: the cloth's inside edge
                g=196+26*max(0,ex)-14*max(0,-ex)
            else:
                g=toon(ex)+6                      # front rim: the lip, turned out a little
                if r<inner+0.07: g=min(252,g+16)  # its top edge catches the light
        elif insurf:
            u=(x-cx(y))/hw(y)
            g=toon(u)
            # under the turned-out lip: a little shadow
            ly=TOP+RY*wave(math.atan2(1,(x-CM)/RX))*math.sqrt(max(0,1-((x-CM)/RX)**2))
            if 0<=y-ly<1.1: g-=30*(1-(y-ly)/1.1)
            # the cloth is gathered where the cord was: folds strongest
            # near it, smoothing out toward the lip
            near=max(0,min(1,(y-TOP)/(WAIST_Y-TOP)))
            for f in (-0.62,-0.22,0.18,0.52,0.82):
                g-=34*max(0,1-abs(u-f)/0.12)*(0.25+0.75*near)
        c2=Cord(x,y)
        if c2 is not None: g=c2
        if g is not None:
            g=int(max(0,min(255,g))); px[X,Y]=(g,g,g,255)
big.save('big.png')
small=big.resize((W,H),Image.LANCZOS)
out=small
out.save('pouch_open.png')
k=8
pv=Image.new('RGBA',(W*k*2+20,H*k),(70,70,70,255))
pv.alpha_composite(src.resize((W*k,H*k),Image.NEAREST),(0,0)); pv.alpha_composite(out.resize((W*k,H*k),Image.NEAREST),(W*k+20,0)); pv.save('preview.png')
# as in game: tinted, ~38 px, on the panel's dark wood
tint=(150,160,215)
def ing(im):
    im=im.resize((31,38),Image.LANCZOS); r,g,b,a=im.split()
    return Image.merge('RGBA',(r.point(lambda v:v*tint[0]//255),g.point(lambda v:v*tint[1]//255),b.point(lambda v:v*tint[2]//255),a))
pv2=Image.new('RGBA',(120,60),(48,40,36,255)); pv2.alpha_composite(ing(src),(20,11)); pv2.alpha_composite(ing(out),(70,11)); pv2.resize((480,240),Image.NEAREST).save('ingame.png')
