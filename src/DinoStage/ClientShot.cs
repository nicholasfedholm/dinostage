using System;
using System.IO;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace DinoStage;

/// <summary>Server to client request for /stage shot. Name is already validated server side and again on the client.</summary>
[ProtoContract]
public class ShotMsg
{
    [ProtoMember(1)] public string Name = "";
}

/// <summary>
/// Client part of /stage shot. Hides the HUD with the vanilla client command ".gui false" (same switch as F4),
/// waits a few frames, grabs the default framebuffer in the Done render stage (same stage and call the vanilla F12 path uses),
/// saves a png, then restores the HUD to what it was. Touches no entity, so freeze and pose are untouched.
/// </summary>
public sealed class ClientShot : IRenderer
{
    public const string Channel = "dinostage";
    const int WaitFrames = 4;

    readonly ICoreClientAPI capi;
    string? pending;
    bool restoreHud;
    int frames;

    public double RenderOrder => 2.0;
    public int RenderRange => 1;

    public ClientShot(ICoreClientAPI api)
    {
        capi = api;
        api.Network.RegisterChannel(Channel).RegisterMessageType<ShotMsg>().SetMessageHandler<ShotMsg>(OnMsg);
        api.Event.RegisterRenderer(this, EnumRenderStage.Done, "dinostage-shot");
    }

    void OnMsg(ShotMsg m)
    {
        if (pending != null) { capi.ShowChatMessage("[DinoStage] a shot is already in progress"); return; }
        if (!Ops.ValidShotName(m.Name)) { capi.ShowChatMessage("[DinoStage] bad shot name"); return; }
        pending = m.Name;
        frames = 0;
        restoreHud = !capi.HideGuis;
        if (restoreHud) capi.TriggerChatMessage(".gui false");
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (pending == null) return;
        if (++frames < WaitFrames) return;
        string name = pending;
        pending = null;
        string? msg = null;
        try
        {
            string dir = Environment.GetEnvironmentVariable("DINOSTAGE_SHOTS") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DinoStage", "shots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name + ".png");
            bool flip = capi.Settings.Bool["flipScreenshot"];
            using (BitmapRef bmp = capi.Render.GrabScreenshot(capi.Render.FrameWidth, capi.Render.FrameHeight, false, flip, false))
            {
                bmp.Save(path);
            }
            msg = "[DinoStage] shot saved: " + path;
        }
        catch (Exception ex)
        {
            msg = "[DinoStage] shot failed: " + ex.Message;
            capi.Logger.Error("[DinoStage] shot failed: " + ex);
        }
        if (restoreHud) capi.TriggerChatMessage(".gui true");
        restoreHud = false;
        if (msg != null) capi.ShowChatMessage(msg);
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Done);
    }
}
