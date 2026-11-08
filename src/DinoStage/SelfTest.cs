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
/// /stage selftest. Scripted checks on live entities, run as a tick-driven step queue (50 ms steps).
/// Writes selftest-[timestamp].txt into the mod data folder only. Spawned entities are removed at the end.
/// </summary>
public sealed class SelfTest
{
    sealed class Ctx
    {
        public string Label = "";
        public string CodeA = "", CodeB = "";
        public Entity? A, B;
        public string AnimA = "", AnimB = "";
        public double X, Z, Gy;
        public bool Abort, ChunkRequested, ChunkLoaded;
        public int Wait;
        public int HB0 = -1;
        public double FzY;
        public Snap BaseA, BaseB;
        public Vec3d ExpA = new(), ExpB = new();
        public float ExpYawA;
        public double MaxDyA, MaxDfA, MaxDpA, MaxDrA, MaxDyB;
        public readonly List<Entity> Spawned = new();
    }

    struct Snap
    {
        public double X, Y, Z;
        public float Frame, Pitch, Roll, Yaw;
        public bool OnGround, CtrlModules, PassiveTicking;
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
    bool moverReal;
    public Action? Done;

    public SelfTest(ICoreServerAPI api, DinoStageSystem sys) { this.api = api; this.sys = sys; }

    // ------------------------------------------------------------------ plumbing

    void L(string s) { log.AppendLine(s); api.Logger.Notification("[DinoStage selftest] " + s); }

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

    void Info(string s) => L("INFO " + s);

    Func<bool> Delay(int ticks) { int left = ticks; return () => --left <= 0; }

    void Add(Ctx c, Func<bool> f) => steps.Enqueue(() => c.Abort || f());

    void AddOnce(Ctx c, Action a) => Add(c, () => { a(); return true; });

    void AddDelay(Ctx c, int ticks) { var d = Delay(ticks); Add(c, d); }

    public string Start()
    {
        Ops.TestAlwaysActive = true;
        string ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        logPath = Path.Combine(api.GetOrCreateDataPath("dinostage"), "selftest-" + ts + ".txt");
        L("DinoStage selftest " + ts + ". Mover guard: " + MoverGuard.Status);
        try
        {
            mover = new ItemEntityMoverTool();
            Type t = typeof(ItemEntityMoverTool);
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            mStart = t.GetMethod("StartDisableEntityMovement", bf, null, new[] { typeof(Entity) }, null);
            mStop = t.GetMethod("StopDisableEntityMovement", bf, null, new[] { typeof(Entity) }, null);
            moverReal = mStart != null && mStop != null;
        }
        catch (Exception ex) { Info("ItemEntityMoverTool not constructible: " + ex.Message); moverReal = false; }
        Info(moverReal
            ? "mover: real ItemEntityMoverTool instance (new ItemEntityMoverTool, protected methods by reflection; item asset 'entitymover' not needed for these methods)"
            : "mover: SIMULATED with the same writes as vanilla (ItemEntityMoverTool.cs:164-179 / 181-194), Harmony hooks cannot fire");

        ResolveSuites(out var suites);
        foreach (var s in suites) BuildSuite(s.label, s.a, s.b);
        steps.Enqueue(() => { Finish(); return true; });
        listener = api.Event.RegisterGameTickListener(Tick, 50);
        return logPath;
    }

    void Tick(float dt)
    {
        if (finished) return;
        if (steps.Count == 0) { Finish(); return; }
        try
        {
            if (steps.Peek()()) steps.Dequeue();
        }
        catch (Exception ex)
        {
            fail++;
            L("FAIL exception in step: " + ex);
            steps.Clear();
            Finish();
        }
    }

    void Finish()
    {
        if (finished) return;
        finished = true;
        L("SUMMARY pass=" + pass + " fail=" + fail);
        try { File.WriteAllText(logPath, log.ToString()); } catch (Exception ex) { api.Logger.Warning("[DinoStage] selftest log write failed: " + ex.Message); }
        api.Event.UnregisterGameTickListener(listener);
        Done?.Invoke();
        api.BroadcastMessageToAllGroups("[DinoStage selftest] pass=" + pass + " fail=" + fail + " log=" + logPath, EnumChatType.Notification);
    }

    // ------------------------------------------------------------------ actor selection

    void ResolveSuites(out List<(string label, string a, string b)> suites)
    {
        suites = new List<(string, string, string)>();
        List<EntityProperties> types = api.World.EntityTypes;
        string? Pick(System.Func<EntityProperties, bool> pred) => types.FirstOrDefault(pred)?.Code.ToString();
        string? pig = Pick(t => t.Code.Domain == "game" && t.Code.Path.StartsWith("pig-wild") && !t.Code.Path.Contains("baby"))
                   ?? Pick(t => t.Code.Domain == "game" && t.Code.Path.StartsWith("pig-") && !t.Code.Path.Contains("baby"));
        string? bear = Pick(t => t.Code.Domain == "game" && t.Code.Path.StartsWith("bear-") && !t.Code.Path.Contains("cub") && !t.Code.Path.Contains("baby"));
        string[] theroKeys = { "tyranno", "dromaeo", "raptor", "allosaur", "carcharo", "spino", "abelisaur", "carnotaur", "gigantosaur", "utahraptor" };
        string[] plesioKeys = { "plesiosaur", "plesio" };
        string? thero = Pick(t => t.Code.Domain != "game" && theroKeys.Any(k => t.Code.Path.Contains(k)));
        string? plesio = Pick(t => t.Code.Domain != "game" && plesioKeys.Any(k => t.Code.Path.Contains(k)));
        Info("resolved codes: pig=" + (pig ?? "none") + " bear=" + (bear ?? "none") + " theropod=" + (thero ?? "none") + " plesiosaur=" + (plesio ?? "none"));
        if (pig != null && bear != null) suites.Add(("vanilla", pig, bear));
        if (thero != null || plesio != null) suites.Add(("lop", thero ?? pig ?? bear ?? "", plesio ?? bear ?? pig ?? ""));
        if (suites.Count == 0) Info("no usable entity codes found");
        if (thero == null || plesio == null) Info("LoP theropod/plesiosaur test actors missing or partial; fallback actors used where absent (vanilla pig/bear)");
    }

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
        s.CtrlModules = e.GetBehavior<EntityBehaviorControlledPhysics>()?.EnableModulesApplication ?? false;
        EntityBehaviorPassivePhysics? pp = e.GetBehavior<EntityBehaviorPassivePhysics>();
        s.PassiveTicking = pp != null ? pp.Ticking : s.CtrlModules; // behavior absent: not applicable, mirrors ctrl
        return s;
    }

    string Fmt(Snap s) => "pos=" + Ops.F(s.X) + "," + Ops.F(s.Y) + "," + Ops.F(s.Z) + " frame=" + Ops.F(s.Frame) + " pitch=" + Ops.F(s.Pitch / Ops.Deg) + " roll=" + Ops.F(s.Roll / Ops.Deg)
        + " yaw=" + Ops.F(s.Yaw / Ops.Deg) + " onGround=" + s.OnGround + " ctrlModules=" + s.CtrlModules + " passiveTicking=" + s.PassiveTicking;

    // ------------------------------------------------------------------ mover simulation

    void MoverStart(Entity e)
    {
        if (moverReal) { mStart!.Invoke(mover, new object[] { e }); return; }
        EntityBehaviorTaskAI? ai = e.GetBehavior<EntityBehaviorTaskAI>();
        if (ai != null) { ai.TaskManager.OnShouldExecuteTask += SimShould; ai.TaskManager.OnExecuteTask += SimExec; ai.TaskManager.StopTasks(); }
        EntityBehaviorControlledPhysics? p = e.GetBehavior<EntityBehaviorControlledPhysics>();
        if (p != null) { p.EnableModulesApplication = false; e.Pos.Motion.Set(0.0, 0.0, 0.0); }
    }

    void MoverStop(Entity e)
    {
        if (moverReal) { mStop!.Invoke(mover, new object[] { e }); return; }
        EntityBehaviorTaskAI? ai = e.GetBehavior<EntityBehaviorTaskAI>();
        if (ai != null) { ai.TaskManager.OnShouldExecuteTask -= SimShould; ai.TaskManager.OnExecuteTask -= SimExec; }
        EntityBehaviorControlledPhysics? p = e.GetBehavior<EntityBehaviorControlledPhysics>();
        if (p != null) p.EnableModulesApplication = true;
    }

    static bool SimShould(IAiTask t) => false;
    static bool SimExec(IAiTask t, IAiTask? a) => false;

    /// <summary>Same three writes as ItemEntityMoverTool.OnHeldInteractStepServer :145-147.</summary>
    static void MoverStep(Entity e, Vec3d target)
    {
        e.Pos.SetPos(target);
        e.GetBehavior<EntityBehaviorControlledPhysics>()?.SetState(new EntityPos(target.X, target.Y, target.Z), 0f);
        e.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager?.StopTasks();
    }

    // ------------------------------------------------------------------ suite

    void BuildSuite(string label, string codeA, string codeB)
    {
        var c = new Ctx { Label = label, CodeA = codeA, CodeB = codeB };
        AddOnce(c, () => Info("=== suite " + label + ": A=" + codeA + " B=" + codeB + " ==="));

        // 1. chunk
        Add(c, () =>
        {
            if (!c.ChunkRequested)
            {
                Ops.SpawnXZ(api, out int spx, out int spz); c.X = spx; c.Z = spz;
                int cs = GlobalConstants.ChunkSize;
                api.WorldManager.LoadChunkColumnPriority((int)c.X / cs, (int)c.Z / cs, new ChunkLoadOptions { KeepLoaded = true, OnLoaded = () => c.ChunkLoaded = true });
                c.ChunkRequested = true;
                return false;
            }
            if (c.ChunkLoaded) return true;
            if (++c.Wait > 600) { L("FAIL chunk column did not load in 30 s"); fail++; c.Abort = true; return true; }
            return false;
        });

        // 2. spawn
        AddOnce(c, () =>
        {
            c.Gy = api.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos((int)c.X, 0, (int)c.Z, 0)) + 1;
            double y = c.Gy + 8;
            c.A = Ops.Spawn(api, codeA, c.X + 0.5, y, c.Z + 0.5, 0f, out string ea);
            c.B = Ops.Spawn(api, codeB, c.X + 8.5, y, c.Z + 0.5, 0f, out string eb);
            if (c.A != null) c.Spawned.Add(c.A);
            if (c.B != null) c.Spawned.Add(c.B);
            if (c.A == null || c.B == null) { fail++; L("FAIL spawn: " + ea + " " + eb); c.Abort = true; return; }
            Info("ground y=" + c.Gy + ", spawned A id " + c.A.EntityId + ", B id " + c.B.EntityId + " at y=" + y + " (8 blocks in the air)");
        });
        AddDelay(c, 10);

        // 3. freeze with anim + progress + pitch + roll + heading
        AddOnce(c, () =>
        {
            foreach (var (e, isA) in new[] { (c.A!, true), (c.B!, false) })
            {
                var (key, code) = PickAnim(e);
                if (!isA) { AiTaskManager? tm0 = e.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager; if (tm0 != null) { c.HB0 = Freeze.ShouldHandlerCount(tm0); Info("B OnShouldExecuteTask subscribers before freeze = " + c.HB0); } }
                int frames = key == null ? 0 : Freeze.FrameCount(e, key);
                float fr = frames > 0 ? 0.5f * (frames - 1) : 0f;
                StageData d = Ops.FreezeEntity(sys, e, key ?? "", 0f, fr);
                d.Pitch = 20f * Ops.Deg; d.Roll = 15f * Ops.Deg; d.Yaw = 90f * Ops.Deg;
                Ops.Commit(sys, e, d);
                if (isA) { c.AnimA = d.Anim; c.FzY = e.Pos.Y; } else c.AnimB = d.Anim;
                Info((isA ? "A" : "B") + " frozen: config anim '" + code + "' shape anim '" + d.Anim + "' frames " + frames + " frame " + Ops.F(fr) + " pitch 20 roll 15 heading 90");
            }
        });
        AddDelay(c, 40);

        // 4. baseline
        AddOnce(c, () =>
        {
            c.BaseA = Take(c.A!, c.AnimA); c.BaseB = Take(c.B!, c.AnimB);
            Info("baseline A " + Fmt(c.BaseA));
            Info("baseline B " + Fmt(c.BaseB));
            ChkBool("A ctrl physics modules off while frozen", c.BaseA.CtrlModules, false);
            ChkBool("A passive physics off while frozen", c.BaseA.PassiveTicking, false);
            Info("A fell " + Ops.F(c.Gy + 8 - c.FzY) + " blocks between spawn and freeze (physics pump runs from spawn)");
            Chk("A Y change during 2 s after freeze (frozen midair)", c.BaseA.Y, c.FzY, 0.02);
            if (c.AnimA.Length == 0) Info("A has no animation on the server; frame checks are NaN and skipped");
        });

        // 5. mover: grab, 20 steps, release
        AddOnce(c, () =>
        {
            MoverStart(c.A!);
            ChkBool("mover grab: entity marked held by guard (needs Harmony patch)", MoverGuard.IsHeld(c.A!.EntityId), moverReal && MoverGuard.Applied);
        });
        for (int i = 1; i <= 20; i++)
        {
            int k = i;
            AddOnce(c, () =>
            {
                Entity a = c.A!;
                MoverStep(a, new Vec3d(c.BaseA.X + 0.25 * k, c.BaseA.Y + 0.1 * k, c.BaseA.Z));
                if (k == 10) a.Pos.Yaw += 0.5f; // Ctrl+wheel (ItemEntityMoverTool.cs:219)
                if (k == 15)
                {
                    c.ExpYawA = c.BaseA.Yaw + 0.5f;
                    Chk("A yaw while held is not re-asserted by the guard (needs Harmony patch)", a.Pos.Yaw, c.ExpYawA, 0.001);
                }
            });
        }
        AddOnce(c, () =>
        {
            Entity a = c.A!;
            MoverStop(a);
            Snap s = Take(a, c.AnimA);
            Info("immediately after mover release (same call): " + Fmt(s));
            ChkBool("release: physics modules re-disabled in the same call (Harmony postfix)", s.CtrlModules, false);
            c.ExpA = new Vec3d(c.BaseA.X + 5.0, c.BaseA.Y + 2.0, c.BaseA.Z);
            c.ExpB = new Vec3d(c.BaseB.X, c.BaseB.Y, c.BaseB.Z);
        });

        // 6. wait 10 s with per-tick max deviation sampling
        int w = 200;
        Add(c, () =>
        {
            Entity a = c.A!, b = c.B!;
            c.MaxDyA = Math.Max(c.MaxDyA, Math.Abs(a.Pos.Y - c.ExpA.Y));
            c.MaxDpA = Math.Max(c.MaxDpA, Math.Abs(a.Pos.Pitch - c.BaseA.Pitch));
            c.MaxDrA = Math.Max(c.MaxDrA, Math.Abs(a.Pos.Roll - c.BaseA.Roll));
            float fa = Freeze.CurrentFrame(a, c.AnimA) ?? float.NaN;
            if (!float.IsNaN(fa)) c.MaxDfA = Math.Max(c.MaxDfA, Math.Abs(fa - c.BaseA.Frame));
            c.MaxDyB = Math.Max(c.MaxDyB, Math.Abs(b.Pos.Y - c.ExpB.Y));
            return --w <= 0;
        });
        AddOnce(c, () =>
        {
            Snap sa = Take(c.A!, c.AnimA), sb = Take(c.B!, c.AnimB);
            Info("after 10 s A " + Fmt(sa));
            Info("after 10 s B " + Fmt(sb));
            Chk("A moved +5 X", sa.X, c.ExpA.X, 0.02);
            Chk("A moved +2 Y (no fall, Y drop = delta)", sa.Y, c.ExpA.Y, 0.02);
            Chk("A Z unchanged", sa.Z, c.ExpA.Z, 0.02);
            Chk("A frame held", sa.Frame, c.BaseA.Frame, 0.01);
            Chk("A pitch held", sa.Pitch, c.BaseA.Pitch, 0.001);
            Chk("A roll held", sa.Roll, c.BaseA.Roll, 0.001);
            Chk("A yaw = baseline + wheel change (adopted on release)", sa.Yaw, c.ExpYawA, 0.001);
            ChkBool("A not on ground (still airborne)", sa.OnGround, false);
            ChkBool("A ctrl physics modules still off", sa.CtrlModules, false);
            ChkBool("A passive physics still off", sa.PassiveTicking, false);
            Info("A max deviation seen over 200 ticks: |dY vs expected|=" + c.MaxDyA.ToString("0.#####") + " |dPitch|=" + c.MaxDpA.ToString("0.#####") + " |dRoll|=" + c.MaxDrA.ToString("0.#####") + " |dFrame|=" + c.MaxDfA.ToString("0.#####"));
            Info("note: the server re-asserts frame every 50 ms, so the frame check shows hold with no stray reset, not independent proof; client display is a manual TEST.md step");
            Chk("B (control, not moved) X", sb.X, c.ExpB.X, 0.02);
            Chk("B (control) Y, no fall", sb.Y, c.ExpB.Y, 0.02);
            Chk("B (control) frame held", sb.Frame, c.BaseB.Frame, 0.01);
            Info("B max |dY| over 200 ticks: " + c.MaxDyB.ToString("0.#####"));
        });

        // 7. thaw B and verify restore
        AddOnce(c, () =>
        {
            Entity b = c.B!;
            AiTaskManager? tm = b.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
            Ops.ThawEntity(sys, b);
            Snap s = Take(b, c.AnimB);
            ChkBool("B thaw: ctrl physics modules on", s.CtrlModules, true);
            ChkBool("B thaw: passive physics ticking on", s.PassiveTicking, true);
            ChkBool("B thaw: tree cleared", b.WatchedAttributes.HasAttribute(StageState.Key), false);
            ChkBool("B thaw: AI hook flag cleared", Freeze.IsHooked(b), false);
            if (tm != null) Chk("B thaw: OnShouldExecuteTask subscribers vs before-freeze count", Freeze.ShouldHandlerCount(tm), c.HB0, 0);
            else Info("B has no TaskAI behavior (no AI handler check)");
            c.Wait = 100;
        });
        Add(c, () => --c.Wait <= 0);
        AddOnce(c, () =>
        {
            Entity b = c.B!;
            Snap s = Take(b, c.AnimB);
            Info("B 5 s after thaw " + Fmt(s));
            Info("B Y change since freeze (gravity restored if negative): " + Ops.F(s.Y - c.BaseB.Y));
            AiTaskManager? tm = b.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
            int running = tm == null ? -1 : tm.ActiveTasksBySlot.Count(t => t != null);
            Info("B active AI tasks 5 s after thaw: " + running + "; active animations: " + b.AnimManager?.ActiveAnimationsByAnimCode.Count);
            if (tm != null && c.CodeB.Contains("plesio")) Info("B is a plesiosaur out of water: tasks=" + running + " is compared with a never-frozen control in the extended test, not asserted here");
            else if (tm != null) ChkBool("B thaw: AI tasks run again (at least one active task)", running > 0, true);
        });

        // 8. scene round trip (A staged; B refrozen)
        string sceneName = "selftest-" + label;
        AddOnce(c, () =>
        {
            Entity b = c.B!;
            var (key, _) = PickAnim(b);
            int frames = key == null ? 0 : Freeze.FrameCount(b, key);
            b.Pos.SetPos(c.X + 8.5, c.Gy + 6, c.Z + 0.5);
            StageData d = Ops.FreezeEntity(sys, b, key ?? "", 0f, frames > 0 ? 0.25f * (frames - 1) : 0f);
            d.Pitch = -10f * Ops.Deg; d.Roll = 5f * Ops.Deg; d.Yaw = 200f * Ops.Deg;
            Ops.Commit(sys, b, d);
            c.AnimB = d.Anim;
            c.Wait = 20;
        });
        Add(c, () => --c.Wait <= 0);
        AddOnce(c, () =>
        {
            Entity a = c.A!, b = c.B!;
            c.BaseA = Take(a, c.AnimA); c.BaseB = Take(b, c.AnimB);
            string path = SceneIO.Save(api, sceneName, new[] { a, b }, a.Pos.XYZ);
            Info("scene saved: " + path);
            Info("saved A " + Fmt(c.BaseA));
            Info("saved B " + Fmt(c.BaseB));
            // clear: thaw all, scramble, remove B
            Ops.ThawEntity(sys, a); Ops.ThawEntity(sys, b);
            a.Pos.SetPos(c.X + 30, c.Gy + 3, c.Z + 30);
            Ops.Despawn(b);
            c.Wait = 15;
        });
        Add(c, () => --c.Wait <= 0);
        AddOnce(c, () =>
        {
            var loaded = new List<Entity>();
            var rep = new StringBuilder();
            bool ok = SceneIO.Load(sys, sceneName, loaded, rep);
            foreach (string line in rep.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)) Info("load: " + line.TrimEnd());
            ChkBool("scene load ok with 2 actors", ok && loaded.Count == 2, true);
            if (loaded.Count == 2)
            {
                c.A = loaded[0]; c.B = loaded[1];
                foreach (Entity e in loaded) if (!c.Spawned.Contains(e)) c.Spawned.Add(e);
            }
            c.Wait = 40;
        });
        Add(c, () => --c.Wait <= 0);
        AddOnce(c, () =>
        {
            Snap a = Take(c.A!, c.AnimA), b = Take(c.B!, c.AnimB);
            Info("restored A " + Fmt(a));
            Info("restored B " + Fmt(b));
            Chk("scene A X", a.X, c.BaseA.X, 0.02); Chk("scene A Y", a.Y, c.BaseA.Y, 0.02); Chk("scene A Z", a.Z, c.BaseA.Z, 0.02);
            Chk("scene A pitch", a.Pitch, c.BaseA.Pitch, 0.001); Chk("scene A roll", a.Roll, c.BaseA.Roll, 0.001); Chk("scene A heading", a.Yaw, c.BaseA.Yaw, 0.001);
            Chk("scene A frame", a.Frame, c.BaseA.Frame, 0.01);
            ChkBool("scene A staged", StageState.IsStaged(c.A!), true);
            Chk("scene B X", b.X, c.BaseB.X, 0.02); Chk("scene B Y", b.Y, c.BaseB.Y, 0.02); Chk("scene B Z", b.Z, c.BaseB.Z, 0.02);
            Chk("scene B pitch", b.Pitch, c.BaseB.Pitch, 0.001); Chk("scene B roll", b.Roll, c.BaseB.Roll, 0.001); Chk("scene B heading", b.Yaw, c.BaseB.Yaw, 0.001);
            Chk("scene B frame", b.Frame, c.BaseB.Frame, 0.01);
            ChkBool("scene B staged", StageState.IsStaged(c.B!), true);
            StageData? da = StageState.Read(c.A!), db = StageState.Read(c.B!);
            ChkBool("scene A anim restored", da != null && da.Anim == c.AnimA, true);
            ChkBool("scene B anim restored", db != null && db.Anim == c.AnimB, true);
            try { SceneIO.Delete(api, sceneName); Info("scene file deleted again"); } catch { }
        });

        // 9. final thaw + cleanup
        AddOnce(c, () =>
        {
            foreach (Entity e in c.Spawned.ToList())
            {
                if (api.World.GetEntityById(e.EntityId) != null && StageState.IsStaged(e)) Ops.ThawEntity(sys, e);
            }
            Info("cleanup: removing " + c.Spawned.Count + " spawned entities");
            foreach (Entity e in c.Spawned.ToList())
            {
                try { if (api.World.GetEntityById(e.EntityId) != null) Ops.Despawn(e); } catch { }
            }
        });
        AddDelay(c, 10);
    }
}
