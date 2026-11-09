using System;
using System.Linq;
using Vintagestory.API.Common;

namespace DinoStage;

/// <summary>A user-facing command failure (bad code, layer cap, ...). The message goes back to the caller.</summary>
public sealed class StageFail : Exception { public StageFail(string m) : base(m) { } }

/// <summary>Pure state edits behind /stage anim, animlayer and resetpose (1.1.0). They change the StageData only; the caller commits it.</summary>
public static class AnimOps
{
    static void Parse(Entity e, string code, string? progress, out string key, out int frames, out bool hasProg, out float fr, out string how)
    {
        string? k = Freeze.ResolveAnimKey(e, code);
        if (k == null) throw new StageFail("Unknown animation '" + code + "' for " + e.Code + ". Use /stage list-anims.");
        key = k;
        frames = Freeze.FrameCount(e, key);
        hasProg = false; fr = 0f; how = "";
        if (progress != null)
        {
            if (!Ops.ParseProgress(progress, frames, out fr, out how)) throw new StageFail("Bad progress '" + progress + "'.");
            hasProg = true;
        }
    }

    /// <summary>/stage anim: clean switch. The animation becomes the only one held: every layer is cleared and (at assert time) every other animation is stopped and hard-stopped.</summary>
    public static string SetBase(Entity e, StageData d, string code, double? speed, string? progress)
    {
        Parse(e, code, progress, out string key, out int frames, out bool hasProg, out float fr, out string how);
        bool changed = !string.Equals(d.Anim, key, StringComparison.OrdinalIgnoreCase);
        int cleared = d.Layers.Count;
        d.Layers.Clear();
        d.Anim = key;
        d.Source = "";
        d.Speed = speed.HasValue ? (float)speed.Value : (hasProg ? 0f : 1f);
        if (hasProg) d.Frame = fr; else if (changed) d.Frame = 0f;
        return "anim " + key + " speed " + Ops.F(d.Speed) + " frame " + Ops.F(d.Frame) + " of " + frames + (hasProg ? " (" + how + ")" : "")
            + " (clean switch: every other animation stopped" + (cleared > 0 ? ", " + cleared + " layer(s) cleared" : "") + ")";
    }

    /// <summary>
    /// /stage animlayer: stack an animation on top of the held pose without resetting anything. With no base animation yet (empty), the base becomes the
    /// entity default animation (frame 0, speed 0) or else the captured highest-weight animation first. Re-applying an existing layer updates it.
    /// At most AnimSet.MaxLayers distinct layers; one more is a StageFail.
    /// </summary>
    public static string AddLayer(Entity e, StageData d, string code, double? speed, string? progress)
    {
        Parse(e, code, progress, out string key, out int frames, out bool hasProg, out float fr, out string how);
        string note = "";
        if (d.Anim.Length == 0)
        {
            Freeze.CaptureAnim(e, d);
            if (d.Anim.Length > 0) note = " (base was empty: became " + d.Anim + " [" + d.Source + "] first)";
        }
        if (string.Equals(d.Anim, key, StringComparison.OrdinalIgnoreCase))
            throw new StageFail("'" + key + "' is already the base animation. Use /stage anim, /stage speed or /stage progress to change the base, or pick another animation for the layer.");
        LayerData? ex = d.Layers.FirstOrDefault(l => string.Equals(l.Anim, key, StringComparison.OrdinalIgnoreCase));
        if (ex == null && d.Layers.Count >= AnimSet.MaxLayers)
            throw new StageFail("Layer limit reached: " + AnimSet.MaxLayers + " layers already (" + string.Join(", ", d.Layers.Select(l => l.Anim)) + "). A 5th layer was NOT added. Re-apply one of these to change it, /stage anim <code> to start over, or /stage resetpose to clear.");
        float sp = speed.HasValue ? (float)speed.Value : (hasProg ? 0f : 1f);
        if (ex == null)
        {
            ex = new LayerData { Anim = key, Speed = sp, Frame = hasProg ? fr : 0f };
            d.Layers.Add(ex);
        }
        else
        {
            ex.Speed = sp;
            if (hasProg) ex.Frame = fr;
        }
        return "layer " + key + " speed " + Ops.F(ex.Speed) + " frame " + Ops.F(ex.Frame) + " of " + frames + (hasProg ? " (" + how + ")" : "")
            + " stacked on base " + (d.Anim.Length == 0 ? "-" : d.Anim) + " (" + d.Layers.Count + "/" + AnimSet.MaxLayers + " layers)" + note;
    }

    /// <summary>/stage resetpose: base and layers cleared. Staged with no animation = bind pose: every animation stopped and hard-stopped, nothing re-held. Heading/pitch/roll untouched.</summary>
    public static string ResetPose(StageData d)
    {
        int n = d.Layers.Count;
        d.Anim = "";
        d.Speed = 0f;
        d.Frame = 0f;
        d.Source = "";
        d.Layers.Clear();
        return "pose reset to the bind pose (base animation" + (n > 0 ? " and " + n + " layer(s)" : "") + " cleared, all animations stopped, client idle suppressed; heading/pitch/roll untouched)";
    }
}
