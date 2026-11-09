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
        Ops.TestRequirePoses = true; // server animator computes RootPoses, read by the 1.1.0 pose checks
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

        // 8b. 1.1.0 animation API checks on A (clean switch, layer, layer cap, resetpose, freeze default capture)
        BuildAnimChecks(c);

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

    // ------------------------------------------------------------------ 1.1.0 animation checks

    /// <summary>
    /// Clean switch, stuck-pose proof, layer stacking, layer cap, mover with layers, client hold, resetpose, freeze default capture and scene files,
    /// all on actor A (requires Ops.TestRequirePoses so the server animator computes RootPoses). Compares real engine poses, not just state.
    /// </summary>
    static string RaDiag(Entity e, string key)
    {
        RunningAnimation? ra = e.AnimManager?.Animator?.GetAnimationState(key);
        return ra == null ? key + " none" : key + " frame=" + ra.CurrentFrame.ToString("0.##") + " ease=" + ra.EasingFactor.ToString("0.###") + " blended=" + ra.BlendedWeight.ToString("0.###") + " active=" + ra.Active + " running=" + ra.Running + " speed=" + (ra.meta?.AnimationSpeed.ToString("0.##") ?? "?");
    }

    void BuildAnimChecks(Ctx c)
    {
        var ks = new List<string>();
        string k1 = "", k2 = "", k3 = "";
        Dictionary<string, float[]>? pBase = null, pBl = null, pSw = null, pRef = null;
        float yaw0 = 0, pitch0 = 0, roll0 = 0;
        bool ok = false;
        const float Eps = 1e-4f;

        AddOnce(c, () =>
        {
            Entity a = c.A!;
            Info("=== 1.1.0 animation checks on A (" + a.Code + ") ===");
            ks = AnimProbe.ShapeKeys(a, 5);
            if (ks.Count >= 1) k1 = ks[0];
            foreach (string k in ks) if (k != k1 && AnimProbe.KeyedElements(a, k).Count > 0) { k3 = k; break; }
            foreach (string k in ks) if (k != k1 && k != k3) { k2 = k; break; }
            ok = k1.Length > 0 && k2.Length > 0 && k3.Length > 0;
            Info("shape anims with >=5 frames: " + ks.Count + "; k1(base)=" + k1 + " k2(switch)=" + k2 + " k3(layer)=" + k3);
            if (!ok) { Info("SKIP 1.1.0 checks: fewer than 3 usable shape animations on A"); return; }
            Ops.ThawEntity(sys, a);
        });
        AddDelay(c, 8);

        // ---- stuck proof: StopAnimation alone leaves a speed 0 RunningAnimation (1.0.0 behavior); AnimSet.Clean removes it
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            var m = new AnimationMetaData { Code = k3, Animation = k3, AnimationSpeed = 0f, Weight = 1f, BlendMode = EnumAnimationBlendMode.Average, EaseInSpeed = 1000f, EaseOutSpeed = 1000f, StartFrameOnce = 0.5f * (Freeze.FrameCount(a, k3) - 1) };
            m.Init();
            a.AnimManager.StartAnimation(m);
        });
        AddDelay(c, 6);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            ChkBool("stuck proof: speed 0 animation is running after StartAnimation", AnimProbe.RunningKeys(a).Contains(k3), true);
            a.AnimManager.StopAnimation(k3);
        });
        AddDelay(c, 6);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            ChkBool("stuck proof: StopAnimation alone removes the dictionary key", AnimProbe.ActiveKeys(a).Contains(k3), false);
            ChkBool("stuck proof (1.0.0 bug): StopAnimation alone leaves the speed 0 RunningAnimation running", AnimProbe.RunningKeys(a).Contains(k3), true);
            int n = AnimSet.Clean(a.AnimManager, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            Info("AnimSet.Clean on the stuck animation: " + n + " change(s)");
        });
        AddDelay(c, 6);
        AddOnce(c, () =>
        {
            if (!ok) return;
            ChkBool("stuck proof: AnimSet.Clean (StopAnimation then Stop) ends the stuck RunningAnimation", AnimProbe.RunningKeys(c.A!).Contains(k3), false);
        });

        // ---- clean switch equivalence: base k1 -> SetBase k2 must equal a fresh freeze straight on k2
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            StageData d = Ops.FreezeEntity(sys, a, k1, 0f, 0.5f * (Freeze.FrameCount(a, k1) - 1));
            ChkBool("fresh freeze starts without layers", d.Layers.Count == 0, true);
            yaw0 = d.Yaw; pitch0 = d.Pitch; roll0 = d.Roll;
        });
        AddDelay(c, 8);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            ChkBool("base k1 held alone before the switch", AnimProbe.SameSet(AnimProbe.RunningKeys(a), new[] { k1 }), true);
            StageData d = StageState.Read(a)!;
            string msg = AnimOps.SetBase(a, d, k2, null, "0.5");
            Info("SetBase: " + msg);
            Ops.Commit(sys, a, d);
        });
        AddDelay(c, 8);
        float swFrame = float.NaN;
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            var run = AnimProbe.RunningKeys(a);
            ChkBool("clean switch: running keys == [k2] (found " + AnimProbe.J(run) + ")", AnimProbe.SameSet(run, new[] { k2 }), true);
            swFrame = Freeze.CurrentFrame(a, k2) ?? float.NaN;
            pSw = AnimProbe.Poses(a);
            Info("switched trot state: " + RaDiag(a, k2));
            ChkBool("poses available on the server animator (requirePosesOnServer)", pSw != null && pSw.Count > 0, true);
            Ops.ThawEntity(sys, a);
        });
        AddDelay(c, 6);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            Ops.FreezeEntity(sys, a, k2, 0f, swFrame);
        });
        AddDelay(c, 8);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            pRef = AnimProbe.Poses(a);
            Info("fresh freeze trot state: " + RaDiag(a, k2) + " ; running=" + AnimProbe.J(AnimProbe.RunningKeys(a)) + " ; active=" + AnimProbe.J(AnimProbe.ActiveKeys(a)));
            Chk("clean switch frame == fresh freeze frame", Freeze.CurrentFrame(a, k2) ?? float.NaN, swFrame, 0.01);
            ChkBool("clean switch running keys == fresh freeze running keys", AnimProbe.SameSet(AnimProbe.RunningKeys(a), new[] { k2 }), true);
            if (pSw != null && pRef != null && AnimProbe.MaxDiff(pSw, pRef) > Eps) Info("clean switch pose worst element: " + AnimProbe.Worst(pSw, pRef) + " ; differing elements: " + AnimProbe.J(AnimProbe.Differing(pSw, pRef, Eps)));
            if (pSw != null && pRef != null) Chk("clean switch pose == fresh freeze pose (max element delta over " + pSw.Count + " elements)", AnimProbe.MaxDiff(pSw, pRef), 0.0, Eps);
            else { fail++; L("FAIL clean switch pose compare: no poses"); }
        });

        // ---- layer stacking: base k1 alone vs base k1 + layer k3, on the layer's keyframed elements only
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            StageData d = StageState.Read(a)!;
            AnimOps.SetBase(a, d, k1, 0, "0.5");
            Ops.Commit(sys, a, d);
        });
        AddDelay(c, 8);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            pBase = AnimProbe.Poses(a);
            StageData d = StageState.Read(a)!;
            string msg = AnimOps.AddLayer(a, d, k3, 0, "0.5");
            Info("AddLayer: " + msg);
            Ops.Commit(sys, a, d);
        });
        AddDelay(c, 8);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            pBl = AnimProbe.Poses(a);
            var run = AnimProbe.RunningKeys(a);
            ChkBool("layer: running keys == [k1,k3] (found " + AnimProbe.J(run) + ")", AnimProbe.SameSet(run, new[] { k1, k3 }), true);
            StageData d = StageState.Read(a)!;
            ChkBool("layer persisted in the stage tree", d.Layers.Count == 1 && d.Layers[0].Anim == k3, true);
            if (pBase == null || pBl == null) { fail++; L("FAIL layer pose compare: no poses"); return; }
            HashSet<string> diff = AnimProbe.Differing(pBl, pBase, Eps);
            HashSet<string> keyed = AnimProbe.KeyedElements(a, k3);
            HashSet<string> outside = new(diff, StringComparer.OrdinalIgnoreCase);
            outside.ExceptWith(keyed);
            Info("layer diff elements=" + diff.Count + " keyed by layer=" + keyed.Count + " outside keyed=" + AnimProbe.J(outside));
            ChkBool("layer: pose differs from base alone on at least one element", diff.Count > 0, true);
            ChkBool("layer: pose differs ONLY on elements keyframed by the layer animation", outside.Count == 0, true);
        });

        // ---- re-apply updates an existing layer (count stays 1)
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            StageData d = StageState.Read(a)!;
            AnimOps.AddLayer(a, d, k3, 0, "0.25");
            Ops.Commit(sys, a, d);
            ChkBool("re-applying a layer updates it in place (1 layer)", StageState.Read(a)!.Layers.Count == 1, true);
        });
        AddDelay(c, 4);

        // ---- mover with layers: vanilla mover moves the frozen actor, base + layer hold
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            StageData d = StageState.Read(a)!;
            AnimOps.AddLayer(a, d, k3, 0, "0.5"); // back to the stacked pose of pBl
            Ops.Commit(sys, a, d);
        });
        AddDelay(c, 6);
        AddOnce(c, () => { if (ok) MoverStart(c.A!); });
        for (int i = 1; i <= 10; i++)
        {
            int k = i;
            AddOnce(c, () => { if (ok) MoverStep(c.A!, new Vec3d(c.BaseA.X + 0.1 * k, c.BaseA.Y + 6 + 0.05 * k, c.BaseA.Z)); });
        }
        AddOnce(c, () => { if (ok) MoverStop(c.A!); });
        AddDelay(c, 8);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            var run = AnimProbe.RunningKeys(a);
            ChkBool("mover with layer: running keys still [k1,k3] (found " + AnimProbe.J(run) + ")", AnimProbe.SameSet(run, new[] { k1, k3 }), true);
            var p = AnimProbe.Poses(a);
            if (p != null && pBl != null) Chk("mover with layer: stacked pose unchanged after the move", AnimProbe.MaxDiff(p, pBl), 0.0, Eps);
            ChkBool("mover with layer: staged and layer kept", StageState.IsStaged(a) && StageState.Read(a)!.Layers.Count == 1, true);
        });

        // ---- client hold against a stray default-style animation, synchronously (no server assert can run in between)
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            var m = new AnimationMetaData { Code = k2, Animation = k2, AnimationSpeed = 1f, Weight = 1f, BlendMode = EnumAnimationBlendMode.Average };
            m.Init();
            a.AnimManager.StartAnimation(m);
            ChkBool("client hold: stray animation is active before HoldEntity", AnimProbe.ActiveKeys(a).Contains(k2), true);
            ClientHold.HoldEntity(a);
            var act = AnimProbe.ActiveKeys(a);
            ChkBool("client hold: HoldEntity leaves exactly [k1,k3] active (found " + AnimProbe.J(act) + ")", AnimProbe.SameSet(act, new[] { k1, k3 }), true);
        });
        AddDelay(c, 4);

        // ---- layer cap
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            List<string> all = AnimProbe.ShapeKeys(a, 1).Where(k => k != k1).ToList();
            if (all.Count < 5) { Info("SKIP layer cap: only " + all.Count + " other shape animations"); return; }
            StageData d = StageState.Read(a)!;
            AnimOps.SetBase(a, d, k1, 0, "0.5");
            int added = 0;
            foreach (string k in all.Take(4)) { AnimOps.AddLayer(a, d, k, 0, "0.5"); added++; }
            Chk("layer cap: four layers accepted", d.Layers.Count, 4, 0);
            string err = "";
            try { AnimOps.AddLayer(a, d, all[4], 0, "0.5"); } catch (StageFail ex) { err = ex.Message; }
            ChkBool("layer cap: fifth layer is refused with an error message", err.Length > 0, true);
            ChkBool("layer cap: message says the 5th layer was not added", err.Contains("5th layer"), true);
            Info("cap message: " + err);
            Chk("layer cap: layer count still 4", d.Layers.Count, 4, 0);
            Ops.Commit(sys, a, d);
            Chk("layer cap: four layers persisted", StageState.Read(a)!.Layers.Count, 4, 0);
            c.Wait = 0;
        });
        AddDelay(c, 8);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            StageData d = StageState.Read(a)!;
            if (d.Layers.Count < 4) return;
            var run = AnimProbe.RunningKeys(a);
            Chk("layer cap: base + 4 layers running", run.Count, 5, 0);
        });

        // ---- resetpose: bind pose, nothing held, angles untouched
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            StageData d = StageState.Read(a)!;
            yaw0 = d.Yaw; pitch0 = d.Pitch; roll0 = d.Roll; // angles as staged right before the reset (AI moves the thawed pig between the earlier refreezes)
            string msg = AnimOps.ResetPose(d);
            Info("ResetPose: " + msg);
            Ops.Commit(sys, a, d);
        });
        AddDelay(c, 8);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            StageData d = StageState.Read(a)!;
            ChkBool("resetpose: staged with no base and no layers", d.Staged && d.Anim.Length == 0 && d.Layers.Count == 0, true);
            var run = AnimProbe.RunningKeys(a);
            ChkBool("resetpose: nothing running (found " + AnimProbe.J(run) + ")", run.Count == 0, true);
            var p = AnimProbe.Poses(a);
            if (p != null) { var ni = AnimProbe.NonIdentity(p, Eps); ChkBool("resetpose: every element pose is the bind pose (non-identity: " + AnimProbe.J(ni) + ")", ni.Count == 0, true); }
            Chk("resetpose: yaw untouched", d.Yaw, yaw0, 1e-6);
            Chk("resetpose: pitch untouched", d.Pitch, pitch0, 1e-6);
            Chk("resetpose: roll untouched", d.Roll, roll0, 1e-6);
        });

        // ---- freeze without an explicit anim holds the default animation at frame 0 (or the old capture when there is no default)
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            Ops.ThawEntity(sys, a);
        });
        AddDelay(c, 6);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            string? dk = Freeze.DefaultAnimKey(a);
            StageData d = Ops.FreezeEntity(sys, a);
            Info("freeze no anim: default anim key=" + (dk ?? "none") + " -> anim='" + d.Anim + "' source='" + d.Source + "' speed=" + Ops.F(d.Speed) + " frame=" + Ops.F(d.Frame));
            if (dk != null)
            {
                ChkBool("freeze default: anim == default animation", string.Equals(d.Anim, dk, StringComparison.OrdinalIgnoreCase), true);
                ChkBool("freeze default: source tag is 'default'", d.Source == "default", true);
                Chk("freeze default: speed 0", d.Speed, 0, 1e-6);
                Chk("freeze default: frame 0", d.Frame, 0, 1e-6);
            }
            else ChkBool("freeze default: no default animation, falls back to captured/empty", d.Source == "captured" || d.Anim.Length == 0, true);
        });
        AddDelay(c, 8);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            string? dk = Freeze.DefaultAnimKey(a);
            if (dk == null) return;
            var run = AnimProbe.RunningKeys(a);
            ChkBool("freeze default: only the default animation runs (found " + AnimProbe.J(run) + ")", AnimProbe.SameSet(run, new[] { dk }), true);
            Chk("freeze default: held at frame 0", Freeze.CurrentFrame(a, dk) ?? float.NaN, 0, 0.01);
        });

        // ---- scene files: a hand-written 1.0.0 scene (no Layers field) loads; a scene with layers round-trips
        string s100 = "dinostage_t100_" + DateTime.Now.ToString("HHmmss");
        string sLay = "dinostage_tlay_" + DateTime.Now.ToString("HHmmss");
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            string json = ("{'Name':'" + s100 + "','HourOfDay':12,'Weather':null,'Actors':[{'Code':'" + a.Code + "','EntityId':" + a.EntityId
                + ",'X':" + a.Pos.X.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",'Y':" + a.Pos.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",'Z':" + a.Pos.Z.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",'Heading':0.5,'Pitch':0.1,'Roll':0.2,'Anim':'" + k1 + "','Speed':0,'Frame':3,'Staged':true,'NoPhys':true}]}").Replace('\'', '"');
            File.WriteAllText(SceneIO.PathFor(api, s100), json);
            Info("1.0.0 style scene written (no Layers, no layer fields): " + SceneIO.PathFor(api, s100));
            var acts = new List<Entity>(); var rep = new StringBuilder();
            bool loaded = SceneIO.Load(sys, s100, acts, rep);
            ChkBool("1.0.0 scene: loads", loaded && acts.Count == 1, true);
            StageData? d = StageState.Read(a);
            ChkBool("1.0.0 scene: actor staged with the stored anim", d != null && d.Staged && d.Anim == k1, true);
            if (d != null) { Chk("1.0.0 scene: frame 3", d.Frame, 3, 1e-6); ChkBool("1.0.0 scene: no layers", d.Layers.Count == 0, true); }
            try { SceneIO.Delete(api, s100); } catch { }
        });
        AddDelay(c, 8);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            ChkBool("1.0.0 scene: held base only (found " + AnimProbe.J(AnimProbe.RunningKeys(a)) + ")", AnimProbe.SameSet(AnimProbe.RunningKeys(a), new[] { k1 }), true);
            StageData d = StageState.Read(a)!;
            AnimOps.SetBase(a, d, k1, 0, "0.5");
            AnimOps.AddLayer(a, d, k3, 0, "0.25");
            AnimOps.AddLayer(a, d, k2, 0.5, null);
            Ops.Commit(sys, a, d);
            string path = SceneIO.Save(api, sLay, new[] { a }, a.Pos.XYZ);
            string txt = File.ReadAllText(path);
            ChkBool("layer scene: file contains the Layers array with 2 entries", txt.Contains("\"Layers\"") && txt.Split("\"Anim\"").Length - 1 == 3, true);
            // change the actor, then load: layers must come back
            StageData d2 = StageState.Read(a)!;
            AnimOps.SetBase(a, d2, k2, 0, "0.1");
            Ops.Commit(sys, a, d2);
        });
        AddDelay(c, 6);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            var acts = new List<Entity>(); var rep = new StringBuilder();
            bool loaded = SceneIO.Load(sys, sLay, acts, rep);
            ChkBool("layer scene: loads", loaded && acts.Count == 1, true);
            StageData d = StageState.Read(a)!;
            ChkBool("layer scene: base restored", d.Anim == k1, true);
            Chk("layer scene: base frame restored", d.Frame, 0.5f * (Freeze.FrameCount(a, k1) - 1) , 0.51);
            ChkBool("layer scene: two layers restored", d.Layers.Count == 2 && d.Layers[0].Anim == k3 && d.Layers[1].Anim == k2, true);
            if (d.Layers.Count == 2)
            {
                Chk("layer scene: layer 0 frame", d.Layers[0].Frame, 0.25f * (Freeze.FrameCount(a, k3) - 1), 0.51);
                Chk("layer scene: layer 1 speed", d.Layers[1].Speed, 0.5, 1e-6);
            }
        });
        AddDelay(c, 8);
        AddOnce(c, () =>
        {
            if (!ok) return;
            Entity a = c.A!;
            var run = AnimProbe.RunningKeys(a);
            ChkBool("layer scene: running keys == [k1,k3,k2] after load (found " + AnimProbe.J(run) + ")", AnimProbe.SameSet(run, new[] { k1, k3, k2 }), true);
            try { SceneIO.Delete(api, sLay); } catch { }
        });
    }
}
