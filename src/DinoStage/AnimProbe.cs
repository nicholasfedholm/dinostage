using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace DinoStage;

/// <summary>Read-only probes of the live animator and the shape animation data, used by the headless tests (SelfTest, ExtTest).</summary>
public static class AnimProbe
{
    /// <summary>Distinct lower-case shape animation codes of the entity with at least minFrames frames.</summary>
    public static List<string> ShapeKeys(Entity e, int minFrames = 5)
    {
        Animation[]? anims = e.Properties?.Client?.LoadedShapeForEntity?.Animations;
        var r = new List<string>();
        if (anims == null) return r;
        foreach (Animation a in anims)
        {
            string c = a.Code.ToLowerInvariant();
            if (a.QuantityFrames >= minFrames && !r.Contains(c)) r.Add(c);
        }
        return r;
    }

    /// <summary>Codes of every RunningAnimation that is Active or Running (what can still add a pose).</summary>
    public static HashSet<string> RunningKeys(Entity e)
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RunningAnimation[]? ras = e.AnimManager?.Animator?.Animations;
        if (ras == null) return s;
        foreach (RunningAnimation ra in ras) if (ra != null && (ra.Active || ra.Running)) s.Add(ra.Animation.Code);
        return s;
    }

    public static HashSet<string> ActiveKeys(Entity e)
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (e.AnimManager != null) foreach (string k in e.AnimManager.ActiveAnimationsByAnimCode.Keys) s.Add(k);
        return s;
    }

    public static string J(IEnumerable<string> s) => "[" + string.Join(",", s.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) + "]";

    public static bool SameSet(IEnumerable<string> a, IEnumerable<string> b) =>
        new HashSet<string>(a, StringComparer.OrdinalIgnoreCase).SetEquals(b);

    /// <summary>Frames of the RunningAnimations in a set: key -> CurrentFrame (NaN when absent).</summary>
    public static float Frame(Entity e, string key) => e.AnimManager?.Animator?.GetAnimationState(key)?.CurrentFrame ?? float.NaN;

    /// <summary>Element name -> [degX, degY, degZ, scaleX, scaleY, scaleZ, translateX, translateY, translateZ] from the animator's pose tree. Null when the server animator did not compute poses.</summary>
    public static Dictionary<string, float[]>? Poses(Entity e)
    {
        List<ElementPose>? roots = (e.AnimManager?.Animator as AnimatorBase)?.RootPoses;
        if (roots == null) return null;
        var r = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        void Walk(List<ElementPose>? l)
        {
            if (l == null) return;
            foreach (ElementPose p in l)
            {
                string n = p.ForElement?.Name ?? "?";
                r[n] = new[] { p.degX, p.degY, p.degZ, p.scaleX, p.scaleY, p.scaleZ, p.translateX, p.translateY, p.translateZ };
                Walk(p.ChildElementPoses);
            }
        }
        Walk(roots);
        return r;
    }

    static readonly float[] Identity = { 0, 0, 0, 1, 1, 1, 0, 0, 0 };

    public static float MaxAbs(float[] a, float[] b)
    {
        float m = 0;
        for (int i = 0; i < a.Length; i++) m = Math.Max(m, Math.Abs(a[i] - b[i]));
        return m;
    }

    /// <summary>Elements whose pose differs from the identity pose by more than eps (bones this animation set actually moves).</summary>
    public static HashSet<string> NonIdentity(Dictionary<string, float[]> p, float eps)
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in p) if (MaxAbs(kv.Value, Identity) > eps) s.Add(kv.Key);
        return s;
    }

    /// <summary>Elements whose pose differs between two pose maps by more than eps.</summary>
    public static HashSet<string> Differing(Dictionary<string, float[]> a, Dictionary<string, float[]> b, float eps)
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out float[]? o)) { s.Add(kv.Key); continue; }
            if (MaxAbs(kv.Value, o) > eps) s.Add(kv.Key);
        }
        return s;
    }

    public static float MaxDiff(Dictionary<string, float[]> a, Dictionary<string, float[]> b)
    {
        float m = 0;
        foreach (var kv in a) m = Math.Max(m, b.TryGetValue(kv.Key, out float[]? o) ? MaxAbs(kv.Value, o) : 1e9f);
        return m;
    }

    /// <summary>Diagnostic: the element with the largest pose difference and both pose rows.</summary>
    public static string Worst(Dictionary<string, float[]> a, Dictionary<string, float[]> b)
    {
        string w = "-"; float m = -1;
        foreach (var kv in a)
        {
            float d = b.TryGetValue(kv.Key, out float[]? o) ? MaxAbs(kv.Value, o) : 1e9f;
            if (d > m) { m = d; w = kv.Key; }
        }
        if (!a.TryGetValue(w, out float[]? ra) || !b.TryGetValue(w, out float[]? rb)) return w;
        return w + " a=" + string.Join(",", ra.Select(x => x.ToString("0.###"))) + " b=" + string.Join(",", rb.Select(x => x.ToString("0.###")));
    }

    /// <summary>Element names keyed by a shape animation (union over its keyframes), from the shape animation data.</summary>
    public static HashSet<string> KeyedElements(Entity e, string animKey)
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Animation[]? anims = e.Properties?.Client?.LoadedShapeForEntity?.Animations;
        if (anims == null) return s;
        foreach (Animation a in anims)
        {
            if (!string.Equals(a.Code, animKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (a.KeyFrames == null) break;
            foreach (AnimationKeyFrame kf in a.KeyFrames)
                if (kf.Elements != null) foreach (string n in kf.Elements.Keys) s.Add(n);
        }
        return s;
    }
}
