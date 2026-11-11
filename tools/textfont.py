"""The Text mod's picture fonts, read from its character maps (mods/text/fonts),
for estimating how many lines a text takes: proportional spacing as the
*_spacing.json files set it, without kerning."""
import json, os, numpy as np
from PIL import Image
class Font:
    def __init__(s, name, d=None):
        d = d or os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'mods', 'text', 'fonts')
        j=json.load(open(f'{d}/{name}.json')); sp=json.load(open(f'{d}/{name}_spacing.json'))
        im=np.array(Image.open(f'{d}/{name}.png').convert('L')).astype(float)
        s.cw, s.ch = j['cellWidth'], j['cellHeight']; s.gap=sp['gap']; s.space=sp['space']; s.linegap=sp.get('lineGap',0)
        s.g={}
        for L in j['letters']:
            g=np.clip(im[L['y']:L['y']+L['h'], L['x']:L['x']+L['w']]/232.0,0,1); g[g<0.05]=0
            cols=np.nonzero(g.max(0)>0)[0]
            s.g[L['char']]=(g, (cols[0] if len(cols) else 0), (cols[-1]-cols[0]+1 if len(cols) else 0))
    def place(s, line):
        at=[]; pen=0
        for c in line:
            at.append(pen)
            if c==' ' or c not in s.g: pen+=s.space
            else: pen+=s.g[c][2]+s.gap
        return at, max(0,pen-s.gap)
    def measure(s,line): return s.place(line)[1]
    def wrap(s,text,px):
        lines=[]
        for para in text.split('\n'):
            line=''
            for w in para.split():
                if not line: line=w
                elif s.measure(line+' '+w)<=px: line+=' '+w
                else: lines.append(line); line=w
            lines.append(line)
        return lines
    def coverage(s,text,width,pitch_extra=0):
        lines=s.wrap(text,width); pitch=s.ch+s.linegap+pitch_extra
        w=max([s.measure(l)+2 for l in lines]+[1]); h=len(lines)*pitch+2
        ink=np.zeros((h,w))
        for li,l in enumerate(lines):
            at,_=s.place(l)
            for c,x in zip(l,at):
                if c==' ' or c not in s.g: continue
                g,left,_=s.g[c]; y=li*pitch
                hh,ww=g.shape
                for q in range(ww):
                    xx=x-left+q
                    if 0<=xx<w: ink[y:y+hh,xx]=np.maximum(ink[y:y+hh,xx], g[:min(hh,h-y),q])
        return ink, lines
