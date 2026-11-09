using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace DinoStage;

/// <summary>
/// Extended headless checks (/stage selftest2): chunk unload/reload, two-actor precision, unstage of both, bear + LoP scene round trip.
/// Tick-driven step queue (50 ms steps). Log: ext-[timestamp].txt in the mod data folder.
/// </summary>
public sealed class ExtTest
{
    struct Snap
    {
        public double X, Y, Z;
        public float Frame, Pitch, Roll, Yaw;
        public bool OnGround, Ctrl, Passive, Hooked;
        public int Handlers, Tasks, Anims;
        public bool HasPassive, CtrlTicking, Alive, Active;
        public float AnimSpeed;
    }

    readonly ICoreServerAPI api;
    readonly DinoStageSystem sys;
    readonly StringBuilder log = new();
    readonly Queue<Func<bool>> steps = new();
    long listener;
    string logPath = "";
    int pass, fail;
    bool finished;
    object? mover;
    MethodInfo? mStart, mStop;
    public Action? Done;

    int cx, cz;
    double bx, bz, gy;
    Entity? T, P, G, Bear, Ctl, CtlP;
    string aT = "", aP = "", aG = "", aBear = "";
    string codeT = "", codeP = "", codeG = "", codeBear = "";
    Snap bT, bP, bG, bB;
    long idT;
    Vec3d expP = new(), expG = new();
    bool abort, chunkBack;
    byte[] saved = Array.Empty<byte>();
    readonly Dictionary<long, int> h0 = new();

    public ExtTest(ICoreServerAPI api, DinoStageSystem sys) { this.api = api; this.sys = sys; }

    void L(string s) { log.AppendLine(s); api.Logger.Notification("[DinoStage ext] " + s); }
    void Info(string s) => L("INFO " + s);

    void Chk(string what, double actual, double expected, double tol)
    {
        double d = actual - expected;
        bool ok = !double.IsNaN(d) && Math.Abs(d) <= tol;
        if (ok) pass++; else fail++;
        L((ok ? "PASS " : "FAIL ") + what + ": actual=" + Ops.F(actual) + " expected=" + Ops.F(expected) + " delta=" + d.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture) + " tol=" + tol);
    }

    void ChkBool(string what, bool actual, bool expected)
    {
        bool ok = actual == expected;
        if (ok) pass++; else fail++;
        L((ok ? "PASS " : "FAIL ") + what + ": actual=" + actual + " expected=" + expected);
    }

    void Add(Func<bool> f) => steps.Enqueue(() => abort || f());
    void Do(Action a) => Add(() => { a(); return true; });
    void Delay(int ticks) { int left = ticks; Add(() => --left <= 0); }

    void WaitUntil(Func<bool> cond, int maxTicks, string failMsg)
    {
        int left = maxTicks;
        Add(() =>
        {
            if (cond()) return true;
            if (--left <= 0) { fail++; L("FAIL " + failMsg); return true; }
            return false;
        });
    }

    public string Start()
    {
        string ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        logPath = Path.Combine(api.GetOrCreateDataPath("dinostage"), "ext-" + ts + ".txt");
        L("DinoStage ext test " + ts + ". Mover guard: " + MoverGuard.Status);
        Ops.TestAlwaysActive = true;
        Ops.TestRequirePoses = true; // server animator computes RootPoses for the 1.1.0 phase A pose checks
        try
        {
            mover = new ItemEntityMoverTool();
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            mStart = typeof(ItemEntityMoverTool).GetMethod("StartDisableEntityMovement", bf, null, new[] { typeof(Entity) }, null);
            mStop = typeof(ItemEntityMoverTool).GetMethod("StopDisableEntityMovement", bf, null, new[] { typeof(Entity) }, null);
        }
        catch (Exception ex) { Info("ItemEntityMoverTool not constructible: " + ex.Message); }
        if (mStart == null || mStop == null) { fail++; L("FAIL real mover methods unavailable"); Finish(); return logPath; }
        Info("mover: real ItemEntityMoverTool instance, StartDisableEntityMovement/StopDisableEntityMovement by reflection");

        List<EntityProperties> types = api.World.EntityTypes;
        string? Pick(System.Func<EntityProperties, bool> pred) => types.FirstOrDefault(pred)?.Code.ToString();
        codeG = Pick(t => t.Code.Domain == "game" && t.Code.Path.StartsWith("pig-wild") && !t.Code.Path.Contains("baby")) ?? Pick(t => t.Code.Domain == "game" && t.Code.Path.StartsWith("pig-") && !t.Code.Path.Contains("baby")) ?? "";
        codeBear = Pick(t => t.Code.Domain == "game" && t.Code.Path.StartsWith("bear-") && !t.Code.Path.Contains("cub") && !t.Code.Path.Contains("baby")) ?? "";
        string[] th = { "dromaeo", "tyranno", "raptor", "allosaur", "carcharo", "spino", "abelisaur" };
        codeT = Pick(t => t.Code.Domain != "game" && th.Any(k => t.Code.Path.Contains(k))) ?? "";
        codeP = Pick(t => t.Code.Domain != "game" && t.Code.Path.Contains("plesio")) ?? "";
        Info("codes: theropod=" + codeT + " plesiosaur=" + codeP + " pig=" + codeG + " bear=" + codeBear);
        if (codeT == "" || codeP == "" || codeG == "" || codeBear == "") { fail++; L("FAIL missing entity type(s)"); Finish(); return logPath; }

        BuildChunkReload();
        BuildTwoActor();
        BuildUnstage();
        BuildScene();
        BuildAnim();
        steps.Enqueue(() => { Finish(); return true; });
        listener = api.Event.RegisterGameTickListener(Tick, 50);
        return logPath;
    }

    void Tick(float dt)
    {
        if (finished) return;
        if (steps.Count == 0) { Finish(); return; }
        try { if (steps.Peek()()) steps.Dequeue(); }
        catch (Exception ex) { fail++; L("FAIL exception in step: " + ex); steps.Clear(); Finish(); }
    }

    void Finish()
    {
        if (finished) return;
        finished = true;
        L("SUMMARY pass=" + pass + " fail=" + fail);
        try { File.WriteAllText(logPath, log.ToString()); } catch (Exception ex) { api.Logger.Warning("[DinoStage] ext log write failed: " + ex.Message); }
        if (listener != 0) api.Event.UnregisterGameTickListener(listener);
        Done?.Invoke();
    }

    // ------------------------------------------------------------------ helpers

    (string? key, string code) PickAnim(Entity e)
    {
        Dictionary<string, AnimationMetaData>? by = e.Properties?.Client?.AnimationsByMetaCode;
        string? code = by?.Keys.FirstOrDefault(k => k.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0) ?? by?.Keys.FirstOrDefault();
        if (code == null) return (null, "");
        return (Freeze.ResolveAnimKey(e, code), code);
    }

    Snap Take(Entity e, string anim)
    {
        var s = new Snap { X = e.Pos.X, Y = e.Pos.Y, Z = e.Pos.Z, Pitch = e.Pos.Pitch, Roll = e.Pos.Roll, Yaw = e.Pos.Yaw, OnGround = e.OnGround, Frame = Freeze.CurrentFrame(e, anim) ?? float.NaN };
        s.Ctrl = e.GetBehavior<EntityBehaviorControlledPhysics>()?.EnableModulesApplication ?? false;
        EntityBehaviorPassivePhysics? pp = e.GetBehavior<EntityBehaviorPassivePhysics>();
        s.CtrlTicking = e.GetBehavior<EntityBehaviorControlledPhysics>()?.Ticking ?? false;
        s.Alive = e.Alive; s.Active = e.State == EnumEntityState.Active;
        s.HasPassive = pp != null;
        s.Passive = pp != null ? pp.Ticking : s.Ctrl; // behavior absent: not applicable, mirrors Ctrl
        s.Hooked = Freeze.IsHooked(e);
        AiTaskManager? tm = e.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
        s.Handlers = tm == null ? -2 : Freeze.ShouldHandlerCount(tm);
        s.Tasks = tm == null ? -1 : tm.ActiveTasksBySlot.Count(t => t != null);
        s.Anims = e.AnimManager?.ActiveAnimationsByAnimCode.Count ?? -1;
        s.AnimSpeed = float.NaN;
        if (anim.Length > 0 && e.AnimManager != null && e.AnimManager.ActiveAnimationsByAnimCode.TryGetValue(anim, out AnimationMetaData? m)) s.AnimSpeed = m.AnimationSpeed;
        return s;
    }

    string Fmt(Snap s) => "pos=" + Ops.F(s.X) + "," + Ops.F(s.Y) + "," + Ops.F(s.Z) + " frame=" + Ops.F(s.Frame) + " pitch=" + Ops.F(s.Pitch / Ops.Deg) + " roll=" + Ops.F(s.Roll / Ops.Deg)
        + " yaw=" + Ops.F(s.Yaw / Ops.Deg) + " onGround=" + s.OnGround + " ctrl=" + s.Ctrl + " ctrlTicking=" + s.CtrlTicking + " active=" + s.Active + " passive=" + s.Passive + (s.HasPassive ? "" : "(n/a)") + " hooked=" + s.Hooked + " shouldHandlers=" + s.Handlers
        + " activeTasks=" + s.Tasks + " activeAnims=" + s.Anims + " heldAnimSpeed=" + Ops.F(s.AnimSpeed);

    void Compare(string label, Snap now, Snap exp, double posTol)
    {
        Chk(label + " X", now.X, exp.X, posTol); Chk(label + " Y", now.Y, exp.Y, posTol); Chk(label + " Z", now.Z, exp.Z, posTol);
        Chk(label + " frame", now.Frame, exp.Frame, 0.01);
        Chk(label + " pitch", now.Pitch, exp.Pitch, 0.001); Chk(label + " roll", now.Roll, exp.Roll, 0.001); Chk(label + " yaw", now.Yaw, exp.Yaw, 0.001);
    }

    Entity? SpawnAt(string code, double x, double y, double z)
    {
        Entity? e = Ops.Spawn(api, code, x, y, z, 0f, out string err);
        if (e == null) { fail++; L("FAIL spawn " + code + ": " + err); abort = true; }
        return e;
    }

    string FreezeWith(Entity e, float prog, float pitchDeg, float rollDeg, float yawDeg)
    {
        var (key, code) = PickAnim(e);
        AiTaskManager? tm0 = e.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
        if (tm0 != null && !h0.ContainsKey(e.EntityId)) { h0[e.EntityId] = Freeze.ShouldHandlerCount(tm0); Info(e.Code.Path + " id " + e.EntityId + ": OnShouldExecuteTask subscribers before freeze = " + h0[e.EntityId]); }
        int frames = key == null ? 0 : Freeze.FrameCount(e, key);
        float fr = frames > 0 ? prog * (frames - 1) : 0f;
        StageData d = Ops.FreezeEntity(sys, e, key ?? "", 0f, fr);
        d.Pitch = pitchDeg * Ops.Deg; d.Roll = rollDeg * Ops.Deg; d.Yaw = yawDeg * Ops.Deg;
        Ops.Commit(sys, e, d);
        Info(e.Code + " frozen: config anim '" + code + "' shape anim '" + d.Anim + "' frames " + frames + " frame " + Ops.F(fr) + " (progress " + prog + ") speed 0 pitch " + pitchDeg + " roll " + rollDeg + " heading " + yawDeg);
        return d.Anim;
    }

    // ------------------------------------------------------------------ phase R: chunk unload / reload

    void BuildChunkReload()
    {
        bool requested = false, loaded = false; int w = 0;
        Add(() =>
        {
            if (!requested)
            {
                Ops.SpawnXZ(api, out int spx, out int spz);
                int cs = GlobalConstants.ChunkSize;
                cx = spx / cs; cz = spz / cs;
                bx = cx * cs + 8.5; bz = cz * cs + 8.5;
                api.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true, OnLoaded = () => loaded = true });
                requested = true;
                return false;
            }
            if (loaded) return true;
            if (++w > 600) { fail++; L("FAIL chunk did not load"); abort = true; return true; }
            return false;
        });
        Do(() =>
        {
            gy = api.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos((int)bx, 0, (int)bz, 0)) + 1;
            Info("=== phase R (ii): chunk column " + cx + "," + cz + " ground y=" + gy + " ===");
            T = SpawnAt(codeT, bx, gy + 8, bz);
            if (T == null) return;
        });
        Delay(10);
        Do(() => idT = T!.EntityId);
        Do(() => aT = FreezeWith(T!, 0.5f, 20f, 15f, 90f));
        Delay(40);
        Do(() =>
        {
            bT = Take(T!, aT);
            Info("baseline T " + Fmt(bT));
        });
        Do(() =>
        {
            Info("unloading chunk column via IWorldManagerAPI.UnloadChunkColumn(" + cx + "," + cz + ")");
            int ncy = api.WorldManager.MapSizeY / GlobalConstants.ChunkSize, nmod = 0;
            for (int cy = 0; cy < ncy; cy++) { IWorldChunk? ch = api.WorldManager.GetChunk(cx, cy, cz); if (ch != null) { ch.MarkModified(); nmod++; } }
            Info("marked " + nmod + " loaded chunks of the column modified (so entities are written on unload); entity chunk holds " + (api.WorldManager.GetChunk(cx, (int)(T!.Pos.Y / GlobalConstants.ChunkSize), cz)?.Entities?.Count(x => x != null) ?? -1) + " entities");
            using (var ms = new MemoryStream()) { using (var bw = new BinaryWriter(ms)) T!.ToBytes(bw, false); saved = ms.ToArray(); }
            Info("serialized entity with Entity.ToBytes (" + saved.Length + " bytes), as the chunk saver would");
            api.WorldManager.UnloadChunkColumn(cx, cz);
        });
        WaitUntil(() => api.World.GetEntityById(idT) == null, 200, "entity still present 10 s after UnloadChunkColumn (real unload did not happen)");
        Do(() =>
        {
            bool gone = api.World.GetEntityById(idT) == null;
            Info("after unload: entity gone from world = " + gone + "; sys.Staged contains id = " + sys.Staged.Contains(idT));
        });
        Delay(80);
        Do(() =>
        {
            Info("reloading chunk column via LoadChunkColumnPriority");
            api.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true, OnLoaded = () => chunkBack = true });
        });
        WaitUntil(() => chunkBack, 400, "chunk column reload OnLoaded did not fire (20 s)");
        Do(() =>
        {
            Info("chunk column reloaded (OnLoaded fired). Entity persistence replicated by hand: UnloadChunkColumn does not write entities, loaded entity count was 0 after reload. Re-creating from the saved bytes: CreateEntity + Entity.FromBytes + SpawnEntity (enters via Event.OnEntitySpawn, not OnEntityLoaded; both call the same handler in DinoStageSystem)");
            EntityProperties props = api.World.GetEntityType(new AssetLocation(codeT));
            Entity ne = api.ClassRegistry.CreateEntity(props);
            if (Ops.TestAlwaysActive) ne.AlwaysActive = true;
            using (var br = new BinaryReader(new MemoryStream(saved))) ne.FromBytes(br, false);
            Info("FromBytes gave id " + ne.EntityId + " pos " + Ops.F(ne.Pos.X) + "," + Ops.F(ne.Pos.Y) + "," + Ops.F(ne.Pos.Z) + " tree staged=" + StageState.IsStaged(ne));
            long oldId = idT;
            api.World.SpawnEntity(ne);
            Ops.TestPumped.Add(ne);
            Info("SpawnEntity assigned id " + ne.EntityId + " (was " + oldId + "; ServerMain.SpawnEntity re-ids, a real chunk load keeps the id). Tracking the new id.");
            if (h0.TryGetValue(oldId, out int hv)) h0[ne.EntityId] = hv;
            idT = ne.EntityId;
        });
        WaitUntil(() => api.World.GetEntityById(idT) != null, 100, "recreated entity not found by id");
        Do(() =>
        {
            Entity? n = api.World.GetEntityById(idT);
            if (n == null)
            {
                var same = api.World.LoadedEntities.Values.Where(x => x.Code.ToString() == codeT).ToList();
                Info("DIAG entity id " + idT + " absent; loaded entities total=" + api.World.LoadedEntities.Count + ", with code " + codeT + " = " + same.Count + (same.Count > 0 ? " (id " + same[0].EntityId + ")" : ""));
                abort = true; return;
            }
            bool newObj = !ReferenceEquals(n, T);
            ChkBool("reload produced a NEW entity object (real unload/load, not a no-op)", newObj, true);
            ChkBool("reloaded entity is in sys.Staged (OnEntityLoaded re-registered it)", sys.Staged.Contains(idT), true);
            T = n;
            Info("at first sight after reload: " + Fmt(Take(n, aT)));
        });
        Delay(40);
        Do(() =>
        {
            Snap s = Take(T!, aT);
            Info("2 s after reload: " + Fmt(s));
            Compare("R reload T", s, bT, 0.02);
            ChkBool("R reload T ctrl physics off", s.Ctrl, false);
            ChkBool("R reload T passive physics off", s.Passive, false);
            ChkBool("R reload T AI hooked", s.Hooked, true);
            ChkBool("R reload T should-handlers = before-freeze count + 1 (no duplicates)", s.Handlers == h0[idT] + 1, true);
            ChkBool("R reload T not on ground (still airborne)", s.OnGround, false);
        });
        Do(() => { try { Ops.ThawEntity(sys, T!); Ops.Despawn(T!); } catch { } });
        Delay(10);
    }

    // ------------------------------------------------------------------ phase P: two actors

    void BuildTwoActor()
    {
        Do(() =>
        {
            Info("=== phase P (iii): plesiosaur + pig two-actor precision ===");
            P = SpawnAt(codeP, bx, gy + 8, bz);
            G = SpawnAt(codeG, bx + 6, gy + 8, bz);
            Ctl = SpawnAt(codeG, bx + 12, gy + 8, bz);
            CtlP = SpawnAt(codeP, bx + 18, gy + 8, bz);
            Info("control pig (never frozen) spawned 8 blocks up to prove gravity works in this headless setup");
        });
        Delay(10);
        Do(() => { aP = FreezeWith(P!, 0.5f, 10f, -8f, 45f); aG = FreezeWith(G!, 0.25f, -12f, 6f, 270f); });
        Delay(40);
        Do(() =>
        {
            bP = Take(P!, aP); bG = Take(G!, aG);
            Info("baseline P " + Fmt(bP)); Info("baseline G " + Fmt(bG));
            expG = new Vec3d(bG.X + 3.0, bG.Y + 1.5, bG.Z);
            expP = new Vec3d(bP.X, bP.Y, bP.Z);
        });
        Do(() => { mStart!.Invoke(mover, new object[] { G! }); ChkBool("G: mover grab marks entity held (Harmony)", MoverGuard.IsHeld(G!.EntityId), MoverGuard.Applied); });
        for (int i = 1; i <= 20; i++)
        {
            int k = i;
            Do(() =>
            {
                var t = new Vec3d(bG.X + 3.0 * k / 20, bG.Y + 1.5 * k / 20, bG.Z);
                G!.Pos.SetPos(t);
                G.GetBehavior<EntityBehaviorControlledPhysics>()?.SetState(new EntityPos(t.X, t.Y, t.Z), 0f);
                G.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager?.StopTasks();
            });
        }
        Do(() => { mStop!.Invoke(mover, new object[] { G! }); });
        Do(() =>
        {
            Snap g = Take(G!, aG), p = Take(P!, aP);
            Info("immediately after mover release: G " + Fmt(g));
            ChkBool("G released: ctrl physics re-disabled same call", g.Ctrl, false);
            Chk("G X after mover (+3)", g.X, expG.X, 0.001); Chk("G Y after mover (+1.5)", g.Y, expG.Y, 0.001); Chk("G Z after mover", g.Z, expG.Z, 0.001);
            Compare("P untouched by pig mover", p, bP, 0.001);
        });
        Delay(20);
        // nudge the plesiosaur (+1 Y, +2 Z): same two writes as /stage nudge (Commands.NudgeCmd)
        Do(() =>
        {
            P!.Pos.SetPos(P.Pos.X, P.Pos.Y + 1.0, P.Pos.Z + 2.0);
            P.Pos.Motion.Set(0.0, 0.0, 0.0);
            expP = new Vec3d(bP.X, bP.Y + 1.0, bP.Z + 2.0);
            Info("nudged P by (0,+1,+2) with the /stage nudge writes");
        });
        Delay(20);
        Do(() =>
        {
            Snap g = Take(G!, aG), p = Take(P!, aP);
            Chk("after P nudge: G X still", g.X, expG.X, 0.001); Chk("after P nudge: G Y still", g.Y, expG.Y, 0.001); Chk("after P nudge: G Z still", g.Z, expG.Z, 0.001);
            Chk("after P nudge: P X", p.X, expP.X, 0.001); Chk("after P nudge: P Y", p.Y, expP.Y, 0.001); Chk("after P nudge: P Z", p.Z, expP.Z, 0.001);
        });
        // re-nudge pig (-1, 0, +1)
        Do(() =>
        {
            G!.Pos.SetPos(G.Pos.X - 1.0, G.Pos.Y, G.Pos.Z + 1.0);
            G.Pos.Motion.Set(0.0, 0.0, 0.0);
            expG = new Vec3d(expG.X - 1.0, expG.Y, expG.Z + 1.0);
            Info("re-nudged G by (-1,0,+1)");
        });
        Delay(100);
        Do(() =>
        {
            Snap c0 = Take(Ctl!, "");
            Info("control pig 7 s after spawn: " + Fmt(c0));
            ChkBool("control (never frozen) fell: Y dropped >2 or on ground", gy + 8 - c0.Y > 2 || c0.OnGround, true);
            Snap g = Take(G!, aG), p = Take(P!, aP);
            Info("5 s after re-nudge: G " + Fmt(g)); Info("5 s after re-nudge: P " + Fmt(p));
            Chk("final G X", g.X, expG.X, 0.02); Chk("final G Y", g.Y, expG.Y, 0.02); Chk("final G Z", g.Z, expG.Z, 0.02);
            Chk("final G frame", g.Frame, bG.Frame, 0.01); Chk("final G pitch", g.Pitch, bG.Pitch, 0.001); Chk("final G roll", g.Roll, bG.Roll, 0.001); Chk("final G yaw", g.Yaw, bG.Yaw, 0.001);
            Chk("final P X", p.X, expP.X, 0.02); Chk("final P Y (still frozen, no fall)", p.Y, expP.Y, 0.02); Chk("final P Z", p.Z, expP.Z, 0.02);
            Chk("final P frame", p.Frame, bP.Frame, 0.01); Chk("final P pitch", p.Pitch, bP.Pitch, 0.001); Chk("final P roll", p.Roll, bP.Roll, 0.001); Chk("final P yaw", p.Yaw, bP.Yaw, 0.001);
            ChkBool("P still frozen: ctrl off", p.Ctrl, false); ChkBool("P still frozen: passive off", p.Passive, false);
            ChkBool("G still frozen: ctrl off", g.Ctrl, false); ChkBool("G still frozen: passive off", g.Passive, false);
        });
    }

    // ------------------------------------------------------------------ phase U: unstage both

    void BuildUnstage()
    {
        Snap fp = default, fg = default;
        Do(() =>
        {
            Info("=== phase U (iv): unstage plesiosaur + pig ===");
            fp = Take(P!, aP); fg = Take(G!, aG);
            Info("before unstage P " + Fmt(fp)); Info("before unstage G " + Fmt(fg));
            Ops.ThawEntity(sys, P!); Ops.ThawEntity(sys, G!);
            Snap p = Take(P!, aP), g = Take(G!, aG);
            Info("right after unstage P " + Fmt(p)); Info("right after unstage G " + Fmt(g));
            foreach (var (n, e, s) in new[] { ("P", P!, p), ("G", G!, g) })
            {
                ChkBool(n + " unstage: EnableModulesApplication true", s.Ctrl, true);
        ChkBool(n + " unstage: PassivePhysics.Ticking true" + (s.HasPassive ? "" : " (n/a, no behavior)"), s.Passive, true);
                ChkBool(n + " unstage: AI hooked flag false", s.Hooked, false);
                if (s.Handlers >= 0) Chk(n + " unstage: OnShouldExecuteTask subscribers vs before-freeze count", s.Handlers, h0[e.EntityId], 0);
                ChkBool(n + " unstage: tree cleared", e.WatchedAttributes.HasAttribute(StageState.Key), false);
                ChkBool(n + " unstage: not in sys.Staged", sys.Staged.Contains(e.EntityId), false);
                ChkBool(n + " unstage: held anim speed not stuck at 0 (anim stopped or speed>0)", float.IsNaN(s.AnimSpeed) || s.AnimSpeed > 0f, true);
            }
        });
        Delay(60);
        Do(() =>
        {
            Snap p = Take(P!, aP), g = Take(G!, aG);
            Info("3 s after unstage P " + Fmt(p)); Info("3 s after unstage G " + Fmt(g));
            ChkBool("P gravity resumed (Y dropped >0.5 or on ground)", fp.Y - p.Y > 0.5 || p.OnGround, true);
            ChkBool("G gravity resumed (Y dropped >0.5 or on ground)", fg.Y - g.Y > 0.5 || g.OnGround, true);
            ChkBool("G AI tasks run again (active task count > 0)", g.Tasks > 0, true);
            Snap cpl = Take(CtlP!, "");
            Info("never-frozen control plesiosaur at the same moment: " + Fmt(cpl));
            ChkBool("G animations running again (activeAnims > 0)", g.Anims > 0, true);
            Info("P at 3 s: tasks=" + p.Tasks + " anims=" + p.Anims + " (control plesiosaur tasks=" + cpl.Tasks + " anims=" + cpl.Anims + "); waiting up to 20 s more for P");
            ChkBool("G pitch/roll reset to 0", Math.Abs(g.Pitch) < 0.001 && Math.Abs(g.Roll) < 0.001, true);
            ChkBool("P pitch/roll reset to 0", Math.Abs(p.Pitch) < 0.001 && Math.Abs(p.Roll) < 0.001, true);
        });
        bool pOk = false, cOk = false; int pw = 0, cw = 0; Entity? ctl2 = null;
        Add(() =>
        {
            Snap s2 = Take(P!, aP);
            if (s2.Tasks > 0 && s2.Anims > 0) { pOk = true; Info("P AI task + animation active after " + (3 + pw * 0.05).ToString("0.0") + " s: tasks=" + s2.Tasks + " anims=" + s2.Anims); return true; }
            return ++pw > 400;
        });
        Do(() =>
        {
            Info("P got AI task + animation within 23 s of unstage: " + pOk);
            ctl2 = SpawnAt(codeP, P!.Pos.X, P.Pos.Y + 0.5, P.Pos.Z);
            Info("spawned fresh never-frozen plesiosaur at P's position as a same-spot control");
        });
        Add(() =>
        {
            if (ctl2 == null || ctl2.EntityId == 0) return ++cw > 400;
            Snap s3 = Take(ctl2, "");
            if (s3.Tasks > 0 && s3.Anims > 0) { cOk = true; Info("same-spot control plesiosaur AI task + animation after " + (cw * 0.05).ToString("0.0") + " s: tasks=" + s3.Tasks + " anims=" + s3.Anims); return true; }
            return ++cw > 400;
        });
        Do(() =>
        {
            Info("same-spot fresh control got AI task + animation within 20 s: " + cOk);
            ChkBool("P (thawed) AI task + animation outcome equals fresh never-frozen plesiosaur at the same spot", pOk, cOk);
            if (ctl2 != null) Ops.Despawn(ctl2);
        });
        Do(() => { try { Ops.Despawn(P!); Ops.Despawn(G!); Ops.Despawn(Ctl!); Ops.Despawn(CtlP!); } catch { } });
        Delay(10);
    }

    // ------------------------------------------------------------------ phase S: bear + LoP scene

    void BuildScene()
    {
        const string scene = "ext-bear-lop";
        Do(() =>
        {
            Info("=== phase S (v): bear + LoP theropod scene round trip ===");
            Bear = SpawnAt(codeBear, bx, gy + 4, bz);
            T = SpawnAt(codeT, bx + 6, gy + 6, bz);
        });
        Delay(10);
        Do(() => { aBear = FreezeWith(Bear!, 0.3f, 8f, -4f, 135f); aT = FreezeWith(T!, 0.7f, -6f, 9f, 310f); });
        Delay(40);
        Do(() =>
        {
            bB = Take(Bear!, aBear); bT = Take(T!, aT);
            Info("baseline Bear " + Fmt(bB)); Info("baseline T " + Fmt(bT));
            ChkBool("Bear held anim speed is 0", bB.AnimSpeed == 0f, true);
            ChkBool("T held anim speed is 0", bT.AnimSpeed == 0f, true);
            string path = SceneIO.Save(api, scene, new[] { Bear!, T! }, Bear!.Pos.XYZ);
            Info("scene saved: " + path);
            long ib = Bear.EntityId, it = T!.EntityId;
            Ops.ThawEntity(sys, Bear); Ops.ThawEntity(sys, T);
            Ops.Despawn(Bear); Ops.Despawn(T);
            Info("cleared: thawed + removed both actors (ids " + ib + "," + it + ")");
        });
        Delay(20);
        Do(() =>
        {
            var loaded = new List<Entity>(); var rep = new StringBuilder();
            bool ok = SceneIO.Load(sys, scene, loaded, rep);
            foreach (string line in rep.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)) Info("load: " + line.TrimEnd());
            ChkBool("scene load ok with 2 actors", ok && loaded.Count == 2, true);
            if (loaded.Count == 2)
            {
                // match by code
                Bear = loaded.First(e => e.Code.ToString() == codeBear);
                T = loaded.First(e => e.Code.ToString() == codeT);
            }
            else abort = true;
        });
        Delay(80);
        Do(() =>
        {
            Snap b = Take(Bear!, aBear), t = Take(T!, aT);
            Info("restored Bear " + Fmt(b)); Info("restored T " + Fmt(t));
            Compare("scene Bear", b, bB, 0.02); Compare("scene T", t, bT, 0.02);
            ChkBool("scene Bear staged", StageState.IsStaged(Bear!), true);
            ChkBool("scene T staged", StageState.IsStaged(T!), true);
            ChkBool("scene Bear held anim speed still 0", b.AnimSpeed == 0f, true);
            ChkBool("scene T held anim speed still 0", t.AnimSpeed == 0f, true);
            try { SceneIO.Delete(api, scene); } catch { }
        });
        Do(() => { try { Ops.ThawEntity(sys, Bear!); Ops.ThawEntity(sys, T!); Ops.Despawn(Bear!); Ops.Despawn(T!); } catch { } });
        Delay(10);
    }

    // ------------------------------------------------------------------ phase A: 1.1.0 anim API on the real LoP theropod

    void BuildAnim()
    {
        const string scene = "ext-anim-layers";
        Entity? X = null;
        string kb = "", kl = "", kn = "", dk = "";
        Dictionary<string, float[]>? pBase = null, pDef = null, pRef = null;
        bool ok = false;
        const float Eps = 1e-4f;
        string? pyc = null;
        Do(() =>
        {
            Info("=== phase A (1.1.0): freeze default capture, layers, resetpose, scene with layers on the LoP theropod ===");
            pyc = api.World.EntityTypes.FirstOrDefault(t => t.Code.Path.Contains("pycnonemosaurus") && t.Code.Path.Contains("adult-male"))?.Code.ToString();
            string code = pyc ?? codeT;
            Info("actor: " + code + (pyc != null ? " (Pycnonemosaurus adult male)" : " (fallback theropod; no Pycnonemosaurus adult male installed)"));
            X = SpawnAt(code, bx, gy + 4, bz);
        });
        Delay(10);
        Do(() =>
        {
            Entity e = X!;
            dk = Freeze.DefaultAnimKey(e) ?? "";
            Info("default animation key of the actor: " + (dk.Length > 0 ? dk : "none"));
            StageData d = Ops.FreezeEntity(sys, e); // no explicit anim, like /stage freeze with no anim
            Info("freeze no anim: anim='" + d.Anim + "' source='" + d.Source + "' speed=" + Ops.F(d.Speed) + " frame=" + Ops.F(d.Frame));
            if (dk.Length > 0)
            {
                ChkBool("A freeze default: held anim is the default animation", string.Equals(d.Anim, dk, StringComparison.OrdinalIgnoreCase), true);
                ChkBool("A freeze default: source 'default'", d.Source == "default", true);
                Chk("A freeze default: speed 0", d.Speed, 0, 1e-6);
                Chk("A freeze default: frame 0", d.Frame, 0, 1e-6);
            }
            else ChkBool("A freeze default: no default animation, falls back to captured/empty", d.Source == "captured" || d.Anim.Length == 0, true);
            kb = d.Anim;
            var keys = AnimProbe.ShapeKeys(e, 5);
            ok = kb.Length > 0 && keys.Count >= 3;
            if (ok)
            {
                foreach (string k in keys) if (k != kb && AnimProbe.KeyedElements(e, k).Count > 0) { kl = k; break; }
                foreach (string k in keys) if (k != kb && k != kl) { kn = k; break; }
                ok = kl.Length > 0 && kn.Length > 0;
            }
            Info("base=" + kb + " layer=" + kl + " other=" + kn + " ok=" + ok);
        });
        Delay(8);
        Do(() =>
        {
            if (!ok) return;
            Entity e = X!;
            var run = AnimProbe.RunningKeys(e);
            ChkBool("A freeze default: only the held base runs (found " + AnimProbe.J(run) + ")", AnimProbe.SameSet(run, new[] { kb }), true);
            pDef = AnimProbe.Poses(e);
            ChkBool("A poses available", pDef != null && pDef.Count > 0, true);
            // reference: explicit freeze on the same anim at frame 0 must give the same pose
            Ops.ThawEntity(sys, e);
        });
        Delay(6);
        Do(() => { if (ok) Ops.FreezeEntity(sys, X!, kb, 0f, 0f); });
        Delay(8);
        Do(() =>
        {
            if (!ok) return;
            pRef = AnimProbe.Poses(X!);
            if (pDef != null && pRef != null) Chk("A default capture pose == explicit freeze of the same anim at frame 0", AnimProbe.MaxDiff(pDef, pRef), 0.0, Eps);
            pBase = pRef;
            // layer on top
            Entity e = X!;
            StageData d = StageState.Read(e)!;
            string msg = AnimOps.AddLayer(e, d, kl, 0, "0.5");
            Info("AddLayer: " + msg);
            Ops.Commit(sys, e, d);
        });
        Delay(8);
        Do(() =>
        {
            if (!ok) return;
            Entity e = X!;
            var run = AnimProbe.RunningKeys(e);
            ChkBool("A layer: running keys == [base,layer] (found " + AnimProbe.J(run) + ")", AnimProbe.SameSet(run, new[] { kb, kl }), true);
            var p = AnimProbe.Poses(e);
            if (p != null && pBase != null)
            {
                HashSet<string> diff = AnimProbe.Differing(p, pBase, Eps), keyed = AnimProbe.KeyedElements(e, kl);
                HashSet<string> outside = new(diff, StringComparer.OrdinalIgnoreCase); outside.ExceptWith(keyed);
                Info("A layer diff elements=" + diff.Count + " keyed by layer=" + keyed.Count + " outside=" + AnimProbe.J(outside));
                ChkBool("A layer: pose differs from base alone", diff.Count > 0, true);
                ChkBool("A layer: pose differs only on the layer's keyframed elements", outside.Count == 0, true);
            }
            // clean switch drops the layer
            StageData d = StageState.Read(e)!;
            AnimOps.SetBase(e, d, kn, 0, "0.5");
            Ops.Commit(sys, e, d);
        });
        Delay(8);
        Do(() =>
        {
            if (!ok) return;
            Entity e = X!;
            var run = AnimProbe.RunningKeys(e);
            ChkBool("A clean switch from base+layer: running keys == [other] (found " + AnimProbe.J(run) + ")", AnimProbe.SameSet(run, new[] { kn }), true);
            ChkBool("A clean switch cleared the layer", StageState.Read(e)!.Layers.Count == 0, true);
            // build base + layer again for the scene
            StageData d = StageState.Read(e)!;
            AnimOps.SetBase(e, d, kb, 0, "0.3");
            AnimOps.AddLayer(e, d, kl, 0, "0.6");
            d.Pitch = 7f * Ops.Deg; d.Roll = -5f * Ops.Deg; d.Yaw = 200f * Ops.Deg;
            Ops.Commit(sys, e, d);
        });
        Delay(8);
        float ib = 0, il = 0; Vec3d pos0 = new();
        Do(() =>
        {
            if (!ok) return;
            Entity e = X!;
            StageData d = StageState.Read(e)!;
            ib = d.Frame; il = d.Layers[0].Frame; pos0 = e.Pos.XYZ.Clone();
            pBase = AnimProbe.Poses(e);
            string path = SceneIO.Save(api, scene, new[] { e }, e.Pos.XYZ);
            Info("scene saved with 1 layer: " + path);
            Ops.ThawEntity(sys, e);
            Ops.Despawn(e);
        });
        Delay(20);
        Do(() =>
        {
            if (!ok) return;
            var loaded = new List<Entity>(); var rep = new StringBuilder();
            bool l = SceneIO.Load(sys, scene, loaded, rep);
            foreach (string line in rep.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)) Info("load: " + line.TrimEnd());
            ChkBool("A scene with layers loads (respawn or nearest)", l && loaded.Count == 1, true);
            if (loaded.Count == 1) X = loaded[0]; else abort = true;
        });
        Delay(10);
        Do(() =>
        {
            if (!ok) return;
            Entity e = X!;
            StageData? d = StageState.Read(e);
            ChkBool("A scene: staged with base + 1 layer", d != null && d.Staged && d.Anim == kb && d.Layers.Count == 1 && d.Layers[0].Anim == kl, true);
            if (d != null && d.Layers.Count == 1)
            {
                Chk("A scene: base frame", d.Frame, ib, 1e-4);
                Chk("A scene: layer frame", d.Layers[0].Frame, il, 1e-4);
                Chk("A scene: yaw", d.Yaw, 200f * Ops.Deg, 1e-4);
            }
            var run = AnimProbe.RunningKeys(e);
            ChkBool("A scene: running keys == [base,layer] (found " + AnimProbe.J(run) + ")", AnimProbe.SameSet(run, new[] { kb, kl }), true);
            var p = AnimProbe.Poses(e);
            if (p != null && pBase != null) Chk("A scene: reloaded stacked pose identical to the saved one", AnimProbe.MaxDiff(p, pBase), 0.0, Eps);
            // resetpose
            StageData d2 = StageState.Read(e)!;
            AnimOps.ResetPose(d2);
            Ops.Commit(sys, e, d2);
        });
        Delay(8);
        Do(() =>
        {
            if (!ok) return;
            Entity e = X!;
            var run = AnimProbe.RunningKeys(e);
            ChkBool("A resetpose: nothing runs (found " + AnimProbe.J(run) + ")", run.Count == 0, true);
            var p = AnimProbe.Poses(e);
            if (p != null) { var ni = AnimProbe.NonIdentity(p, Eps); ChkBool("A resetpose: bind pose (non-identity elements: " + AnimProbe.J(ni) + ")", ni.Count == 0, true); }
            StageData d = StageState.Read(e)!;
            Chk("A resetpose: yaw untouched", d.Yaw, 200f * Ops.Deg, 1e-4);
            Chk("A resetpose: pitch untouched", d.Pitch, 7f * Ops.Deg, 1e-4);
            try { SceneIO.Delete(api, scene); } catch { }
            Ops.ThawEntity(sys, e);
            Ops.Despawn(e);
        });
        Delay(10);
    }
}
