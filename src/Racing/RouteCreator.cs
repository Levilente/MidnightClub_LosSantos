using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.UI;

namespace MidnightClub.Racing
{
    /// <summary>Route creator: drive the route you want, tap the horn to drop checkpoints, hold the horn to
    /// finish, pick sprint / circuit / time trial / drift and opponents. Custom routes are saved to
    /// data\custom_races.json, show up on the map and in the race list, and can be raced against the AI.</summary>
    public sealed class RouteCreator
    {
        private static readonly DataContractJsonSerializer Ser = new DataContractJsonSerializer(typeof(List<RaceDefinition>));
        public static RouteCreator Instance;
        private readonly RaceManager races; private readonly MenuStack stack;
        private readonly List<float[]> pts = new List<float[]>();
        public bool Recording { get; private set; }
        private int hornSince = -1, startedAt;
        private readonly List<Blip> blips = new List<Blip>();

        public RouteCreator(RaceManager races, MenuStack stack)
        {
            this.races = races; this.stack = stack; Instance = this;
            EventBus.Subscribe<MidnightTickEvent>(e => { try { Tick(); } catch (Exception ex) { Log.Error("Route creator", ex); Cancel(); } });
            EventBus.Subscribe<ModeChangedEvent>(e => Cancel());
        }

        // ---------------------------------------------------------------- storage
        private static string FileIn(string dir) { return Path.Combine(dir, "custom_races.json"); }
        public static List<RaceDefinition> LoadAll(string dir)
        {
            try { var f = FileIn(dir); if (File.Exists(f)) using (var fs = File.OpenRead(f)) return (List<RaceDefinition>)Ser.ReadObject(fs) ?? new List<RaceDefinition>(); }
            catch (Exception ex) { Log.Error("custom_races.json", ex); }
            return new List<RaceDefinition>();
        }
        private static void SaveAll(string dir, List<RaceDefinition> list)
        {
            try { Directory.CreateDirectory(dir); using (var fs = File.Create(FileIn(dir))) Ser.WriteObject(fs, list); }
            catch (Exception ex) { Log.Error("Saving custom races", ex); }
        }

        // ---------------------------------------------------------------- recording
        public void Begin()
        {
            var car = Game.Player.Character.CurrentVehicle;
            if (car == null || RaceManager.IsRacing) { Shard.Mid("ROUTE CREATOR", "Get in your car first.", Shard.Red, 2500); return; }
            pts.Clear(); ClearBlips();
            Add(car.Position, car.Heading);
            Recording = true; startedAt = Game.GameTime;
            Shard.Mid("ROUTE CREATOR", "Drive your route. Tap the horn for a checkpoint, hold it to finish.", Shard.Purple, 5000);
        }

        private void Add(Vector3 p, float h)
        {
            pts.Add(new[] { p.X, p.Y, p.Z, h });
            var b = BlipRegistry.Own(World.CreateBlip(p)); b.Sprite = pts.Count == 1 ? BlipSprite.RaceFinish : BlipSprite.Standard; b.Color = BlipColor.Purple; b.Scale = pts.Count == 1 ? 0.9f : 0.6f;
            b.Name = pts.Count == 1 ? "Your route: start" : "Your route: checkpoint " + (pts.Count - 1);
            if (pts.Count > 1) { Function.Call(Hash.SHOW_NUMBER_ON_BLIP, b, pts.Count - 1); }
            blips.Add(b);
            if (pts.Count > 1) Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "CHECKPOINT_NORMAL", "HUD_MINI_GAME_SOUNDSET", true);
        }

        private void Tick()
        {
            if (!Recording) return;
            var car = Game.Player.Character.CurrentVehicle;
            if (car == null || RaceManager.IsRacing) { Cancel(); return; }
            Game.DisableControlThisFrame(Control.VehicleHorn);
            int now = Game.GameTime;
            bool horn = Game.IsControlPressed(Control.VehicleHorn);
            if (horn && hornSince < 0) hornSince = now;
            if (!horn && hornSince >= 0)
            {
                if (now - hornSince < 600 && now - startedAt > 500)
                {
                    var last = pts[pts.Count - 1]; var lp = new Vector3(last[0], last[1], last[2]);
                    if (car.Position.DistanceTo(lp) < 40f) Shard.Mid("TOO CLOSE", "Checkpoints need at least 40 m between them.", Shard.Red, 1500);
                    else Add(car.Position, car.Heading);
                }
                hornSince = -1;
            }
            if (horn && hornSince >= 0 && now - hornSince > 1200) { hornSince = -1; Finish(car); return; }
            // markers + HUD
            for (int i = 1; i < pts.Count; i++) Function.Call(Hash.DRAW_MARKER, 1, pts[i][0], pts[i][1], pts[i][2] - 1f, 0f, 0f, 0f, 0f, 0f, 0f, 8f, 8f, 2.5f, 168, 85, 247, 90, false, false, 2, false, 0, 0, false);
            float len = Length();
            float w = Ds.Px(0.3f), x = 0.5f - w / 2f, y = 0.04f;
            Ds.Rect(x, y, w, 0.075f, Ds.A(Ds.Bg, 0.88f));
            Ds.Rect(x, y, w, 0.004f, Ds.Violet);
            Ds.T("ROUTE CREATOR", x + Ds.Px(0.014f), y + 0.012f, 0.3f, Ds.Violet, Ds.Display);
            Ds.T((pts.Count - 1) + " CHECKPOINTS  ·  " + (len / 1000f).ToString("0.0") + " KM", x + w - Ds.Px(0.014f), y + 0.012f, 0.3f, Ds.Text, Ds.Display, GTA.UI.Alignment.Right);
            Ds.T("TAP HORN  CHECKPOINT   ·   HOLD HORN  FINISH", x + Ds.Px(0.014f), y + 0.042f, 0.26f, Ds.Muted, Ds.Display);
            if (hornSince >= 0 && now - hornSince > 250) Ds.Rect(x, y + 0.071f, w * Math.Min(1f, (now - hornSince) / 1200f), 0.004f, Ds.Lime);
        }

        private float Length() { float L = 0; for (int i = 1; i < pts.Count; i++) L += new Vector3(pts[i][0], pts[i][1], pts[i][2]).DistanceTo(new Vector3(pts[i - 1][0], pts[i - 1][1], pts[i - 1][2])); return L; }

        private void Cancel() { if (!Recording) return; Recording = false; ClearBlips(); }
        private void ClearBlips() { foreach (var b in blips) try { if (b.Exists()) b.Delete(); } catch { } blips.Clear(); }

        private void Finish(Vehicle car)
        {
            var last = pts[pts.Count - 1];
            if (car.Position.DistanceTo(new Vector3(last[0], last[1], last[2])) > 40f) Add(car.Position, car.Heading);
            Recording = false;
            if (pts.Count < 2) { ClearBlips(); Shard.Mid("ROUTE TOO SHORT", "Add at least one checkpoint.", Shard.Red, 2500); return; }
            stack.Open(SaveMenu());
        }

        private static string Zone(float[] p) { try { return World.GetZoneLocalizedName(new Vector3(p[0], p[1], p[2])); } catch { return "Los Santos"; } }

        private Menu SaveMenu()
        {
            string[] types = { "Sprint", "Circuit", "TimeTrial", "Drift" }; string[] typeNames = { "Sprint", "Circuit", "Time trial", "Drift" };
            int type = 0, laps = 3, opp = 5;
            float len = Length();
            var start = new Vector3(pts[0][0], pts[0][1], pts[0][2]); var end = new Vector3(pts[pts.Count - 1][0], pts[pts.Count - 1][1], pts[pts.Count - 1][2]);
            if (start.DistanceTo(end) < 120f && pts.Count >= 3) type = 1;
            var m = new Menu("Save route", (pts.Count - 1) + " checkpoints · " + (len / 1000f).ToString("0.0") + " km");
            m.Add(new MenuItem("Race type") { Value = () => typeNames[type], OnAdjust = d => type = (type + d + types.Length) % types.Length, Description = () => type == 1 ? "Circuits loop back to the start." : type >= 2 ? "Solo run with medals. Your best run becomes a ghost." : "Point to point against the AI." });
            m.Add(new MenuItem("Laps") { Value = () => type == 1 ? laps.ToString() : "-", OnAdjust = d => laps = Math.Max(1, Math.Min(10, laps + d)) });
            m.Add(new MenuItem("Opponents") { Value = () => type <= 1 ? opp.ToString() : "-", OnAdjust = d => opp = Math.Max(1, Math.Min(7, opp + d)) });
            Func<RaceDefinition> build = () =>
            {
                var list = new List<float[]>(pts);
                if (types[type] == "Circuit" && start.DistanceTo(end) < 60f && list.Count > 2) list.RemoveAt(list.Count - 1);   // the loop closes itself
                string name = Zone(pts[0]) + (types[type] == "Circuit" ? " Loop" : " to " + Zone(pts[pts.Count - 1]));
                float km = len / 1000f * (types[type] == "Circuit" ? laps : 1);
                return new RaceDefinition
                {
                    Id = "custom_" + DateTime.UtcNow.Ticks, Name = name + " (custom)", Type = types[type], Laps = types[type] == "Circuit" ? laps : 1, Opponents = opp,
                    Points = list, CashReward = (long)Math.Max(1500, km * 900), RepReward = (long)Math.Max(60, km * 45), Tier = 1
                };
            };
            Action<bool> save = raceNow =>
            {
                var r = build();
                var all = LoadAll(races.DataDir); all.Add(r); SaveAll(races.DataDir, all);
                races.AddCustom(r); ClearBlips(); stack.Close();
                Shard.Mid("ROUTE SAVED", r.Name, Shard.Purple, 3000);
                if (raceNow) races.StartNow(r);
            };
            m.Add(new MenuItem("Save and race now", () => save(true)));
            m.Add(new MenuItem("Save", () => save(false)) { Description = () => "It goes on the map and in Races > My routes." });
            m.Add(new MenuItem("Discard", () => { ClearBlips(); stack.Close(); }));
            return m;
        }

        // ---------------------------------------------------------------- my routes
        public Menu MyRoutes()
        {
            var m = new Menu("My routes", "Built with the route creator");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                m.Add(new MenuItem("Create a new route", () => { stack.Close(); Begin(); }) { Description = () => "Drive it, tap the horn for checkpoints, hold the horn to finish." });
                foreach (var rr in LoadAll(races.DataDir))
                {
                    var r = rr;
                    m.Add(new MenuItem(r.Name, () => stack.Push(RouteMenu(r))) { Value = () => r.Kind == RaceType.TimeTrial ? "Time trial" : r.Kind.ToString() });
                }
            };
            return m;
        }

        private Menu RouteMenu(RaceDefinition r)
        {
            var m = new Menu(r.Name, (r.Points.Count - 1) + " checkpoints");
            m.Add(new MenuItem("Race now", () => { stack.Close(); races.StartNow(r); }) { Description = () => "You'll be taken to the start line." });
            m.Add(new MenuItem("Set GPS to start", () => { Function.Call(Hash.SET_NEW_WAYPOINT, r.Start.X, r.Start.Y); stack.Close(); }));
            m.Add(new MenuItem("Delete route", () =>
            {
                var all = LoadAll(races.DataDir); all.RemoveAll(x => x.Id == r.Id); SaveAll(races.DataDir, all);
                races.RemoveCustom(r.Id); stack.Pop(); Shard.Mid("ROUTE DELETED", r.Name, Shard.Red, 2000);
            }));
            return m;
        }
    }
}
