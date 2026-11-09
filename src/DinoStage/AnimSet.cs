using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace DinoStage;

/// <summary>
/// Makes exactly the wanted set of animations active (1.1.0). Shared by the server assert and the client hold.
/// Evidence (dec_proj, VintagestoryAPI): see API_EVIDENCE.md "DinoStage 1.1.0 reset mechanism".
/// StopAnimation only removes the dictionary key; a speed 0 held RunningAnimation then stays Running with EasingFactor 1
/// (RunningAnimation.Progress returns at speed 0, AnimatorBase.ProgressRunningAnimation never reaches its stop test) and keeps adding its pose.
/// RunningAnimation.Stop() is the only immediate hard stop, and it only sticks when the key is already gone from the dictionary
/// (AnimatorBase.OnFrame re-activates any RunningAnimation whose key is present).
/// </summary>
public static class AnimSet
{
    public const int MaxLayers = 4;

    /// <summary>Base animation plus layers, lower case, case-insensitive set.</summary>
    public static HashSet<string> Wanted(StageData d)
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (d.Anim.Length > 0) s.Add(d.Anim);
        foreach (LayerData l in d.Layers) if (l.Anim.Length > 0) s.Add(l.Anim);
        return s;
    }

    public static bool HasKey(Dictionary<string, AnimationMetaData> active, string code)
    {
        if (active.ContainsKey(code)) return true;
        foreach (string k in active.Keys) if (string.Equals(k, code, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// (1) am.StopAnimation(key) for every active key not in keep, then (2) RunningAnimation.Stop() on every RunningAnimation (Active or Running)
    /// that is not in keep. Order matters. Returns the number of keys removed + running animations hard-stopped.
    /// </summary>
    public static int Clean(IAnimationManager am, HashSet<string> keep)
    {
        int n = 0;
        Dictionary<string, AnimationMetaData> active = am.ActiveAnimationsByAnimCode;
        List<string>? stop = null;
        foreach (string k in active.Keys)
        {
            if (!keep.Contains(k)) (stop ??= new List<string>()).Add(k);
        }
        if (stop != null) foreach (string k in stop) { am.StopAnimation(k); n++; }

        RunningAnimation[]? ras = am.Animator?.Animations;
        if (ras != null)
        {
            foreach (RunningAnimation ra in ras)
            {
                if (ra == null || !(ra.Active || ra.Running)) continue;
                string? code = ra.Animation?.Code;
                if (code == null || keep.Contains(code)) continue;
                if (HasKey(active, code)) continue; // would be re-activated by AnimatorBase.OnFrame; never reached after step 1
                ra.Stop();
                n++;
            }
        }
        return n;
    }

    /// <summary>Hard-stop every Active/Running RunningAnimation whose key is NOT in the dictionary (the stuck speed 0 leftovers). Never removes a dictionary key.</summary>
    public static int HardStopOrphans(IAnimationManager am)
    {
        int n = 0;
        RunningAnimation[]? ras = am.Animator?.Animations;
        if (ras == null) return 0;
        Dictionary<string, AnimationMetaData> active = am.ActiveAnimationsByAnimCode;
        foreach (RunningAnimation ra in ras)
        {
            if (ra == null || !(ra.Active || ra.Running)) continue;
            string? code = ra.Animation?.Code;
            if (code == null || HasKey(active, code)) continue;
            ra.Stop();
            n++;
        }
        return n;
    }
}
