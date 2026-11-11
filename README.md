# Verdite2 mods

Mods for [Verdite2](https://github.com/Voicedrew11/verdite2), the PC recompilation of King's Field (US) / King's Field II (JP): new fonts and a new translation, the Japanese opening and ending, more shortcut slots, and a few King's Field III comforts. They are C# source mods: Verdite2 compiles them itself the first time it starts with them, so there is nothing else to install.

Made for Verdite2 v0.5.2. The mods hook the game's own code, so a later Verdite2 update can break one until the mod is updated.

| Mod | What it does | Needs |
|---|---|---|
| [Text](#text) | New fonts for all of the game's text, in normal capitalisation | |
| [Retranslation](#retranslation) | A new English translation from the Japanese release | Text |
| [Japanese Disc](#japanese-disc) | The Japanese title screen, opening and ending, from your Japanese disc | |
| [Shortcuts](#shortcuts) | Five Select shortcuts instead of one | |
| [Truth Glass Hotkey](#truth-glass-hotkey) | Use the Truth Glass with one key | |
| [NPC Dialogue](#npc-dialogue) | King's Field III style conversations | |
| [Cursor Memory](#cursor-memory) | Menus open where you left them, or on what's equipped | |

Every mod works on its own except Retranslation, and they all work together.

## Installing

1. Download the latest zip from [Releases](../../releases) and unzip it.
2. Copy the folders inside its `mods` folder (all of them, or just the ones you want) into Verdite2's own `mods` folder:
   - **Windows:** `%LOCALAPPDATA%\Verdite2\mods\` (paste this into Explorer's address bar)
   - **Linux:** `~/.local/share/verdite2/mods/`
3. Start the game. The mods compile on the first launch, and each one can be switched on or off in the **Mods** panel. Their settings are in the same panel.

To update, replace the mod folders with the new ones. Settings are stored by Verdite2 and are kept. If you have edited the Text mod's fonts or the Retranslation workbook, copy them somewhere first: the new folders replace them.

If a mod that needs another is installed without it, Verdite2 skips it and says so in its console.

## The mods

### Text

New fonts for all of the game's text. It changes how the text looks, not what it says, and it is what the other mods draw their words with.

- **New fonts:** all three of the game's fonts redrawn in a thin serif style, with full upper and lower case: the dialogue font (dialogue and descriptions), the sign font (signs, gravestones, nameplates and place cards) and the menu font.
- **The whole game in the new fonts:** every sign, dialogue line, description and place card is redrawn from the US text, along with the menus, the item, equipment and magic names, the numbers the game draws and the short messages in play. Only the opening and ending movies are left as they are.
- **Normal capitalisation** for the menus, names and messages in play: Use Item, Short Sword, Checking the RAM card, Level up. Signs written in capitals on purpose (DANGEROUS!! NO EXIT) keep them. The port's own settings page is drawn in title case to match.
- **Proportional spacing and kerning:** letters are placed by their ink rather than in fixed cells, and lines are wrapped by their real width. Right-aligned menu labels (Good, Poison, ...) still line up with the numbers above them.
- **Optional drop shadows** on pictures and on menu text.
- **Editable fonts:** each font is a character map (a PNG) in the mod's `fonts` folder, with an empty template for drawing a whole new one and a spacing file for fine-tuning.

**Settings:**
- Signs, dialogue, descriptions and place cards in the new fonts (on)
- Menus, names and messages in normal capitalisation (on)
- Proportional spacing and kerning in signs, dialogue and cards (on)
- Proportional spacing and kerning in menus (on)
- Drop shadow on signs, dialogue and cards (off)
- Drop shadow on menu text and numbers (off)
- **Rebuild the character maps from the disc:** puts the game's original lettering back, cut from your own disc, with a few hand-drawn letters in the same style for characters the game doesn't have

More on the character maps, and the calls other mods use: [the Text mod's README](mods/text/README.md).

### Retranslation

**Needs:** Text.

A new English translation made from the Japanese release, King's Field II. The original US translation was loose in places; this one follows the Japanese closely, with a light touch of tone, and names closer to their Japanese pronunciation.

- Every sign, dialogue line, description and place card.
- The menus, the item, equipment and magic names, the status labels and the short messages in play.
- With the **Japanese Disc** mod, the text in the Japanese opening and ending.

The whole translation is in a LibreOffice workbook (`KF2_retranslation.ods`) in the mod's folder, beside the Japanese and the US text it was made from. The mod reads it when the game starts, so you can change any line, save the workbook and press **Import from workbook** in the mod's settings to see it in game.

**Settings:**
- Signs, dialogue and descriptions (on)
- Menus, items, equipment and magic names, and messages in play (on)
- Opening and ending text, with the Japanese Disc mod (on)
- **Translation workbook:** Import from workbook, Use the built-in text, and Read the workbook at start (on)

Switching a part off puts the game's own text back, still in the Text mod's fonts. More: [the Retranslation README](mods/retranslation/README.md).

### Japanese Disc

Plays the Japanese release's title screen, opening and ending while you play the US game, read from your own copy of the Japanese disc (King's Field II).

- **Title screen:** the Japanese gold logo and the painting of the doorway, in place of the US stone logo. This part needs no Japanese disc: the US disc carries the Japanese pictures too.
- **Opening:** the prologue with its own music at the Japanese speed, without the US narration.
- **Ending:** the Japanese ending with its music and the Japanese staff credits.
- **English text:** with **Text** and **Retranslation** on, the movies' Japanese text cards are lettered in the new translation as they play.

**Setup:** put your Japanese disc image (`.cue` and `.bin`, or `.chd`) in the same folder as your US one, or in the mod's folder, and the mod finds it. Or choose it in the mod's settings with **Browse...**. Then restart the game. Nothing is copied from either disc: it is read as the game plays.

**Settings:**
- Japanese title screen (on)
- Japanese opening, with its music (on)
- Japanese ending, with its music and credits (on)
- Then the US ending's second part: the four spells, the US credits and The End (off)
- Lettering brightness and font, for the movies' English

More, including how it works: [the Japanese Disc README](mods/jpdisc/README.md).

### Shortcuts

The game's Select button is a shortcut that holds one item or spell. This makes it five, and any of them can hold an item, an attack spell or a support spell.

- **Assign:** open Equipment, choose the Select button row (Hotkeys with keyboard labels), pick an item or spell and answer Yes. A second list asks which shortcut it goes on.
- **Use:** press the shortcut's key or button. It works exactly like pressing Select, so MP, the magic gauge, item counts and "use it on what's in front of you" all still apply.

| Shortcut | Keyboard | Controller |
|---|---|---|
| 1 (the game's Select) | 1 | Select |
| 2 | 2 | D-pad Up |
| 3 | 3 | D-pad Down |
| 4 | 4 | D-pad Left |
| 5 | 5 | D-pad Right |

Shortcuts 2-5 are kept with each memory-card save. With Text and Retranslation on, the shortcut list is in their fonts and names.

**Settings:**
- In-game labels: keyboard keys or controller buttons (keyboard)
- Each shortcut's key and controller button (1-5; Select and the d-pad)
- D-pad directions used by a shortcut don't walk or turn (on)

### Truth Glass Hotkey

Press **`** (the key left of 1) to use the Truth Glass on whatever is in front of you, exactly as if you had chosen it from the item menu: the game's own enemy, person or place description.

**Settings:**
- Key (`) and pad button (none)
- Only when carrying the Truth Glass (off: it works without it, and doesn't give you one)

### NPC Dialogue

King's Field III style conversations. While an NPC's line is up, **Interact** goes straight to their next line, and **Cancel** ends the conversation. At the end of their dialogue, Interact repeats the last line until you cancel. Shopkeepers and the fortune teller, who open a menu after talking, are left as they are.

**Settings:**
- No animations between lines (on): the NPC stays in their talking pose until the conversation ends
- Cancel does not open the menu (on)

### Cursor Memory

King's Field III style menu cursors.

- The **Items** and **Magic** pages open on the entry you last had highlighted. The cursor follows the item or spell itself, so it stays on the same thing when the list changes.
- The **Equipment** sub menus (weapon, armour, accessories, attack magic and the Select slot) open on what is equipped.

**Settings:** each of the three can be switched off on its own.

## Tools

The `tools` folder has the scripts behind the text, for anyone editing the translation or the fonts. They need Python 3; the movie tools also need numpy, scipy, Pillow and ffmpeg.

- `build_text_cs.py` writes the workbook's translation into the Retranslation mod as its built-in text.
- `build_us_text.py` writes the workbook's US text into the Text mod, with its capitalisation rules.
- `movies/preview.py` previews the Japanese Disc mod's movie lettering from the two disc images, without the game.

## License

[MIT](LICENSE).

King's Field is © FromSoftware. Verdite2 and these mods need your own copy of the game, and the Japanese Disc mod your own Japanese disc: no disc images or game files are included. The Text mod carries the US release's text, and the Retranslation workbook quotes the Japanese and US text beside the translation.
