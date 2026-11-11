"""PlayStation MDEC v2/v3 frames in Python: the prototype of jpdisc/Mdec.cs, checked
against ffmpeg (51 dB decoding the disc frames; 50 dB for a frame encoded here)."""
"""PSX MDEC v2/v3 frame decode/encode (prototype for Mdec.cs)."""
import numpy as np, struct
W,H=320,240
Q=[2,16,19,22,26,27,29,34,16,16,22,24,27,29,34,37,19,22,26,27,29,34,34,38,22,22,26,27,29,34,37,40,
   22,26,27,29,32,35,40,48,26,27,29,32,35,40,48,58,26,27,29,34,38,46,56,69,27,29,35,38,46,56,69,83]
ZZ=[0,1,8,16,9,2,3,10,17,24,32,25,18,11,4,5,12,19,26,33,40,48,41,34,27,20,13,6,7,14,21,28,
    35,42,49,56,57,50,43,36,29,22,15,23,30,37,44,51,58,59,52,45,38,31,39,46,53,60,61,54,47,55,62,63]
TABLE=("10 -1 0|11 0 1|011 1 1|0100 0 2|0101 2 1|00101 0 3|00111 3 1|00110 4 1|000110 1 2|000111 5 1|000101 6 1|000100 7 1|"
"0000110 0 4|0000100 2 2|0000111 8 1|0000101 9 1|000001 -2 0|00100110 0 5|00100001 0 6|00100101 1 3|00100100 3 2|00100111 10 1|"
"00100011 11 1|00100010 12 1|00100000 13 1|0000001010 0 7|0000001100 1 4|0000001011 2 3|0000001111 4 2|0000001001 5 2|"
"0000001110 14 1|0000001101 15 1|0000001000 16 1|000000011101 0 8|000000011000 0 9|000000010011 0 10|000000010000 0 11|"
"000000011011 1 5|000000010100 2 4|000000011100 3 3|000000010010 4 3|000000011110 6 2|000000010101 7 2|000000010001 8 2|"
"000000011111 17 1|000000011010 18 1|000000011001 19 1|000000010111 20 1|000000010110 21 1|0000000011010 0 12|0000000011001 0 13|"
"0000000011000 0 14|0000000010111 0 15|0000000010110 1 6|0000000010101 1 7|0000000010100 2 5|0000000010011 3 4|0000000010010 5 3|"
"0000000010001 9 2|0000000010000 10 2|0000000011111 22 1|0000000011110 23 1|0000000011101 24 1|0000000011100 25 1|0000000011011 26 1|"
"00000000011111 0 16|00000000011110 0 17|00000000011101 0 18|00000000011100 0 19|00000000011011 0 20|00000000011010 0 21|"
"00000000011001 0 22|00000000011000 0 23|00000000010111 0 24|00000000010110 0 25|00000000010101 0 26|00000000010100 0 27|"
"00000000010011 0 28|00000000010010 0 29|00000000010001 0 30|00000000010000 0 31|000000000011000 0 32|000000000010111 0 33|"
"000000000010110 0 34|000000000010101 0 35|000000000010100 0 36|000000000010011 0 37|000000000010010 0 38|000000000010001 0 39|"
"000000000010000 0 40|000000000011111 1 8|000000000011110 1 9|000000000011101 1 10|000000000011100 1 11|000000000011011 1 12|"
"000000000011010 1 13|000000000011001 1 14|0000000000010011 1 15|0000000000010010 1 16|0000000000010001 1 17|0000000000010000 1 18|"
"0000000000010100 6 3|0000000000011010 11 2|0000000000011001 12 2|0000000000011000 13 2|0000000000010111 14 2|0000000000010110 15 2|"
"0000000000010101 16 2|0000000000011111 27 1|0000000000011110 28 1|0000000000011101 29 1|0000000000011100 30 1|0000000000011011 31 1")
LOOK={}; CODES={}
for e in TABLE.split('|'):
    c,r,l=e.split(); LOOK[c]=(int(r),int(l))
    if int(r)>=0: CODES[(int(r),int(l))]=c
DCL=['100','00','01','101','110','1110','11110','111110','1111110']
DCC=['00','01','10','110','1110','11110','111110','1111110','11111110']
DCLD={c:i for i,c in enumerate(DCL)}; DCCD={c:i for i,c in enumerate(DCC)}
C=np.array([[np.sqrt((1 if u==0 else 2)/8)*np.cos((2*x+1)*u*np.pi/16) for x in range(8)] for u in range(8)])
class R:
    def __init__(s,d,start):
        n=len(d)//2; w=struct.unpack('<%dH'%n, d[:2*n]); s.bits=''.join(format(x,'016b') for x in w); s.p=(start)*8
    def read(s,n): v=int(s.bits[s.p:s.p+n] or '0',2) if n else 0; s.p+=n; return v
    def code(s,table,maxlen=17):
        for L in range(1,maxlen+1):
            c=s.bits[s.p:s.p+L]
            if c in table: s.p+=L; return table[c]
        raise ValueError('bad code at %d'%s.p)
def sx(v,b): return v-(1<<b) if v>=1<<(b-1) else v
def decode(data, init=0, dcmul=4, dcoff=0, verbose=False):
    qs=data[4]|data[5]<<8; ver=data[6]|data[7]<<8
    r=R(data,8)
    y=np.zeros((H,W)); cb=np.zeros((H//2,W//2)); cr=np.zeros((H//2,W//2))
    last=[init,init,init]
    for mx in range(W//16):
        for my in range(H//16):
            for b in range(6):
                co=np.zeros(64)
                if ver==2: co[0]=sx(r.read(10),10)*Q[0]
                else:
                    comp = 2 if b==0 else (1 if b==1 else 0)
                    size = r.code(DCLD if comp==0 else DCCD, 8)
                    diff=0
                    if size:
                        v=r.read(size); diff = v if v>>(size-1) else v-(1<<size)+1
                    last[comp]+=diff
                    co[0]=(last[comp]*dcmul+dcoff)*Q[0]
                i=0
                while True:
                    run,lev=r.code(LOOK)
                    if run==-1: break
                    if run==-2: run=r.read(6); lev=sx(r.read(10),10)
                    else:
                        if r.read(1): lev=-lev
                    i+=run+1
                    if i>63: raise ValueError('run past block')
                    co[ZZ[i]]=lev*Q[ZZ[i]]*qs/8.0
                blk=(C.T@co.reshape(8,8)@C)   # rows=y
                if b==0: cr[my*8:my*8+8,mx*8:mx*8+8]=blk
                elif b==1: cb[my*8:my*8+8,mx*8:mx*8+8]=blk
                else:
                    k=b-2; y[my*16+(k//2)*8:my*16+(k//2)*8+8, mx*16+(k%2)*8:mx*16+(k%2)*8+8]=blk
    end=r.p
    tail=r.bits[end:end+40]
    Y=y+128; Cb=np.repeat(np.repeat(cb,2,0),2,1); Cr=np.repeat(np.repeat(cr,2,0),2,1)
    rgb=np.stack([Y+1.402*Cr, Y-0.3437*Cb-0.7143*Cr, Y+1.772*Cb],-1)
    return np.clip(np.round(rgb),0,255).astype(np.uint8), qs, ver, (end, tail, len(r.bits))

def count_codes(data):
    """number of MDEC halfwords (DC + each AC + EOB per block)."""
    ver=data[6]|data[7]<<8; r=R(data,8); n=0; last=[0,0,0]
    for mb in range(W//16*H//16):
        for b in range(6):
            if ver==2: r.read(10)
            else:
                comp = 2 if b==0 else (1 if b==1 else 0)
                size=r.code(DCLD if comp==0 else DCCD,8); r.read(size)
            n+=1
            while True:
                run,lev=r.code(LOOK)
                n+=1
                if run==-1: break
                if run==-2: r.read(16)
                else: r.read(1)
    return n, r.bits[r.p:r.p+12]

class Wr:
    def __init__(s): s.b=[]
    def put(s,v,n):
        if n: s.b.append(format(v & ((1<<n)-1),'0%db'%n))
    def bytes(s, head0, qs, ver):
        bits=''.join(s.b); bits+='0'*((-len(bits))%32); bits+='0'*32
        ws=[int(bits[i:i+16],2) for i in range(0,len(bits),16)]
        return struct.pack('<4H',head0,0x3800,qs,ver)+struct.pack('<%dH'%len(ws),*ws)
def encode(rgb, qs, ver=3):
    rgb=rgb.astype(float); r,g,b=rgb[...,0],rgb[...,1],rgb[...,2]
    Y=0.299*r+0.587*g+0.114*b
    cb=((b-Y)/1.772).reshape(H//2,2,W//2,2).mean((1,3)); cr=((r-Y)/1.402).reshape(H//2,2,W//2,2).mean((1,3)); y=Y-128
    w=Wr(); codes=0; last=[0,0,0]
    for mx in range(W//16):
        for my in range(H//16):
            blocks=[(2,cr[my*8:my*8+8,mx*8:mx*8+8]),(1,cb[my*8:my*8+8,mx*8:mx*8+8])]
            for k in range(4): blocks.append((0,y[my*16+(k//2)*8:my*16+(k//2)*8+8, mx*16+(k%2)*8:mx*16+(k%2)*8+8]))
            for comp,px in blocks:
                F=C@px@C.T   # F[u,v]: u vertical
                Ff=F.reshape(64)
                if ver==2:
                    w.put(int(np.clip(round(Ff[0]/2),-512,511)),10)
                else:
                    m=int(np.clip(round(Ff[0]/8),-128,127)); diff=m-last[comp]; last[comp]=m
                    size=0 if diff==0 else int(abs(diff)).bit_length()
                    w.b.append((DCL if comp==0 else DCC)[size])
                    if size: w.put(diff if diff>0 else diff+(1<<size)-1, size)
                run=0; n=1
                for i in range(1,64):
                    l=int(round(Ff[ZZ[i]]*8/(Q[ZZ[i]]*qs)))
                    if l==0: run+=1; continue
                    a=abs(l)
                    if (run,a) in CODES: w.b.append(CODES[(run,a)]); w.put(1 if l<0 else 0,1)
                    else: l=max(-512,min(511,l)); w.b.append('000001'); w.put(run,6); w.put(l,10)
                    run=0; n+=1
                w.b.append('10'); codes+=n+1
    w.put(0x3FF if ver==3 else 0x1FF,10)
    return w.bytes(((codes+1)//2+31)&~31, qs, ver)
