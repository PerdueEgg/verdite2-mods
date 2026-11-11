# Japanese Disc (KF2): engine notes

What was found building this, for whoever picks it up next. Addresses are the US discs' (SLUS-00158) unless marked JP.

## The two discs

| | US (King's Field, SLUS-00158) | JP (King's Field II) |
|---|---|---|
| Boot | `SLUS_001.58` | `PSX.EXE` (no SYSTEM.CNF) |
| Logos | `OP\OP0.S` (ASCII, 139 frames), `OP\OP1.S` (From Software, 270) | none: the JP boots straight to the title |
| Prologue | `OP\OP2.S`: 1089 frames, 13-15 video sectors a frame + XA (narration and music), 10 fps | `OP\OP.S`: the same 1089 frames, 10 sectors a frame, **no sound**, 15 fps |
| Ending | `OP\ED0.S`: 1700 frames + XA; then `OP\ED1.S`: the four spells, US and localization credits, The End | `OP\ED.S`: the same 1700 frames, then 441 frames of the JP staff credits, no sound |
| Title data | `OP\OPU.D` (US art), `OP\OP.D` (JP art + US strips) | `OP\OP.D` |
| Ending music | none | `OP\ED.D` (VAB + SEQ) |

- The movies' footage is identical frame for frame (offset 0 is always the best match); only the burned-in text differs. The JP frames are encoded harder (to fit 10 sectors), so there are small brightness differences, up to about 10 levels in parts of the ending.
- Every movie on both discs is **MDEC version 3**. The DC is a DPCM difference coded with MPEG-1's dct_dc_size tables, one predictor each for Y, Cb and Cr, starting at 0 each frame; the 10-bit DC is the predictor x 4. Every frame ends with the ten bits `1111111111` (version 2 used `0111111111`). The header's code count is `((codes + 1) / 2 + 31) & ~31`, as in version 2.
- `OP\OP.D`: TIMs, then a VAB header (0x1A20), its body (0x34CA0), the title SEQ (0x670) and the opening SEQ (0x1C68). The sound data is byte-identical on both discs; the US OPEN.EXE opens both SEQs but plays only the title's. TIMs 0-2 of the US `OP.D` are byte-identical to the JP ones.
- `OP\ED.D` (JP only): a VAB header (0x1C20), its body (0x3C170), one SEQ (0x2584).

## OPEN.EXE

- **Main** `func_80011AE0`: plays OP0-OP2 with `func_80013DC8(n)` unless the boot stub's byte at `0x800102F8` says not to. Then `func_80012084` (OP.D or OPU.D into a 0x96000 buffer) and `func_80012348` (VAB, SEQs). Then the title loop, and on a timeout of 0x3A3 frames the three movies again.
- **JP main** `func_80011AC0`: title first; the opening (`func_800134F0`) plays on the timeout or from the OPENING menu item. It stops the title SEQ, plays the opening SEQ (`SsSeqPlay(seq, 1, 1)` at 0x40, 0x40), streams OP.S to frame 0x43D, then waits 90 VSyncs (unless skipped) and fades the SEQ volume over 64.
- **Movie player** `func_80013DC8(n)`: fades the master volume in (`SsSetMVol` 0 to 0x7E over 64 VSyncs), stores the frame count to stop at in `0x8003EA60` (0x89, 0x10C, 0x43F), starts the stream (`func_80014170`), counts frames in `0x8003EA58`, then fades the master volume out over 64 VSyncs. It returns 1 for Start (skip everything) and 2 for another button.
- **Title name**: `func_80012084` copies "OPU.D" from `0x80011000`; with pad bits 0x10, 0x20, 0x40 and 0x80 all held during its ten reads it writes ".D" over it, making "OP.D". So the JP title is a hidden US feature.
- The title draw routine is identical in both (US `func_80012D28`, JP `func_80012560`), apart from addresses. The menus differ: the JP has START and OPENING, the US only START.
- Sound init `func_800122BC`: SsInit, tick mode 1, reverb type 5 (hall), depth 0x20, SsStart2.

## END.EXE

- **Main** `func_800119A4`: `func_80011CC4(0)`, `func_80011CC4(1)`, then `while(1);` (the port's EndingHold patch holds and returns to the title).
- **Movie player** `func_80011CC4(n)`: stores the frame count to stop at in `0x80039A04` (0x6A2 for ED0, 0xA6D for ED1), starts the stream with `func_80011EA0`, and counts the frames shown in `0x800399FC`. After ED1 it holds the last frame for 150 x 6 VSyncs.
- The US END.EXE only uses XA. It links SsInit and the sequencer internals, but not SsVabOpenHead, SsVabTransBody, SsSeqOpen or SsSeqPlay, and it turns no reverb on.
- **JP END.EXE**: VAB and SEQ from ED.D. It plays the SEQ on repeat at 0x40, streams ED.S, and from frame 0x538 turns the song down a step a frame. At 0 it stops it and plays it once more from the top at 0x40. At frame 0x858 it pauses the CD and hangs on the last frame.

## The movie text cards (STR frame numbers, from 1)

| Key | JP movie | Japanese text shows | Block (x, y) |
|---|---|---|---|
| OP.1 | OP.S | 139-244 | 47-271, 154-199 |
| OP.2 | OP.S | 335-459 | 54-264, 154-199 |
| OP.3 | OP.S | 592-710 | 40-285, 154-183 |
| OP.4 | OP.S | 808-984 | 40-284, 154-199 |
| ED.1 | ED.S | 166-244 | 47-264, 99-127 |
| ED.2 | ED.S | 566-830 | 40-278, 55-158 |
| ED.3 | ED.S | 1043-1372 | 28-278, 69-143 |

The US cards share the opening's timings. In the ending the US third card shows at 920-1190, earlier than the Japanese one. The fades go in pairs of frames.

## Notes from building it

- Written without a .NET SDK; built and played through in the game by the user (title, opening, movies, ending).
- The music follows libsnd's own note-on, read from OPEN.EXE: NoteOn `0x8002448C` (velocity x channel volume / 127, the channel pan, both taken at the key-on), SpuVmKeyOn `0x80021B5C`, the pitch `0x80020D30` (table `0x8003DE18`, 16 steps a semitone, fine / 8) and the volume `0x80020040`. The volume is integer products of velocity, VAB, program and tone volume and the song volume; each pan turns one side down by pan/64; then each side is **squared over 0x3FFF**. Linear plays far too loud and flattens the balance. Tone mode bit 2 turns reverb on (`0x800203A0`). No vibrato, as in the game: this libsnd's vibrato is stubbed out (SsUtVibrateOn calls `func_800210D4`, a bare `jr ra`, in OPEN.EXE; the same in the JP END.EXE), only SsUtKeyOn would call it, for tones with mode bit 0, and control 1 (ContModulation) only writes the tones' vibW. So the ending's vibW 50 / vibT 20 never sound on the Japanese disc either. The damper (control 64) isn't done; neither song uses it.
- The music is timed by the port's mixer (`Spu.Mixed`, after each 256-sample buffer). VSync comes unevenly while a movie plays, so ticking from it lands notes late or bunched.
- The opening's music ends as the Japanese one does: at the movie player's first fade-out step (SsSetMVol `0x8001FD90`), 90 VSyncs of the last note (unless a button ended the movie), then the song turned down a step a VSync and stopped; then the US master fade runs.
- A song's end (FF 2F) only stops the sequencing: held notes are let go and ring out by their release, as libsnd does; only Stop() silences. Cutting everything at the end event clips the opening's last note.
- With the US ED1.S on (off by default), the Japanese ending's last frame is held after the movie player's first call until the music has ended (`Music.Done`) plus 180 VSyncs of ringing, or until a button is seen going down (EndingHold's test, `Controller.State != 0xFFFF`). Then the song is faded quickly and stopped; fading it as soon as the movie ends would cut into the credits' music.
- Cleaning the frames (`tools/movies/twin.py` is the prototype of MovieText.cs) has to catch, as the preview tool shows: lone ellipsis dots (a piece filter that drops pieces under 8% of the biggest loses them); places where a JP and a US stroke share pixels (the twins agree there, so a "US text" test misses it and copies the US stroke back in: a short tick); JP strokes' soft edges outside a one-pixel grown mask; and, in what is filled from around, thin marks and flat, grainless patches. The JP frames' high frequencies are mostly encoding noise (they decorrelate frame to frame), so the grain added is fresh each frame. A US fill blurred to the JP look matched worse than the plain one (the twins agree to 1-4 levels outside the text), and borrowing from the clean frames with a global shift (phase correlation) found nothing usable on the moving cards, so neither is in. What is left: under both texts on a moving shot (ED.3's left pillar, the OP.4 stairs late in the card) there is nothing to take the scene from, and the fill smears; a few scene sparkles on the text in ED.2 go while the card shows.
- `tools/movies/preview.py` runs the lettering steps offline (numpy, ffmpeg) and writes before-and-after frames, for tuning without the game.
