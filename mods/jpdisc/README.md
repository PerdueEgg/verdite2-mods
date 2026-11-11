# Japanese Disc (King's Field)

Plays the Japanese release's title screen, opening and ending (King's Field II in Japan), read from your own copy of the Japanese disc, while you play the US game.

- **Title screen:** the Japanese gold KING'S FIELD logo and the painting of the doorway, in place of the US stone logo and the crossed swords. START and the copyright lines are still the US ones. This part needs no Japanese image: the US disc carries the Japanese pictures too (`OP\OP.D`, which the US game shows only with a hidden button combo).
- **Opening:** the prologue with its Japanese text and its own music, at the Japanese speed. The Japanese movies carry no sound of their own: the Japanese game plays their music as a sequence while they run, and so does this mod. The US narrator is gone, so nothing reads out text the new translation no longer has. The US ASCII and From Software logos before it are left as they are.
- **Ending:** the Japanese ending with its music and the Japanese staff credits. The last Japanese frame is then held with the music playing on, as in the Japanese game. A setting adds the US ending's second part (the four spells, the US and localization credits, The End) after it. It is off by default, since it mostly repeats the Japanese credits. With it on, the last Japanese frame waits for the music to finish before the US part starts, and any button starts it sooner.
- **Movie text:** with **Retranslation** and **Text** on, the Japanese text cards are replaced as the movie plays by the new English (the workbook's Movies sheet), in the Text mod's dialogue font, where the Japanese was, fading in and out with it.

## Setup

1. Copy the `jpdisc` folder into Verdite2's `mods` folder and switch it on. For the English in the movies, `text` and `retranslation` have to be on too.
2. Put your Japanese disc image (the `.cue` with its `.bin`, or a `.chd`) in the same folder as your US one, or in the mod's `jpdisc` folder, and the mod finds it by itself. Or, in the mod's settings, press **Browse...** and pick it, or type its path and press **Use this image**. The status line should say "Japanese disc ready".
3. Restart the game, since the title screen and the opening load once, at boot.

Nothing is copied from either disc: the Japanese image is read as the game plays, the same way the port reads the US one.

## How it works

- **Movies:** the port streams a movie by reading its sectors off the disc image. The mod sits between the port and the US image. It answers the US prologue's sectors (`OP\OP2.S`) with the Japanese `OP\OP.S`'s, and the US ending's (`OP\ED0.S`) with the Japanese `OP\ED.S`'s, sector for sector. The footage is the same frame for frame on both discs; only the burned-in text differs. The Japanese files take fewer sectors a frame, because they hold no sound, and the port paces a movie by the sectors it reads, so they play at their own 15 frames a second where the US ones play at 10. The Japanese ending is longer, so the mod lets the US game run it to the Japanese game's own last frame.
- **Music:** the opening's music (in the Japanese `OP\OP.D`) and the ending's (in `OP\ED.D`) are libsnd sequences with their instrument banks. The US END.EXE can't play a sequence, so the mod plays them itself, straight onto the port's sound chip, with the reverb the US title screen sets up. The ending's music goes quiet and starts again for the credits, as it does in the Japanese game.
- **Movie text:** every text card's frames have a twin in the US movie, with the scene under the Japanese text wherever the US text isn't. So the mod takes the Japanese text out by putting the US frame's pixels back, matched to the Japanese frame's brightness. Where the US text covers the same place, it takes them from the frames just before or after the card, or blends them from around. It then draws the English in and encodes the frame back into its own sectors. All of that is done once, in the background, after the Japanese disc is opened (the settings show how far it has got), and kept in memory, about 25 MB. A card that isn't ready yet plays in Japanese.
- **Title:** the US OPEN.EXE reads `OP\OPU.D` for its title pictures, and `OP\OP.D` (the Japanese ones) with four buttons held at boot. The mod makes it read `OP\OP.D`. The Japanese and US games draw the title with the same routine, so the pictures sit as they do in the Japanese game.

## Settings

- **Japanese title screen**, **Japanese opening, with its music**, **Japanese ending...**, and **Then the US ending's second part** (off by default).
- **Lettering brightness** and **Lettering font** (dialogue, or the larger sign font) for the movies' English.

## If something looks or sounds wrong

The console lines starting `[jpdisc]` say what the mod did. The movie swap, the music and the lettering each say in the console when they start.
