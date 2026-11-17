using System;
using System.Collections.Generic;
using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using KingsField2 = Recompiled.KingsField2_game;

namespace Kf2.Mods.Encyclopedia;

/// <summary>
/// The models above the Enemies, People and Places lists: loaded off the disc,
/// textured from their own area, and drawn where the Items page draws its
/// preview. The models and their tuning are in Models.cs.
///
/// ---- where a model goes ----
///
/// The game's model table is `0x8018E1A0`, a pointer per model id; ids
/// 0x80-0xFF are the creatures it streams from MO.T. The MO.T entry is copied
/// into guest RAM past the port's relocated primitive buffers (or failing that
/// the game's own heap), fixed up as the stream's completion callback does
/// (`func_8002E314`), and put in an empty table slot below 0x80, which the
/// streaming never touches. The slots are given back when the model goes.
///
/// ---- drawing ----
///
/// The Items page preview's own state (light, projection, depth), with the
/// rotation scaled so a model of any size fills the space an item does, and an
/// animated model posed by the game's MO blender `func_80034DA8`. The menu
/// shrinks the frame's packet buffer to 0x6400 bytes, too small for a
/// creature, so the model's packets are built in the free RAM instead.
///
/// ---- textures ----
///
/// Every area's RTIM.T set writes its creatures into the same part of VRAM.
/// When the model's area (the place's area, or where FDAT.T places the
/// creature) isn't the one loaded, the pairs its CLUTs use are uploaded, after
/// the VRAM they cover is saved; it is put back when the model goes.
///
/// An item (a place's key or map) is shown by the game's own item preview.
/// </summary>
public sealed partial class EncyclopediaMod
{
    // ---- game addresses ----
    const uint ModelTable = 0x8018E1A0;          // u32 per model id
    const uint CurrentMesh = 0x8018E19C;         // the selected model's TMD
    const uint VertexBase = 0x8018EAA0;
    const uint FogWord = 0x80192EA8;             // func_8002DDDC's
    const uint ResidentSet = 0x8017E062;         // the RTIM.T entry last loaded
    const uint ResidentArea = 0x8017E060;        // the area last loaded
    const int MaxVertices = 1000;                // the vertex cache at 0x8018EB94, 8 bytes each

    // ---- the look, as the Items page shows an item ----
    const int FitSize = 450;                     // the half-height or half-width a model is scaled to
    const int TurnSpeed = 4;                     // per drawn frame, as an item turns
    const int AnimSpeed = 32;                    // clip time per drawn frame

    // ---- the renderer's scratch, at OffModel in the session's ----
    const uint VRot = 0x00;        // SVECTOR, the turn
    const uint VLight = 0x10;      // MATRIX, the preview's light directions
    const uint VColor = 0x30;      // MATRIX, the light colours
    const uint VMat = 0x50;        // MATRIX, the model's
    const uint VLightOut = 0x70;   // MATRIX, light x rotation
    const uint VSlots = 0x90;      // u32 per piece, the blender records' slot words

    static uint _v;

    // ---- memory ----
    static uint _arena, _arenaEnd, _cursor;       // a bump allocator, reset per model
    static bool _heap;                            // else the game's malloc
    static readonly List<uint> _heapBlocks = new();

    /// <summary>Model-table slots below the streamed range, one per piece on show.</summary>
    static readonly List<(int Id, uint Old)> _spares = new();
    const int MaxPieces = 4;

    sealed class Piece
    {
        public int Model, Spare, Clips, Clip, Time, Objects;
        public double Dx, Dy, Dz;  // where it sits in the main model's space
        public uint Slot;          // its blender slot word
    }
    static readonly List<Piece> _pieces = new();

    static int _shown = -1;        // the model on show, -1 none
    static int _shownArea = -1;    // and the area it was textured for
    static int _item = -1;         // an item on show by the game's preview
    static bool _ok;
    static int _yaw;
    static double _cx, _cy, _cz, _fit = 1;

    static readonly List<(uint Rect, uint Data)> _saved = new();

    // ================================================================
    // The session
    // ================================================================

    static void BeginModels(CpuContext c, IMemory m, uint scratch)
    {
        _v = scratch;
        for (uint i = 0; i < 0x100; i += 4) m.WriteU32(_v + i, 0);

        // The Items page's light: three directions, white.
        short[] light = [0x800, 0x800, -0x800, 0, 0, -0x800, 0x1000, 0, -0x800];
        for (int i = 0; i < 9; i++) m.WriteU16(_v + VLight + (uint)i * 2, (ushort)light[i]);
        for (int i = 0; i < 9; i++) m.WriteU16(_v + VColor + (uint)i * 2, 0x1000);

        _shown = _shownArea = _item = -1;
        _ok = false;
        _pieces.Clear();
        _saved.Clear();
        _heapBlocks.Clear();

        _heap = !FindArena(out _arena, out _arenaEnd);
        _cursor = _arena;

        // Table slots below the streamed range, empty ones first.
        _spares.Clear();
        for (int id = 0x7F; id >= 0x40 && _spares.Count < MaxPieces; id--)
            if (m.ReadU32(ModelTable + (uint)id * 4) == 0) _spares.Add((id, 0));
        for (int id = 0x7F; _spares.Count < MaxPieces; id--)
            if (!_spares.Exists(s => s.Id == id)) _spares.Add((id, m.ReadU32(ModelTable + (uint)id * 4)));
    }

    static void EndModels(CpuContext c, IMemory m)
    {
        if (_v == 0) return;
        HideModel(c, m);
        _spares.Clear();
        _v = 0;
    }

    /// <summary>The port asks for more RAM than the PlayStation's 2 MB and lays its own
    /// buffers out past 2 MB, one after another (PrimBuffer: the primitive buffers, then
    /// each XxxScratch with its XxxBytes: the waves', the props', the text's, the HUD
    /// icons' models, and any it adds later). What is left of that RAM past the last of
    /// them is free. Every Scratch / Bytes pair is read, so a buffer the port adds is
    /// never written over -- this once took the space the HUD's item icons were read
    /// into, and their models came back as garbage.</summary>
    static bool FindArena(out uint start, out uint end)
    {
        start = end = 0;
        try
        {
            Type? t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if ((t = asm.GetType("Kf2.PrimBuffer", false)) != null) break;
            if (t == null) return false;
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            if (t.GetProperty("Relocated", all)?.GetValue(null) as bool? != true) return false;
            uint last = 0;
            int pairs = 0;
            foreach (var prop in t.GetProperties(all))
            {
                if (!prop.Name.EndsWith("Scratch", StringComparison.Ordinal) || prop.PropertyType != typeof(uint)) continue;
                string size = prop.Name.Substring(0, prop.Name.Length - "Scratch".Length) + "Bytes";
                object? b = t.GetField(size, all)?.GetValue(null) ?? t.GetProperty(size, all)?.GetValue(null);
                if (b is not uint bytes || prop.GetValue(null) is not uint at) continue;
                last = Math.Max(last, at + bytes);
                pairs++;
            }
            if (pairs == 0) return false;
            start = (last + 0xFFFu) & ~0xFFFu;
            end = 0x80000000u + RecompOne.Runtime.Runtime.RamSize;
            Console.WriteLine($"[encyclopedia] free RAM for the models: 0x{start:X8}-0x{end:X8} (past {pairs} of the port's buffers)");
            return end > start + 0x10000u;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[encyclopedia] finding free RAM: {e.Message}");
            return false;
        }
    }

    static uint Alloc(CpuContext c, IMemory m, int bytes)
    {
        uint n = ((uint)bytes + 15u) & ~15u;
        if (!_heap)
        {
            if (_cursor + n > _arenaEnd) return 0;
            uint at = _cursor;
            _cursor += n;
            return at;
        }
        uint p = Call(c, m, KingsField2.func_80017798, n);   // malloc, 0 past 2 MB or out of room
        if (p != 0) _heapBlocks.Add(p);
        return p;
    }

    // ================================================================
    // Showing a model
    // ================================================================

    /// <summary>Make <paramref name="id"/> the model on show (-1: none), textured
    /// for <paramref name="area"/> (-1: the model's own); nothing if it already is.</summary>
    static Tune? _tuneOverride;    // the entry's own tuning, over the model's (Models.ForEntryTune)

    static void ShowModel(CpuContext c, IMemory m, int id, int area, Tune? tune = null)
    {
        _tuneOverride = tune;          // even for the same model: the Sigill and the Deep-Sea Sigill
        if (_v == 0 || (id == _shown && area == _shownArea)) return;
        HideModel(c, m);
        if (id < 0) return;
        _shown = id;
        _shownArea = area;
        _yaw = 0;
        if (id >= Models.ItemBase)
        {
            Call(c, m, KingsField2.func_80022CAC, (uint)(id - Models.ItemBase));   // ITEM.T into bank 3, as the Items page does
            _item = id - Models.ItemBase;
            _ok = true;
            return;
        }
        try { Load(c, m, id, area); }
        catch (Exception e)
        {
            _ok = false;
            Console.WriteLine($"[encyclopedia] loading model {id}: {e}");
        }
    }

    /// <summary>Put back everything the model on show changed.</summary>
    static void HideModel(CpuContext c, IMemory m)
    {
        if (_item >= 0)
        {
            Call(c, m, KingsField2.func_80022D80);
            _item = -1;
        }
        RestoreVram(c, m);
        if (_v != 0)
            for (uint i = 0; i < MaxPieces; i++)
            {
                uint slot = _v + VSlots + i * 4;
                uint rec = m.ReadU32(slot);
                if (rec != 0) Call(c, m, KingsField2.func_800353E8, rec);   // frees its keyframe, zeroes the slot word
                m.WriteU32(slot, 0);
            }
        foreach (var (sid, old) in _spares) m.WriteU32(ModelTable + (uint)sid * 4, old);
        foreach (uint p in _heapBlocks) Call(c, m, KingsField2.func_800177F8, p);   // free
        _heapBlocks.Clear();
        _pieces.Clear();
        _cursor = _arena;
        _ok = false;
        _shown = _shownArea = -1;
    }

    static void Load(CpuContext c, IMemory m, int id, int area)
    {
        _ok = false;
        if (!Disc.Load()) return;   // read once, the first time a model is shown

        var want = new List<(int Model, double X, double Y, double Z)> { (id, 0, 0, 0) };
        if (Models.Parts.TryGetValue(id, out var parts))
            foreach (var (part, x, y, z) in parts) want.Add((part, x, y, z));

        var cluts = new HashSet<(int X, int Y)>();
        var pts = new List<(double X, double Y, double Z)>();
        for (int i = 0; i < want.Count && i < _spares.Count; i++)
        {
            var (mid, dx, dy, dz) = want[i];
            var bytes = Disc.Model(mid);
            if (bytes == null || !Geometry(bytes, cluts, out var shape, out int objects) || shape == null)
            {
                Console.WriteLine($"[encyclopedia] model {mid} could not be read");
                if (i == 0) return;
                continue;
            }
            uint at = Alloc(c, m, bytes.Length);
            if (at == 0)
            {
                Console.WriteLine($"[encyclopedia] no room for model {mid} ({bytes.Length} bytes)");
                if (i == 0) return;
                continue;
            }
            m.LoadBytes(at, bytes);

            // What the stream's completion callback does: the TMD fixed up in place.
            Call(c, m, KingsField2.func_8002E314, at + BitConverter.ToUInt32(bytes, 8));
            var spare = _spares[_pieces.Count];
            m.WriteU32(ModelTable + (uint)spare.Id * 4, at);

            var p = new Piece
            {
                Model = mid, Spare = spare.Id, Objects = objects,
                Clips = (int)BitConverter.ToUInt32(bytes, 4),
                Dx = dx, Dy = dy, Dz = dz,
                Slot = _v + VSlots + (uint)_pieces.Count * 4,
            };
            p.Clip = p.Clips > 0 ? Math.Clamp(Models.TuneOf(mid).Clip, 0, p.Clips - 1) : 0;
            _pieces.Add(p);
            foreach (var (x, y, z) in shape) pts.Add((x + dx, y + dy, z + dz));
        }
        if (_pieces.Count == 0) return;

        // The fit, over every piece where it sits.
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (var (x, y, z) in pts)
        {
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }
        _cx = (minX + maxX) / 2; _cy = (minY + maxY) / 2; _cz = (minZ + maxZ) / 2;
        double r = 0;
        foreach (var (x, _, z) in pts) r = Math.Max(r, Math.Sqrt((x - _cx) * (x - _cx) + (z - _cz) * (z - _cz)));
        _fit = Math.Max(Math.Max((maxY - minY) / 2, r), 16);

        _ok = true;
        UploadTextures(c, m, id, area, cluts);
    }

    /// <summary>The TMD's vertices and the CLUTs its textured faces use, and
    /// whether it fits the game's buffers. Offsets are relative to the object
    /// table, which starts 12 bytes into the TMD.</summary>
    static bool Geometry(byte[] b, HashSet<(int X, int Y)> cluts, out List<(int X, int Y, int Z)>? pts, out int objects)
    {
        pts = null;
        objects = 0;
        int len = b.Length;
        int tmd = (int)BitConverter.ToUInt32(b, 8);
        if (tmd < 0xC || tmd + 12 > len || BitConverter.ToUInt32(b, tmd) != 0x41) return false;
        int nobj = (int)BitConverter.ToUInt32(b, tmd + 8);
        int tab = tmd + 12;
        if (nobj <= 0 || nobj > 32 || tab + nobj * 28 > len) return false;

        var list = new List<(int X, int Y, int Z)>();
        for (int o = 0; o < nobj; o++)
        {
            int h = tab + o * 28;
            int vt = tab + (int)BitConverter.ToUInt32(b, h), nv = (int)BitConverter.ToUInt32(b, h + 4);
            int pt = tab + (int)BitConverter.ToUInt32(b, h + 16), np = (int)BitConverter.ToUInt32(b, h + 20);
            if (nv < 0 || nv > MaxVertices || vt + nv * 8 > len) return false;
            for (int i = 0; i < nv; i++)
                list.Add((BitConverter.ToInt16(b, vt + i * 8), BitConverter.ToInt16(b, vt + i * 8 + 2), BitConverter.ToInt16(b, vt + i * 8 + 4)));
            int q = pt;
            for (int i = 0; i < np && q + 4 <= len; i++)
            {
                int ilen = b[q + 1], mode = b[q + 3];
                int body = q + 4;
                if ((mode & 0xE0) == 0x20 && (mode & 0x04) != 0 && ilen >= 2 && body + 4 <= len)
                {
                    int cba = BitConverter.ToUInt16(b, body + 2);
                    cluts.Add(((cba & 0x3F) * 16, (cba >> 6) & 0x1FF));
                }
                q = body + ilen * 4;
            }
        }
        if (list.Count == 0) return false;
        pts = list;
        objects = nobj;
        return true;
    }

    // ================================================================
    // Textures
    // ================================================================

    /// <summary>The area set whose creature textures are in VRAM now.</summary>
    static int CurrentSet(IMemory m)
    {
        int set = m.ReadU8(ResidentSet);
        if (set < 8) return set;
        int area = m.ReadU8(ResidentArea);
        return area < 8 ? area : -1;
    }

    static void UploadTextures(CpuContext c, IMemory m, int id, int area, HashSet<(int X, int Y)> cluts)
    {
        var homes = area >= 0 ? new List<int> { area }
                  : Models.HomeArea.TryGetValue(id, out int home) ? new List<int> { home } : Disc.HomeAreas(id);
        var pairs = new List<Disc.Pair>();
        if (homes.Count > 0 && !homes.Contains(CurrentSet(m)))
            foreach (var p in Disc.Pairs(homes[0]))
                if ((id >= 128 || (p.Clut.Y >= 256 && p.Image.Y >= 256)) && cluts.Contains((p.Clut.X, p.Clut.Y))) pairs.Add(p);

        // Textures a model takes from a map region's set rather than its area's.
        if (Models.ExtraTextures.TryGetValue(id, out var extra))
            foreach (var (eset, cx, cy) in extra)
                foreach (var p in Disc.Pairs(eset))
                    if (p.Clut.X == cx && p.Clut.Y == cy) pairs.Add(p);
        if (pairs.Count == 0) return;

        // Every rect that will be written, once, to be saved first.
        var rects = new List<Disc.Block>();
        var seen = new HashSet<(int, int, int, int)>();
        foreach (var p in pairs)
            foreach (var blk in new[] { p.Clut, p.Image })
                if (seen.Add((blk.X, blk.Y, blk.W, blk.H))) rects.Add(blk);

        int saveBytes = 0, loadBytes = 0;
        foreach (var r in rects) saveBytes += 8 + r.Bytes;
        foreach (var p in pairs) loadBytes += 16 + p.Clut.Bytes + p.Image.Bytes;
        uint save = Alloc(c, m, saveBytes), load = save == 0 ? 0 : Alloc(c, m, loadBytes);
        if (save == 0 || load == 0) { Console.WriteLine($"[encyclopedia] no room to swap model {id}'s textures in"); return; }

        // Save. Each rect and its pixels at an address of their own, so nothing a
        // transfer still reads is written over before it is done.
        uint at = save;
        foreach (var r in rects)
        {
            WriteRect(m, at, r);
            Call(c, m, KingsField2.func_80060660, at, at + 8);   // StoreImage
            _saved.Add((at, at + 8));
            at += 8 + (uint)r.Bytes;
        }
        Call(c, m, KingsField2.func_800605A4, 0);                // DrawSync

        // Upload, in the set's own order.
        at = load;
        foreach (var p in pairs)
            foreach (var blk in new[] { p.Clut, p.Image })
            {
                WriteRect(m, at, blk);
                m.LoadBytes(at + 8, Disc.Bytes(blk).ToArray());
                Call(c, m, KingsField2.func_80060624, at, at + 8);   // LoadImage
                at += 8 + (uint)blk.Bytes;
            }
        Call(c, m, KingsField2.func_800605A4, 0);
        if (!_heap) _cursor = load;   // the staging is spent: the model's packets can have it
    }

    static void WriteRect(IMemory m, uint at, Disc.Block b)
    {
        m.WriteU16(at, (ushort)b.X);
        m.WriteU16(at + 2, (ushort)b.Y);
        m.WriteU16(at + 4, (ushort)b.W);
        m.WriteU16(at + 6, (ushort)b.H);
    }

    static void RestoreVram(CpuContext c, IMemory m)
    {
        if (_saved.Count == 0) return;
        for (int i = _saved.Count - 1; i >= 0; i--)
            Call(c, m, KingsField2.func_80060624, _saved[i].Rect, _saved[i].Data);   // LoadImage
        Call(c, m, KingsField2.func_800605A4, 0);
        _saved.Clear();
    }

    // ================================================================
    // Drawing
    // ================================================================

    static Action<CpuContext, IMemory>? _geomScreen;
    static bool _geomLooked;

    /// <summary>SetGeomScreen (0x8005B2D4), by address: its generated name is not
    /// one any of the port's own patches use.</summary>
    static void GeomScreen(CpuContext c, IMemory m, uint h)
    {
        if (!_geomLooked)
        {
            _geomLooked = true;
            var mi = SymbolRegistry.Resolve("game", null, 0x8005B2D4);
            if (mi != null) _geomScreen = (Action<CpuContext, IMemory>)Delegate.CreateDelegate(typeof(Action<CpuContext, IMemory>), mi);
            else Console.WriteLine("[encyclopedia] SetGeomScreen was not found; the menu's projection is used");
        }
        if (_geomScreen != null) Call(c, m, _geomScreen, h);
    }

    /// <summary>One frame of the model on show, where the Items page draws its preview.</summary>
    static void DrawModel(CpuContext c, IMemory m)
    {
        if (!_ok) return;
        if (_item >= 0) { Call(c, m, KingsField2.func_8002156C, (uint)_item); return; }
        if (_pieces.Count == 0) return;
        uint fog = m.ReadU32(FogWord), mesh0 = m.ReadU32(CurrentMesh), verts0 = m.ReadU32(VertexBase);

        // The packets go in the free RAM's tail, by pointing the frame's
        // {start, end, current} there for the length of the draw: the menu's own
        // buffer is too small for a creature's hundreds of faces.
        uint desc = m.ReadU32(ActiveDescriptor);
        uint d0 = m.ReadU32(desc), d1 = m.ReadU32(desc + 4), d2 = m.ReadU32(desc + 8);
        uint pk = (_cursor + 15u) & ~15u;
        bool own = !_heap && _arenaEnd > pk + 0x4000u;
        if (own)
        {
            m.WriteU32(desc, pk);
            m.WriteU32(desc + 4, _arenaEnd - 0x100u);
            m.WriteU32(desc + 8, pk);
        }
        else
        {
            m.WriteU32(desc + 8, m.ReadU32(Cursor));   // after the menu's quads, at least
        }
        try
        {
            var tune = _tuneOverride ?? Models.TuneOf(_shown);
            short tilt = (short)tune.Tilt;
            // A model seen only from the front stands at its facing; the rest turn from it.
            ushort yaw = (ushort)((tune.Fixed ? tune.Facing : _yaw + tune.Facing) & 0xFFF);
            _yaw = (_yaw + TurnSpeed) & 0xFFF;

            uint mat = _v + VMat;
            double s = Math.Clamp(FitSize / _fit * tune.Scale, 1.0 / 64, 7.5);
            var e = PieceMatrix(c, m, tilt, yaw, s);
            Call(c, m, KingsField2.SetColorMatrix, _v + VColor);

            // func_800346CC's own state.
            Call(c, m, KingsField2.SetBackColor_game, 0x3C, 0x3C, 0x3C);
            GeomScreen(c, m, 0xC8);
            Call(c, m, KingsField2.func_8002DDDC, 0x59D8);

            for (int i = 0; i < _pieces.Count; i++)
            {
                var p = _pieces[i];
                if (i > 0) PieceMatrix(c, m, tilt, yaw, s);   // the blender and assembler leave the matrices changed
                // Scaled to fit, and placed so the pieces' centre sits where an item's origin does.
                double px = p.Dx - _cx, py = p.Dy - _cy, pz = p.Dz - _cz;
                double tx = tune.X + (e[0] * px + e[1] * py + e[2] * pz) / 4096.0;
                double ty = tune.Y + (e[3] * px + e[4] * py + e[5] * pz) / 4096.0;
                double tz = 1000 + (e[6] * px + e[7] * py + e[8] * pz) / 4096.0;
                m.WriteU32(mat + 0x14, (uint)(int)Math.Round(tx));
                m.WriteU32(mat + 0x18, (uint)(int)Math.Round(ty));
                m.WriteU32(mat + 0x1C, (uint)(int)Math.Round(tz));
                Call(c, m, KingsField2.SetRotMatrix, mat);
                Call(c, m, KingsField2.SetTransMatrix, mat);
                DrawPiece(c, m, p);
            }
        }
        catch (Exception ex)
        {
            _ok = false;
            Console.WriteLine($"[encyclopedia] drawing model {_shown}: {ex}");
        }
        finally
        {
            m.WriteU32(FogWord, fog);
            m.WriteU32(CurrentMesh, mesh0);
            m.WriteU32(VertexBase, verts0);
            if (own)
            {
                m.WriteU32(desc, d0);
                m.WriteU32(desc + 4, d1);
                m.WriteU32(desc + 8, d2);
            }
            // func_8002156C's last step: the menu's quad cursor taken from the frame's
            // current, past the model's packets. With the free RAM's own buffer the
            // frame's current never moved, and neither did the cursor.
            if (!own) m.WriteU32(Cursor, m.ReadU32(desc + 8));
        }
    }

    /// <summary>The rotation, scaled, and the light turned with it; the scaled
    /// matrix's elements are returned for placing the pieces.</summary>
    static int[] PieceMatrix(CpuContext c, IMemory m, short tilt, ushort yaw, double s)
    {
        uint mat = _v + VMat, rot = _v + VRot;
        m.WriteU16(rot, (ushort)tilt);
        m.WriteU16(rot + 2, yaw);
        m.WriteU16(rot + 4, 0);
        Call(c, m, KingsField2.RotMatrix, rot, mat);
        Call(c, m, KingsField2.MulMatrix0, _v + VLight, mat, _v + VLightOut);   // the light before the scale
        Call(c, m, KingsField2.SetLightMatrix, _v + VLightOut);
        var e = new int[9];
        for (uint i = 0; i < 9; i++)
        {
            e[i] = Math.Clamp((int)Math.Round((short)m.ReadU16(mat + i * 2) * s), -32768, 32767);
            m.WriteU16(mat + i * 2, (ushort)(short)e[i]);
        }
        return e;
    }

    static void DrawPiece(CpuContext c, IMemory m, Piece p)
    {
        uint id = (uint)p.Spare;
        Call(c, m, KingsField2.func_80034834, id);           // the model's TMD current
        if (p.Clips > 0)
        {
            uint mesh = Call(c, m, KingsField2.func_8002E1BC, 0);
            uint count = m.ReadU32(mesh + 4);
            m.WriteU32(c.SP + 0x10, count);                    // the blender's stack argument
            int hold = Models.TuneOf(p.Model).Hold;
            uint posed = Call(c, m, KingsField2.func_80034DA8, p.Slot, id, (uint)p.Clip, (uint)(hold >= 0 ? hold : p.Time));
            if (posed == 0) Call(c, m, KingsField2.func_8002E1F0, 0);   // no record free: the rest pose
            Call(c, m, KingsField2.func_8002E650, count);
            Call(c, m, KingsField2.func_8002F214, 0, 0);
            if (hold < 0) p.Time = (p.Time + AnimSpeed) & 0xFFF;
        }
        else
        {
            for (uint k = 0; k < (uint)p.Objects; k++)
            {
                Call(c, m, KingsField2.func_8002E1F0, k);
                uint mesh = Call(c, m, KingsField2.func_8002E1BC, k);
                Call(c, m, KingsField2.func_8002E650, m.ReadU32(mesh + 4));
                Call(c, m, KingsField2.func_8002F214, k, 0);
            }
        }
    }
}
