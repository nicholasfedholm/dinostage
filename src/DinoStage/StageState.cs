using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace DinoStage;

/// <summary>One stacked animation (1.1.0): held on top of the base animation, same speed/frame semantics as the base.</summary>
public sealed class LayerData
{
    /// <summary>Shape animation code (lower case).</summary>
    public string Anim = "";
    public float Speed;
    public float Frame;

    public LayerData Copy() => (LayerData)MemberwiseClone();
}

/// <summary>Plain copy of the watched-attribute tree "dinostage" on an entity.</summary>
public sealed class StageData
{
    public bool Staged;
    /// <summary>Shape animation code (AnimationMetaData.Animation, lower case). Empty = bind pose (nothing held, every other animation stopped).</summary>
    public string Anim = "";
    /// <summary>0 = hold the stored frame. Non-zero = animation plays at that speed.</summary>
    public float Speed;
    public float Frame;
    public float Yaw;
    public float Pitch;
    public float Roll;
    /// <summary>true = physics (gravity etc.) off while staged.</summary>
    public bool NoPhys = true;
    /// <summary>How the base animation was chosen: "" = explicit (command or scene), "default" = the entity default animation at frame 0, "captured" = highest weight active animation at freeze time. Optional (1.1.0).</summary>
    public string Source = "";
    /// <summary>Stacked animations (1.1.0, optional, at most AnimSet.MaxLayers). Empty for a 1.0.0 tree or scene.</summary>
    public List<LayerData> Layers = new();

    public StageData Copy()
    {
        var c = (StageData)MemberwiseClone();
        c.Layers = Layers.Select(l => l.Copy()).ToList();
        return c;
    }
}

/// <summary>Reads and writes the synced, persisted tree entity.WatchedAttributes["dinostage"].</summary>
public static class StageState
{
    public const string Key = "dinostage";

    public static StageData? Read(Entity e)
    {
        ITreeAttribute? t = e.WatchedAttributes.GetTreeAttribute(Key);
        if (t == null) return null;
        var d = new StageData
        {
            Staged = t.GetBool("staged", false),
            Anim = t.GetString("anim", "") ?? "",
            Speed = t.GetFloat("speed", 0f),
            Frame = t.GetFloat("frame", 0f),
            Yaw = t.GetFloat("yaw", 0f),
            Pitch = t.GetFloat("pitch", 0f),
            Roll = t.GetFloat("roll", 0f),
            NoPhys = t.GetBool("noPhys", true),
            Source = t.GetString("animSrc", "") ?? "",
        };
        // optional 1.1.0 subtree; a 1.0.0 tree has none
        ITreeAttribute? lt = t.GetTreeAttribute("layers");
        if (lt != null)
        {
            int n = System.Math.Min(lt.GetInt("n", 0), AnimSet.MaxLayers);
            for (int i = 0; i < n; i++)
            {
                string a = lt.GetString("a" + i, "") ?? "";
                if (a.Length == 0) continue;
                d.Layers.Add(new LayerData { Anim = a, Speed = lt.GetFloat("s" + i, 0f), Frame = lt.GetFloat("f" + i, 0f) });
            }
        }
        return d;
    }

    public static bool IsStaged(Entity e) => e.WatchedAttributes.GetTreeAttribute(Key)?.GetBool("staged", false) ?? false;

    public static void Write(Entity e, StageData d)
    {
        ITreeAttribute? t = e.WatchedAttributes.GetTreeAttribute(Key);
        if (t == null)
        {
            t = new TreeAttribute();
            e.WatchedAttributes.SetAttribute(Key, t);
        }
        t.SetBool("staged", d.Staged);
        t.SetString("anim", d.Anim);
        t.SetFloat("speed", d.Speed);
        t.SetFloat("frame", d.Frame);
        t.SetFloat("yaw", d.Yaw);
        t.SetFloat("pitch", d.Pitch);
        t.SetFloat("roll", d.Roll);
        t.SetBool("noPhys", d.NoPhys);
        t.SetString("animSrc", d.Source);
        // always write the subtree (n = 0 when empty) so a synced client never keeps stale layers
        var lt = new TreeAttribute();
        int n = System.Math.Min(d.Layers.Count, AnimSet.MaxLayers);
        lt.SetInt("n", n);
        for (int i = 0; i < n; i++)
        {
            lt.SetString("a" + i, d.Layers[i].Anim);
            lt.SetFloat("s" + i, d.Layers[i].Speed);
            lt.SetFloat("f" + i, d.Layers[i].Frame);
        }
        t["layers"] = lt;
        e.WatchedAttributes.MarkPathDirty(Key);
    }

    public static void Clear(Entity e)
    {
        if (e.WatchedAttributes.HasAttribute(Key))
        {
            e.WatchedAttributes.RemoveAttribute(Key);
            e.WatchedAttributes.MarkPathDirty(Key);
        }
    }
}
