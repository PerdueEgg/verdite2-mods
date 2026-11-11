#!/usr/bin/env python3
"""Preview the Japanese Disc mod's movie lettering offline: the same steps as
jpdisc/MovieText.cs, in numpy, on frames ffmpeg decodes from the two disc images.

    python3 preview.py KF2_retranslation.ods JP.bin US.bin out_dir [card ...]

The cards, their frames and their English come from the workbook's Movies sheet.
JP.bin / US.bin: the raw 2352-byte-sector .bin of each disc. Writes, for each card,
card_<key>.png: three frames (mid-card, fade-in, fade-out), each as Japanese |
Japanese taken out | English in. Needs numpy, scipy, Pillow and ffmpeg on the PATH.
"""
import os, sys, subprocess, tempfile, numpy as np
from PIL import Image
here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, here); sys.path.insert(0, os.path.join(here, '..'))
import disc, twin, textfont
from build_text_cs import read_sheets, table, clean

TWINS = {'OP': ('OP/OP.S', 'OP/OP2.S'), 'ED': ('OP/ED.S', 'OP/ED0.S')}

def frames(binpath, name, tmp):
    d = disc.Disc(binpath); l, n = d.files()[name]
    raw = os.path.join(tmp, name.replace('/', '_'))
    with open(raw, 'wb') as o:
        for i in range((n + 2047) // 2048): o.write(d.raw(l + i))
    rgb = raw + '.rgb'
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-f', 'psxstr', '-i', raw, '-f', 'rawvideo', '-pix_fmt', 'rgb24', rgb], check=False)
    a = np.memmap(rgb, np.uint8, 'r').reshape(-1, 240, 320, 3)
    return lambda f: np.array(a[f - 1]) if 1 <= f <= len(a) else None   # STR frames count from 1

def cards(ods):
    """(key, movie, first frame, last frame, English) for each card the workbook letters."""
    for r in table(read_sheets(ods), 'Movies'):
        key, mv, en = r.get('Key', '').strip(), r.get('Movie', '').strip(), clean(r.get('Mod EN', ''))
        first, _, last = r.get('Frames', '').partition('-')
        if key and mv in TWINS and en and first.isdigit() and last.isdigit():
            yield key, mv, int(first), int(last), en

def main(ods, jp, us, out, want):
    os.makedirs(out, exist_ok=True)
    fnt = textfont.Font('dialogue')
    with tempfile.TemporaryDirectory() as tmp:
        cache = {}
        for key, mv, a, b, en in cards(ods):
            if want and key not in want: continue
            if mv not in cache: cache[mv] = (frames(jp, TWINS[mv][0], tmp), frames(us, TWINS[mv][1], tmp))
            J, U = cache[mv]
            c = twin.prepare(key, a, b, J, U)
            width = min(max(c.x1 - c.x0, 240), 320 - c.x0 - 12)
            ink, lines = fnt.coverage(en, width)
            y0 = int(np.clip((c.y0 + c.y1) // 2 - ink.shape[0] // 2, 10, 230 - ink.shape[0]))
            tiles = []
            for f in ((a + b) // 2, a + 2, b - 3):
                j, u = J(f), U(f)
                out_img, _, _ = twin.clean(c, f, j, u)
                cleaned = np.clip(out_img, 0, 255).astype(np.uint8)
                twin.compose(out_img, ink, c.x0, y0, c.s.get(f, 0))
                tiles.append(np.concatenate([j, cleaned, np.clip(out_img, 0, 255).astype(np.uint8)], 1))
            Image.fromarray(np.concatenate(tiles, 0)).save(os.path.join(out, f'card_{key}.png'))
            print(key, f'block x {c.x0}-{c.x1} y {c.y0}-{c.y1}, {len(lines)} lines')

if __name__ == '__main__':
    if len(sys.argv) < 5: raise SystemExit(__doc__)
    main(sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4], set(sys.argv[5:]))
