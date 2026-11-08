using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace DinoStage;

/// <summary>
/// Harmony guard around ItemEntityMoverTool.StartDisableEntityMovement / StopDisableEntityMovement (protected virtual).
/// Grab: mark held (yaw not re-asserted). Release: adopt the yaw the mover left, then re-assert the freeze in the same call.
/// </summary>
public static class MoverGuard
{
    static readonly HashSet<long> held = new();
    static readonly object lk = new();
    public static bool Applied { get; private set; }
    public static string Status { get; private set; } = "not attempted";
    public static ICoreAPI? Api;

    public static bool IsHeld(long id) { lock (lk) return held.Contains(id); }

    public static void Apply(Harmony harmony, ICoreAPI api)
    {
        Api = api;
        try
        {
            MethodInfo? start = AccessTools.Method(typeof(ItemEntityMoverTool), "StartDisableEntityMovement", new[] { typeof(Entity) });
            MethodInfo? stop = AccessTools.Method(typeof(ItemEntityMoverTool), "StopDisableEntityMovement", new[] { typeof(Entity) });
            if (start == null || stop == null)
            {
                Status = "method not found: start=" + (start != null) + " stop=" + (stop != null);
                api.Logger.Warning("[DinoStage] Mover patch NOT applied: " + Status + ". Falling back to 50 ms ticker only.");
                return;
            }
            harmony.Patch(start,
                prefix: new HarmonyMethod(typeof(MoverGuard), nameof(StartPrefix)),
                postfix: new HarmonyMethod(typeof(MoverGuard), nameof(StartPostfix)));
            harmony.Patch(stop,
                postfix: new HarmonyMethod(typeof(MoverGuard), nameof(StopPostfix)));
            Applied = true;
            Status = "applied (Start prefix+postfix, Stop postfix)";
            api.Logger.Notification("[DinoStage] Mover patch applied.");
        }
        catch (Exception ex)
        {
            Applied = false;
            Status = "failed: " + ex.GetType().Name + ": " + ex.Message;
            api.Logger.Warning("[DinoStage] Mover patch FAILED, falling back to 50 ms ticker only: " + ex);
        }
    }

    public static void Remove(Harmony? harmony)
    {
        try { harmony?.UnpatchAll(harmony.Id); } catch { }
        lock (lk) held.Clear();
        Applied = false;
    }

    static void StartPrefix(Entity target)
    {
        try { if (target != null && StageState.IsStaged(target)) lock (lk) held.Add(target.EntityId); }
        catch (Exception ex) { Api?.Logger.Warning("[DinoStage] StartPrefix: " + ex.Message); }
    }

    static void StartPostfix(Entity target)
    {
        try
        {
            if (target == null) return;
            StageData? d = StageState.Read(target);
            if (d != null && d.Staged) Freeze.Assert(target, d, held: true);
        }
        catch (Exception ex) { Api?.Logger.Warning("[DinoStage] StartPostfix: " + ex.Message); }
    }

    static void StopPostfix(Entity target)
    {
        try
        {
            if (target == null) return;
            lock (lk) held.Remove(target.EntityId);
            StageData? d = StageState.Read(target);
            if (d == null || !d.Staged) return;
            d.Yaw = target.Pos.Yaw; // adopt what Ctrl+wheel did
            StageState.Write(target, d);
            Freeze.Assert(target, d, held: false);
        }
        catch (Exception ex) { Api?.Logger.Warning("[DinoStage] StopPostfix: " + ex.Message); }
    }
}
