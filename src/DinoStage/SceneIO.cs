using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace DinoStage;

public sealed class SceneActor
{
    public string Code { get; set; } = "";
    public long EntityId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public float Heading { get; set; }
    public float Pitch { get; set; }
    public float Roll { get; set; }
    public string Anim { get; set; } = "";
    public float Speed { get; set; }
    public float Frame { get; set; }
    public bool Staged { get; set; }
    public bool NoPhys { get; set; } = true;
    /// <summary>1.1.0, optional: stacked animations. Absent in a 1.0.0 scene (loads as none) and omitted when there are none.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<SceneLayer>? Layers { get; set; }
}

public sealed class SceneLayer
{
    public string Anim { get; set; } = "";
    public float Speed { get; set; }
    public float Frame { get; set; }
}

public sealed class SceneData
{
    public string Name { get; set; } = "";
    public float HourOfDay { get; set; }
    public string? Weather { get; set; }
    public List<SceneActor> Actors { get; set; } = new();
}

public static class SceneIO
{
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    public static string Dir(ICoreServerAPI api)
    {
        string d = Path.Combine(api.GetOrCreateDataPath("dinostage"), "scenes");
        Directory.CreateDirectory(d);
        return d;
    }

    public static string SafeName(string n)
    {
        var sb = new StringBuilder();
        foreach (char c in n) if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
        return sb.ToString();
    }

    public static string PathFor(ICoreServerAPI api, string name) => Path.Combine(Dir(api), SafeName(name) + ".json");

    public static string? CurrentWeatherCode(ICoreServerAPI api, Vec3d? near)
    {
        try
        {
            WeatherSystemServer? ws = api.ModLoader.GetModSystem<WeatherSystemServer>();
            if (ws == null) return null;
            WeatherSimulationRegion? sim = null;
            if (near != null)
            {
                int rs = api.World.BlockAccessor.RegionSize;
                long idx = ws.MapRegionIndex2D((int)near.X / rs, (int)near.Z / rs);
                ws.weatherSimByMapRegion.TryGetValue(idx, out sim);
            }
            sim ??= ws.weatherSimByMapRegion.Values.FirstOrDefault();
            return sim?.NewWePattern?.config?.Code;
        }
        catch { return null; }
    }

    public static bool SetWeather(ICoreServerAPI api, string code, out string msg)
    {
        try
        {
            WeatherSystemServer? ws = api.ModLoader.GetModSystem<WeatherSystemServer>();
            if (ws == null) { msg = "Weather system not available."; return false; }
            ws.ReloadConfigs();
            bool ok = true; int n = 0;
            foreach (WeatherSimulationRegion sim in ws.weatherSimByMapRegion.Values.ToArray())
            {
                ok &= sim.SetWeatherPattern(code, true);
                if (ok) { sim.TickEvery25ms(0.025f); n++; }
            }
            msg = ok ? "weather " + code + " set in " + n + " loaded region(s)" : "no such weather pattern '" + code + "' (or no region loaded)";
            return ok && n > 0;
        }
        catch (Exception ex) { msg = "weather failed: " + ex.Message; return false; }
    }

    /// <summary>Same arithmetic as GameCalendar.SetDayTime (Lib), via the public IGameCalendar.Add.</summary>
    public static void SetHour(ICoreServerAPI api, float hour)
    {
        IGameCalendar cal = api.World.Calendar;
        float cur = cal.HourOfDay;
        float add = cur > hour ? hour + (cal.HoursPerDay - cur) : hour - cur;
        cal.Add(add);
    }

    public static string Save(ICoreServerAPI api, string name, IEnumerable<Entity> actors, Vec3d? near)
    {
        var s = new SceneData { Name = SafeName(name), HourOfDay = api.World.Calendar.HourOfDay, Weather = CurrentWeatherCode(api, near) };
        foreach (Entity e in actors)
        {
            StageData? d = StageState.Read(e);
            bool st = d != null && d.Staged;
            s.Actors.Add(new SceneActor
            {
                Code = e.Code.ToString(), EntityId = e.EntityId,
                X = e.Pos.X, Y = e.Pos.Y, Z = e.Pos.Z,
                Heading = st ? d!.Yaw : e.Pos.Yaw, Pitch = st ? d!.Pitch : e.Pos.Pitch, Roll = st ? d!.Roll : e.Pos.Roll,
                Anim = st ? d!.Anim : "", Speed = st ? d!.Speed : 0f, Frame = st ? d!.Frame : 0f,
                Staged = st, NoPhys = st ? d!.NoPhys : true,
                Layers = st && d!.Layers.Count > 0 ? d.Layers.Select(l => new SceneLayer { Anim = l.Anim, Speed = l.Speed, Frame = l.Frame }).ToList() : null,
            });
        }
        string path = PathFor(api, name);
        File.WriteAllText(path, JsonSerializer.Serialize(s, Opt));
        return path;
    }

    public static IEnumerable<string> List(ICoreServerAPI api) =>
        Directory.GetFiles(Dir(api), "*.json").Select(f => Path.GetFileNameWithoutExtension(f)).OrderBy(x => x);

    public static bool Delete(ICoreServerAPI api, string name)
    {
        string p = PathFor(api, name);
        if (!File.Exists(p)) return false;
        File.Delete(p);
        return true;
    }

    /// <summary>
    /// Load: re-find each actor by stored id (code must match), else the nearest of the same code within 64 blocks of the stored position
    /// (not already used), else respawn. Then re-apply position, angles, animation, speed, frame, staged. Also time and weather.
    /// </summary>
    public static bool Load(DinoStageSystem sys, string name, List<Entity> actorsOut, StringBuilder report)
    {
        ICoreServerAPI api = sys.Sapi!;
        string path = PathFor(api, name);
        if (!File.Exists(path)) { report.AppendLine("no scene file " + path); return false; }
        SceneData? s = JsonSerializer.Deserialize<SceneData>(File.ReadAllText(path));
        if (s == null) { report.AppendLine("scene file unreadable"); return false; }

        var used = new HashSet<long>();
        actorsOut.Clear();
        foreach (SceneActor a in s.Actors)
        {
            Entity? e = api.World.GetEntityById(a.EntityId);
            string how = "by id";
            if (e != null && !string.Equals(e.Code.ToString(), a.Code, StringComparison.OrdinalIgnoreCase)) e = null;
            if (e != null && used.Contains(e.EntityId)) e = null;
            if (e == null)
            {
                how = "nearest";
                var at = new Vec3d(a.X, a.Y, a.Z);
                e = api.World.GetNearestEntity(at, 64f, 64f, en =>
                    !used.Contains(en.EntityId) && string.Equals(en.Code.ToString(), a.Code, StringComparison.OrdinalIgnoreCase));
            }
            if (e == null)
            {
                how = "respawned";
                e = Ops.Spawn(api, a.Code, a.X, a.Y, a.Z, a.Heading, out string err);
                if (e == null) { report.AppendLine("actor " + a.Code + ": " + err); continue; }
            }
            used.Add(e.EntityId);
            e.Pos.SetPos(a.X, a.Y, a.Z);
            e.Pos.Motion.Set(0.0, 0.0, 0.0);
            e.Pos.Yaw = a.Heading; e.Pos.Pitch = a.Pitch; e.Pos.Roll = a.Roll;
            if (a.Staged)
            {
                var d = new StageData { Staged = true, Anim = a.Anim, Speed = a.Speed, Frame = a.Frame, Yaw = a.Heading, Pitch = a.Pitch, Roll = a.Roll, NoPhys = a.NoPhys };
                if (a.Layers != null)
                    foreach (SceneLayer l in a.Layers.Take(AnimSet.MaxLayers))
                        if (!string.IsNullOrEmpty(l.Anim)) d.Layers.Add(new LayerData { Anim = l.Anim.ToLowerInvariant(), Speed = l.Speed, Frame = l.Frame });
                Ops.Commit(sys, e, d);
            }
            else if (StageState.IsStaged(e)) Ops.ThawEntity(sys, e);
            actorsOut.Add(e);
            report.AppendLine("actor " + a.Code + " id " + a.EntityId + " -> " + e.EntityId + " (" + how + ")");
        }
        SetHour(api, s.HourOfDay);
        report.AppendLine("hour set to " + Ops.F(s.HourOfDay));
        if (!string.IsNullOrEmpty(s.Weather))
        {
            SetWeather(api, s.Weather!, out string wm);
            report.AppendLine(wm);
        }
        return true;
    }
}
