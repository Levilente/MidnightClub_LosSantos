using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.UI;

namespace MidnightClub.Racing
{
    /// <summary>Discipline series: three-round championships per discipline (a Cup and a harder Masters), points per
    /// round (positions or medals) against two series rivals, a podium ceremony at the end. Series wins feed the
    /// story chapters.</summary>
    public sealed class Series
    {
        public sealed class Def { public string Id, Name; public Discipline Disc; public List<RaceDefinition> Rounds; public long Prize; public string Car; public int Level; }
        private readonly SaveManager saves; private readonly TransactionManager money; private readonly RaceManager races; private readonly MenuStack stack;
        public static Series Instance;
        private static readonly string[][] Cars = { new[] { "jester4", "pariah" }, new[] { "elegy", "futo2" }, new[] { "trophytruck2", "caracara2" }, new[] { "gauntlet4", "dominator8" }, new[] { "shinobi", "reever" } };

        public Series(SaveManager saves, TransactionManager money, RaceManager races, MenuStack stack)
        {
            this.saves = saves; this.money = money; this.races = races; this.stack = stack; Instance = this;
            EventBus.Subscribe<RaceFinishedEvent>(OnRace);
        }

        public List<Def> All()
        {
            var list = new List<Def>();
            foreach (Discipline d in Enum.GetValues(typeof(Discipline)))
            {
                var pool = races.Races.Where(r => r.Discipline == d && !r.Id.StartsWith("custom_") && (d != Discipline.Street || r.HasOpponents)).OrderBy(r => r.LevelNeeded).ThenBy(r => r.RequiredRep).ThenBy(r => r.Id).ToList();
                if (pool.Count < 3) continue;
                string n = Career.Disciplines.Name(d);
                list.Add(Make(d, "cup", n + " Cup", pool.Take(3).ToList(), 25000, Cars[(int)d][0]));
                if (pool.Count >= 6) list.Add(Make(d, "masters", n + " Masters", pool.Skip(pool.Count - 3).ToList(), 70000, Cars[(int)d][1]));
            }
            return list;
        }
        private static Def Make(Discipline d, string k, string name, List<RaceDefinition> rounds, long prize, string car)
        { return new Def { Id = d + ":" + k, Name = name, Disc = d, Rounds = rounds, Prize = prize, Car = car, Level = rounds.Max(r => r.LevelNeeded) }; }

        private string Active { get { return saves.Data.SeriesActive; } }
        private bool Won(string id) { return saves.Data.SeriesWon.Contains(id); }

        public Menu Menu()
        {
            var m = new Menu("Series", "Three rounds, points, a podium");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                foreach (var dd in All())
                {
                    var d = dd; bool open = d.Rounds.All(races.Available);
                    m.Add(new MenuItem(d.Name, () => stack.Push(SeriesMenu(d)))
                    {
                        Value = () => Won(d.Id) ? "~y~WON" : Active == d.Id ? "ROUND " + (saves.Data.SeriesRound + 1) + " / 3" : open ? "$" + d.Prize.ToString("N0") : "LOCKED",
                        Description = () => string.Join("  ·  ", d.Rounds.Select(r => r.Name).ToArray()) + (open ? "" : ". Needs " + Career.Disciplines.Name(d.Disc) + " level " + d.Level + ".")
                    });
                }
            };
            return m;
        }

        private Menu SeriesMenu(Def d)
        {
            var m = new Menu(d.Name, "Win: $" + d.Prize.ToString("N0") + " and a car");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                bool mine = Active == d.Id;
                for (int i = 0; i < 3; i++) { var r = d.Rounds[i]; int ii = i; m.Add(new MenuItem("Round " + (i + 1) + "  ·  " + r.Name) { Enabled = false, Value = () => mine && ii < saves.Data.SeriesRound ? "DONE" : mine && ii == saves.Data.SeriesRound ? "NEXT" : "" }); }
                if (mine)
                {
                    m.Add(new MenuItem("Points") { Enabled = false, Value = () => saves.Data.SeriesPoints + " pts" });
                    m.Add(new MenuItem("Race round " + (saves.Data.SeriesRound + 1), () => { stack.Close(); races.Lobby(d.Rounds[saves.Data.SeriesRound]); }));
                    m.Add(new MenuItem("Retire from the series", () => { saves.Data.SeriesActive = null; saves.IsDirty = true; m.OnOpen(); }));
                }
                else m.Add(new MenuItem("Enter the series", () =>
                {
                    if (!d.Rounds.All(races.Available)) { Toast.Post("~r~Locked.~s~ Needs " + Career.Disciplines.Name(d.Disc) + " level " + d.Level + "."); return; }
                    saves.Data.SeriesActive = d.Id; saves.Data.SeriesRound = 0; saves.Data.SeriesPoints = 0; saves.IsDirty = true;
                    stack.Close(); races.Lobby(d.Rounds[0]);
                }) { Description = () => "Points: win / gold 10, second / silver 8, third / bronze 6. Beat both series rivals on points." });
            };
            return m;
        }

        private static int Points(RaceDefinition r, RaceResult res)
        {
            if (res.Position == 0) return 0;
            if (!r.HasOpponents) return res.Medal == "Gold" ? 10 : res.Medal == "Silver" ? 8 : res.Medal == "Bronze" ? 6 : 2;
            return new[] { 0, 10, 8, 6, 5, 4, 3, 2, 1 }[Math.Min(8, res.Position)];
        }

        private string[] Rivals(Def d) { var rnd = new Random(d.Id.GetHashCode()); var n = RaceManager.DriverNames.OrderBy(x => rnd.Next()).Take(2).ToArray(); return n; }
        private int RivalPoints(Def d, int who, int rounds) { var rnd = new Random(d.Id.GetHashCode() * 7 + who); int p = 0; for (int i = 0; i < rounds; i++) p += who == 0 ? rnd.Next(6, 11) : rnd.Next(4, 10); return p; }

        private void OnRace(RaceFinishedEvent e)
        {
            if (Active == null) return;
            var d = All().FirstOrDefault(x => x.Id == Active); if (d == null) { saves.Data.SeriesActive = null; return; }
            int round = saves.Data.SeriesRound; if (round >= 3 || d.Rounds[round].Id != e.Race.Id) return;
            saves.Data.SeriesPoints += Points(e.Race, e.Result); saves.Data.SeriesRound++; saves.IsDirty = true;
            if (saves.Data.SeriesRound < 3) { Toast.Post("~p~" + d.Name + "~s~: round " + (round + 1) + " done, " + saves.Data.SeriesPoints + " pts. Next: " + d.Rounds[round + 1].Name + "."); return; }
            pendingPodium = d;
        }

        private Def pendingPodium;
        /// <summary>Called after the results screen.</summary>
        public void AfterResults()
        {
            var d = pendingPodium; pendingPodium = null; if (d == null) return;
            var names = Rivals(d); int me = saves.Data.SeriesPoints, a = RivalPoints(d, 0, 3), b = RivalPoints(d, 1, 3);
            var table = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("YOU", me), new KeyValuePair<string, int>(names[0], a), new KeyValuePair<string, int>(names[1], b) }.OrderByDescending(x => x.Value).ThenBy(x => x.Key == "YOU" ? 0 : 1).ToList();
            int place = table.FindIndex(x => x.Key == "YOU") + 1;
            saves.Data.SeriesActive = null;
            if (place == 1)
            {
                if (!Won(d.Id)) saves.Data.SeriesWon.Add(d.Id);
                money.Earn(d.Prize, 1200, "series", d.Name + " win", "series:" + d.Id + ":" + saves.Data.RacesStarted);
                if (Career.Progression.Instance != null) { Career.Progression.Instance.GiveCar(d.Car, d.Name); Career.Progression.Instance.Award(3000, "Series win"); }
                if (Career.Story.Instance != null) Career.Story.Instance.Track("series:" + d.Disc, 1, true);
                if (Career.Disciplines.Instance != null) Career.Disciplines.Instance.Add(d.Disc, 1500, "series");
            }
            else if (place == 2) money.Earn(d.Prize / 3, 400, "series", d.Name + " second", null);
            saves.IsDirty = true;
            Podium(d, table, place);
        }

        private void Podium(Def d, List<KeyValuePair<string, int>> table, int place)
        {
            int start = Environment.TickCount;
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, place == 1 ? "RACE_PLACED" : "MEDAL_UP", place == 1 ? "HUD_AWARDS" : "HUD_MINI_GAME_SOUNDSET", true);
            while (true)
            {
                float el = (Environment.TickCount - start) / 1000f;
                Game.DisableAllControlsThisFrame(); Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
                Ds.RectPhys(0f, 0f, 1f, 1f, Ds.A(Ds.Bg, 0.92f));
                Ds.Streaks(0f, 0.1f, 1f, 0.8f, place == 1 ? Ds.Amber : Ds.Violet, 0.45f, 14, 0.8f);
                Ds.T(d.Name.ToUpperInvariant(), 0.5f, 0.08f, 0.4f, Ds.Muted, Ds.Display, GTA.UI.Alignment.Center);
                Ds.T(place == 1 ? "CHAMPION" : place == 2 ? "RUNNER-UP" : "THIRD", 0.5f, 0.11f, 1.6f, place == 1 ? Ds.Amber : Ds.Text, Ds.Display, GTA.UI.Alignment.Center);
                int[] order = { 1, 0, 2 }; float[] heights = { 0.22f, 0.3f, 0.16f }; Color[] cols = { Color.FromArgb(255, 200, 205, 220), Color.FromArgb(255, 251, 191, 36), Color.FromArgb(255, 205, 127, 70) };
                float w = Ds.Px(0.22f), gap = Ds.Px(0.02f), x0 = 0.5f - (3 * w + 2 * gap) / 2f, baseY = 0.85f;
                for (int s = 0; s < 3; s++)
                {
                    int idx = order[s]; if (idx >= table.Count) continue;
                    float k = Ds.EaseBack(Math.Min(1f, (el - 0.3f - s * 0.25f) * 2f)); if (k <= 0) continue;
                    float h = heights[s] * k, x = x0 + s * (w + gap);
                    bool me = table[idx].Key == "YOU";
                    Ds.Rect(x, baseY - h, w, h, me ? Ds.A(Ds.Violet, 0.95f) : Ds.A(Ds.Surface2, 0.95f));
                    Ds.Rect(x, baseY - h, w, 0.006f, cols[s]);
                    Ds.T((idx + 1).ToString(), x + w / 2f, baseY - h + 0.015f, 1.3f, cols[s], Ds.Display, GTA.UI.Alignment.Center);
                    Ds.T(table[idx].Key.ToUpperInvariant(), x + w / 2f, baseY - h - 0.08f, 0.5f, me ? Color.White : Ds.Text, Ds.Display, GTA.UI.Alignment.Center);
                    Ds.T(table[idx].Value + " PTS", x + w / 2f, baseY - h - 0.04f, 0.32f, Ds.Muted, Ds.Display, GTA.UI.Alignment.Center);
                }
                if (place == 1 && el > 1.2f) Ds.T("+$" + d.Prize.ToString("N0") + "  ·  " + d.Car.ToUpperInvariant() + " DELIVERED TO YOUR GARAGE", 0.5f, 0.9f, 0.34f, Ds.Lime, Ds.Display, GTA.UI.Alignment.Center);
                if (el > 1.5f) { Buttons.Draw(Control.FrontendAccept, "Continue"); if (Input.JustPressed(Control.FrontendAccept) || Input.JustPressed(Control.FrontendCancel)) break; }
                Script.Wait(0);
            }
        }
    }
}
