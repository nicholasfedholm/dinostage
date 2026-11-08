using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace DinoStage;

/// <summary>
/// Optional client part. The server AnimationPacket carries only a CRC32 and the speed per animation, not the frame,
/// so the client reads the synced tree "dinostage" and holds the stored frame itself.
/// </summary>
public static class ClientHold
{
    static ICoreClientAPI? capi;
    static long listener;

    public static void Start(ICoreClientAPI api)
    {
        capi = api;
        listener = api.Event.RegisterGameTickListener(Tick, 50);
    }

    public static void Stop()
    {
        if (capi != null && listener != 0) capi.Event.UnregisterGameTickListener(listener);
        listener = 0;
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
                try { Hold(e); } catch { /* one bad entity must not stop the rest */ }
            }
        }
        catch { }
    }

    static void Hold(Entity e)
    {
        ITreeAttribute? t = e.WatchedAttributes?.GetTreeAttribute(StageState.Key);
        if (t == null || !t.GetBool("staged", false)) return;
        string anim = t.GetString("anim", "") ?? "";
        if (anim.Length == 0) return;
        if (t.GetFloat("speed", 0f) != 0f) return; // playing, let the normal animator run
        RunningAnimation? st = e.AnimManager?.Animator?.GetAnimationState(anim);
        if (st == null || !st.Active) return;
        if (st.meta != null) st.meta.AnimationSpeed = 0f;
        st.CurrentFrame = t.GetFloat("frame", 0f);
        st.EasingFactor = 1f;
        st.BlendedWeight = 1f;
    }
}
