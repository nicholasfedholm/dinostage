using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace DinoStage;

/// <summary>
/// Server-side freeze model. Everything is re-asserted from the stored tree (StageState), never respawned.
/// </summary>
public static class Freeze
{
    static readonly ConditionalWeakTable<AiTaskManager, object> hooked = new();
    static readonly ConditionalWeakTable<Entity, AnimationMetaData> heldMeta = new();
    static readonly object Mark = new();
    static readonly ActionBoolReturn<IAiTask> ShouldH = static _ => false;
    static readonly AiTaskManager.OnExecuteTaskDelegate ExecH = static (_, _) => false;

    /// <summary>Apply the stored state to the live entity. Idempotent. held = a mover currently holds it (yaw is not re-asserted).</summary>
    public static void Assert(Entity e, StageData d, bool held)
    {
        // AI
        EntityBehaviorTaskAI? ai = e.GetBehavior<EntityBehaviorTaskAI>();
        AiTaskManager? tm = ai?.TaskManager;
        if (tm != null)
        {
            if (!hooked.TryGetValue(tm, out _))
            {
                tm.OnShouldExecuteTask += ShouldH;
                tm.OnExecuteTask += ExecH;
                hooked.Add(tm, Mark);
            }
            tm.StopTasks();
            ai!.PathTraverser?.Stop();
        }

        // Physics
        EntityBehaviorControlledPhysics? cp = e.GetBehavior<EntityBehaviorControlledPhysics>();
        if (cp != null) cp.EnableModulesApplication = !d.NoPhys;
        EntityBehaviorPassivePhysics? pp = e.GetBehavior<EntityBehaviorPassivePhysics>();
        if (pp != null) pp.Ticking = !d.NoPhys;
        if (d.NoPhys) e.Pos.Motion.Set(0.0, 0.0, 0.0);

        // Angles
        e.Pos.Pitch = d.Pitch;
        e.Pos.Roll = d.Roll;
        if (!held)
        {
            e.Pos.Yaw = d.Yaw;
            if (e is EntityAgent ag)
            {
                ag.BodyYaw = d.Yaw;
                ag.BodyYawServer = d.Yaw;
            }
        }

        AssertAnim(e, d);
    }

    static void AssertAnim(Entity e, StageData d)
    {
        IAnimationManager? am = e.AnimManager;
        if (am == null || d.Anim.Length == 0) return;
        AnimationManager? amc = am as AnimationManager;
        Dictionary<string, AnimationMetaData> active = am.ActiveAnimationsByAnimCode;

        List<string>? stop = null;
        foreach (string k in active.Keys)
        {
            if (!string.Equals(k, d.Anim, StringComparison.OrdinalIgnoreCase)) (stop ??= new List<string>()).Add(k);
        }
        if (stop != null) foreach (string k in stop) am.StopAnimation(k);

        AnimationMetaData meta = GetMeta(e, d.Anim);
        bool dirty = false;
        if (!active.TryGetValue(d.Anim, out AnimationMetaData? cur))
        {
            meta.AnimationSpeed = d.Speed;
            meta.StartFrameOnce = d.Frame;
            am.StartAnimation(meta);
        }
        else if (!ReferenceEquals(cur, meta))
        {
            meta.AnimationSpeed = d.Speed;
            active[d.Anim] = meta;
            RunningAnimation? st0 = am.Animator?.GetAnimationState(d.Anim);
            if (st0 != null) st0.meta = meta;
            dirty = true;
        }
        else if (meta.AnimationSpeed != d.Speed)
        {
            meta.AnimationSpeed = d.Speed;
            dirty = true;
        }

        if (d.Speed == 0f)
        {
            RunningAnimation? st = am.Animator?.GetAnimationState(d.Anim);
            if (st != null && st.Active)
            {
                st.CurrentFrame = d.Frame;
                st.EasingFactor = 1f;
                st.BlendedWeight = 1f;
            }
        }
        if (dirty && amc != null) amc.AnimationsDirty = true;
    }

    /// <summary>Held meta for this entity and shape animation; a private clone so vanilla metas keep their speed.</summary>
    static AnimationMetaData GetMeta(Entity e, string anim)
    {
        if (heldMeta.TryGetValue(e, out AnimationMetaData? m) && string.Equals(m.Animation, anim, StringComparison.OrdinalIgnoreCase)) return m;
        AnimationMetaData? src = null;
        if (e.AnimManager.ActiveAnimationsByAnimCode.TryGetValue(anim, out AnimationMetaData? act)) src = act;
        src ??= FindConfigMeta(e, anim);
        AnimationMetaData n = src != null
            ? src.Clone()
            : new AnimationMetaData { Code = anim, Animation = anim, CodeCrc32 = AnimationMetaData.GetCrc32(anim) };
        n.Animation = anim;
        n.MulWithWalkSpeed = false;
        n.SupressDefaultAnimation = true;
        n.StartFrameOnce = 0f;
        heldMeta.AddOrUpdate(e, n);
        return n;
    }

    public static AnimationMetaData? FindConfigMeta(Entity e, string codeOrAnim)
    {
        Dictionary<string, AnimationMetaData>? byCode = e.Properties?.Client?.AnimationsByMetaCode;
        if (byCode == null) return null;
        if (byCode.TryGetValue(codeOrAnim, out AnimationMetaData? m)) return m;
        foreach (AnimationMetaData v in byCode.Values)
        {
            if (string.Equals(v.Animation, codeOrAnim, StringComparison.OrdinalIgnoreCase)) return v;
        }
        return null;
    }

    /// <summary>Resolve a user code (entity config code or shape animation code) to the shape animation code, or null.</summary>
    public static string? ResolveAnimKey(Entity e, string code)
    {
        AnimationMetaData? m = FindConfigMeta(e, code);
        if (m != null) return (m.Animation ?? m.Code ?? code).ToLowerInvariant();
        Animation[]? anims = e.Properties?.Client?.LoadedShapeForEntity?.Animations;
        if (anims != null)
        {
            foreach (Animation a in anims)
            {
                if (string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase)) return a.Code.ToLowerInvariant();
            }
        }
        return null;
    }

    public static int FrameCount(Entity e, string anim)
    {
        Animation[]? anims = e.Properties?.Client?.LoadedShapeForEntity?.Animations;
        if (anims != null)
        {
            foreach (Animation a in anims)
            {
                if (string.Equals(a.Code, anim, StringComparison.OrdinalIgnoreCase)) return a.QuantityFrames;
            }
        }
        RunningAnimation? st = e.AnimManager?.Animator?.GetAnimationState(anim);
        return st?.Animation?.QuantityFrames ?? 0;
    }

    public static float? CurrentFrame(Entity e, string anim)
    {
        if (anim.Length == 0) return null;
        return e.AnimManager?.Animator?.GetAnimationState(anim)?.CurrentFrame;
    }

    /// <summary>Snapshot of the entity as it is now (used when freezing for the first time).</summary>
    public static StageData Capture(Entity e)
    {
        var d = new StageData { Staged = true, Yaw = e.Pos.Yaw, Pitch = e.Pos.Pitch, Roll = e.Pos.Roll, NoPhys = true };
        IAnimationManager? am = e.AnimManager;
        if (am != null)
        {
            string? best = null;
            float bestW = float.MinValue;
            foreach (KeyValuePair<string, AnimationMetaData> kv in am.ActiveAnimationsByAnimCode)
            {
                if (kv.Value.Weight > bestW) { bestW = kv.Value.Weight; best = kv.Key; }
            }
            if (best != null)
            {
                d.Anim = best.ToLowerInvariant();
                d.Frame = CurrentFrame(e, d.Anim) ?? 0f;
            }
        }
        return d;
    }

    /// <summary>Number of subscribers on AiTaskManager.OnShouldExecuteTask (field-like event, read by reflection) or -1.</summary>
    public static int ShouldHandlerCount(AiTaskManager tm)
    {
        try
        {
            var f = typeof(AiTaskManager).GetField("OnShouldExecuteTask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var del = f?.GetValue(tm) as Delegate;
            return del == null ? 0 : del.GetInvocationList().Length;
        }
        catch { return -1; }
    }

    public static bool IsHooked(Entity e)
    {
        AiTaskManager? tm = e.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
        return tm != null && hooked.TryGetValue(tm, out _);
    }

    /// <summary>Undo the freeze: AI handlers off, physics back on, held animation stopped so tasks restart, tree cleared.</summary>
    public static void Thaw(Entity e)
    {
        StageData? d = StageState.Read(e);

        EntityBehaviorTaskAI? ai = e.GetBehavior<EntityBehaviorTaskAI>();
        AiTaskManager? tm = ai?.TaskManager;
        if (tm != null && hooked.TryGetValue(tm, out _))
        {
            tm.OnShouldExecuteTask -= ShouldH;
            tm.OnExecuteTask -= ExecH;
            hooked.Remove(tm);
        }

        EntityBehaviorControlledPhysics? cp = e.GetBehavior<EntityBehaviorControlledPhysics>();
        if (cp != null) cp.EnableModulesApplication = true;
        EntityBehaviorPassivePhysics? pp = e.GetBehavior<EntityBehaviorPassivePhysics>();
        if (pp != null) pp.Ticking = true;

        IAnimationManager? am = e.AnimManager;
        if (am != null && d != null && d.Anim.Length > 0)
        {
            am.StopAnimation(d.Anim);
            if (am is AnimationManager amc) amc.AnimationsDirty = true;
        }
        heldMeta.Remove(e);

        e.Pos.Pitch = 0f;
        e.Pos.Roll = 0f;
        StageState.Clear(e);
    }
}
