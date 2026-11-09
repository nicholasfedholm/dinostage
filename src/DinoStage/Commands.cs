using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace DinoStage;

/// <summary>Server chat commands, root /stage.</summary>
public sealed class Commands
{
    sealed class ActorList { public readonly List<long> Ids = new(); public int Cur = -1; }

    readonly ICoreServerAPI api;
    readonly DinoStageSystem sys;
    readonly Dictionary<string, ActorList> lists = new();
    bool hints = true;
    const float Deg = Ops.Deg;

    public Commands(ICoreServerAPI api, DinoStageSystem sys) { this.api = api; this.sys = sys; }

    // ---------------------------------------------------------------- registration

    ICommandArgumentParser[] Words() =>
        Enumerable.Range(0, 6).Select(i => (ICommandArgumentParser)api.ChatCommands.Parsers.OptionalWord("a" + i)).ToArray();

    void Sub(IChatCommand root, string name, string desc, OnCommandDelegate h) =>
        root.BeginSubCommand(name).WithDescription(desc).WithArgs(Words()).IgnoreAdditionalArgs().HandleWith(h).EndSubCommand();

    public void Register()
    {
        IChatCommand root = api.ChatCommands.Create("stage")
            .WithDescription("DinoStage: freeze, pose and stage entities for screenshots")
            .RequiresPrivilege(Privilege.controlserver)
            .IgnoreAdditionalArgs()
            .HandleWith(Help);
        Sub(root, "select", "select [code]: add the entity you look at (or the nearest of code within 30 blocks) to the actors", Select);
        Sub(root, "actors", "list actors", ListActors);
        Sub(root, "clear", "deselect and fully thaw every actor", Clear);
        Sub(root, "spawn", "spawn <entitycode> [x y z]: spawn at look point (or x y z) and select", Spawn);
        Sub(root, "list-anims", "list-anims [actor#]", ListAnims);
        Sub(root, "freeze", "freeze [all] [phys]: hold current actor (AI off, physics off, animation paused)", FreezeCmd);
        Sub(root, "thaw", "thaw [all]: restore AI, physics, animation", ThawCmd);
        Sub(root, "anim", "anim <code> [speed] [progress]: clean switch, exactly this animation (every other animation stopped, layers cleared; heading/pitch/roll untouched)", AnimCmd);
        Sub(root, "animlayer", "animlayer <code> [speed] [progress]: stack an animation on top of the held pose without resetting (max 4 layers; cleared by anim and resetpose)", AnimLayerCmd);
        Sub(root, "resetpose", "resetpose: clear the base animation and all layers back to the bind pose (heading/pitch/roll untouched)", ResetPoseCmd);
        Sub(root, "speed", "speed <v>: 0 holds the frame (base animation)", SpeedCmd);
        Sub(root, "progress", "progress <frame | 0.0..1.0 with a dot | n%> (base animation)", ProgressCmd);
        Sub(root, "place", "place <dx> <dy> <dz>: offset from the player's feet", PlaceCmd);
        Sub(root, "placeabs", "placeabs <x> <y> <z>", PlaceAbsCmd);
        Sub(root, "heading", "heading <deg>", HeadingCmd);
        Sub(root, "pitch", "pitch <deg>", PitchCmd);
        Sub(root, "roll", "roll <deg>", RollCmd);
        Sub(root, "nudge", "nudge <fwd|strafe|up> <amount in 0.1 block steps>", NudgeCmd);
        Sub(root, "nudgepitch", "nudgepitch <degrees>", NudgePitchCmd);
        Sub(root, "nudgeroll", "nudgeroll <degrees>", NudgeRollCmd);
        Sub(root, "actor", "actor <n>: choose current actor (1 based)", ActorCmd);
        Sub(root, "scale", "scale: not shipped, see reply", ScaleCmd);
        Sub(root, "scene", "scene save|load|list|delete <name>", SceneCmd);
        Sub(root, "time", "time <hour 0..24>", TimeCmd);
        Sub(root, "weather", "weather <code|clear>", WeatherCmd);
        Sub(root, "light", "light noon: 12h and clearsky", LightCmd);
        Sub(root, "camera", "camera loom [back] [up]", CameraCmd);
        Sub(root, "hints", "hints on|off", HintsCmd);
        Sub(root, "shot", "shot <name>: hide the HUD and save the frame to shots/<name>.png (poses untouched)", ShotCmd);
        Sub(root, "selftest", "run the scripted self test (console friendly)", SelfTestCmd);
        Sub(root, "selftest2", "run the extended self test (chunk reload, two actors, unstage, scene)", SelfTest2Cmd);
    }

    // ---------------------------------------------------------------- helpers

    static string? A(TextCommandCallingArgs a, int i)
    {
        if (i >= a.Parsers.Count) return null;
        string? s = a.Parsers[i].GetValue() as string;
        return string.IsNullOrEmpty(s) ? null : s;
    }

    IPlayer? Pl(TextCommandCallingArgs a) => a.Caller.Player;

    ActorList Lst(TextCommandCallingArgs a) => Lst(a.Caller.Player?.PlayerUID ?? "console");

    ActorList Lst(string key)
    {
        if (!lists.TryGetValue(key, out ActorList? l)) lists[key] = l = new ActorList();
        return l;
    }

    TextCommandResult Ok(string msg, string hint = "") =>
        TextCommandResult.Success(hints && hint.Length > 0 ? msg + "\nhint: " + hint : msg);

    static TextCommandResult Err(string msg) => TextCommandResult.Error(msg);

    List<Entity> Loaded(ActorList l)
    {
        var r = new List<Entity>();
        foreach (long id in l.Ids)
        {
            Entity? e = api.World.GetEntityById(id);
            if (e != null) r.Add(e);
        }
        return r;
    }

    bool TryCur(TextCommandCallingArgs a, out Entity e, out TextCommandResult res)
    {
        ActorList l = Lst(a);
        e = null!;
        res = null!;
        if (l.Cur < 0 || l.Cur >= l.Ids.Count) { res = Err("No current actor. Use /stage select or /stage spawn first."); return false; }
        Entity? ent = api.World.GetEntityById(l.Ids[l.Cur]);
        if (ent == null) { res = Err("Actor " + (l.Cur + 1) + " is not loaded (entity id " + l.Ids[l.Cur] + ")."); return false; }
        e = ent;
        return true;
    }

    Vec3d? DefaultSpot()
    {
        int[]? sp = api.WorldManager.DefaultSpawnPosition;
        return sp != null && sp.Length >= 3 ? new Vec3d(sp[0], sp[1], sp[2]) : null;
    }

    static bool Match(Entity e, string code)
    {
        string full = e.Code.ToString();
        string path = e.Code.Path;
        return string.Equals(full, code, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, code, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(code, StringComparison.OrdinalIgnoreCase);
    }

    Entity? LookEntity(IPlayer pl)
    {
        Entity? sel = pl.CurrentEntitySelection?.Entity;
        if (sel != null && sel is not EntityPlayer) return sel;
        BlockSelection? bs = null;
        EntitySelection? es = null;
        EntityPos p = pl.Entity.Pos;
        Vec3d eye = p.XYZ.Add(0.0, pl.Entity.LocalEyePos.Y, 0.0);
        long self = pl.Entity.EntityId;
        api.World.RayTraceForSelection(eye, p.Pitch, p.Yaw, 60f, ref bs, ref es, null, en => en.EntityId != self && en is not EntityPlayer);
        return es?.Entity;
    }

    Vec3d? LookPoint(IPlayer pl)
    {
        BlockSelection? bs = null;
        EntitySelection? es = null;
        EntityPos p = pl.Entity.Pos;
        Vec3d eye = p.XYZ.Add(0.0, pl.Entity.LocalEyePos.Y, 0.0);
        long self = pl.Entity.EntityId;
        api.World.RayTraceForSelection(eye, p.Pitch, p.Yaw, 80f, ref bs, ref es, null, en => en.EntityId != self);
        if (bs?.Position != null) return new Vec3d(bs.Position.X + 0.5, bs.Position.Y + 1.0, bs.Position.Z + 0.5);
        Vec3f v = p.GetViewVector();
        return new Vec3d(eye.X + v.X * 5.0, eye.Y + v.Y * 5.0, eye.Z + v.Z * 5.0);
    }

    void AddActor(ActorList l, Entity e)
    {
        int ix = l.Ids.IndexOf(e.EntityId);
        if (ix < 0) { l.Ids.Add(e.EntityId); ix = l.Ids.Count - 1; }
        l.Cur = ix;
    }

    /// <summary>Run f on the current actor with a staged state (freezing it first if needed), then commit.</summary>
    TextCommandResult Mod(TextCommandCallingArgs a, System.Func<Entity, StageData, string> f)
    {
        if (!TryCur(a, out Entity e, out TextCommandResult res)) return res;
        bool was = StageState.IsStaged(e);
        StageData d = StageState.Read(e) ?? Freeze.Capture(e);
        if (!was)
        {
            d.Staged = true; d.Yaw = e.Pos.Yaw; d.Pitch = e.Pos.Pitch; d.Roll = e.Pos.Roll;
        }
        string msg;
        try { msg = f(e, d); }
        catch (StageFail ex) { return Err(ex.Message); }
        Ops.Commit(sys, e, d);
        return Ok((was ? "" : "(froze actor first) ") + msg);
    }

    // ---------------------------------------------------------------- commands

    TextCommandResult Help(TextCommandCallingArgs a) =>
        Ok("DinoStage commands: select actors clear spawn list-anims freeze thaw anim animlayer resetpose speed progress place placeabs heading pitch roll nudge nudgepitch nudgeroll actor scale scene time weather light camera hints selftest. Use /stage <command> with the usage shown in /help stage.");

    TextCommandResult Select(TextCommandCallingArgs a)
    {
        string? code = A(a, 0);
        IPlayer? pl = Pl(a);
        Entity? e = null;
        if (pl != null)
        {
            e = LookEntity(pl);
            if (e != null && code != null && !Match(e, code)) e = null;
        }
        if (e == null && code != null)
        {
            Vec3d? at = pl?.Entity.Pos.XYZ ?? DefaultSpot();
            if (at == null) return Err("No position to search from.");
            e = api.World.GetNearestEntity(at, 30f, 30f, en => en is not EntityPlayer && Match(en, code));
        }
        if (e == null) return Err("Nothing selected. Look at an entity, or pass a code that exists within 30 blocks.");
        ActorList l = Lst(a);
        AddActor(l, e);
        return Ok("Actor " + (l.Cur + 1) + " selected: " + Ops.Describe(e), "Next: /stage freeze, /stage anim <code> 0 0.5, /stage pitch 20");
    }

    TextCommandResult ListActors(TextCommandCallingArgs a)
    {
        ActorList l = Lst(a);
        if (l.Ids.Count == 0) return Ok("No actors.");
        var sb = new StringBuilder();
        for (int i = 0; i < l.Ids.Count; i++)
        {
            Entity? e = api.World.GetEntityById(l.Ids[i]);
            sb.Append(i == l.Cur ? "> " : "  ").Append(i + 1).Append(": ")
              .AppendLine(e == null ? "(not loaded) id " + l.Ids[i] : Ops.Describe(e));
        }
        return Ok(sb.ToString().TrimEnd());
    }

    TextCommandResult Clear(TextCommandCallingArgs a)
    {
        ActorList l = Lst(a);
        int n = 0;
        foreach (Entity e in Loaded(l)) { if (StageState.IsStaged(e)) { Ops.ThawEntity(sys, e); n++; } }
        int total = l.Ids.Count;
        l.Ids.Clear(); l.Cur = -1;
        return Ok("Cleared " + total + " actor(s), thawed " + n + ".");
    }

    TextCommandResult Spawn(TextCommandCallingArgs a)
    {
        string? code = A(a, 0);
        if (code == null) return Err("Usage: /stage spawn <entitycode> [x y z]");
        Vec3d? at;
        float yaw = 0f;
        if (Ops.TryNum(A(a, 1), out double x) && Ops.TryNum(A(a, 2), out double y) && Ops.TryNum(A(a, 3), out double z)) at = new Vec3d(x, y, z);
        else if (Pl(a) is IPlayer pl) { at = LookPoint(pl); yaw = pl.Entity.Pos.Yaw + (float)Math.PI; }
        else return Err("Console needs explicit x y z.");
        if (at == null) return Err("No spawn point.");
        Entity? e = Ops.Spawn(api, code, at.X, at.Y, at.Z, yaw, out string err);
        if (e == null) return Err(err + " Try /stage list-anims after spawning a known code, or check the code with /entity spawn.");
        ActorList l = Lst(a);
        AddActor(l, e);
        return Ok("Spawned and selected as actor " + (l.Cur + 1) + ": " + Ops.Describe(e));
    }

    TextCommandResult ListAnims(TextCommandCallingArgs a)
    {
        Entity? e;
        if (Ops.TryNum(A(a, 0), out double n))
        {
            ActorList l = Lst(a);
            int ix = (int)n - 1;
            if (ix < 0 || ix >= l.Ids.Count) return Err("No actor " + n);
            e = api.World.GetEntityById(l.Ids[ix]);
            if (e == null) return Err("Actor not loaded.");
        }
        else if (!TryCur(a, out e!, out TextCommandResult res)) return res;
        var sb = new StringBuilder();
        sb.AppendLine("Animations of " + e.Code + " (config code -> shape animation):");
        Dictionary<string, AnimationMetaData>? by = e.Properties?.Client?.AnimationsByMetaCode;
        if (by != null)
            foreach (var kv in by.OrderBy(k => k.Key))
                sb.Append("  ").Append(kv.Key).Append(" -> ").Append(kv.Value.Animation).Append(" (").Append(Freeze.FrameCount(e, kv.Value.Animation ?? kv.Key)).AppendLine(" frames)");
        Animation[]? shapeAnims = e.Properties?.Client?.LoadedShapeForEntity?.Animations;
        if (shapeAnims != null)
        {
            sb.Append("Shape animations: ");
            sb.Append(string.Join(", ", shapeAnims.Select(s => s.Code + ":" + s.QuantityFrames)));
        }
        else sb.Append("(shape animations not available on the server for this entity)");
        return Ok(sb.ToString());
    }

    TextCommandResult FreezeCmd(TextCommandCallingArgs a)
    {
        bool all = A(a, 0) == "all" || A(a, 1) == "all";
        bool phys = A(a, 0) == "phys" || A(a, 1) == "phys";
        IEnumerable<Entity> targets;
        if (all) targets = Loaded(Lst(a));
        else { if (!TryCur(a, out Entity e, out TextCommandResult res)) return res; targets = new[] { e }; }
        int n = 0; var sb = new StringBuilder();
        foreach (Entity t in targets)
        {
            StageData d = Ops.FreezeEntity(sys, t);
            if (phys) { d.NoPhys = false; Ops.Commit(sys, t, d); }
            n++;
            sb.AppendLine("Frozen: " + Ops.Describe(t));
        }
        string note = targets.Any(t => (StageState.Read(t)?.Anim ?? "").Length == 0) ? "\nNo default or active animation was found: bind pose held, AI and physics frozen. Use /stage anim <code> to choose a pose." : "";
        return Ok(sb.ToString().TrimEnd() + note, "Pose it: /stage anim <code> 0 0.5 then /stage pitch 20");
    }

    TextCommandResult ThawCmd(TextCommandCallingArgs a)
    {
        if (A(a, 0) == "all")
        {
            int n = 0;
            foreach (Entity e in Loaded(Lst(a))) { if (StageState.IsStaged(e)) { Ops.ThawEntity(sys, e); n++; } }
            return Ok("Thawed " + n + " actor(s).");
        }
        if (!TryCur(a, out Entity cur, out TextCommandResult res)) return res;
        Ops.ThawEntity(sys, cur);
        return Ok("Thawed " + cur.Code + " id " + cur.EntityId + ": AI handlers removed, physics on, held animation stopped.");
    }

    TextCommandResult AnimCmd(TextCommandCallingArgs a)
    {
        string? code = A(a, 0);
        if (code == null) return Err("Usage: /stage anim <code> [speed] [progress]. Clean switch (everything else stopped, layers cleared). See /stage list-anims.");
        return Mod(a, (e, d) => AnimOps.SetBase(e, d, code, Ops.TryNum(A(a, 1), out double sp) ? sp : null, A(a, 2)));
    }

    TextCommandResult AnimLayerCmd(TextCommandCallingArgs a)
    {
        string? code = A(a, 0);
        if (code == null) return Err("Usage: /stage animlayer <code> [speed] [progress]. Stacks on top of the held pose (max " + AnimSet.MaxLayers + " layers). See /stage list-anims.");
        return Mod(a, (e, d) => AnimOps.AddLayer(e, d, code, Ops.TryNum(A(a, 1), out double sp) ? sp : null, A(a, 2)));
    }

    TextCommandResult ResetPoseCmd(TextCommandCallingArgs a) => Mod(a, (e, d) => AnimOps.ResetPose(d));

    TextCommandResult SpeedCmd(TextCommandCallingArgs a)
    {
        if (!Ops.TryNum(A(a, 0), out double v)) return Err("Usage: /stage speed <v> (0 holds)");
        return Mod(a, (e, d) =>
        {
            if (d.Speed != 0f && v == 0.0) d.Frame = Freeze.CurrentFrame(e, d.Anim) ?? d.Frame;
            d.Speed = (float)v;
            return v == 0.0 ? "holding frame " + Ops.F(d.Frame) : "speed " + Ops.F(v);
        });
    }

    TextCommandResult ProgressCmd(TextCommandCallingArgs a)
    {
        string? s = A(a, 0);
        if (s == null) return Err("Usage: /stage progress <frame number | fraction with a dot e.g. 0.5 | 50%>");
        return Mod(a, (e, d) =>
        {
            if (d.Anim.Length == 0) throw new StageFail("Actor has no base animation. Use /stage anim <code> first.");
            int frames = Freeze.FrameCount(e, d.Anim);
            if (!Ops.ParseProgress(s, frames, out float fr, out string how)) throw new StageFail("Bad progress '" + s + "'.");
            d.Frame = fr; d.Speed = 0f;
            return "holding frame " + Ops.F(fr) + " of " + frames + " (" + how + ")";
        });
    }

    TextCommandResult PlaceCmd(TextCommandCallingArgs a)
    {
        IPlayer? pl = Pl(a);
        if (pl == null) return Err("place needs a player; use placeabs on the console.");
        if (!Ops.TryNum(A(a, 0), out double dx) || !Ops.TryNum(A(a, 1), out double dy) || !Ops.TryNum(A(a, 2), out double dz))
            return Err("Usage: /stage place <dx> <dy> <dz> (world axes, from the player's feet)");
        return MoveTo(a, pl.Entity.Pos.X + dx, pl.Entity.Pos.Y + dy, pl.Entity.Pos.Z + dz);
    }

    TextCommandResult PlaceAbsCmd(TextCommandCallingArgs a)
    {
        if (!Ops.TryNum(A(a, 0), out double x) || !Ops.TryNum(A(a, 1), out double y) || !Ops.TryNum(A(a, 2), out double z))
            return Err("Usage: /stage placeabs <x> <y> <z>");
        return MoveTo(a, x, y, z);
    }

    TextCommandResult MoveTo(TextCommandCallingArgs a, double x, double y, double z)
    {
        if (!TryCur(a, out Entity e, out TextCommandResult res)) return res;
        e.Pos.SetPos(x, y, z);
        e.Pos.Motion.Set(0.0, 0.0, 0.0);
        return Ok("Placed at " + Ops.F(x) + "," + Ops.F(y) + "," + Ops.F(z));
    }

    TextCommandResult HeadingCmd(TextCommandCallingArgs a)
    {
        if (!Ops.TryNum(A(a, 0), out double v)) return Err("Usage: /stage heading <deg>");
        return Mod(a, (e, d) => { d.Yaw = (float)v * Deg; return "heading " + Ops.F(v); });
    }

    TextCommandResult PitchCmd(TextCommandCallingArgs a)
    {
        if (!Ops.TryNum(A(a, 0), out double v)) return Err("Usage: /stage pitch <deg>");
        return Mod(a, (e, d) => { d.Pitch = (float)v * Deg; return "pitch " + Ops.F(v); });
    }

    TextCommandResult ShotCmd(TextCommandCallingArgs a)
    {
        string? name = A(a, 0);
        if (name == null || !Ops.ValidShotName(name)) return Err("Usage: /stage shot <name>  (letters, digits, - and _, up to 64 characters)");
        if (a.Caller.Player is not IServerPlayer sp) return Err("shot needs a player caller (the frame is saved by that player's client)");
        api.Network.GetChannel(ClientShot.Channel).SendPacket(new ShotMsg { Name = name }, sp);
        return TextCommandResult.Success("Shot requested: " + name + ".png (HUD hides for a few frames; the client chat confirms the saved path).");
    }

    TextCommandResult RollCmd(TextCommandCallingArgs a)
    {
        if (!Ops.TryNum(A(a, 0), out double v)) return Err("Usage: /stage roll <deg>");
        return Mod(a, (e, d) => { d.Roll = (float)v * Deg; return "roll " + Ops.F(v); });
    }

    TextCommandResult NudgePitchCmd(TextCommandCallingArgs a)
    {
        if (!Ops.TryNum(A(a, 0), out double v)) return Err("Usage: /stage nudgepitch <degrees>");
        return Mod(a, (e, d) => { d.Pitch += (float)v * Deg; return "pitch now " + Ops.F(d.Pitch / Deg); });
    }

    TextCommandResult NudgeRollCmd(TextCommandCallingArgs a)
    {
        if (!Ops.TryNum(A(a, 0), out double v)) return Err("Usage: /stage nudgeroll <degrees>");
        return Mod(a, (e, d) => { d.Roll += (float)v * Deg; return "roll now " + Ops.F(d.Roll / Deg); });
    }

    TextCommandResult NudgeCmd(TextCommandCallingArgs a)
    {
        string? dir = A(a, 0);
        if (dir == null || !Ops.TryNum(A(a, 1), out double amt)) return Err("Usage: /stage nudge <fwd|strafe|up> <amount in 0.1 block steps>");
        if (!TryCur(a, out Entity e, out TextCommandResult res)) return res;
        float yaw = Pl(a)?.Entity.Pos.Yaw ?? e.Pos.Yaw;
        double fx = -Math.Sin(yaw), fz = -Math.Cos(yaw);
        double dist = amt * 0.1;
        double dx = 0, dy = 0, dz = 0;
        switch (dir.ToLowerInvariant())
        {
            case "fwd": dx = fx * dist; dz = fz * dist; break;
            case "strafe": dx = -fz * dist; dz = fx * dist; break;
            case "up": dy = dist; break;
            default: return Err("Direction must be fwd, strafe or up.");
        }
        e.Pos.SetPos(e.Pos.X + dx, e.Pos.Y + dy, e.Pos.Z + dz);
        e.Pos.Motion.Set(0.0, 0.0, 0.0);
        return Ok("Nudged " + dir + " " + Ops.F(dist) + " blocks (relative to your facing). Now " + Ops.F(e.Pos.X) + "," + Ops.F(e.Pos.Y) + "," + Ops.F(e.Pos.Z));
    }

    TextCommandResult ActorCmd(TextCommandCallingArgs a)
    {
        ActorList l = Lst(a);
        if (!Ops.TryNum(A(a, 0), out double n) || n < 1 || n > l.Ids.Count) return Err("Usage: /stage actor <n>, with n from 1 to " + l.Ids.Count);
        l.Cur = (int)n - 1;
        Entity? e = api.World.GetEntityById(l.Ids[l.Cur]);
        return Ok("Current actor " + n + (e == null ? " (not loaded)" : ": " + Ops.Describe(e)));
    }

    TextCommandResult ScaleCmd(TextCommandCallingArgs a) =>
        Err("scale is not shipped. Visual scale is entity.Properties.Client.Size (EntityShapeRenderer.cs:795), a client-only value; no per-entity scale attribute exists and the server cannot set it. "
          + "Evidence: Properties is cloned per entity (ServerMain.cs:2889, ClientSystemEntities.cs:351, EntityProperties.Clone :155-218), so a client-side Size write would stay per entity, but hitbox, selection box and shadow would not follow. See BUILD_NOTES.md.");

    TextCommandResult SceneCmd(TextCommandCallingArgs a)
    {
        string? act = A(a, 0)?.ToLowerInvariant();
        string? name = A(a, 1);
        ActorList l = Lst(a);
        switch (act)
        {
            case "list":
                string[] names = SceneIO.List(api).ToArray();
                return Ok(names.Length == 0 ? "No scenes." : "Scenes: " + string.Join(", ", names));
            case "save":
                if (name == null) return Err("Usage: /stage scene save <name>");
                List<Entity> ents = Loaded(l);
                if (ents.Count == 0) return Err("No actors to save.");
                string path = SceneIO.Save(api, name, ents, ents[0].Pos.XYZ);
                return Ok("Saved " + ents.Count + " actor(s), hour and weather to " + path);
            case "load":
                if (name == null) return Err("Usage: /stage scene load <name>");
                var loaded = new List<Entity>();
                var rep = new StringBuilder();
                bool ok = SceneIO.Load(sys, name, loaded, rep);
                if (!ok) return Err(rep.ToString().TrimEnd());
                l.Ids.Clear();
                foreach (Entity e in loaded) l.Ids.Add(e.EntityId);
                l.Cur = l.Ids.Count > 0 ? 0 : -1;
                return Ok(rep.ToString().TrimEnd());
            case "delete":
                if (name == null) return Err("Usage: /stage scene delete <name>");
                return SceneIO.Delete(api, name) ? Ok("Deleted scene " + name) : Err("No such scene.");
            default:
                return Err("Usage: /stage scene save|load|list|delete <name>");
        }
    }

    TextCommandResult TimeCmd(TextCommandCallingArgs a)
    {
        if (!Ops.TryNum(A(a, 0), out double h) || h < 0 || h > 24) return Err("Usage: /stage time <hour 0..24>");
        SceneIO.SetHour(api, (float)h);
        return Ok("Hour set to " + Ops.F(h) + ". Time keeps running; use the vanilla /time stop to freeze it.");
    }

    TextCommandResult WeatherCmd(TextCommandCallingArgs a)
    {
        string? code = A(a, 0);
        if (code == null) return Err("Usage: /stage weather <code|clear>");
        if (code == "clear") code = "clearsky";
        bool ok = SceneIO.SetWeather(api, code, out string msg);
        return ok ? Ok(msg) : Err(msg);
    }

    TextCommandResult LightCmd(TextCommandCallingArgs a)
    {
        if (A(a, 0) != "noon") return Err("Usage: /stage light noon");
        SceneIO.SetHour(api, 12f);
        bool ok = SceneIO.SetWeather(api, "clearsky", out string msg);
        return Ok("Hour 12. " + msg + (ok ? "" : " (weather not changed)"));
    }

    TextCommandResult CameraCmd(TextCommandCallingArgs a)
    {
        if (A(a, 0) != "loom") return Err("Usage: /stage camera loom [back blocks=3.5] [up blocks=1]");
        IPlayer? pl = Pl(a);
        if (pl == null) return Err("camera needs a player.");
        List<Entity> ents = Loaded(Lst(a));
        if (ents.Count < 2) return Err("camera loom needs actor 1 (the looming animal) and actor 2 (the foreground animal).");
        Entity a1 = ents[0], a2 = ents[1];
        double back = Ops.TryNum(A(a, 1), out double b) ? b : 3.5;
        double up = Ops.TryNum(A(a, 2), out double u) ? u : 1.0;
        double dx = a1.Pos.X - a2.Pos.X, dz = a1.Pos.Z - a2.Pos.Z;
        double hl = Math.Sqrt(dx * dx + dz * dz);
        double ux = hl < 0.01 ? 0 : dx / hl, uz = hl < 0.01 ? 1 : dz / hl;
        double px = a2.Pos.X - ux * back, pz = a2.Pos.Z - uz * back;
        int ground = api.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos((int)Math.Floor(px), 0, (int)Math.Floor(pz), 0)) + 1;
        double py = ground + up;
        double eyeY = py + pl.Entity.LocalEyePos.Y;
        double ty = a1.Pos.Y + (a1.SelectionBox != null ? a1.SelectionBox.Y2 * 0.7 : 1.5);
        double lx = a1.Pos.X - px, ly = ty - eyeY, lz = a1.Pos.Z - pz;
        double len = Math.Sqrt(lx * lx + ly * ly + lz * lz);
        float yaw = (float)Math.Atan2(-lx, -lz);
        float pitch = (float)Math.Asin(len < 1e-6 ? 0 : ly / len);
        pl.Entity.TeleportTo(new EntityPos(px, py, pz, yaw, pitch, 0f));
        pl.Entity.Pos.Yaw = yaw; pl.Entity.Pos.Pitch = pitch;
        pl.Entity.WatchedAttributes.GetIntAndIncrement("positionVersionNumber");
        pl.Entity.WatchedAttributes.MarkAllDirty();
        return Ok("Camera placed at " + Ops.F(px) + "," + Ops.F(py) + "," + Ops.F(pz) + " behind actor 2, looking at actor 1 (yaw " + Ops.F(yaw / Deg) + ", pitch " + Ops.F(pitch / Deg) + " deg)."
            + "\nIf the view angle did not apply (the client may keep its own look direction), aim manually at actor 1 from this spot; the position is correct either way.",
            "Widen FOV in settings, hide the HUD with F4, take the shot with F12.");
    }

    TextCommandResult HintsCmd(TextCommandCallingArgs a)
    {
        string? v = A(a, 0);
        if (v != "on" && v != "off") return Err("Usage: /stage hints on|off");
        hints = v == "on";
        return TextCommandResult.Success("Hints " + v + ".");
    }

    TextCommandResult SelfTestCmd(TextCommandCallingArgs a)
    {
        var st = new SelfTest(api, sys);
        string path = st.Start();
        return TextCommandResult.Success("Selftest started (takes about 1 to 2 minutes of game time). Log: " + path);
    }

    TextCommandResult SelfTest2Cmd(TextCommandCallingArgs a)
    {
        var st = new ExtTest(api, sys);
        string path = st.Start();
        return TextCommandResult.Success("Extended selftest started. Log: " + path);
    }
}
