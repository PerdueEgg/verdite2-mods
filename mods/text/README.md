# Text

New fonts for all of King's Field II's text. It changes how the text looks, not what it says.

- **On its own** it letters the whole game in its fonts: every sign, dialogue line, description and place card, the menus, the item, equipment and magic names, the numbers the game draws and the short messages in play. The menus, names and messages are shown in normal capitalisation (Use Item, Short Sword, Level up) rather than the game's capitals. The words are the US release's own; signs written in capitals on purpose (DANGEROUS!! NO EXIT) keep them. Only the opening and ending movies are left as they are.
- **With other mods** it is also how they put their own words in the game: mods like **Retranslation** and **Encyclopedia** hand it their text, and it draws that in the same fonts. A mod's text always comes first; where it has none, the game's own shows.

## Install

Copy the `text` folder into Verdite2's `mods` folder and switch **Text** on in the Mods panel, along with the mods that use it. See the [main README](../../README.md#installing) for where that folder is.

## Settings

| Setting | Default | What it does |
|---|---|---|
| Signs, dialogue, descriptions and place cards in the new fonts | On | The game's own words, redrawn. Off shows the disc's pictures, unless another mod gives them text. |
| Menus, names and messages in normal capitalisation | On | Use Item, Short Sword, Level up. Off keeps the game's capitals. A translation's own text is left as it is. |
| Proportional spacing and kerning in signs, dialogue and cards | On | Letters placed by their ink, lines wrapped by their real width. Applies to pictures shown from then on. |
| Proportional spacing and kerning in menus | On | The same for menu labels. Numbers the game draws itself stay in fixed columns. |
| Drop shadow on signs, dialogue and cards | Off | A near-black shadow a pixel down and right of the letters. |
| Drop shadow on menu text and numbers | Off | The same for menu labels, the game's numbers and the short messages in play. |
| Rebuild the character maps from the disc | (button) | Puts the game's original lettering back, cut from your own disc. See below. |

The settings page also shows where the `fonts` folder is and whether the fonts and the menu font's lowercase are in place.

## What it provides

- **Three redrawn fonts with full upper and lower case:** the dialogue font (dialogue and descriptions), the sign font (signs, gravestones, nameplates and place cards), and the menu font. All three have A-Z, a-z, 0-9 and `! " # % & ' ( ) * + , - . / : ; = ?` and arrows; the menu font also has the button box.
- **Lowercase in the menu font:** the new cells are added to the game's menu font in video memory and kept there, along with the map's digits in the strip the game draws its own numbers from (levels, HP, gold, counts).
- **Proportional spacing and kerning:** letters are placed by their ink rather than in fixed cells, and pairs with room to spare (To, LT, AV) are pulled together a pixel. In pictures, lines are wrapped by their real width. In menus, a label never runs more than 3 pixels wider than the game's own (otherwise it is drawn the game's way), and labels the game right-aligns by padding them with spaces end flush with the numbers in the same column: the status page's Condition (Good, Poison, ...) lines up with the numbers above it whatever its last letter, and a translation's word lands in the same place, padded or not. Numbers the game draws itself stay in fixed columns. Both can be switched off in the Text settings.
- **The game's own text:** the US text of every sign, dialogue line, description and place card, read from the translation workbook and built into the mod (`UsText.cs`), so each picture is redrawn in the new fonts whenever the game shows it. The menus' and names' capitals are drawn in normal case only while they are drawn; the game's memory keeps its own text, so a translation that rewrites a name still finds it.
- **The short messages in play** (Level up, Locked, A grave, the gold after a pickup's amount, 36 in all) are one picture on the disc, beside the menu font. It is redrawn in the menu font's letters and spacing, in sentence case, centred like the disc's (gold starts right after the amount). A message too long for its 127 pixels is tightened just enough to fit: its spaces first, then a pixel out of some of the gaps between letters. Other mods word them through the string table (below).
- **Redrawn pictures for other mods:** any sign, dialogue, description or place card a mod gives new text is drawn with that text instead, and goes back to the game's own when the mod takes it away. Dialogue wraps like the US pictures; signs and cards are centred with their lines balanced to even lengths, so a mod only needs line breaks where it wants a new line.
- **Drop shadows (optional, off by default):** a near-black shadow a pixel down and right of the letters, like the Japanese Disc mod's movie text. There are two switches in the Text settings. One covers signs, dialogue, descriptions and cards, both the disc's own and redrawn ones. It fades in and out with the letters, and the pictures themselves are not changed. The other covers menu labels, the numbers the game draws (levels, HP, gold, counts) and the short messages in play (36 gold, Level up, ...), which fade in and out with theirs. If a crowded frame has no room left in the primitive buffer, its menu letters go without a shadow. The Encyclopedia's lists and counts get it too. The port's own panels (the gear comparison and the settings page) draw their text themselves and get no shadow.
- **A shared string table:** mods use it to share wording. For example, a translation tells the Encyclopedia what to call things.

## The character maps

The fonts are character maps in the `fonts` folder inside the Text mod's own folder. The mod ships with its own redrawn letters; the Text settings show where the folder is.

| File | What it is |
|---|---|
| `dialogue.png`, `sign.png`, `menu.png` | Every letter of each font. Edit these and restart the game to use your changes. |
| `dialogue.json`, `sign.json`, `menu.json` | Where each letter sits in its map. |
| `*_template.png` | The same grid, empty, for drawing a whole new font. Save it over the font's map to use it. |
| `dialogue_spacing.json`, `sign_spacing.json`, `menu_spacing.json` | Each font's spacing: the gap between letters, a space's width, extra space between lines (`lineGap`), how far kerning may tighten a pair, per-letter or per-pair overrides such as `"advance": { "i": 3 }` or `"kern": { "Te": -1 }`, and `clear`, the fewest pixels kept between a letter and its neighbours, never kerned closer. The dialogue font ships with `"clear": { "'": 1, ".": 1 }`, so kerning no longer tucks an apostrophe or a period right against the letters beside it ("who're"). |

- **Picture fonts** (dialogue, sign): ink is grey. 232 is full ink, darker greys are fainter, black is clear.
- **Menu font:** uses the menu palette's own colours. Any other colour is read as the nearest one.
- **Guides:** the green frames and blue ticks mark each cell, its cap height and its baseline. They're never drawn in game, but they do say where each letter is: a cell can be made wider or taller by moving its frame, and the letter is read at that size. Keep the letters in the same order. Picture lines move apart to fit letters made taller.
- **Empty cells:** a cell left empty is a letter the font doesn't have.

**Rebuild the character maps from the disc** in the Text settings is for anyone who wants the game's original lettering back. It replaces the maps with letters cut from your own disc, plus a few hand-drawn letters in the same style for characters the game's fonts don't have. It replaces the included fonts and any edits; reinstall the mod to get the included fonts back. A font whose map is missing is rebuilt this way on launch.

## Where the game's text comes from

`UsText.cs` is written from the translation workbook's **Current US** column by `tools/build_us_text.py` (in this repository's `tools` folder; it needs Python 3 only):

```
python3 tools/build_us_text.py mods/retranslation/KF2_retranslation.ods mods/text/UsText.cs
```

Pictures keep the US text as it is, line breaks included. Menu text, names and the messages in play are put in normal case by the tool's rules (title case for menus and names, sentence case for the memory-card messages and messages in play, acronyms and names kept); a string they get wrong goes in its `OVERRIDES`.

## For mod authors

- **Dependency:** if your mod needs the Text mod, list `"kf2.text"` under `"dependencies"` in your `mod.json`. It then loads first, and the loader skips your mod with a message if it's missing. A mod that only uses it when it's there (as Shortcuts and Japanese Disc do) can leave it out: every call looks the Text mod up as it's made and gives a plain answer when it's missing.
- **Client file:** copy `TextApi.cs` from Retranslation, Shortcuts or Japanese Disc (they're the same file) into your mod, changing only its namespace.
- **The calls:**

| Call | What it does |
|---|---|
| `TextApi.MenuEncode(text, max)` | Menu font cells for a game string, then `0xFF`. Lowercase once `TextApi.MenuReady`. |
| `TextApi.MenuIndex(c)` / `TextApi.MenuChar(i)` | Character to cell and back. |
| `TextApi.MenuShown(text)` | A label in the game's capitals as the Text mod will draw it (`SHORT SWORD` as `Short Sword` with normal case on), for measuring text you lay out yourself. |
| `TextApi.SetPicture(owner, archive, entry, style, text)` | Redraws a disc picture (archive 3 = TALK.T, 6 = ITEM.T) wherever it's shown. Style is `"dialogue"`, `"card"` or `"auto"`. |
| `TextApi.ClearPictures(owner)` | Removes everything that owner set. |
| `TextApi.NextPicture(style, text)` | Redraws the very next picture shown. Point the game at any card of the same kind, then call this. |
| `TextApi.SetString(key, value)` / `TextApi.GetString(key)` | The shared string table. `"hud:"` + 0-35 words a message in play, in the page's order (`HudMessages.Us` in this mod; 21 is GOLD). A mod's own words can be reworded by a translation the same way (Shortcuts reads `"shortcuts:hotkeys"`, ...). |
| `TextApi.MenuShadow(memory, quad, slot)` | A drop shadow under a menu letter quad your mod has just linked onto an ordering slot, when the menu shadow is on. |
| `TextApi.MenuPlace(text)` | A string's menu cells, each one's x offset and the total width, spaced as the menus are, for a mod that draws menu letters itself. |
| `TextApi.Coverage(style, text, width)` | The text wrapped to a width in pixels and drawn as ink coverage, a byte a pixel, in the `dialogue` or `card` font with the pictures' spacing and line pitch, for a mod that letters something itself (the Japanese Disc mod's movie text). |
