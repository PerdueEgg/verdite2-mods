#!/usr/bin/env python3
"""Write the Text mod's US text (text/UsText.cs) from the translation workbook's
"Current US" column, so the Text mod can letter the unmodded game in its fonts.

    python3 tools/build_us_text.py mods/retranslation/KF2_retranslation.ods mods/text/UsText.cs

Standard library only. Three tables come out:

  - Pictures: every sign, dialogue line, description and place card (Messages
    sheet), as the US disc has it, line breaks and all. Their capitals are kept:
    the US pictures are already in upper and lower case, and the few in capitals
    (DANGEROUS!! NO EXIT) are meant to be.
  - Menu: the menu-font text (Menus & Names, Code rows included), keyed by its
    US capitals, with its normal capitalisation: title case for menus, labels and
    names, sentence case for the memory-card messages.
  - Hud: the 36 short messages in play (Messages in play), in sentence case.

The capitalisation is worked out here, by the rules below; a string the rules
get wrong goes in OVERRIDES.
"""
import re
import sys

sys.path.insert(0, __file__.rsplit('/', 1)[0] if '/' in __file__ else '.')
from build_text_cs import read_sheets, table, cs, CHARSET  # noqa: E402

# Words kept in capitals wherever they are.
ACRONYMS = {'HP', 'MP', 'HP/MP', 'RAM', 'CD', 'STR', 'MAG'}
# Names, capitalised even in sentence case.
PROPER = {'SEATH', "SEATH'S", 'VERDITE', "TEO'S", 'TEO', "HARRIS'S", 'HARRIS', "HARVINE'S", 'HARVINE',
          "NECRON'S", 'NECRON', 'PSYCPROS'}
# Small words left in lower case inside a title.
SMALL = {'A', 'AN', 'THE', 'OF', 'FOR', 'AND', 'IN', 'ON', 'TO', 'AT', 'BY', 'OR'}

# Menu-font labels in sentence case: the memory-card messages. The ones that carry
# on a message from the line above start in lower case.
SENTENCE = range(0x800658B4, 0x80065A95)   # CHECKING THE RAM CARD .. PLEASE INSERT A NEW CARD
SENTENCE_ALSO = {0x80065764}        # ACCESS MEMORYCARD
CONTINUES = {'CARD IS NOT FORMATTED', 'IN THIS CARD', 'AND TURN OFF THE POWER', 'WITHOUT THE RAM CARD'}

# Whole strings the rules would get wrong.
OVERRIDES = {
    'GOLD@hud': 'gold',                    # shown after the amount: "36 gold"
    'DRAGON GRASS@hud': 'Dragon Grass',    # the plant's name
    'THE PILLAR OF WIND@hud': 'The Pillar of Wind',
}


def word_case(w, first, title):
    """One word (it may hold hyphens or a slash) in its case."""
    if w in ACRONYMS: return w
    if any(ch.isdigit() for ch in w): return w
    if w in PROPER or title and not (w in SMALL and not first):
        return '-'.join(p[:1] + p[1:].lower() for p in w.split('-'))
    return w.lower() if not first else w[:1] + w[1:].lower()


def recase(text, title, lower_start=False):
    lead = len(text) - len(text.lstrip(' '))
    words = text[lead:].split(' ')
    out = []
    for i, w in enumerate(words):
        if not w: out.append(w); continue
        first = i == 0 and not lower_start
        if w.startswith('■'):
            out.append(w); continue
        out.append(word_case(w, first, title))
    s = ' '.join(out)
    if lower_start and s and s.split(' ')[0] not in ACRONYMS and words[0] not in PROPER:
        s = s[:1].lower() + s[1:]
    return ' ' * lead + s


def main(ods, out):
    sheets = read_sheets(ods)
    warn = []

    pictures = {}
    for r in table(sheets, 'Messages'):
        k, us = r.get('Key', '').strip(), r.get('Current US', '').replace('\r', '')
        us = '\n'.join(line.rstrip() for line in us.strip('\n').split('\n'))
        if not k or not us.strip(): continue
        bad = sorted({c for c in us if c not in CHARSET})
        if bad: warn.append(f'Messages {k}: no glyph for {" ".join(bad)}')
        pictures[k] = us

    menu = {}
    def add(us, cased, where):
        key = us
        if key in menu and menu[key] != cased:
            warn.append(f'{where}: "{us}" is "{menu[key]}" elsewhere; keeping that')
            return
        menu[key] = cased

    for r in table(sheets, 'Menus & Names'):
        us = r.get('Current US', '').rstrip()
        if not us.strip(): continue
        kind = r.get('Kind', '').strip()
        try: a = int(r.get('US address', '').strip(), 16)
        except ValueError: a = None
        core = us.strip()
        if us in OVERRIDES: cased = OVERRIDES[us]
        elif a is not None and (a in SENTENCE or a in SENTENCE_ALSO):
            cased = recase(us, title=False, lower_start=core in CONTINUES)
        else:
            cased = recase(us, title=True)
        add(core, cased.strip(), f'Menus & Names {kind} {r.get("US address", "")}')

    hud = []
    for r in table(sheets, 'Messages in play'):
        k, us = r.get('Key', '').strip(), r.get('Current US', '').strip()
        if not re.fullmatch(r'H\d+', k): continue
        cased = OVERRIDES.get(us + '@hud') or recase(us, title=False)
        hud.append((int(k[1:]), us, cased))
    hud.sort()
    if [i for i, _, _ in hud] != list(range(36)): raise SystemExit('Messages in play: expected H0-H35')

    for us, cased in menu.items():
        if len(cased) != len(us) or cased.upper() != us.upper():
            warn.append(f'Menu "{us}" -> "{cased}": more than its case changed; skipped')
    menu = {us: c for us, c in menu.items() if len(c) == len(us) and c.upper() == us.upper() and c != us}

    lines = [
        '// The US text, for lettering the unmodded game in the Text mod\'s fonts.',
        '// Written by tools/build_us_text.py from the translation workbook\'s',
        '// "Current US" column: edit the workbook (or the tool\'s rules) and run it again.',
        'using System.Collections.Generic;',
        '',
        'namespace Kf2.Mods.Text;',
        '',
        'static class UsText',
        '{',
        '    /// <summary>Every sign, dialogue line, description and place card, by its disc',
        '    /// entry: T = COM\\TALK.T, I = COM\\ITEM.T.</summary>',
        '    public static readonly Dictionary<string, string> Pictures = new()',
        '    {',
    ]
    lines += [f'        [{cs(k)}] = {cs(v)},' for k, v in pictures.items()]
    lines += [
        '    };',
        '',
        '    /// <summary>Menu-font text in its normal capitalisation, by the US capitals the',
        '    /// game draws, without the spaces some are padded with. Each is the same length as its key.</summary>',
        '    public static readonly Dictionary<string, string> Menu = new()',
        '    {',
    ]
    lines += [f'        [{cs(k)}] = {cs(v)},' for k, v in menu.items()]
    lines += [
        '    };',
        '',
        '    /// <summary>The short messages in play, in sentence case, in page order.</summary>',
        '    public static readonly string[] Hud =',
        '    [',
    ]
    lines += [f'        {cs(c) + ",":<28}// H{i:<3}{us}' for i, us, c in hud]
    lines += ['    ];', '}', '']
    open(out, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines))
    print(f'wrote {out}: {len(pictures)} pictures, {len(menu)} menu strings, {len(hud)} messages in play')
    for w in warn: print('  note:', w)


if __name__ == '__main__':
    if len(sys.argv) != 3: raise SystemExit(__doc__)
    main(sys.argv[1], sys.argv[2])
