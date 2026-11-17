# Verdite2 mods

Mods for [Verdite2](https://github.com/Voicedrew11/verdite2), the PC recompilation of King's Field (US) / King's Field II (JP). They are C# source mods: Verdite2 compiles them itself the first time it starts with them, so there is nothing else to install.

Made for Verdite2 v0.6.0. The mods hook the game's own code, so a later Verdite2 update can break one until the mod is updated.

| Mod | What it does |
|---|---|
| [Encyclopedia](#encyclopedia) | Every enemy, person, place and item, with their descriptions and models, unlocked as you find them |
| [Japanese Disc](#japanese-disc) | The Japanese title screen, opening and ending, from your Japanese disc, with the movies' text in English |
| [Truth Glass Hotkey](#truth-glass-hotkey) | Use the Truth Glass with one key |

## Now part of Verdite2

**Text, Retranslation, Shortcuts, Cursor Memory and NPC Dialogue** are built into Verdite2 from v0.6.0: the new fonts, the Retranslation (PORT SETTINGS ▸ GAMEPLAY ▸ LANGUAGE), the five shortcuts, the menu cursors and King's Field III style conversations are all in its own settings now.

**If you are updating from the v1.0 mods, delete those five folders** (`text`, `retranslation`, `shortcuts`, `cursor-memory`, `npc-dialogue`) from Verdite2's `mods` folder. An old Text mod left switched on takes the place of Verdite2's own lettering, and the old Retranslation no longer loads.

For an older Verdite2, the [v1.0 release](../../releases/tag/v1.0) still has them.

## Installing

1. Download the latest zip from [Releases](../../releases) and unzip it.
2. Copy the folders inside its `mods` folder (all of them, or just the ones you want) into Verdite2's own `mods` folder:
   - **Windows:** `%LOCALAPPDATA%\Verdite2\mods\` (paste this into Explorer's address bar)
   - **Linux:** `~/.local/share/verdite2/mods/`
3. Start the game. The mods compile on the first launch, and each one can be switched on or off in the **Mods** panel. Their settings are in the same panel.

To update, replace the mod folders with the new ones. Settings and the Encyclopedia's progress are stored by Verdite2 and are kept.

## The mods

### Encyclopedia

An encyclopedia of everything in the game, in the game's own menu style. Press **J** in the world, or choose **Encyclopedia** on the in-game menu's DATA page.

- **Enemies, People, Places and Equipment:** every creature and person with a Truth Glass description, every place, and every item, weapon and piece of armour with the fortune teller's description. Pick one to read it.
- **Models:** each discovered enemy, person and place turns above its list, as items do on the Items page, read off your disc with their own textures, whatever area you are in.
- **Discovery:** the Truth Glass and the fortune teller unlock what they describe. Each list can also unlock by encounter: killing an enemy, talking to a person, entering a place, using or equipping an item. Progress is kept with each memory-card save.
- **Notifications:** a pop-up for each entry you find ("Encyclopedia updated: 1. Kraken"), with a switch for each list.
- **Fixes:** the deep-sea Sigill shows its own description, and the Demon's Grave and the Warrior's Graveyard can be named with the Truth Glass.
- **Places the game never named:** the Kraken's Nest, the Underworld Prisons, the Royal Treasure House, the Space for Completion and Guyra's Dimension, with cards of their own (can be switched off).
- With the Retranslation as the language, its names follow the translation.

**Settings:**
- Open key (J) and pad button (none)
- Places the game never named (on)
- Unlock by encounter: Enemies, People, Places, Equipment (off)
- Notifications: Enemies, People, Places, Equipment (on)
- Unlock all and Lock all, for the current game

More: [the Encyclopedia README](mods/encyclopedia/README.md).

### Japanese Disc

Plays the Japanese release's title screen, opening and ending while you play, read from your own copy of the Japanese disc (King's Field II).

- **Title screen:** the Japanese gold logo and the painting of the doorway, in place of the US stone logo. This part needs no Japanese disc: the US disc carries the Japanese pictures too.
- **Opening:** the prologue with its own music at the Japanese speed, without the US narration.
- **Ending:** the Japanese ending with its music and the Japanese staff credits.
- **English text:** the movies' Japanese text cards are lettered in the Retranslation's English as they play, in Verdite2's fonts. In a Japanese run they keep their Japanese text.

Verdite2 plays the Japanese title and movies itself in a Japanese run; this mod adds them in English and Retranslation runs, with the English lettering. While it is on, Verdite2's own stand down.

**Setup:** put your Japanese disc image (`.cue` and `.bin`, or `.chd`) in the same folder as your US one, or in the mod's folder, and the mod finds it. Or choose it in the mod's settings with **Browse...**. Then restart the game. Nothing is copied from either disc: it is read as the game plays.

**Settings:**
- Japanese title screen (on)
- Japanese opening, with its music (on)
- Japanese ending, with its music and credits (on)
- Then the US ending's second part: the four spells, the US credits and The End (off)
- English text on the movies' cards (on), with its brightness and font

More, including how it works: [the Japanese Disc README](mods/jpdisc/README.md).

### Truth Glass Hotkey

Press **`** (the key left of 1) to use the Truth Glass on whatever is in front of you, exactly as if you had chosen it from the item menu: the game's own enemy, person or place description.

**Settings:**
- Key (`) and pad button (none)
- Only when carrying the Truth Glass (off: it works without it, and doesn't give you one)

## Tools

`tools/movies` has the Japanese Disc mod's movie work in Python: a reader for the disc images, the movie decoder and the lettering steps it was prototyped in, and `preview.py`, which letters the cards offline from the two disc images so the result can be checked without the game. They need Python 3, numpy, scipy, Pillow and ffmpeg.

## License

[MIT](LICENSE).

King's Field is © FromSoftware. Verdite2 and these mods need your own copy of the game, and the Japanese Disc mod your own Japanese disc: no disc images or game files are included.
