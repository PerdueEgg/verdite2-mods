# Retranslation

A new English translation made from the Japanese release (King's Field II).

## Install

Needs the **Text** mod. Copy both the `text` and `retranslation` folders into Verdite2's `mods` folder and switch both on in the Mods panel. If Text is missing, the loader skips this mod and says so in the console.

## What it changes

- **Signs, dialogue, descriptions and place cards:** handed to the Text mod, which redraws each one in its fonts whenever the game shows it.
- **Menus, item, equipment and magic names:** rewritten in memory while the game runs, in normal capitalisation once the menu font's lowercase is in place.
- **Status labels the game builds in code** (Slash, Pierce, the magic types, Yes/No, On/Off, ...) are changed only for the moment they're drawn, up to 11 letters, and put back straight after. The game builds some of them by writing over part of the previous one, so a rewrite left in place would show through.
- **Condition on the status page** (Good, Poison, ...) is padded to the US text's ten cells, so it right-aligns even with proportional menus off; with them on, the Text mod ends it flush with the numbers above it.
- **Messages in play** (Level up, Locked, A gravestone, the gold after a pickup's amount, ...): handed to the Text mod, which redraws them in the menu font. Part of the menus switch.
- **Encyclopedia:** with the Encyclopedia mod (not yet released), its names, titles and custom place cards follow the translation, through the Text mod's shared string table.
- **Opening and ending:** with the **Japanese Disc** mod, the text cards of the Japanese opening and ending are lettered in the new English (the workbook's Movies sheet). Without it nothing changes in the movies.

Each part has its own checkbox in this mod's settings. Switching a part off puts the game's own text back, still in the Text mod's fonts unless those are switched off there.

## Editing the translation

The text comes from the translation workbook's **Mod EN** column. Two ways to get your edits into the game:

- **From the game:** keep `KF2_retranslation.ods` in this mod's folder (`mods/retranslation`). It's read when the game starts, and **Import from workbook** in this mod's settings reads it again and puts the new text in place while you play. Save the workbook in LibreOffice first: the mod reads the file on disk. **Use the built-in text** goes back to the text the mod ships with, and **Read the workbook at start** can be switched off. Without a workbook in the folder, the built-in text is used.
- **Into the mod itself:** `python3 tools/build_text_cs.py mods/retranslation/KF2_retranslation.ods mods/retranslation/Text.cs` (from the top of this repository) writes the workbook's text into `Text.cs`, so it becomes the built-in text. Needs only Python 3, no extra packages.

Both follow the same rules:

- A blank Mod EN cell keeps the built-in text for that entry.
- Curly quotes become straight ones (the fonts have none).
- Menus & Names rows are matched by **US address**, to the slots the mod already knows. Over 23 characters, the row is skipped. Code rows are matched by **Current US** and must fit 11 letters.
- Encyclopedia rows are matched by **Mod name (current)**. When two rows share a name (Places and Area name), the first one wins: the Encyclopedia mod uses one name for both.
- Anything skipped, and letters the fonts don't have, are listed as notes (in the settings and the console).

## Known limits

- Menu-font text is at most 23 characters per slot.
- The memory-card messages are the US fragments in sentence case: the US build puts its messages together from different pieces than the Japanese one.
- Pictures are 256x256: a dialogue page holds 18 lines of 31 characters, and a sign 14 lines of 27.
