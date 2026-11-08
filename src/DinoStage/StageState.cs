using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace DinoStage;

/// <summary>Plain copy of the watched-attribute tree "dinostage" on an entity.</summary>
public sealed class StageData
{
    public bool Staged;
    /// <summary>Shape animation code (AnimationMetaData.Animation, lower case). Empty = AI/physics freeze only.</summary>
    public string Anim = "";
    /// <summary>0 = hold the stored frame. Non-zero = animation plays at that speed.</summary>
    public float Speed;
    public float Frame;
    public float Yaw;
    public float Pitch;
    public float Roll;
    /// <summary>true = physics (gravity etc.) off while staged.</summary>
    public bool NoPhys = true;

    public StageData Copy() => (StageData)MemberwiseClone();
}

/// <summary>Reads and writes the synced, persisted tree entity.WatchedAttributes["dinostage"].</summary>
public static class StageState
{
    public const string Key = "dinostage";

    public static StageData? Read(Entity e)
    {
        ITreeAttribute? t = e.WatchedAttributes.GetTreeAttribute(Key);
        if (t == null) return null;
        return new StageData
        {
            Staged = t.GetBool("staged", false),
            Anim = t.GetString("anim", "") ?? "",
            Speed = t.GetFloat("speed", 0f),
            Frame = t.GetFloat("frame", 0f),
            Yaw = t.GetFloat("yaw", 0f),
            Pitch = t.GetFloat("pitch", 0f),
            Roll = t.GetFloat("roll", 0f),
            NoPhys = t.GetBool("noPhys", true),
        };
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
