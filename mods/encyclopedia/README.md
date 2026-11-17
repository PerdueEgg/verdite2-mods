# Encyclopedia

An encyclopedia of everything in the game, in the game's own menu style.

## Install

Copy the `encyclopedia` folder into Verdite2's `mods` folder and switch it on in the Mods panel. It needs Verdite2 0.6.0 or later, whose built-in text draws its names in the port's fonts; no other mod is needed.

With PORT SETTINGS' FONT on CLASSIC, the names are in the game's own capitals and the places the game never named show no card when you use the Truth Glass there (they still unlock). In Japanese, the descriptions are the Japanese disc's and the names stay in English.

## Use

Press **J** in the world, or choose **ENCYCLOPEDIA** on the in-game menu's DATA page.

- **Enemies** and **People:** every creature and person with a Truth Glass description. Pick one to read it.
- **Places:** the eight areas, each listing its places. An area shows ?????? until you find one of its places.
- **Equipment:** items, weapons, shields and every armour slot, with the fortune teller's description of each.

Each discovered enemy, person and place turns above its list, as items do on the Items page. Models from other areas are read off the disc with their own textures, and everything is put back when you leave. The title box shows how much you have found.

## Discovery

The Truth Glass and the fortune teller always unlock what they describe. Each list can also unlock by encounter (off by default):

| List | Unlocks when you... |
|---|---|
| Enemies | kill one. The Mage's Candlesticks, which are lit rather than killed, count once you strike one |
| People | talk to them |
| Places | enter them (checked every 15 game ticks, under a second) |
| Equipment | use or equip it. A key counts once it has opened its door |

Progress is kept with each memory-card save, beside the save, and lost on a reload exactly as the game's own progress is. **Unlock all** and **Lock all** change the current game only.

## Fixes

- **Deep-sea Sigill:** the game shows the shore Sigill's text for both; the Truth Glass on a deep-sea one now shows its own.
- **Area-6 graves:** the Demon's Grave and the Warrior's Graveyard can be named with the Truth Glass.

## Places the game never named

The Kraken's Nest, the two Underworld Prisons, the Royal Treasure House, the Space for Completion and Guyra's Dimension are the mod's own additions: places, with cards made for them, where the game has none. They are on by default and can be switched off in the settings. Off, they leave the Places list (Guyra's Dimension with its area) and every total, and the Truth Glass names those spots as the game itself does. Anything found there is kept, and counts again when they are switched back on.

## Notifications

Each entry found in play pops up in the port's notifications, with its number and name as its list shows them:

> **Encyclopedia updated**
> 1. Kraken

Each list (enemies, people, places, equipment) has its own switch in the settings, all on by default. **Unlock all** and loading a save don't announce anything, nor does the equipment you start with.

## Settings

- **Open key:** J by default, or None.
- **Open pad button:** none by default; Back, L3, R3 or Misc.
- **Places the game never named:** on by default (above).
- **Discovery:** the encounter switches above.
- **Notifications:** one switch per list (above).

## Translations

With the Retranslation as the language (PORT SETTINGS ▸ GAMEPLAY ▸ LANGUAGE), the names, titles and custom place cards follow it. Another translation can do the same through the port's text string table (the `kf2.text` slot): `encyclopedia:` + the English name for names, titles and areas, and `encyclopedia-card:` + it for a custom place card.
