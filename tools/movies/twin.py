"""The movie lettering in numpy: the prototype of jpdisc/MovieText.cs."""
import numpy as np
from scipy import ndimage as nd
W,H=320,240
CROSS=nd.generate_binary_structure(2,1)
def gray(a): return a.astype(float).mean(-1)
def tophat(a):
    g=gray(a); return g-nd.grey_opening(g,size=(3,5),mode='nearest')
def dil(m,n): return nd.binary_dilation(m,CROSS,iterations=n) if n>0 else m
def boxmean(v,k,r):
    """mean of v over k within a (2r+1)^2 window, and the count."""
    s=nd.uniform_filter(v*k,2*r+1,mode='constant'); c=nd.uniform_filter(k.astype(float),2*r+1,mode='constant')
    return np.where(c>1e-6, s/np.maximum(c,1e-6), 0), c
class Card:
    pass
def prepare(key, a, b, J, U, lo_pad=8):
    c=Card(); c.key=key; c.a=a; c.b=b; c.lo=max(1,a-lo_pad); c.hi=b+lo_pad
    span=b-a; m0,m1=a+span//4, b-span//4
    fs=list(range(m0,m1,max(1,(m1-m0)//9)))[:9]
    votes=np.zeros((H,W))
    for f in fs:
        j,u=J(f),U(f); d=np.abs(j.astype(float)-u).max(-1); gj,gu=gray(j),gray(u)
        tj=tophat(j); base=(d>20)&(gj>gu+12)&(tj>15); jt=base|((tj>25)&dil(base,3)); votes+=jt
    M0=votes>=len(fs)/2
    # the Japanese strokes under the US text, where the frames agree: the JP frame's own
    # thin bright strokes, high mid-card and not on the clean frames around it (KF3's way)
    th=np.median(np.stack([tophat(J(f)) for f in fs]),0)
    off=np.zeros((H,W))
    for f in (c.lo,c.lo+1,c.hi-1,c.hi):
        if J(f) is not None: off=np.maximum(off,tophat(J(f)))
    M0=M0|((th-off>25)&dil(M0,4))
    lab,n=nd.label(dil(M0,4),CROSS)
    if n==0: return None
    sizes=nd.sum(M0,lab,range(1,n+1)); keep=np.zeros(n+1,bool); keep[1:]=sizes>=0.08*sizes.max()
    M0=M0&keep[lab]
    # the block: the run of rows (gaps of up to 8 bridged) holding the most of it,
    # then its columns; whatever lies outside is stray and dropped
    def run(counts, gap):
        counts=np.where(counts>=max(3,0.1*counts.max()),counts,0)
        best=(0,0,-1); i=0; n=len(counts)
        while i<n:
            if counts[i]<3: i+=1; continue
            j=i; last=i; tot=0
            while j<n and j-last<=gap:
                if counts[j]>=3: last=j; tot+=counts[j]
                j+=1
            if tot>best[0]: best=(tot,i,last)
            i=last+1
        return best[1],best[2]
    y0,y1=run(M0.sum(1),8); x0,x1=run(M0[y0:y1+1].sum(0),24)
    keep=np.zeros_like(M0); keep[max(0,y0-4):y1+5,max(0,x0-4):x1+5]=True; M0=M0&keep
    c.M0=M0; c.M=dil(M0,1); c.y0,c.y1,c.x0,c.x1=y0,y1,x0,x1
    vals={}
    for f in range(c.lo,c.hi+1):
        j,u=J(f),U(f)
        if j is None or u is None: continue
        vals[f]=np.clip(gray(j)-gray(u),0,None)[M0].mean()
    edge=sorted([vals.get(f,0) for f in (c.lo,c.lo+1,c.lo+2,c.hi-2,c.hi-1,c.hi)])
    basev=np.median(edge); peak=np.median([vals[f] for f in fs])
    c.s={f:float(np.clip((v-basev)/max(1e-6,peak-basev),0,1)) for f,v in vals.items()}
    c.refLo=J(c.lo); c.refHi=J(c.hi); c.J=J
    # the US text, still for as long as it shows: where it is at its fullest, and how
    # much of it shows on each frame (it can come and go apart from the Japanese)
    cnt={}; ms={}
    for f in range(c.lo,c.hi+1):
        j,u=J(f),U(f)
        if j is None or u is None: continue
        d=np.abs(j.astype(float)-u).max(-1); m=(d>20)&(gray(u)>gray(j)+12)&(tophat(u)>15); ms[f]=m; cnt[f]=m.sum()
    mx=max(cnt.values()) if cnt else 0
    full=[f for f in cnt if mx>0 and cnt[f]>=0.7*mx]
    c.UTS=dil(np.mean([ms[f] for f in full],0)>=0.5,1) if full else np.zeros((H,W),bool)
    c.utc={f:(cnt[f]/mx if mx else 0) for f in cnt}
    return c
def temporal(img, maskAll, out, rest, ref, box):
    x0,x1,y0,y1=box
    sub=np.s_[y0:y1+1,x0:x1+1]
    known=~maskAll[sub]
    d=np.abs(img[sub].astype(float)-ref[sub]).mean(-1)
    num=nd.uniform_filter(d*known,9,mode='constant'); den=nd.uniform_filter(known.astype(float),9,mode='constant')
    ok=rest[sub]&(den>0.15)&(num/np.maximum(den,1e-6)<10)
    o=out[sub]; o[ok]=ref[sub][ok]; r=rest[sub]; r[ok]=False
def diffuse(out, rest, box, rounds=60):
    x0,x1,y0,y1=box; sub=np.s_[y0:y1+1,x0:x1+1]
    a=out[sub].astype(float); w=(~rest[sub]).astype(float); a*=w[...,None]
    k=np.ones((3,3)); k[1,1]=0
    m=rest[sub]
    for _ in range(rounds):
        sa=np.stack([nd.convolve(a[...,ch]*1.0,k,mode='nearest') for ch in range(3)],-1); sw=nd.convolve(w,k,mode='nearest')
        upd=m&(sw>0)
        a[upd]=sa[upd]/sw[upd,None]*1.0
        # store as weighted (weight 1 once filled)
        w=np.where(upd,1.0,w)
        a=a*1.0
    o=out[sub]; o[m]=np.clip(a[m],0,255)
def clean(c, f, j, u):
    """the frame with the Japanese taken out (float RGB)."""
    j=j.astype(float); u=u.astype(float)
    d=np.abs(j-u).max(-1); gj,gu=gray(j),gray(u)
    tu=tophat(u); ub=(d>10)&(gu>gj+6)&(tu>10)
    UT=dil(ub|((tu>30)&dil(ub,2)),1)
    if c.utc.get(f,0)>0.03: UT=UT|c.UTS
    MM=c.M
    known=~MM&~UT&(d<40)
    off=np.stack([boxmean(j[...,ch]-u[...,ch],known,7)[0] for ch in range(3)],-1)
    out=j.copy()
    use=MM&~UT
    out[use]=np.clip(u[use]+off[use],0,255)
    rest=MM&UT
    box=(max(0,c.x0-6),min(W-1,c.x1+6),max(0,c.y0-6),min(H-1,c.y1+6))
    allm=MM|UT
    # from the clean JP frames just outside the card, nearest first, while the scene
    # around the pixel still matches
    for g in sorted([g for g in list(range(c.a-12,c.a))+list(range(c.b+1,c.b+13)) if abs(g-f)<=40], key=lambda g: abs(g-f)):
        if not rest.any(): break
        r=c.J(g)
        if r is not None: temporal(j,allm,out,rest,r.astype(float),box)
    diffuse(out,rest,box)
    return out, UT, rest
def compose(img, ink, x0, y0, st, level=205):
    h,w=ink.shape
    for p in (0,1):
        ox,oy=x0+(1-p), y0+(1-p)
        y1=min(H,oy+h); x1=min(W,ox+w)
        a=ink[:y1-oy,:x1-ox]*st
        reg=img[oy:y1,ox:x1]
        if p==0: reg*= (1-a*0.6)[...,None]
        else: reg[:]= reg*(1-a)[...,None]+level*a[...,None]
    return img
