// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf2.Mods.JpDisc;

/// <summary>
/// The title screen of the Japanese release, in the US OPEN.EXE.
///
/// The US disc carries both title screens. OP\OPU.D is the US one (the KING'S FIELD
/// logo in stone and the crossed swords over the dragon); OP\OP.D has, in its first
/// three TIMs, the Japanese one byte for byte (the gold KING'S FIELD logo, 300 x 121
/// in two halves at VRAM (640, 256) and (640, 384), and the painting of the doorway,
/// 243 x 240 at (640, 0)), and after them the US's own menu strips, so START and the
/// copyright lines stay the US's. The Japanese and US OPEN.EXE draw the title with the
/// same routine (US func_80012D28, JP func_80012560), so the pictures sit as they
/// do in the Japanese game.
///
/// func_80012084 builds the file name from the string at 0x80011000, "OPU.D", and
/// reads the pad ten times: with four buttons held it makes it "OP.D". So the
/// Japanese title is the US game's own hidden one, and this only writes "OP.D"
/// there before it runs. Nothing is read from the Japanese image for it.
/// </summary>
public sealed partial class JpDiscMod
{
    const uint TitleLoad = 0x80012084, TitleName = 0x80011000;

    [PreHook("open", Address = TitleLoad)]
    static void BeforeTitleLoad(CpuContext c, IMemory m)
    {
        if (!_title) return;
        try
        {
            // "OPU.D" -> "OP.D", the rest of the six bytes it copies zero
            if (m.ReadU8(TitleName) != (byte)'O' || m.ReadU8(TitleName + 1) != (byte)'P') return;
            m.WriteU8(TitleName + 2, (byte)'.');
            m.WriteU8(TitleName + 3, (byte)'D');
            m.WriteU8(TitleName + 4, 0);
            m.WriteU8(TitleName + 5, 0);
            Console.WriteLine("[jpdisc] the Japanese title screen (OP.D)");
        }
        catch (Exception e) { Console.WriteLine($"[jpdisc] title: {e.Message}"); }
    }
}
