using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.Racing;
using MidnightClub.UI;

namespace MidnightClub.Career
{
    /// <summary>The career story that ties every system together: seven chapters, each a short list of goals drawn
    /// from all of Midnight Club (street racing, rally, drift, drag, bikes, turf, rivals, outruns, takeovers...).
    /// Finishing a chapter pays out, makes the news and opens the next one; the Hub always shows what's next.</summary>
    public sealed class Story
    {
        public sealed class Goal { public string Key, Text; public long Target; public bool Max; }
        public sealed class Chapter { public string Title, Intro, Outro; public long Cash, Rep; public string Car; public Goal[] Goals; }

        public static readonly Chapter[] Chapters =
        {
            new Chapter { Title = "Rookie Nights", Intro = "Nobody knows your name yet. Win a few street races and show the city you're here.", Outro = "People are starting to talk about the new driver.", Cash = 15000, Rep = 400, Goals = new[] {
                G("win:street", "Win 2 street races", 2), G("outrun", "Win an outrun battle", 1), G("token", "Find 3 Midnight Tokens", 3), G("lvl:Street", "Reach Street level 2", 2, true) } },
            new Chapter { Title = "Making a Name", Intro = "Crews run this city district by district. Take one, and somebody will want it back.", Outro = "You hold turf now. The rivals have noticed.", Cash = 30000, Rep = 800, Goals = new[] {
                G("turfown", "Take control of a district", 1, true), G("rival", "Beat a rival in a duel", 1), G("series:Street", "Win the Street Cup", 1, true), G("escape", "Escape a police pursuit", 1), G("win:any", "Win 6 events of any kind", 6) } },
            new Chapter { Title = "Dirt & Dust", Intro = "Up north the roads run out. Rally stages, trail races and hill climbs: Blaine County races on dirt.", Outro = "Mud on the bodywork, trophies on the shelf.", Cash = 40000, Rep = 1000, Car = "trophytruck", Goals = new[] {
                G("medal:offroad", "Earn a medal on 3 rally stages or hill climbs", 3), G("win:offroad", "Win an off-road race or cross country", 1), G("lvl:Offroad", "Reach Off-road level 3", 3, true), G("series:Offroad", "Win an Off-road series", 1, true) } },
            new Chapter { Title = "Sideways", Intro = "The drift scene wants to see angle and smoke. Gymkhana, drift events and takeovers.", Outro = "You can hold a slide through an intersection full of people filming. Respect.", Cash = 45000, Rep = 1200, Goals = new[] {
                G("medal:drift", "Earn a medal in 2 drift or gymkhana events", 2), G("takeover", "Score 15,000 in a street takeover", 15000, true), G("lvl:Drift", "Reach Drift level 3", 3, true), G("series:Drift", "Win a Drift series", 1, true) } },
            new Chapter { Title = "Quarter Miles & Two Wheels", Intro = "Straight lines and superbikes: the drag strips and the bike crews.", Outro = "Fast on four wheels and on two.", Cash = 55000, Rep = 1500, Goals = new[] {
                G("win:drag", "Win 2 drag races", 2), G("win:bikes", "Win 2 bike races", 2), G("lvl:Drag", "Reach Drag level 3", 3, true), G("series:Drag", "Win a Drag series", 1, true), G("series:Bikes", "Win a Bikes series", 1, true) } },
            new Chapter { Title = "City of Crews", Intro = "Half of Los Santos answers to somebody. Make it answer to you.", Outro = "Most of the city is yours. Only one name is left.", Cash = 80000, Rep = 2500, Goals = new[] {
                G("turfown", "Control 5 districts", 5, true), G("rivals", "Beat 4 rivals for good", 4, true), G("win:any", "Win 25 events", 25) } },
            new Chapter { Title = "The Midnight Club", Intro = "An invitation, no return address: the President wants to meet you on the streets.", Outro = "You are the Midnight Club.", Cash = 250000, Rep = 5000, Goals = new[] {
                G("president", "Beat the President", 1, true) } },
        };
        private static Goal G(string k, string t, long n, bool max = false) { return new Goal { Key = k, Text = t, Target = n, Max = max }; }

        public static Story Instance;
        private readonly SaveManager saves; private readonly TransactionManager money;

        public Story(SaveManager saves, TransactionManager money)
        {
            this.saves = saves; this.money = money; Instance = this;
            EventBus.Subscribe<RaceFinishedEvent>(OnRace);
            EventBus.Subscribe<Rivals.RivalDefeatedEvent>(e => { Track("rivals", saves.Data.Rivals.Count(r => r.Defeated), true); if (e.Id == "president") Track("president", 1, true); });
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To == GameMode.MIDNIGHT_CLUB) introAt = Game.GameTime + 14000; });
            EventBus.Subscribe<MidnightTickEvent>(e => { if (introAt > 0 && Game.GameTime > introAt && !RaceManager.IsRacing) { introAt = 0; ShowChapterToast(); } });
        }
        private int introAt;

        public Chapter Current { get { return saves.Data == null || saves.Data.StoryChapter >= Chapters.Length ? null : Chapters[saves.Data.StoryChapter]; } }
        public long Count(string key) { var kv = saves.Data.StoryCounters.FirstOrDefault(x => x.Key == key); long v; return kv != null && long.TryParse(kv.Value, out v) ? v : 0; }
        public bool Done(Goal g) { return Count(g.Key) >= g.Target; }
        public string NextGoalText { get { var c = Current; if (c == null) return null; var g = c.Goals.FirstOrDefault(x => !Done(x)); return g == null ? null : c.Title + ": " + g.Text + (g.Target > 1 && !g.Max ? "  (" + Count(g.Key) + "/" + g.Target + ")" : ""); } }

        /// <summary>Counts toward story goals (all chapters share the counters, so nothing done early is wasted).</summary>
        public void Track(string key, long amount, bool max = false)
        {
            if (saves.Data == null) return;
            long v = max ? Math.Max(Count(key), amount) : Count(key) + amount;
            saves.Data.StoryCounters.RemoveAll(x => x.Key == key); saves.Data.StoryCounters.Add(new KeyValue { Key = key, Value = v.ToString() }); saves.IsDirty = true;
            Check();
        }

        private void OnRace(RaceFinishedEvent e)
        {
            var r = e.Race; var res = e.Result; if (res.Position == 0) return;
            var d = r.Discipline;
            var cur = Vehicles.VehicleManager.Instance != null ? Vehicles.VehicleManager.Instance.Current : null;
            bool bike = cur != null && cur.Exists() && cur.Model.IsBike;
            bool won = r.HasOpponents ? res.Position == 1 : res.Medal == "Gold";
            bool medal = res.Medal == "Gold" || res.Medal == "Silver" || res.Medal == "Bronze";
            if (won) Track("win:any", 1);
            if (r.HasOpponents && res.Position == 1)
            {
                if (bike) Track("win:bikes", 1);
                else if (d == Discipline.Street) Track("win:street", 1);
                else if (d == Discipline.Offroad) Track("win:offroad", 1);
                else if (d == Discipline.Drag) Track("win:drag", 1);
            }
            if (!r.HasOpponents && medal) { if (d == Discipline.Offroad) Track("medal:offroad", 1); if (d == Discipline.Drift) Track("medal:drift", 1); }
            if (e.RivalId != null && e.Won) Track("rival", 1);
        }

        private void Check()
        {
            var c = Current; if (c == null) return;
            if (!c.Goals.All(Done)) return;
            saves.Data.StoryChapter++; saves.IsDirty = true;
            money.Earn(c.Cash, c.Rep, "story", "Chapter complete: " + c.Title, "story:" + c.Title);
            if (c.Car != null && Progression.Instance != null) Progression.Instance.GiveCar(c.Car, c.Title);
            if (Progression.Instance != null) Progression.Instance.Award(2500 + 1000 * saves.Data.StoryChapter, "Chapter complete");
            saves.Data.SuperWheelspins++;
            Shard.Big("CHAPTER COMPLETE  ·  " + c.Title.ToUpperInvariant(), c.Outro + "  +$" + c.Cash.ToString("N0") + "  ·  SUPER WHEELSPIN", Shard.Gold, 7000);
            if (Phone.PhoneManager.Instance != null) Phone.PhoneManager.Instance.News(c.Title + ": the new name on the streets", c.Outro);
            introAt = Game.GameTime + 9000;
            Check();
        }

        private void ShowChapterToast()
        {
            var c = Current; if (c == null) return;
            Toast.Post("~p~Chapter " + (saves.Data.StoryChapter + 1) + ": " + c.Title + "~s~. " + c.Intro, true);
        }

        public Menu Menu()
        {
            var m = new Menu("Story", "Chapter " + Math.Min(Chapters.Length, (saves.Data.StoryChapter + 1)) + " of " + Chapters.Length);
            m.OnOpen = () =>
            {
                m.Items.Clear();
                for (int i = 0; i < Chapters.Length; i++)
                {
                    var c = Chapters[i]; int idx = i; bool done = i < saves.Data.StoryChapter, cur = i == saves.Data.StoryChapter;
                    if (i > saves.Data.StoryChapter) { m.Add(new MenuItem("Chapter " + (i + 1) + "  ·  ???") { Enabled = false, Value = () => "Locked" }); continue; }
                    m.Add(new MenuItem("Chapter " + (i + 1) + "  ·  " + c.Title, () => stack.Push(ChapterMenu(idx))) { Value = () => done ? "COMPLETE" : c.Goals.Count(Done) + " / " + c.Goals.Length, Description = () => c.Intro });
                }
            };
            return m;
        }
        public MenuStack stack;

        private Menu ChapterMenu(int i)
        {
            var c = Chapters[i]; var m = new Menu(c.Title, "Chapter " + (i + 1));
            foreach (var g in c.Goals) { var gg = g; m.Add(new MenuItem(gg.Text) { Value = () => Done(gg) ? "DONE" : (gg.Target > 1 ? Math.Min(Count(gg.Key), gg.Target).ToString("N0") + " / " + gg.Target.ToString("N0") : "-"), Description = () => c.Intro }); }
            m.Add(new MenuItem("Reward") { Enabled = false, Value = () => "$" + c.Cash.ToString("N0") + " + Super Wheelspin" + (c.Car != null ? " + car" : "") });
            return m;
        }
    }
}
