import math, itertools
def mf(x,B,C,E):
    bx=B*x
    return math.sin(C*math.atan(bx-E*(bx-math.atan(bx))))
def peak(B,C,E,xmax):
    best=(0,0)
    n=4000
    for i in range(1,n):
        x=xmax*i/n; v=mf(x,B,C,E)
        if v>best[1]: best=(x,v)
    return best
def fit(peak_x, ratio_at, at_mult, C):
    # find B,E: peak at peak_x, value(at_mult*peak_x)/peakval == ratio_at
    best=None
    for E in [e/100 for e in range(-200,100,2)]:
        # B s.t. peak at peak_x: search
        lo,hi=0.5,200
        for _ in range(60):
            B=(lo+hi)/2
            px,_=peak(B,C,E,peak_x*6)
            if px>peak_x: lo=B
            else: hi=B
        px,pv=peak(B,C,E,peak_x*6)
        r=mf(peak_x*at_mult,B,C,E)/pv
        err=abs(r-ratio_at)
        if best is None or err<best[0]: best=(err,B,C,E,px,pv,r)
    return best
lat = {  # peak deg, ratio at 3x peak
 'Vintage':(12,0.86),'Touring':(10,0.84),'UHP':(8.5,0.82),'Sport':(8,0.80),'Semi':(7,0.77),'Slick':(6,0.72),'Drift':(11,0.90)}
lon = {'Vintage':(0.14,0.80),'Touring':(0.12,0.78),'UHP':(0.10,0.76),'Sport':(0.10,0.75),'Semi':(0.09,0.73),'Slick':(0.08,0.70),'Drift':(0.13,0.82)}
for k in lat:
    pd,ra=lat[k]
    for C in (1.3,1.5,1.7,1.9):
        e,B,C_,E,px,pv,r=fit(math.radians(pd),ra,3,C)
        if e<0.01: break
    e2,Bx,Cx,Ex,pxx,pvx,rx=None,0,0,0,0,0,0
    kp,kr=lon[k]
    for Cx_ in (1.5,1.65,1.8,1.9):
        e2,Bx,Cx,Ex,pxx,pvx,rx=fit(kp,kr,4,Cx_)
        if e2<0.01: break
    print(f"{k}: lat B={B:.3f} C={C_} E={E:.2f} peak={math.degrees(px):.2f} pv={pv:.3f} r3={r:.3f} err={e:.3f} | lon B={Bx:.3f} C={Cx} E={Ex:.2f} peak={pxx:.3f} r4={rx:.3f} err={e2:.3f}")
