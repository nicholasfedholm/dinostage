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
    /// <summary>Per entity, per shape animation key: the private held clone (base and layers each have one).</summary>
    static readonly ConditionalWeakTable<Entity, Dictionary<string, AnimationMetaData>> heldMeta = new();
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

    /// <summary>
    /// Make exactly {base} + {layers} active (1.1.0): StopAnimation + RunningAnimation.Stop() on everything else (AnimSet.Clean),
    /// then start/refresh/hold the base and each layer. Staged with no base and no layers = bind pose (everything stopped).
    /// </summary>
    static void AssertAnim(Entity e, StageData d)
    {
        IAnimationManager? am = e.AnimManager;
        if (am == null) return;
        AnimationManager? amc = am as AnimationManager;

        AnimSet.Clean(am, AnimSet.Wanted(d)); // StopAnimation sets AnimationsDirty itself on the server

        bool dirty = false;
        if (d.Anim.Length > 0) dirty |= AssertOne(e, am, d.Anim, d.Speed, d.Frame);
        foreach (LayerData l in d.Layers) dirty |= AssertOne(e, am, l.Anim, l.Speed, l.Frame);
        if (dirty && amc != null) amc.AnimationsDirty = true;
    }

    static bool AssertOne(Entity e, IAnimationManager am, string key, float speed, float frame)
    {
        Dictionary<string, AnimationMetaData> active = am.ActiveAnimationsByAnimCode;
        AnimationMetaData meta = GetMeta(e, key);
        bool dirty = false;
        if (!active.TryGetValue(key, out AnimationMetaData? cur))
        {
            meta.AnimationSpeed = speed;
            meta.StartFrameOnce = frame;
            am.StartAnimation(meta);
            if (speed == 0f)
            {
                // pre-arm the held weight so there is no bind-pose flash between start and the next assert (a Stop()ed animation has EasingFactor 0)
                RunningAnimation? pre = am.Animator?.GetAnimationState(key);
                if (pre != null) { pre.EasingFactor = 1f; pre.BlendedWeight = 1f; }
            }
        }
        else if (!ReferenceEquals(cur, meta))
        {
            meta.AnimationSpeed = speed;
            active[key] = meta;
            RunningAnimation? st0 = am.Animator?.GetAnimationState(key);
            if (st0 != null) st0.meta = meta;
            dirty = true;
        }
        else if (meta.AnimationSpeed != speed)
        {
            meta.AnimationSpeed = speed;
            dirty = true;
        }

        // A RunningAnimation kept from before (e.g. the AI's own trot, speed 1.2) can still point at the old meta; make it follow the held meta
        // so a speed 0 hold really stops progressing between asserts.
        RunningAnimation? own = am.Animator?.GetAnimationState(key);
        if (own != null && !ReferenceEquals(own.meta, meta)) { own.meta = meta; dirty = true; }

        if (speed == 0f)
        {
            RunningAnimation? st = am.Animator?.GetAnimationState(key);
            if (st != null && st.Active)
            {
                st.CurrentFrame = frame;
                st.EasingFactor = 1f;
                st.BlendedWeight = 1f;
            }
        }
        return dirty;
    }

    /// <summary>Held meta for this entity and shape animation; a private clone so vanilla metas keep their speed.</summary>
    static AnimationMetaData GetMeta(Entity e, string anim)
    {
        Dictionary<string, AnimationMetaData> per = heldMeta.GetOrCreateValue(e);
        if (per.TryGetValue(anim, out AnimationMetaData? m)) return m;
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
        per[anim] = n;
        return n;
    }

    /// <summary>The entity's default animation config entry: client.animations entry with triggeredBy.defaultAnim == true (the same test EntityAgent.cs:608 uses), or null.</summary>
    public static AnimationMetaData? DefaultAnimMeta(Entity e)
    {
        AnimationMetaData[]? anims = e.Properties?.Client?.Animations;
        if (anims == null) return null;
        foreach (AnimationMetaData a in anims)
        {
            if (a != null && a.TriggeredBy?.DefaultAnim == true) return a;
        }
        return null;
    }

    /// <summary>Lower-case shape animation key of the default animation, or null.</summary>
    public static string? DefaultAnimKey(Entity e)
    {
        AnimationMetaData? m = DefaultAnimMeta(e);
        string? k = m?.Animation ?? m?.Code;
        return string.IsNullOrEmpty(k) ? null : k!.ToLowerInvariant();
    }

    /// <summary>
    /// Choose the base animation for d (no explicit stage anim): the entity default animation at frame 0, speed 0 (Source "default");
    /// else the highest-Weight active animation at its current frame (Source "captured"); else nothing (Anim stays empty).
    /// </summary>
    public static void CaptureAnim(Entity e, StageData d)
    {
        string? def = DefaultAnimKey(e);
        if (def != null)
        {
            d.Anim = def; d.Speed = 0f; d.Frame = 0f; d.Source = "default";
            return;
        }
        IAnimationManager? am = e.AnimManager;
        if (am == null) return;
        string? best = null;
        float bestW = float.MinValue;
        foreach (KeyValuePair<string, AnimationMetaData> kv in am.ActiveAnimationsByAnimCode)
        {
            if (kv.Value.Weight > bestW) { bestW = kv.Value.Weight; best = kv.Key; }
        }
        if (best != null)
        {
            d.Anim = best.ToLowerInvariant();
            d.Speed = 0f;
            d.Frame = CurrentFrame(e, d.Anim) ?? 0f;
            d.Source = "captured";
        }
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
        CaptureAnim(e, d);
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
        if (am != null && d != null)
        {
            // base + layers: key removed first, then the speed 0 leftovers are hard-stopped (a plain StopAnimation leaves them stuck, see AnimSet)
            AnimSet.Clean(am, AnimSet.Wanted(new StageData()));
            if (am is AnimationManager amc) amc.AnimationsDirty = true;
        }
        heldMeta.Remove(e);

        e.Pos.Pitch = 0f;
        e.Pos.Roll = 0f;
        StageState.Clear(e);
    }
}
