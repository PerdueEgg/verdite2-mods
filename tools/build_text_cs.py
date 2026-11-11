#!/usr/bin/env python3
"""Rewrite the translation tables in retranslation/Text.cs from the workbook's
"Mod EN" column: Messages, Strings (Menus & Names), Hud (Messages in play),
Coded (the code-built labels), Encyclopedia, EncyclopediaCards and Movies.

    python3 tools/build_text_cs.py mods/retranslation/KF2_retranslation.ods mods/retranslation/Text.cs

Standard library only (no odfpy). Uses the same rules as the mod's own
"Import from workbook" button (retranslation/Workbook.cs), so the two give the
same text:

  - A blank Mod EN cell keeps the text already in Text.cs.
  - Curly quotes become straight ones; the game's fonts have no curly quotes.
  - Menus & Names rows are matched by US address, and only slots already in
    Text.cs are written (an unknown address is not a known 24-byte slot).
    Rows over 23 characters are skipped.
  - Code rows are matched by their Current US text and must fit 11 letters.
    Labels in Text.cs that the workbook doesn't list (RETURN, QUIT) are kept.
  - Encyclopedia: rows whose Mod EN differs from the Encyclopedia's own name.
    The first row wins when two rows share a name (Places before Area name).
  - The Encyclopedia's own place cards take their text from the place's row,
    or from the sign it copies (I143 for Finished Unit Storage).

"""
import re
import sys
import zipfile
import xml.etree.ElementTree as ET

T = '{urn:oasis:names:tc:opendocument:xmlns:table:1.0}'
X = '{urn:oasis:names:tc:opendocument:xmlns:text:1.0}'
O = '{urn:oasis:names:tc:opendocument:xmlns:office:1.0}'

MENU_MAX = 23
CODED_MAX = 11
SKIP_SLOTS = {0x80065764}       # ACCESS MEMORYCARD: the JP text's meaning is unclear, US kept
CARDS = [                        # the Encyclopedia's own cards: (its key, where the text comes from)
    ("Kraken's Nest", 'place', "Kraken's Nest"),
    ('Royal Treasure House', 'place', 'Royal Treasure House'),
    ('The Space For Completion', 'message', 'I143'),
    ("Guyra's Dimension", 'place', "Guyra's Dimension"),
]
CHARSET = set("!\"#%&'()*+,-./0123456789:;=?ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz\u2190\u2192 \n")


def cell_text(cell):
    """A cell's text as LibreOffice shows it: paragraphs joined by line breaks,
    <text:s/> as spaces, comments left out."""
    def walk(e):
        out = []
        if e.text: out.append(e.text)
        for c in e:
            if c.tag == X + 's': out.append(' ' * int(c.get(X + 'c', '1')))
            elif c.tag == X + 'tab': out.append('\t')
            elif c.tag == X + 'line-break': out.append('\n')
            elif c.tag == O + 'annotation': pass
            else: out.append(walk(c))
            if c.tail: out.append(c.tail)
        return ''.join(out)
    return '\n'.join(walk(p) for p in cell if p.tag == X + 'p')


def read_sheets(path):
    with zipfile.ZipFile(path) as z:
        root = ET.fromstring(z.read('content.xml'))
    sheets = {}
    for t in root.iter(T + 'table'):
        rows = []
        for tr in t.iter(T + 'table-row'):
            vals = []
            for tc in tr:
                if tc.tag not in (T + 'table-cell', T + 'covered-table-cell'): continue
                rep = int(tc.get(T + 'number-columns-repeated', '1'))
                v = cell_text(tc)
                vals += [v] * min(rep, max(0, 64 - len(vals)))
            while vals and not vals[-1]: vals.pop()
            if vals: rows.append(vals)
        sheets[t.get(T + 'name')] = rows
    return sheets


def table(sheets, name):
    """The sheet's rows as dicts by the header row's names."""
    if name not in sheets: raise SystemExit(f'the workbook has no "{name}" sheet')
    rows = sheets[name]
    head = [h.strip() for h in rows[0]]
    return [{h: (r[i] if i < len(r) else '') for i, h in enumerate(head) if h} for r in rows[1:]]


def clean(s):
    s = s.replace('\r', '').replace('\u2018', "'").replace('\u2019', "'").replace('\u201c', '"').replace('\u201d', '"')
    return '\n'.join(line.rstrip() for line in s.strip().split('\n'))


def cs(s): return '"' + s.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n') + '"'


def unescape(s):
    return re.sub(r'\\(.)', lambda m: {'n': '\n', '\\': '\\', '"': '"'}[m.group(1)], s)


def main(ods, out):
    src = open(out, encoding='utf-8').read()
    sheets = read_sheets(ods)
    warn = []

    def bad_chars(where, text):
        bad = sorted({c for c in text if c not in CHARSET})
        if bad: warn.append(f'{where}: no glyph for {" ".join(bad)}')

    # --- current tables, to keep what the workbook doesn't change ---
    def dict_block(name):
        m = re.search(r'(public static readonly Dictionary<string, string> ' + name + r' = new\(\)\n    \{\n)(.*?)(\n    \};)', src, re.S)
        return m, [(unescape(k), unescape(v)) for k, v in re.findall(r'\["((?:[^"\\]|\\.)*)"\] = "((?:[^"\\]|\\.)*)"', m.group(2))]

    def replace(m, body):
        nonlocal src
        src = src[:m.start(2)] + body + src[m.end(2):]

    # Messages
    m, old = dict_block('Messages')
    messages = dict(old)
    for r in table(sheets, 'Messages'):
        k, v = r.get('Key', '').strip(), clean(r.get('Mod EN', ''))
        if not k: continue
        if k not in messages: warn.append(f'Messages {k}: not a disc entry the mod knows; skipped'); continue
        if not v: warn.append(f'Messages {k}: Mod EN is blank; kept the old text'); continue
        bad_chars(f'Messages {k}', v)
        messages[k] = v
    replace(m, '\n'.join(f'        [{cs(k)}] = {cs(v)},' for k, v in messages.items()))

    # Strings
    m = re.search(r'(public static readonly \(uint Address, string Text\)\[\] Strings =\n    \[\n)(.*?)(\n    \];)', src, re.S)
    old = [(int(a, 16), unescape(t), c) for a, t, c in re.findall(r'\(0x([0-9A-Fa-f]+), "((?:[^"\\]|\\.)*)"\),\s*//\s*(\w+)', m.group(2))]
    strings = {a: [t, c] for a, t, c in old}
    names = table(sheets, 'Menus & Names')
    for r in names:
        if r.get('Kind', '').strip() == 'Code': continue
        try: a = int(r.get('US address', '').strip(), 16)
        except ValueError: continue
        v = clean(r.get('Mod EN', ''))
        if a in SKIP_SLOTS: continue
        if a not in strings: warn.append(f'Menus & Names {a:#x}: not a slot the mod knows; skipped'); continue
        if not v: warn.append(f'Menus & Names {a:#x}: Mod EN is blank; kept the old text'); continue
        if len(v) > MENU_MAX: warn.append(f'Menus & Names {a:#x}: "{v}" is over {MENU_MAX} characters; kept the old text'); continue
        strings[a][0] = v
    replace(m, '\n'.join(f'        (0x{a:08X}, {cs(t)}),   // {c}' for a, (t, c) in strings.items()))

    # Coded
    m, old = dict_block('Coded')
    coded = dict(old)
    for r in names:
        if r.get('Kind', '').strip() != 'Code': continue
        us, v = r.get('Current US', '').strip(), clean(r.get('Mod EN', ''))
        if not us or not v or v == us: continue
        if len(v) > CODED_MAX: warn.append(f'Code label {us}: "{v}" is over {CODED_MAX} letters; skipped'); continue
        coded[us] = v
    replace(m, '\n'.join(f'        [{cs(k)}] = {cs(v)},' for k, v in coded.items()))

    # Hud
    m = re.search(r'(public static readonly string\[\] Hud =\n    \[\n)(.*?)(\n    \];)', src, re.S)
    hud = [[unescape(t), us] for t, us in re.findall(r'^\s*"((?:[^"\\]|\\.)*)",\s*//\s*H\d+\s+(.*)$', m.group(2), re.M)]
    for r in table(sheets, 'Messages in play'):
        k, v = r.get('Key', '').strip(), clean(r.get('Mod EN', ''))
        if not re.fullmatch(r'H\d+', k): continue
        i = int(k[1:])
        if i >= len(hud): warn.append(f'Messages in play {k}: the page has {len(hud)} messages; skipped'); continue
        if not v: warn.append(f'Messages in play {k}: Mod EN is blank; kept the old text'); continue
        hud[i][0] = v
    replace(m, '\n'.join(f'        {cs(t) + ",":<28}// {"H" + str(i):<4}{us}' for i, (t, us) in enumerate(hud)))

    # Encyclopedia
    m, old = dict_block('Encyclopedia')
    enc, seen = {}, {}
    rows = table(sheets, 'Encyclopedia')
    for r in rows:
        name, v = r.get('Mod name (current)', '').strip(), clean(r.get('Mod EN', ''))
        if not name or not v: continue
        if name in seen:
            if seen[name] != v:
                warn.append(f'Encyclopedia "{name}": two rows give "{seen[name]}" and "{v}", but the Encyclopedia '
                            f'uses one name for both; using "{seen[name]}"')
            continue
        seen[name] = v
        if v != name: enc[name] = v
    replace(m, '\n'.join(f'        [{cs(k)}] = {cs(v)},' for k, v in enc.items()))

    # Encyclopedia cards
    m, old = dict_block('EncyclopediaCards')
    cards = dict(old)
    for key, kind, ref in CARDS:
        v = seen.get(ref) if kind == 'place' else messages.get(ref)
        if v: cards[key] = v
    replace(m, '\n'.join(f'        [{cs(k)}] = {cs(v)},' for k, v in cards.items()))

    # Movies
    m, old = dict_block('Movies')
    movies = dict(old)
    for r in table(sheets, 'Movies'):
        k, v = r.get('Key', '').strip(), clean(r.get('Mod EN', ''))
        if k and v:
            bad_chars(f'Movies {k}', v)
            movies[k] = v
    replace(m, '\n'.join(f'        [{cs(k)}] = {cs(v)},' for k, v in movies.items()))

    open(out, 'w', encoding='utf-8', newline='\n').write(src)
    print(f'wrote {out}: {len(messages)} messages, {len(strings)} menu strings, {len(coded)} code labels, '
          f'{len(hud)} messages in play, {len(enc)} encyclopedia names, {len(cards)} cards, {len(movies)} movie cards')
    for w in warn: print('  note:', w)


if __name__ == '__main__':
    if len(sys.argv) != 3: raise SystemExit(__doc__)
    main(sys.argv[1], sys.argv[2])
