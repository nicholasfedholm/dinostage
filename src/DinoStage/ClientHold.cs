using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace DinoStage;

/// <summary>
/// Optional client part. The server AnimationPacket carries only a CRC32 and the speed per animation, not the frame (and skips the default animation),
/// so the client reads the synced tree "dinostage" and holds the stored frames itself (base + layers).
/// 1.1.0: the client also makes exactly {base} + {layers} active (AnimSet.Clean), because the client default idle animation is restarted by
/// EntityAgent.OnGameTick (EntityAgent.cs:583-652) on every client tick. HoldEntity runs from a Harmony postfix on EntityAgent.OnGameTick (right after
/// that block, before the animator's OnFrame in the render pass) and from the 50 ms tick below as a fallback / for entities that do not reach the patch.
/// </summary>
public static class ClientHold
{
    static ICoreClientAPI? capi;
    static long listener;
    static Harmony? harmony;
    /// <summary>Entity ids this client has held; used to release them once the tree is gone (thaw).</summary>
    static readonly HashSet<long> held = new();
    /// <summary>Original speed of a config default-animation meta that we zeroed, so a thaw restores the idle speed.</summary>
    static readonly ConditionalWeakTable<AnimationMetaData, StrongBox<float>> origSpeed = new();

    public static string PatchStatus { get; private set; } = "not attempted";

    public static void Start(ICoreClientAPI api)
    {
        capi = api;
        listener = api.Event.RegisterGameTickListener(Tick, 50);
        try
        {
            MethodInfo? m = AccessTools.Method(typeof(EntityAgent), "OnGameTick", new[] { typeof(float) });
            if (m == null) { PatchStatus = "EntityAgent.OnGameTick not found; 50 ms tick only"; api.Logger.Warning("[DinoStage] client patch: " + PatchStatus); return; }
            harmony = new Harmony("dinostage.client");
            harmony.Patch(m, postfix: new HarmonyMethod(typeof(ClientHold), nameof(AgentTickPostfix)));
            PatchStatus = "applied (EntityAgent.OnGameTick postfix)";
            api.Logger.Notification("[DinoStage] client hold: " + PatchStatus);
        }
        catch (Exception ex)
        {
            PatchStatus = "failed: " + ex.GetType().Name + ": " + ex.Message + "; 50 ms tick only";
            api.Logger.Warning("[DinoStage] client patch FAILED, falling back to the 50 ms tick: " + ex.Message);
        }
    }

    public static void Stop()
    {
        if (capi != null && listener != 0) capi.Event.UnregisterGameTickListener(listener);
        listener = 0;
        try { harmony?.UnpatchAll("dinostage.client"); } catch { }
        harmony = null;
        held.Clear();
        capi = null;
    }

    static void Tick(float dt)
    {
        ICoreClientAPI? api = capi;
        if (api == null) return;
        try
        {
            foreach (Entity e in api.World.LoadedEntities.Values)
            {
                try
                {
                    if (StageState.IsStaged(e)) { held.Add(e.EntityId); HoldEntity(e); }
                    else if (held.Remove(e.EntityId)) Release(e);
                }
                catch { /* one bad entity must not stop the rest */ }
            }
        }
        catch { }
    }

    /// <summary>Harmony postfix: runs after EntityAgent.OnGameTick, i.e. after the client default-animation block restarted idle.</summary>
    static void AgentTickPostfix(EntityAgent __instance)
    {
        try
        {
            if (__instance.World == null || __instance.World.Side != EnumAppSide.Client) return;
            if (!StageState.IsStaged(__instance)) return;
            HoldEntity(__instance);
        }
        catch { }
    }

    /// <summary>Make exactly the wanted animations active on this (client) entity and hold their frames. Public so the headless tests can drive it against a server entity.</summary>
    public static void HoldEntity(Entity e)
    {
        StageData? d = StageState.Read(e);
        if (d == null || !d.Staged) return;
        IAnimationManager? am = e.AnimManager;
        if (am == null) return;
        AnimSet.Clean(am, AnimSet.Wanted(d));
        if (d.Anim.Length > 0) HoldOne(e, am, d.Anim, d.Speed, d.Frame);
        foreach (LayerData l in d.Layers) HoldOne(e, am, l.Anim, l.Speed, l.Frame);
    }

    static void HoldOne(Entity e, IAnimationManager am, string key, float speed, float frame)
    {
        Dictionary<string, AnimationMetaData> active = am.ActiveAnimationsByAnimCode;
        if (!AnimSet.HasKey(active, key))
        {
            // Only the default animation is started here (the server packet never carries it, and an Average-blend layer makes EntityAgent stop it, EntityAgent.cs:633-638).
            AnimationMetaData? dm = Freeze.DefaultAnimMeta(e);
            if (dm != null && string.Equals((dm.Animation ?? dm.Code ?? ""), key, StringComparison.OrdinalIgnoreCase))
            {
                dm.WasStartedFromTrigger = true;
                dm.StartFrameOnce = frame;
                am.StartAnimation(dm);
            }
        }
        if (speed != 0f) return; // playing: the normal animator runs it

        active.TryGetValue(key, out AnimationMetaData? meta);
        if (meta != null) { Remember(meta); meta.AnimationSpeed = 0f; }
        RunningAnimation? st = am.Animator?.GetAnimationState(key);
        if (st == null) return;
        if (st.meta != null && !ReferenceEquals(st.meta, meta)) { Remember(st.meta); st.meta.AnimationSpeed = 0f; }
        if (st.Active)
        {
            st.CurrentFrame = frame;
            st.EasingFactor = 1f;
            st.BlendedWeight = 1f;
        }
    }

    static void Remember(AnimationMetaData m)
    {
        if (m.TriggeredBy?.DefaultAnim != true) return; // packet-delivered metas get their speed from the next packet anyway
        origSpeed.GetValue(m, x => new StrongBox<float>(x.AnimationSpeed));
    }

    /// <summary>The entity is no longer staged: restore the default idle speed we zeroed and hard-stop leftover speed 0 animations that are not in the dictionary.</summary>
    static void Release(Entity e)
    {
        AnimationMetaData? dm = Freeze.DefaultAnimMeta(e);
        if (dm != null && origSpeed.TryGetValue(dm, out StrongBox<float>? box))
        {
            dm.AnimationSpeed = box.Value;
            origSpeed.Remove(dm);
        }
        IAnimationManager? am = e.AnimManager;
        if (am != null) AnimSet.HardStopOrphans(am);
    }
}
