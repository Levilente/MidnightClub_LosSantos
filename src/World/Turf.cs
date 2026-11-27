using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.Racing;
using MidnightClub.UI;

namespace MidnightClub.Explore
{
    /// <summary>Turf war: Los Santos is split into nine districts, each run by a crew. Win races that start in a
    /// district to take control of it (3 wins = 100 %). Owned districts pay out every night and show on the map in
    /// club purple. Crews strike back: every few nights one of your districts comes under attack and slowly slips
    /// away until you win a race there again.</summary>
    public sealed class Turf
    {
        public sealed class District { public string Id, Name, Crew; public Vector3 Center; public float Radius; public long Income; public Blip Blip; }

        public static readonly District[] Districts =
        {
            new District { Id = "downtown", Name = "Downtown", Crew = "Pillbox Kings", Center = new Vector3(150f, -800f, 31f), Radius = 650f, Income = 5000 },
            new District { Id = "vinewood", Name = "Vinewood", Crew = "Hollywood Hustle", Center = new Vector3(330f, 200f, 103f), Radius = 650f, Income = 6000 },
            new District { Id = "westside", Name = "Rockford & Del Perro", Crew = "Westside Euros", Center = new Vector3(-1100f, -250f, 37f), Radius = 750f, Income = 7000 },
            new District { Id = "beach", Name = "Vespucci Beach", Crew = "Canal Runners", Center = new Vector3(-1200f, -1150f, 5f), Radius = 600f, Income = 4500 },
            new District { Id = "south", Name = "South Los Santos", Crew = "Davis Lowriders", Center = new Vector3(150f, -1700f, 29f), Radius = 750f, Income = 4000 },
            new District { Id = "east", Name = "East Los Santos", Crew = "La Mesa Iron", Center = new Vector3(1100f, -1100f, 30f), Radius = 750f, Income = 4000 },
            new District { Id = "port", Name = "Port & LSIA", Crew = "Terminal Tuners", Center = new Vector3(-400f, -2400f, 6f), Radius = 900f, Income = 5500 },
            new District { Id = "county", Name = "Blaine County", Crew = "Senora Dust Devils", Center = new Vector3(1500f, 3300f, 40f), Radius = 1800f, Income = 3500 },
            new District { Id = "paleto", Name = "Paleto Bay", Crew = "Northern Kings", Center = new Vector3(-150f, 6300f, 31f), Radius = 900f, Income = 3000 },
        };

        public static Turf Instance;
        private readonly SaveManager saves; private readonly TransactionManager money;
        private District here; private int lastCheck, toastUntil; private string toastText, toastSub; private Color toastColor;

        public Turf(SaveManager saves, TransactionManager money)
        {
            this.saves = saves; this.money = money; Instance = this;
            EventBus.Subscribe<MidnightTickEvent>(e => { try { Tick(); } catch (Exception ex) { Log.Error("Turf", ex); } });
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To == GameMode.MIDNIGHT_CLUB) PlaceBlips(); else ClearBlips(); });
            EventBus.Subscribe<RaceFinishedEvent>(OnRace);
            EventBus.Subscribe<WorldSim.NewDayEvent>(e => NewDay(e.Day));
        }

        public int Control(string id) { var kv = saves.Data.Turf.FirstOrDefault(x => x.Key == id); int v; return kv != null && int.TryParse(kv.Value, out v) ? v : 0; }
        private void Set(string id, int v)
        {
            v = Math.Max(0, Math.Min(100, v));
            saves.Data.Turf.RemoveAll(x => x.Key == id); if (v > 0) saves.Data.Turf.Add(new KeyValue { Key = id, Value = v.ToString() });
            saves.IsDirty = true;
        }
        public int Owned { get { return Districts.Count(d => Control(d.Id) >= 100); } }
        private bool UnderAttack(string id) { return saves.Data.Turf.Any(x => x.Key == "attack:" + id); }

        public static District At(Vector3 p)
        {
            District best = null; float bd = float.MaxValue;
            foreach (var d in Districts) { float dd = new Vector2(p.X - d.Center.X, p.Y - d.Center.Y).Length() / d.Radius; if (dd < 1f && dd < bd) { bd = dd; best = d; } }
            return best;
        }

        // ---------------------------------------------------------------- races take turf
        private void OnRace(RaceFinishedEvent e)
        {
            var d = At(e.Race.Start); if (d == null || e.Result.Position == 0) return;
            int gain = e.Result.Position == 1 || e.Result.Medal == "Gold" ? 34 : e.Result.Position == 2 || e.Result.Medal == "Silver" ? 12 : 0;
            if (gain == 0) return;
            int before = Control(d.Id), after = Math.Min(100, before + gain);
            if (Career.Progression.Instance != null) Career.Progression.Instance.Bump("turf", 1);
            Set(d.Id, after);
            if (UnderAttack(d.Id)) { saves.Data.Turf.RemoveAll(x => x.Key == "attack:" + d.Id); Shard.Mid("TURF DEFENDED", d.Name + " is safe. " + d.Crew + " backed off.", Shard.Purple); }
            if (before < 100 && after >= 100)
            {
                Shard.Big("DISTRICT TAKEN", d.Name + " is yours. " + d.Crew + " are out. +$" + d.Income.ToString("N0") + " every night.", Shard.Purple, 6000);
                if (Phone.PhoneManager.Instance != null) Phone.PhoneManager.Instance.News("Street racers take over " + d.Name, "The " + d.Crew + " crew has lost its grip on " + d.Name + ". Locals say a new name runs the streets after midnight.");
                if (Career.Progression.Instance != null) Career.Progression.Instance.Award(2000, "District taken");
                if (Owned == Districts.Length) Shard.Big("KING OF LOS SANTOS", "Every district in the city answers to you.", Shard.Gold, 7000);
            }
            else Toast("TURF  " + d.Name.ToUpperInvariant(), "Control " + after + "%  ·  " + d.Crew, Ds.Violet);
            PlaceBlips();
        }

        private void NewDay(int day)
        {
            long total = 0;
            foreach (var d in Districts)
            {
                int c = Control(d.Id);
                if (UnderAttack(d.Id)) { c -= 20; Set(d.Id, c); if (c <= 0) { saves.Data.Turf.RemoveAll(x => x.Key == "attack:" + d.Id); Shard.Mid("DISTRICT LOST", d.Crew + " took back " + d.Name + ".", Shard.Red); } continue; }
                if (c >= 100) total += d.Income;
            }
            if (total > 0) money.Earn(total, 0, "turf", "Turf income (" + Owned + " districts)", "turf:" + day);
            // crews strike back every ~3 nights
            var owned = Districts.Where(d => Control(d.Id) >= 100 && !UnderAttack(d.Id)).ToList();
            if (owned.Count > 0 && day % 3 == 0)
            {
                var d = owned[new Random(day * 977).Next(owned.Count)];
                saves.Data.Turf.Add(new KeyValue { Key = "attack:" + d.Id, Value = day.ToString() });
                if (Phone.PhoneManager.Instance != null) Phone.PhoneManager.Instance.Send(d.Crew, "We want " + d.Name + " back", "Your name doesn't scare anybody in " + d.Name + ". Every night you don't show up, we take a little more. Win a race there if you want it.", null);
                Shard.Mid("TURF UNDER ATTACK", d.Crew + " is moving on " + d.Name + ". Win a race there to defend it.", Shard.Red);
            }
            PlaceBlips();
        }

        // ---------------------------------------------------------------- map + district toasts
        private void PlaceBlips()
        {
            ClearBlips();
            foreach (var d in Districts)
            {
                try
                {
                    int c = Control(d.Id);
                    var b = BlipRegistry.Own(World.CreateBlip(d.Center, d.Radius));
                    b.Color = c >= 100 ? (UnderAttack(d.Id) ? BlipColor.Red : BlipColor.Purple) : BlipColor.Grey;
                    b.Alpha = c >= 100 ? 70 : 28 + c / 3;
                    b.IsShortRange = false;
                    GTA.Native.Function.Call(GTA.Native.Hash.SET_BLIP_DISPLAY, b, 3);
                    GTA.Native.Function.Call(GTA.Native.Hash.SET_BLIP_AS_SHORT_RANGE, b, false);
                    d.Blip = b;
                }
                catch { }
            }
        }
        private void ClearBlips() { foreach (var d in Districts) { try { if (d.Blip != null && d.Blip.Exists()) d.Blip.Delete(); } catch { } d.Blip = null; } }

        private void Tick()
        {
            int now = Game.GameTime;
            if (now - lastCheck > 1000)
            {
                lastCheck = now;
                var d = At(Game.Player.Character.Position);
                if (d != here)
                {
                    here = d;
                    if (d != null && !RaceManager.IsRacing)
                    {
                        int c = Control(d.Id);
                        Toast(d.Name.ToUpperInvariant(), c >= 100 ? (UnderAttack(d.Id) ? "YOUR TURF  ·  UNDER ATTACK" : "YOUR TURF  ·  +$" + d.Income.ToString("N0") + " / NIGHT") : d.Crew.ToUpperInvariant() + " TERRITORY  ·  " + c + "% YOURS",
                            c >= 100 ? (UnderAttack(d.Id) ? Ds.Red : Ds.Violet) : Ds.Muted);
                    }
                }
            }
            if (now < toastUntil) DrawToast(now);
        }

        private void Toast(string t, string s, Color c) { toastText = t; toastSub = s; toastColor = c; toastUntil = Game.GameTime + 4200; }

        private void DrawToast(int now)
        {
            float left = (toastUntil - now) / 1000f, el = 4.2f - left;
            float a = Math.Min(1f, Math.Min(el * 4f, left * 2f)), off = (1f - Ds.EaseOut(el * 3f)) * 0.03f;
            float x = 0.5f, y = 0.115f + off;
            float w = Math.Max(Ds.Width(toastText, 0.7f, Ds.Display), Ds.Width(toastSub, 0.28f, Ds.Display)) + Ds.Px(0.06f);
            Ds.Rect(x - w / 2f, y, w, 0.004f, Ds.A(toastColor, a));
            Ds.T(toastText, x, y + 0.01f, 0.7f, Ds.A(Ds.Text, a), Ds.Display, GTA.UI.Alignment.Center);
            Ds.T(toastSub, x, y + 0.062f, 0.28f, Ds.A(toastColor == Ds.Muted ? Ds.Muted : toastColor, a), Ds.Display, GTA.UI.Alignment.Center);
        }

        // ---------------------------------------------------------------- menu
        public Menu TurfMenu()
        {
            var m = new Menu("Turf war", Owned + " of " + Districts.Length + " districts");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                foreach (var dd in Districts)
                {
                    var d = dd; int c = Control(d.Id);
                    m.Add(new MenuItem(d.Name, () => { GTA.World.WaypointPosition = d.Center; Shard.Mid("WAYPOINT SET", d.Name, Shard.Purple, 2000); })
                    {
                        Value = () => c >= 100 ? (UnderAttack(d.Id) ? "~r~UNDER ATTACK" : "~p~YOURS") : c + "%",
                        Description = () => (c >= 100 ? "You run " + d.Name + ". Pays $" + d.Income.ToString("N0") + " every night." : "Run by " + d.Crew + ". Win races that start here to take it (a win: +34 %, second / silver: +12 %).") + " Select to set a waypoint."
                    });
                }
            };
            return m;
        }
    }
}
