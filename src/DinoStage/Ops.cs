using System;
using System.Collections.Generic;
using System.Globalization;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace DinoStage;

/// <summary>Entity-level operations shared by commands, scenes and the selftest.</summary>
public static class Ops
{
    /// <summary>Test only: spawned entities get AlwaysActive (headless server has no player, so entities would stay Inactive and never tick: no gravity, no AI, no animator).</summary>
    public static bool TestAlwaysActive;

    /// <summary>Test only: spawned entities get requirePosesOnServer so the server animator computes element poses (RootPoses), letting tests compare real engine pose data.</summary>
    public static bool TestRequirePoses;

    public static readonly List<Entity> TestPumped = new();
    static readonly HashSet<Entity> pumpSeen = new();
    static long pumpId;
    public static int PumpTicks;

    /// <summary>Test only. A headless server with no player never runs physics for entities (IsTracked=0, ControlledPhysics.Ticking stays false: even a never-frozen control pig hangs in the air).
    /// This drives the real EntityBehaviorControlledPhysics.OnPhysicsTick/AfterPhysicsTick (2 x 1/30 s per 50 ms game tick) for test entities, so freeze/thaw is tested against the game's own gravity and collision code.</summary>
    public static void StartPump(ICoreServerAPI api)
    {
        if (pumpId != 0) return;
        pumpId = api.Event.RegisterGameTickListener(_ =>
        {
            for (int i = TestPumped.Count - 1; i >= 0; i--)
            {
                Entity e = TestPumped[i];
                bool present = e.EntityId != 0 && ReferenceEquals(api.World.GetEntityById(e.EntityId), e);
                if (present) pumpSeen.Add(e);
                else { if (pumpSeen.Contains(e)) { TestPumped.RemoveAt(i); pumpSeen.Remove(e); } continue; }
                EntityBehaviorControlledPhysics? cp = e.GetBehavior<EntityBehaviorControlledPhysics>();
                if (cp == null) continue;
                try { cp.OnPhysicsTick(1f / 30f); cp.OnPhysicsTick(1f / 30f); cp.AfterPhysicsTick(1f / 30f); PumpTicks++; }
                catch (Exception ex) { api.Logger.Warning("[DinoStage test] physics pump: " + ex.Message); }
            }
        }, 50);
    }

    public const float Deg = (float)(Math.PI / 180.0);

    public static Entity? Spawn(ICoreServerAPI api, string code, double x, double y, double z, float yaw, out string error)
    {
        error = "";
        EntityProperties? props = api.World.GetEntityType(new AssetLocation(code));
        if (props == null) { error = "Unknown entity type '" + code + "'."; return null; }
        Entity e = api.ClassRegistry.CreateEntity(props);
        if (TestAlwaysActive) e.AlwaysActive = true;
        if (TestRequirePoses) e.requirePosesOnServer = true;
        e.Pos.SetPos(x, y, z);
        e.Pos.Yaw = yaw;
        e.Pos.Pitch = 0f;
        e.Pos.Roll = 0f;
        api.World.SpawnEntity(e);
        if (TestAlwaysActive) { TestPumped.Add(e); StartPump(api); }
        return e;
    }

    /// <summary>Spawn column for tests: WorldManager.DefaultSpawnPosition (throws NRE headless with no player yet), else World.DefaultSpawnPosition, else map centre.</summary>
    public static void SpawnXZ(ICoreServerAPI api, out int x, out int z)
    {
        try { int[]? sp = api.WorldManager.DefaultSpawnPosition; if (sp != null && sp.Length >= 3) { x = sp[0]; z = sp[2]; return; } } catch { }
        try { EntityPos? p = api.World.DefaultSpawnPosition; if (p != null) { x = (int)p.X; z = (int)p.Z; return; } } catch { }
        x = api.WorldManager.MapSizeX / 2; z = api.WorldManager.MapSizeZ / 2;
    }

    public static void Despawn(Entity e)
    {
        e.Die(EnumDespawnReason.Removed);
    }

    /// <summary>Freeze (first time: capture current pose; later: keep stored). Optional overrides.</summary>
    public static StageData FreezeEntity(DinoStageSystem sys, Entity e, string? animKey = null, float? speed = null, float? frame = null)
    {
        StageData d = StageState.Read(e) ?? Freeze.Capture(e);
        if (!d.Staged)
        {
            // stored tree existed but not staged: recapture the physical angles
            d.Staged = true;
            d.Yaw = e.Pos.Yaw; d.Pitch = e.Pos.Pitch; d.Roll = e.Pos.Roll;
        }
        if (animKey != null) { d.Anim = animKey; d.Source = ""; }
        if (speed.HasValue) d.Speed = speed.Value;
        if (frame.HasValue) d.Frame = frame.Value;
        Commit(sys, e, d);
        return d;
    }

    public static void Commit(DinoStageSystem sys, Entity e, StageData d)
    {
        d.Staged = true;
        StageState.Write(e, d);
        sys.Staged.Add(e.EntityId);
        Freeze.Assert(e, d, MoverGuard.IsHeld(e.EntityId));
    }

    public static void ThawEntity(DinoStageSystem sys, Entity e)
    {
        sys.Staged.Remove(e.EntityId);
        Freeze.Thaw(e);
    }

    /// <summary>Progress text: a decimal in 0..1 (has a '.') or ending with % is a fraction; otherwise a frame number.</summary>
    public static bool ParseProgress(string s, int frames, out float frame, out string how)
    {
        frame = 0f; how = "";
        s = s.Trim();
        bool pct = s.EndsWith("%");
        if (pct) s = s.Substring(0, s.Length - 1);
        if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return false;
        int last = Math.Max(0, frames - 1);
        if (pct) { frame = v / 100f * last; how = "fraction " + (v / 100f).ToString("0.###", CultureInfo.InvariantCulture); }
        else if (s.Contains('.') && v >= 0f && v <= 1f) { frame = v * last; how = "fraction " + v.ToString("0.###", CultureInfo.InvariantCulture); }
        else { frame = v; how = "frame number"; }
        if (frames > 0) frame = Math.Clamp(frame, 0f, last);
        return true;
    }

    public static bool ValidShotName(string? n)
    {
        if (string.IsNullOrEmpty(n) || n.Length > 64) return false;
        foreach (char c in n) if (!(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')) return false;
        return true;
    }

    public static bool TryNum(string? s, out double v)
    {
        v = 0;
        return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    public static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Describe(Entity e)
    {
        StageData? d = StageState.Read(e);
        string s = e.Code.ToString() + " id=" + e.EntityId + " at " + F(e.Pos.X) + "," + F(e.Pos.Y) + "," + F(e.Pos.Z);
        if (d != null && d.Staged)
        {
            // "(default)" = the entity default animation was chosen at freeze time, "(captured)" = highest weight active animation
            string src = d.Source.Length > 0 && d.Anim.Length > 0 ? " (" + d.Source + ")" : "";
            s += " STAGED anim=" + (d.Anim.Length == 0 ? "-" : d.Anim) + " speed=" + F(d.Speed) + " frame=" + F(d.Frame) + src;
            if (d.Layers.Count > 0)
                s += " layers=[" + string.Join(", ", d.Layers.ConvertAll(l => l.Anim + " speed=" + F(l.Speed) + " frame=" + F(l.Frame))) + "]";
            s += " heading=" + F(d.Yaw / Deg) + " pitch=" + F(d.Pitch / Deg) + " roll=" + F(d.Roll / Deg);
        }
        return s;
    }
}
