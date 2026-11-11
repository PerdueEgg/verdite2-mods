"""A plain ISO 9660 reader for raw 2352-byte-sector .bin images."""
import struct, sys
class Disc:
    def __init__(s, path): s.f = open(path, 'rb')
    def raw(s, lba): s.f.seek(lba*2352); return s.f.read(2352)
    def data(s, lba): return s.raw(lba)[24:24+2048]
    def view(s, lba): return s.raw(lba)[16:16+2336]
    def files(s):
        pvd = s.data(16); root = pvd[156:156+34]
        out = {}
        def walk(lba, size, pre):
            buf = b''.join(s.data(lba+i) for i in range((size+2047)//2048))
            p = 0
            while p < len(buf):
                l = buf[p]
                if l == 0: p = (p//2048+1)*2048; continue
                rec = buf[p:p+l]; el = struct.unpack_from('<I', rec, 2)[0]; sz = struct.unpack_from('<I', rec, 10)[0]
                nl = rec[32]; name = rec[33:33+nl]; fl = rec[25]
                if name not in (b'\0', b'\1'):
                    n = name.decode('ascii', 'replace').split(';')[0]
                    if fl & 2: walk(el, sz, pre+n+'/')
                    else: out[pre+n] = (el, sz)
                p += l
        el = struct.unpack_from('<I', root, 2)[0]; sz = struct.unpack_from('<I', root, 10)[0]
        walk(el, sz, '')
        return out
    def read(s, name):
        l, n = s.files()[name]; return b''.join(s.data(l+i) for i in range((n+2047)//2048))[:n]
if __name__ == '__main__':
    d = Disc(sys.argv[1])
    for k, (l, n) in sorted(d.files().items(), key=lambda x: x[1][0]): print(f'{l:7d} {n:10d} {k}')
