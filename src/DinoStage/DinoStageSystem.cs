using System;
using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace DinoStage;

public sealed class DinoStageSystem : ModSystem
{
    public ICoreServerAPI? Sapi;
    ICoreClientAPI? capi;
    ClientShot? shot;
    Harmony? harmony;
    long tickId;
    int errCount;

    /// <summary>Entity ids whose tree says staged=true; the 50 ms ticker re-asserts them.</summary>
    public readonly HashSet<long> Staged = new();
    public Commands? Cmds;

    public override void StartServerSide(ICoreServerAPI api)
    {
        Sapi = api;
        api.Network.RegisterChannel(ClientShot.Channel).RegisterMessageType<ShotMsg>();
        harmony = new Harmony("dinostage");
        MoverGuard.Apply(harmony, api);

        api.Event.OnEntityLoaded += OnEntityIn;
        api.Event.OnEntitySpawn += OnEntityIn;
        api.Event.OnEntityDespawn += OnEntityOut;
        tickId = api.Event.RegisterGameTickListener(Tick, 50);

        Cmds = new Commands(api, this);
        Cmds.Register();
        TestAutorun(api);
        api.Logger.Notification("[DinoStage] server part started. Mover guard: " + MoverGuard.Status);
    }

    /// <summary>Test hook. If env DINOSTAGE_AUTORUN names a file, run the tests it lists (words: selftest, ext, exit) once the server is running.</summary>
    void TestAutorun(ICoreServerAPI api)
    {
        string? f = Environment.GetEnvironmentVariable("DINOSTAGE_AUTORUN");
        if (string.IsNullOrEmpty(f) || !System.IO.File.Exists(f)) return;
        string[] words = System.IO.File.ReadAllText(f).Split(new[] { ' ', (char)13, (char)10, ',' }, StringSplitOptions.RemoveEmptyEntries);
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
        {
            api.Event.RegisterCallback(_ =>
            {
                api.Logger.Notification("[DinoStage] autorun: " + string.Join(",", words));
                bool exit = Array.IndexOf(words, "exit") >= 0;
                void Ext()
                {
                    if (Array.IndexOf(words, "ext") >= 0)
                    {
                        var x = new ExtTest(api, this);
                        x.Done = () => { if (exit) api.Event.RegisterCallback(__ => api.Server.ShutDown(), 2000); };
                        x.Start();
                    }
                    else if (exit) api.Server.ShutDown();
                }
                if (Array.IndexOf(words, "selftest") >= 0)
                {
                    var s = new SelfTest(api, this);
                    s.Done = () => api.Event.RegisterCallback(__ => Ext(), 1000);
                    s.Start();
                }
                else Ext();
            }, 8000);
        });
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        ClientHold.Start(api);
        shot = new ClientShot(api);
    }

    void OnEntityIn(Entity e)
    {
        try
        {
            if (!StageState.IsStaged(e)) return;
            Staged.Add(e.EntityId);
            // assert now, not at the next 50 ms ticker pass: a physics tick in between would drop the entity (measured 0.07 blocks on reload)
            StageData? d = StageState.Read(e);
            if (d != null) Freeze.Assert(e, d, MoverGuard.IsHeld(e.EntityId));
        }
        catch (Exception ex) { Sapi?.Logger.Warning("[DinoStage] load assert: " + ex.Message); }
    }

    void OnEntityOut(Entity e, EntityDespawnData reason)
    {
        Staged.Remove(e.EntityId);
    }

    void Tick(float dt)
    {
        ICoreServerAPI? api = Sapi;
        if (api == null || Staged.Count == 0) return;
        long[] ids = new long[Staged.Count];
        Staged.CopyTo(ids);
        foreach (long id in ids)
        {
            try
            {
                Entity? e = api.World.GetEntityById(id);
                if (e == null) continue; // unloaded; OnEntityLoaded re-adds
                StageData? d = StageState.Read(e);
                if (d == null || !d.Staged) { Staged.Remove(id); continue; }
                Freeze.Assert(e, d, MoverGuard.IsHeld(id));
            }
            catch (Exception ex)
            {
                if (errCount++ < 20) api.Logger.Warning("[DinoStage] ticker error for " + id + ": " + ex.Message);
            }
        }
    }

    public override void Dispose()
    {
        if (Sapi != null && tickId != 0) Sapi.Event.UnregisterGameTickListener(tickId);
        MoverGuard.Remove(harmony);
        harmony = null;
        ClientHold.Stop();
        shot?.Dispose();
        shot = null;
        base.Dispose();
    }
}
