using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Core;
using MidnightClub.Persistence;
using MidnightClub.UI;

namespace MidnightClub.Explore
{
    public sealed class DiscoveredEvent { public string Id, Kind; public DiscoveredEvent(string id, string kind) { Id = id; Kind = kind; } }

    /// <summary>Exploration layer: map locations (workshops, dealerships, garages, races, speed traps, skill zones) stay
    /// hidden until you drive near them; '?' marks hint at what's close. Also runs the speed/drift skill zones (built
    /// from the road network) and the barn finds (rumours unlocked by level, the car is physically hidden there).</summary>
    public sealed class Discovery
    {
        public static Discovery Instance { get; private set; }
        private sealed class Poi { public string Id, Name, Kind; public Vector3 Pos; public float Radius; }
        private readonly Dictionary<string, Poi> pois = new Dictionary<string, Poi>();
        private readonly Dictionary<string, Blip> hints = new Dictionary<string, Blip>();
        private readonly SaveManager saves;
        private static readonly string[] Starter = { "ws:lsc_lamesa", "dl:pdm", "dl:used", "race:r01", "race:r02", "race:r03", "race:r04" };
        private int nextScan;

        public Discovery(SaveManager saves)
        {
            Instance = this; this.saves = saves;
            LoadSkills();
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To == GameMode.MIDNIGHT_CLUB) { PlaceZoneBlips(); PlaceRumours(); } else CleanupAll(); });
        }

        // ------------------------------------------------------------------ registry
        public static bool Known(string id)
        {
            var d = Instance != null ? Instance.saves.Data : null;
            if (d == null) return true;
            return Starter.Contains(id) || d.Discovered.Contains(id);
        }

        /// <summary>Managers register each map location as they place blips; undiscovered ones get no blip.</summary>
        public static void Register(string id, string name, string kind, Vector3 pos, float radius = 90f)
        {
            if (Instance == null) return;
            Instance.pois[id] = new Poi { Id = id, Name = name, Kind = kind, Pos = pos, Radius = radius };
        }

        public int TotalCount { get { return pois.Count; } }
        public int KnownCount { get { return pois.Keys.Count(Known); } }

        public void Reveal(string id, bool announce = true)
        {
            if (saves.Data == null || Known(id)) return;
            saves.Data.Discovered.Add(id); saves.IsDirty = true;
            Poi p; pois.TryGetValue(id, out p);
            Blip h; if (hints.TryGetValue(id, out h)) { try { if (h.Exists()) h.Delete(); } catch { } hints.Remove(id); }
            if (announce && p != null)
            {
                Shard.Mid("DISCOVERED", p.Kind.ToUpperInvariant() + "  ·  " + p.Name, Shard.Purple, 3500);
                if (Career.Progression.Instance != null) { Career.Progression.Instance.Award(p.Kind == "Race" ? 150 : 350, "Discovered " + p.Name); Career.Progression.Instance.Bump("discover", 1); }
            }
            EventBus.Publish(new DiscoveredEvent(id, p != null ? p.Kind : ""));
            if (id.StartsWith("zone:")) PlaceZoneBlips();
        }

        // ------------------------------------------------------------------ tick
        private void Tick()
        {
            if (saves.Data == null) return;
            var ped = Game.Player.Character; var pos = ped.Position;
            ZoneTick(ped);
            BarnTick(ped);
            if (Game.GameTime < nextScan) return;
            nextScan = Game.GameTime + 500;
            foreach (var p in pois.Values.ToList())
            {
                if (Known(p.Id)) continue;
                float d = pos.DistanceTo(p.Pos);
                if (d < p.Radius) { Reveal(p.Id); continue; }
                bool near = d < 450f;
                Blip h; bool has = hints.TryGetValue(p.Id, out h);
                if (near && !has)
                {
                    try { var b = UI.BlipRegistry.Own(World.CreateBlip(p.Pos)); b.Sprite = BlipSprite.Random; b.Color = BlipColor.Purple; b.Scale = 0.7f; b.IsShortRange = true; b.Name = "Undiscovered"; hints[p.Id] = b; } catch { }
                }
                else if (!near && has) { try { if (h.Exists()) h.Delete(); } catch { } hints.Remove(p.Id); }
            }
        }

        // ------------------------------------------------------------------ skill zones
        [DataContract] private sealed class SkillFile { [DataMember(Name = "speed")] public List<List<float[]>> Speed; [DataMember(Name = "drift")] public List<List<float[]>> Drift; [DataMember(Name = "barn")] public List<float[]> Barn; }
        private sealed class Zone { public string Id, Name; public bool Drift; public List<Vector3> Pts; public float Length; public float[] Stars; public Blip Blip; }
        private readonly List<Zone> zones = new List<Zone>();
        private readonly List<Vector3> barns = new List<Vector3>();
        public int BarnCount { get { return barns.Count; } }
        private static readonly string[] BarnModels = { "ztype", "jb700", "stinger", "monroe", "cheetah2", "torero", "infernus2", "mamba" };

        private void LoadSkills()
        {
            try
            {
                using (var s = typeof(Discovery).Assembly.GetManifestResourceStream("skills.json"))
                {
                    var f = (SkillFile)new DataContractJsonSerializer(typeof(SkillFile)).ReadObject(s);
                    int i = 0;
                    foreach (var z in f.Speed) zones.Add(MakeZone("zone:s:" + (i++), false, z));
                    i = 0;
                    foreach (var z in f.Drift) zones.Add(MakeZone("zone:d:" + (i++), true, z));
                    foreach (var b in f.Barn) barns.Add(new Vector3(b[0], b[1], b[2]));
                }
                Log.Info("Skill zones: " + zones.Count + ", barn finds: " + barns.Count);
            }
            catch (Exception ex) { Log.Error("Skill zones load failed", ex); }
        }

        private Zone MakeZone(string id, bool drift, List<float[]> pts)
        {
            var z = new Zone { Id = id, Drift = drift, Pts = pts.Select(p => new Vector3(p[0], p[1], p[2])).ToList() };
            for (int k = 1; k < z.Pts.Count; k++) z.Length += z.Pts[k].DistanceTo(z.Pts[k - 1]);
            z.Stars = drift ? new[] { 4000f, 9000f, 16000f } : new[] { 120f, 155f, 190f };
            return z;
        }

        private void PlaceZoneBlips()
        {
            foreach (var z in zones)
            {
                if (z.Name == null) { try { z.Name = World.GetStreetName(z.Pts[0]); } catch { z.Name = "Unknown road"; } if (string.IsNullOrEmpty(z.Name)) z.Name = "Back road"; }
                Register(z.Id, z.Name, z.Drift ? "Drift zone" : "Speed zone", z.Pts[0], 70f);
                bool known = Known(z.Id);
                if (known && (z.Blip == null || !z.Blip.Exists()))
                {
                    z.Blip = UI.BlipRegistry.Own(World.CreateBlip(z.Pts[0])); z.Blip.Sprite = z.Drift ? BlipSprite.SteeringWheel : BlipSprite.Stopwatch;
                    z.Blip.Color = z.Drift ? BlipColor.Orange : BlipColor.BlueLight; z.Blip.Scale = 0.75f; z.Blip.IsShortRange = true;
                    z.Blip.Name = (z.Drift ? "Drift zone: " : "Speed zone: ") + z.Name;
                }
            }
        }

        public float Best(string id) { var kv = saves.Data.SkillRecords.FirstOrDefault(x => x.Key == id); float v; return kv != null && float.TryParse(kv.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0f; }
        private int StarsFor(Zone z, float v) { int s = 0; foreach (var t in z.Stars) if (v >= t) s++; return s; }
        public int StarsEarned { get { return saves.Data == null ? 0 : zones.Sum(z => StarsFor(z, Best(z.Id))); } }
        public int StarsTotal { get { return zones.Count * 3; } }

        private Zone active; private int activeIdx, startedAt; private float score;
        private void ZoneTick(Ped ped)
        {
            var car = ped.CurrentVehicle;
            if (car == null || !car.Exists() || Racing.RaceManager.IsRacing) { active = null; return; }
            var p = car.Position;
            if (active == null)
            {
                foreach (var z in zones)
                {
                    if (p.DistanceTo(z.Pts[0]) > 160f) continue;
                    if (Known(z.Id)) World.DrawMarker(MarkerType.ChevronUpx1, z.Pts[0] + new Vector3(0, 0, 1.4f), Vector3.Zero, new Vector3(0, 0, 0), new Vector3(2.5f, 2.5f, 2.5f), z.Drift ? System.Drawing.Color.FromArgb(160, 251, 146, 60) : System.Drawing.Color.FromArgb(160, 34, 211, 238), true, false, false, null, null, false);
                    if (p.DistanceTo(z.Pts[0]) < 18f && car.Speed > 6f)
                    {
                        var dir = Vector3.Normalize(z.Pts[1] - z.Pts[0]);
                        if (Vector3.Dot(Vector3.Normalize(car.Velocity), dir) < 0.5f) continue;
                        if (!Known(z.Id)) Reveal(z.Id);
                        active = z; activeIdx = 0; startedAt = Game.GameTime; score = 0f;
                        Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Checkpoint_Hit", "GTAO_FM_Events_Soundset", true);
                        break;
                    }
                }
                return;
            }
            var zz = active;
            // progress along the zone; leave the road and it's over
            int best = activeIdx; float bd = float.MaxValue;
            for (int k = activeIdx; k < Math.Min(zz.Pts.Count, activeIdx + 6); k++) { float d = p.DistanceTo(zz.Pts[k]); if (d < bd) { bd = d; best = k; } }
            if (bd > 45f) { Shard.Mid(zz.Drift ? "DRIFT ZONE FAILED" : "SPEED ZONE FAILED", "You left the zone", Shard.Red, 2500); active = null; return; }
            activeIdx = best;
            float t = (Game.GameTime - startedAt) / 1000f;
            if (zz.Drift)
            {
                var v = car.Velocity; float spd = v.Length();
                if (spd > 7f)
                {
                    var fwd = car.ForwardVector; float ang = (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, Vector3.Dot(Vector3.Normalize(new Vector3(v.X, v.Y, 0)), Vector3.Normalize(new Vector3(fwd.X, fwd.Y, 0)))))) * 180.0 / Math.PI);
                    if (ang > 12f && ang < 110f) score += (ang - 10f) * spd * Game.LastFrameTime * 2f;
                }
            }
            float live = zz.Drift ? score : (t > 0.5f ? (zz.Length * Math.Max(0.01f, (activeIdx / (float)(zz.Pts.Count - 1)))) / t * 3.6f : 0f);
            DrawZoneHud(zz, live);
            if (activeIdx >= zz.Pts.Count - 2 && p.DistanceTo(zz.Pts[zz.Pts.Count - 1]) < 30f)
            {
                float result = zz.Drift ? score : zz.Length / Math.Max(0.1f, t) * 3.6f;
                Finish(zz, result);
                active = null;
            }
        }

        private void Finish(Zone z, float v)
        {
            float old = Best(z.Id); int s = StarsFor(z, v), os = StarsFor(z, old);
            string val = z.Drift ? v.ToString("N0") + " PTS" : v.ToString("0") + " KM/H";
            string stars = new string('*', s) + new string('-', 3 - s);
            if (v > old)
            {
                saves.Data.SkillRecords.RemoveAll(x => x.Key == z.Id);
                saves.Data.SkillRecords.Add(new KeyValue { Key = z.Id, Value = v.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                saves.IsDirty = true;
            }
            Shard.Mid((z.Drift ? "DRIFT ZONE  " : "SPEED ZONE  ") + stars, val + (v > old && old > 0 ? "  ·  NEW RECORD" : ""), s >= 3 ? Shard.Gold : Shard.Blue, 4000);
            if (s > os && Career.Progression.Instance != null)
            {
                Career.Progression.Instance.Award((s - os) * 400, "Skill zone");
                Career.Progression.Instance.Bump("zone", 1);
            }
        }

        private void DrawZoneHud(Zone z, float live)
        {
            float w = 0.26f, x = 0.5f - w / 2, y = 0.02f;
            Ds.Skew(x, y, w, 0.07f, Ds.SlantFor(0.07f) * 0.5f, Ds.A(Ds.Surface, 0.92f));
            Ds.Tag(z.Drift ? "DRIFT ZONE" : "SPEED ZONE", x + Ds.Px(0.02f), y + 0.012f, z.Drift ? Ds.Amber : Ds.Cyan, Ds.Bg, 0.26f);
            Ds.T(Ds.Fit(z.Name.ToUpperInvariant(), 0.26f, w * 0.5f), x + w - Ds.Px(0.016f), y + 0.012f, 0.26f, Ds.Muted, Ds.Display, Alignment.Right);
            Ds.T(z.Drift ? live.ToString("N0") : live.ToString("0"), x + Ds.Px(0.02f), y + 0.03f, 0.55f, Ds.Text, Ds.Display);
            int s = StarsFor(z, live);
            string next = s >= 3 ? "MAX" : (z.Drift ? z.Stars[s].ToString("N0") : z.Stars[s].ToString("0") + " KM/H") + " FOR " + (s + 1) + "*";
            Ds.T(next, x + w - Ds.Px(0.016f), y + 0.042f, 0.26f, Ds.Muted, Ds.Display, Alignment.Right);
        }

        // ------------------------------------------------------------------ barn finds
        private readonly Dictionary<int, Blip> rumourBlips = new Dictionary<int, Blip>();
        private Vehicle barnCar; private int barnCarIdx = -1;

        public void RevealRumour(int i)
        {
            if (i < 0 || i >= barns.Count || saves.Data == null) return;
            string id = "rumour:" + i;
            if (!saves.Data.Discovered.Contains(id)) { saves.Data.Discovered.Add(id); saves.IsDirty = true; }
            Shard.Mid("BARN FIND RUMOUR", "Someone saw an old car around " + SafeZone(barns[i]) + ". Search the marked area.", Shard.Orange, 5000);
            PlaceRumours();
        }

        private static string SafeZone(Vector3 p) { try { return World.GetZoneLocalizedName(p); } catch { return "the countryside"; } }

        private void PlaceRumours()
        {
            if (saves.Data == null) return;
            for (int i = 0; i < barns.Count; i++)
            {
                bool show = saves.Data.Discovered.Contains("rumour:" + i) && !saves.Data.BarnFound.Contains("barn:" + i);
                Blip b; bool has = rumourBlips.TryGetValue(i, out b) && b.Exists();
                if (show && !has)
                {
                    var r = new Random(i * 31 + 7); double a = r.NextDouble() * Math.PI * 2; float off = 60f + (float)r.NextDouble() * 120f;
                    var c = barns[i] + new Vector3((float)Math.Cos(a) * off, (float)Math.Sin(a) * off, 0);
                    b = UI.BlipRegistry.Own(World.CreateBlip(c, 260f)); b.Color = BlipColor.Orange; b.Alpha = 110; b.Name = "Barn find rumour"; rumourBlips[i] = b;
                }
                else if (!show && has) { b.Delete(); rumourBlips.Remove(i); }
            }
        }

        private void BarnTick(Ped ped)
        {
            if (saves.Data == null) return;
            var p = ped.Position;
            for (int i = 0; i < barns.Count; i++)
            {
                if (!saves.Data.Discovered.Contains("rumour:" + i) || saves.Data.BarnFound.Contains("barn:" + i)) continue;
                float d = p.DistanceTo(barns[i]);
                if (d < 180f && barnCarIdx != i)
                {
                    DespawnBarnCar();
                    var m = new Model(BarnModels[i % BarnModels.Length]); if (!m.IsValid) m = new Model("tornado");
                    if (m.Request(3000))
                    {
                        barnCar = World.CreateVehicle(m, barns[i], (i * 47) % 360);
                        if (barnCar != null) { barnCar.DirtLevel = 15f; barnCar.IsEngineRunning = false; barnCar.LockStatus = VehicleLockStatus.Locked; barnCarIdx = i; Function.Call(Hash.SET_VEHICLE_ENGINE_HEALTH, barnCar, 250f); }
                        m.MarkAsNoLongerNeeded();
                    }
                }
                if (barnCarIdx == i && barnCar != null && barnCar.Exists() && p.DistanceTo(barnCar.Position) < 7f)
                {
                    string model = BarnModels[i % BarnModels.Length]; if (!new Model(model).IsValid) model = "tornado";
                    saves.Data.BarnFound.Add("barn:" + i); saves.IsDirty = true;
                    string name = Game.GetLocalizedString(Function.Call<string>(Hash.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL, barnCar.Model.Hash));
                    Shard.Big("BARN FIND", (string.IsNullOrEmpty(name) ? model.ToUpperInvariant() : name) + " is being restored and delivered to your garage", Shard.Orange, 6000);
                    if (Career.Progression.Instance != null) { Career.Progression.Instance.GiveCar(model, "Barn find"); Career.Progression.Instance.Award(2000, "Barn find"); }
                    DespawnBarnCar(); PlaceRumours();
                }
                if (d > 260f && barnCarIdx == i) DespawnBarnCar();
            }
        }

        private void DespawnBarnCar() { try { if (barnCar != null && barnCar.Exists()) barnCar.Delete(); } catch { } barnCar = null; barnCarIdx = -1; }

        private void CleanupAll()
        {
            foreach (var h in hints.Values) try { if (h.Exists()) h.Delete(); } catch { }
            hints.Clear();
            foreach (var z in zones) { try { if (z.Blip != null && z.Blip.Exists()) z.Blip.Delete(); } catch { } z.Blip = null; }
            foreach (var b in rumourBlips.Values) try { if (b.Exists()) b.Delete(); } catch { }
            rumourBlips.Clear(); DespawnBarnCar(); active = null;
        }
    }
}
