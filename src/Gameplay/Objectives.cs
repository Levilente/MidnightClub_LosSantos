using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Native;
using GTA.UI;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.Police;
using MidnightClub.Racing;
using MidnightClub.Rivals;
using MidnightClub.UI;
using Font = GTA.UI.Font;

namespace MidnightClub.Gameplay
{
    /// <summary>"New game" guidance: a short chain of starter objectives shown on the HUD, then long-term
    /// milestones (achievements) with rewards. Both listen to events, nothing polls the world.</summary>
    public sealed class Objectives
    {
        private sealed class Goal { public string Id, Text; public long Cash, Rep; }
        private sealed class Milestone { public string Id, Name, Desc; public long Cash, Rep; public Func<SaveData, bool> Done; }
        private readonly SaveManager saves;
        private readonly TransactionManager money;
        private int nextMilestoneCheck;

        private static readonly List<Goal> Chain = new List<Goal>
        {
            new Goal { Id = "garage", Text = "Drive into your garage (Davis Lockup, green garage blip)", Cash = 1000, Rep = 25 },
            new Goal { Id = "workshop", Text = "Buy any upgrade at an LS Customs workshop", Cash = 1500, Rep = 40 },
            new Goal { Id = "showroom", Text = "Browse a dealership showroom", Cash = 500, Rep = 25 },
            new Goal { Id = "race", Text = "Win a race (red race-car blips, or the phone's Races app)", Cash = 2500, Rep = 80 },
            new Goal { Id = "style", Text = "Score a near miss or a drift in traffic", Cash = 500, Rep = 30 },
            new Goal { Id = "meet", Text = "Show off your car at a car meet (night or weekend)", Cash = 1000, Rep = 50 },
            new Goal { Id = "police", Text = "Escape a police pursuit", Cash = 2000, Rep = 80 },
            new Goal { Id = "rival", Text = "Beat a rival in a duel (phone > Rivals)", Cash = 4000, Rep = 120 },
        };

        private static readonly List<Milestone> Milestones = new List<Milestone>
        {
            new Milestone { Id = "cars5", Name = "Collector", Desc = "Own 5 cars", Cash = 5000, Rep = 100, Done = d => d.Vehicles.Count >= 5 },
            new Milestone { Id = "cars15", Name = "Curator", Desc = "Own 15 cars", Cash = 25000, Rep = 300, Done = d => d.Vehicles.Count >= 15 },
            new Milestone { Id = "garages3", Name = "Property ladder", Desc = "Own 3 garages", Cash = 10000, Rep = 150, Done = d => d.Garages.Count >= 3 },
            new Milestone { Id = "wins10", Name = "Contender", Desc = "Win 10 races", Cash = 15000, Rep = 250, Done = d => d.RaceHistory.Count(h => h.Position == 1) >= 10 },
            new Milestone { Id = "wins50", Name = "Street legend", Desc = "Win 50 races", Cash = 75000, Rep = 1000, Done = d => d.RaceHistory.Count(h => h.Position == 1) >= 50 },
            new Milestone { Id = "speed250", Name = "Two-fifty", Desc = "Hit 250 km/h", Cash = 5000, Rep = 100, Done = d => d.BestSpeedKmh >= 250 },
            new Milestone { Id = "speed320", Name = "Terminal velocity", Desc = "Hit 320 km/h", Cash = 20000, Rep = 300, Done = d => d.BestSpeedKmh >= 320 },
            new Milestone { Id = "escapes10", Name = "Ghost", Desc = "Escape the police 10 times", Cash = 15000, Rep = 250, Done = d => d.PursuitEscapes >= 10 },
            new Milestone { Id = "style100k", Name = "Showboat", Desc = "Earn 100,000 style points", Cash = 10000, Rep = 200, Done = d => d.StylePointsTotal >= 100000 },
            new Milestone { Id = "km500", Name = "Road warrior", Desc = "Drive 500 km in your cars", Cash = 15000, Rep = 200, Done = d => d.Vehicles.Sum(v => v.Stats.DistanceKm) >= 500 },
            new Milestone { Id = "rivals", Name = "Undisputed", Desc = "Beat all six rivals", Cash = 100000, Rep = 2000, Done = d => d.Rivals.Count(r => r.Defeated) >= 6 },
            new Milestone { Id = "champ", Name = "Champion", Desc = "Win a championship", Cash = 20000, Rep = 400, Done = d => d.Championships.Any(c => c.Finished && c.Points.OrderByDescending(p => int.Parse(p.Value)).First().Key == "You") },
            new Milestone { Id = "elite", Name = "Midnight Club", Desc = "Reach Midnight Club Elite", Cash = 250000, Rep = 0, Done = d => d.CareerTierNotified >= 5 },
        };

        public Func<bool> stackOpen = () => false;
        public static string CurrentText;

        public Objectives(SaveManager saves, TransactionManager money)
        {
            this.saves = saves; this.money = money;
            EventBus.Subscribe<GarageEnteredEvent>(e => Complete("garage"));
            EventBus.Subscribe<WorkshopPurchaseEvent>(e => Complete("workshop"));
            EventBus.Subscribe<ShowroomOpenedEvent>(e => Complete("showroom"));
            EventBus.Subscribe<RaceFinishedEvent>(e => { if (e.Result.Position == 1 && e.Race.HasOpponents) Complete("race"); if (e.RivalId != null && e.Won) Complete("rival"); });
            EventBus.Subscribe<StyleEvent>(e => { if (e.Name == "NEAR MISS" || e.Name == "DRIFT") Complete("style"); });
            EventBus.Subscribe<MeetShowoffEvent>(e => Complete("meet"));
            EventBus.Subscribe<PursuitEndedEvent>(e => { if (e.Escaped) Complete("police"); });
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
        }

        private Goal Current { get { return Chain.FirstOrDefault(g => !saves.Data.ObjectivesDone.Contains(g.Id)); } }

        /// <summary>Goals can complete in any order; only the first open one is shown.</summary>
        private void Complete(string id)
        {
            if (saves.Data == null || saves.Data.ObjectivesDone.Contains(id)) return;
            var g = Chain.FirstOrDefault(x => x.Id == id); if (g == null) return;
            saves.Data.ObjectivesDone.Add(id);
            money.Earn(g.Cash, g.Rep, "objective", "Objective: " + g.Text, "obj:" + id);
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET", true);
            Notification.PostTicker("~g~Objective complete~s~: " + g.Text, true, false);
            saves.IsDirty = true;
        }

        private void Tick()
        {
            if (saves.Data == null) return;
            var g = Current;
            CurrentText = g == null ? null : g.Text;
            if (g != null && !RaceManager.IsRacing && !stackOpen() && !Function.Call<bool>(Hash.IS_HELP_MESSAGE_BEING_DISPLAYED))
            {
                // Top-centre pill: clear of help text (top-left), the feed (bottom-left) and the button bar (bottom-right).
                float w = 0.30f;
                UI.Theme.Rect(0.5f - w / 2, 0.012f, w, 0.036f, UI.Theme.Panel);
                UI.Theme.Rect(0.5f - w / 2, 0.012f, 0.003f, 0.036f, UI.Theme.Purple);
                UI.Theme.Text("OBJECTIVE", 0.5f - w / 2 + 0.01f, 0.019f, 0.24f, UI.Theme.Purple, Font.ChaletLondon);
                UI.Theme.Text(g.Text, 0.5f - w / 2 + 0.065f, 0.019f, 0.26f, Color.White, Font.ChaletLondon, Alignment.Left, w - 0.075f);
            }
            if (Game.GameTime < nextMilestoneCheck) return;
            nextMilestoneCheck = Game.GameTime + 5000;
            foreach (var m in Milestones)
            {
                if (saves.Data.MilestonesDone.Contains(m.Id)) continue;
                bool done; try { done = m.Done(saves.Data); } catch { done = false; }
                if (!done) continue;
                saves.Data.MilestonesDone.Add(m.Id);
                money.Earn(m.Cash, m.Rep, "milestone", "Milestone: " + m.Name, "ms:" + m.Id);
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "MEDAL_GOLD", "HUD_AWARDS", true);
                Screen.ShowSubtitle("~y~MILESTONE: " + m.Name.ToUpperInvariant() + "~s~~n~" + m.Desc + "   ~g~+$" + m.Cash.ToString("N0") + (m.Rep > 0 ? "  +" + m.Rep + " REP" : ""), 6000);
                saves.IsDirty = true;
            }
        }

        public Menu MilestonesMenu()
        {
            var m = new Menu("Milestones", saves.Data.MilestonesDone.Count + " / " + Milestones.Count);
            foreach (var g in Chain) { var x = g; m.Add(new MenuItem("Starter: " + x.Text) { Value = () => saves.Data.ObjectivesDone.Contains(x.Id) ? "~g~DONE" : "", Enabled = false }); }
            foreach (var ms in Milestones)
            {
                var x = ms;
                m.Add(new MenuItem(x.Name) { Value = () => saves.Data.MilestonesDone.Contains(x.Id) ? "~g~DONE" : "$" + x.Cash.ToString("N0"), Description = () => x.Desc + (x.Rep > 0 ? " (+" + x.Rep + " REP)" : "") });
            }
            return m;
        }
    }
}
