using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Native;
using GTA.UI;
using MidnightClub.Career;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Garages;
using MidnightClub.Persistence;
using MidnightClub.Racing;
using MidnightClub.UI;
using MidnightClub.Vehicles;

namespace MidnightClub.Rivals
{
    public sealed class RivalProfile
    {
        public string Id, Name, Archetype, Bio;
        public Func<VehicleInfo, bool> Likes;      // preferred cars
        public RaceType Style;                     // the race type they challenge you to
        public int UnlockTier, BaseCarTier;        // career tier to appear; car tier at stage 0 (1=D/C .. 5=X)
    }

    /// <summary>Six recurring rivals with their own style, car and three escalating challenges each.
    /// Beat all three and they hand over their car.</summary>
    public sealed class RivalManager
    {
        public const int Stages = 3;
        private readonly SaveManager saves;
        private readonly TransactionManager money;
        private readonly VehicleRegistry registry;
        private readonly RaceManager races;
        private readonly GarageManager garages;
        private readonly MenuStack stack;

        public static readonly List<RivalProfile> Profiles = new List<RivalProfile>
        {
            new RivalProfile { Id = "kenji", Name = "Kenji Mori", Archetype = "Tuner specialist", Bio = "Builds every car himself. Hates losing to a stock engine.", Likes = v => v.Tuner || v.Class == VehicleClass.Sports, Style = RaceType.Sprint, UnlockTier = 1, BaseCarTier = 2 },
            new RivalProfile { Id = "blackout", Name = "Dee 'Blackout' Ramos", Archetype = "Muscle car specialist", Bio = "V8s, straight lines, no apologies.", Likes = v => v.Class == VehicleClass.Muscle, Style = RaceType.Sprint, UnlockTier = 1, BaseCarTier = 2 },
            new RivalProfile { Id = "lexi", Name = "Lexi Cruz", Archetype = "Circuit specialist", Bio = "Lap after lap, she never misses an apex.", Likes = v => v.Class == VehicleClass.Sports || v.Class == VehicleClass.SportsClassics, Style = RaceType.Circuit, UnlockTier = 2, BaseCarTier = 3 },
            new RivalProfile { Id = "nikki", Name = "Nikki Sato", Archetype = "Drift specialist", Bio = "Scores before style? Not in her book.", Likes = v => v.Drift || v.Tuner, Style = RaceType.Drift, UnlockTier = 2, BaseCarTier = 3 },
            new RivalProfile { Id = "vance", Name = "Vance Holloway", Archetype = "Highway racer", Bio = "Lives at 300 km/h on the freeway at 3 a.m.", Likes = v => v.Class == VehicleClass.Sports || v.Class == VehicleClass.Super, Style = RaceType.Sprint, UnlockTier = 3, BaseCarTier = 4 },
            new RivalProfile { Id = "volkov", Name = "Dmitri Volkov", Archetype = "Supercar specialist", Bio = "Money buys speed. He has plenty of both.", Likes = v => v.Class == VehicleClass.Super, Style = RaceType.Circuit, UnlockTier = 4, BaseCarTier = 5 },
        };

        public RivalManager(SaveManager saves, TransactionManager money, VehicleRegistry registry, RaceManager races, GarageManager garages, MenuStack stack)
        {
            this.saves = saves; this.money = money; this.registry = registry; this.races = races; this.garages = garages; this.stack = stack;
            EventBus.Subscribe<RaceFinishedEvent>(OnRaceFinished);
            EventBus.Subscribe<MidnightTickEvent>(e => PinkTick());
            EventBus.Subscribe<TierUpEvent>(e =>
            {
                foreach (var p in Profiles.Where(p => p.UnlockTier == e.Tier))
                    Notification.PostTicker("~p~" + p.Name + "~s~ (" + p.Archetype + ") has heard about you. F6 > Rivals.", true, false);
            });
        }

        public RivalState State(string id)
        {
            var s = saves.Data.Rivals.FirstOrDefault(x => x.Id == id);
            if (s == null) { s = new RivalState { Id = id }; saves.Data.Rivals.Add(s); }
            return s;
        }

        private int Tier { get { return CareerManager.Instance != null ? CareerManager.Instance.Tier : 1; } }

        /// <summary>The rival's car for a stage: deterministic per rival/stage, matching their taste and tier.</summary>
        public VehicleInfo CarFor(RivalProfile p, int stage)
        {
            string[] tiers = { "D", "C", "B", "A", "S", "X" };
            int ti = Math.Min(5, p.BaseCarTier - 1 + stage);
            var pool = registry.All.Where(v => v.Enabled && p.Likes(v) && v.Class != VehicleClass.Motorcycles && Array.IndexOf(tiers, v.Tier) == ti).OrderBy(v => v.Hash).ToList();
            if (pool.Count == 0) pool = registry.All.Where(v => v.Enabled && p.Likes(v) && v.Class != VehicleClass.Motorcycles).OrderByDescending(v => v.Performance).ToList();
            if (pool.Count == 0) return null;
            return pool[Dealerships.RotationMath.Seed(p.Id, stage) % pool.Count];
        }

        private RaceDefinition RaceFor(RivalProfile p, int stage)
        {
            var list = races.Races.Where(r => r.Kind == p.Style && r.RequiredRep <= saves.Data.Rep).ToList();
            if (list.Count == 0) list = races.Races.Where(r => (r.Kind == RaceType.Sprint || r.Kind == RaceType.Circuit) && r.RequiredRep <= saves.Data.Rep).ToList();
            if (list.Count == 0) return null;
            return list[Dealerships.RotationMath.Seed(p.Id, "race", stage) % list.Count];
        }

        // ------------------------------------------------------------------ menus
        public Menu RivalsMenu()
        {
            var m = new Menu("Rivals", "Career tier " + Tier);
            m.OnOpen = () =>
            {
                m.Items.Clear();
                foreach (var pr in Profiles)
                {
                    var p = pr; var st = State(p.Id); bool unlocked = Tier >= p.UnlockTier;
                    m.Add(new MenuItem(unlocked ? p.Name : "??? (" + p.Archetype + ")", unlocked ? (Action)(() => stack.Push(RivalMenu(p))) : null)
                    {
                        Enabled = unlocked,
                        Value = () => !unlocked ? "Tier " + p.UnlockTier : st.Defeated ? "~g~BEATEN" : "Stage " + (st.Stage + 1) + "/" + Stages,
                        Description = () => p.Archetype + ". " + (unlocked ? p.Bio : "Reach career tier " + p.UnlockTier + ".")
                    });
                }
            };
            return m;
        }

        private Menu RivalMenu(RivalProfile p)
        {
            var m = new Menu(p.Name, p.Archetype);
            m.OnOpen = () =>
            {
                m.Items.Clear();
                var st = State(p.Id);
                m.Add(new MenuItem("Record vs you") { Value = () => st.Wins + " W / " + st.Losses + " L", Enabled = false });
                if (st.Defeated) { m.Add(new MenuItem("Beaten. Respect earned.") { Enabled = false }); PinkSlipItems(m, p); return; }
                var car = CarFor(p, st.Stage); var race = RaceFor(p, st.Stage);
                m.Add(new MenuItem("Their car") { Value = () => car == null ? "?" : car.Name + " (" + car.Tier + ")", Enabled = false });
                if (race == null) { m.Add(new MenuItem("No suitable race unlocked yet") { Enabled = false }); return; }
                m.Add(new MenuItem("Challenge: " + race.Name, () => { stack.Close(); Challenge(p, race, car, st.Stage); })
                {
                    Value = () => TypeName(p.Style),
                    Description = () => "Stage " + (st.Stage + 1) + "/" + Stages + ". You'll be taken to the start line. Prize: $" + Prize(st.Stage).ToString("N0") + " + " + RepPrize(st.Stage) + " REP" + (st.Stage == Stages - 1 ? " and their car." : ".")
                });
            };
            return m;
        }

        private static string TypeName(RaceType t) { return t == RaceType.Drift ? "Drift duel" : t == RaceType.Circuit ? "Circuit duel" : "Sprint duel"; }
        private long Prize(int stage) { return 4000L * (stage + 1) * Math.Max(1, Tier); }
        private long RepPrize(int stage) { return 150L * (stage + 1) + 50L * Tier; }

        private void Challenge(RivalProfile p, RaceDefinition race, VehicleInfo car, int stage)
        {
            // Teleport to the start line (behind a fade) so the duel starts immediately.
            var my = Game.Player.Character.CurrentVehicle;
            if (my == null || !my.Exists()) return;
            GTA.UI.Screen.FadeOut(400); Script.Wait(450);
            my.Position = race.Start; my.PlaceOnGround();
            Script.Wait(300);
            GTA.UI.Screen.FadeIn(300);
            var spec = new RivalSpec { Id = p.Id, Name = p.Name, Car = car, Skill = 1.0f + 0.06f * stage + 0.03f * p.BaseCarTier };
            if (p.Style == RaceType.Drift) spec.DriftTarget = 9000f * (1 + stage * 0.35f);
            Notification.PostTicker("~p~" + p.Name + "~s~: " + Taunt(p, stage), true, false);
            races.StartRivalRace(race, spec);
        }

        private static string Taunt(RivalProfile p, int stage)
        {
            string[] t = { "Let's see what you've got.", "Lucky last time. Not tonight.", "This is the last time you'll embarrass me." };
            return t[Math.Min(stage, t.Length - 1)];
        }

        // ------------------------------------------------------------------ pink slips
        private string pinkRival, pinkWager; private VehicleInfo pinkCar; private bool pinkLost; private int pinkLostAt;

        private VehicleInfo PinkCar(RivalProfile p) { return CarFor(p, Stages + Wins(p) % 2); }
        private int Wins(RivalProfile p) { return State(p.Id).Wins; }

        private void PinkSlipItems(Menu m, RivalProfile p)
        {
            var their = PinkCar(p); var mine = Vehicles.VehicleManager.Instance != null ? Vehicles.VehicleManager.Instance.CurrentRecord : null;
            var race = RaceFor(p, Wins(p));
            if (their == null || race == null) return;
            var myInfo = mine == null ? null : registry.Get(mine.ModelHash);
            bool ok = mine != null && saves.Data.Vehicles.Count >= 2;
            m.Add(new MenuItem("PINK SLIP RACE", ok ? (Action)(() => stack.Push(PinkConfirm(p, race, their, mine))) : null)
            {
                Enabled = ok,
                Value = () => "Win their " + their.DisplayName,
                Description = () => !ok ? "You need at least two cars and to be driving one of your own. Put your keys on the line against theirs." : "Your " + (mine.Nickname ?? (myInfo != null ? myInfo.Name : mine.Model)) + " (" + (myInfo != null ? "$" + myInfo.BasePrice.ToString("N0") : "?") + ") against their " + their.Name + " ($" + their.BasePrice.ToString("N0") + "). Winner drives home in both."
            });
        }

        private Menu PinkConfirm(RivalProfile p, RaceDefinition race, VehicleInfo their, OwnedVehicle mine)
        {
            var m = new Menu("Pink slips", p.Name + " · " + TypeName(p.Style));
            m.Add(new MenuItem("You put up") { Value = () => mine.Nickname ?? mine.Model, Enabled = false });
            m.Add(new MenuItem("They put up") { Value = () => their.Name + " (" + their.Tier + ")", Enabled = false });
            m.Add(new MenuItem("Race") { Value = () => race.Name, Enabled = false });
            m.Add(new MenuItem("SHAKE ON IT", () =>
            {
                stack.Close();
                pinkRival = p.Id; pinkWager = mine.Id; pinkCar = their;
                Shard.Big("PINK SLIPS", "Your " + (mine.Nickname ?? mine.Model) + " vs their " + their.DisplayName + ". Winner takes the keys.", Shard.Red, 3500);
                Script.Wait(200);
                ChallengePink(p, race, their);
            }) { Description = () => "Lose or quit and your car is gone for good." });
            m.Add(new MenuItem("Walk away", () => stack.Pop()));
            return m;
        }

        private void ChallengePink(RivalProfile p, RaceDefinition race, VehicleInfo car)
        {
            var my = Game.Player.Character.CurrentVehicle;
            if (my == null || !my.Exists()) { pinkRival = null; return; }
            GTA.UI.Screen.FadeOut(400); Script.Wait(450);
            my.Position = race.Start; my.PlaceOnGround();
            Script.Wait(300);
            GTA.UI.Screen.FadeIn(300);
            var spec = new RivalSpec { Id = p.Id, Name = p.Name, Car = car, Skill = 1.12f + 0.03f * p.BaseCarTier, PinkSlip = true };
            if (p.Style == RaceType.Drift) spec.DriftTarget = 14000f;
            races.StartRivalRace(race, spec);
            if (!RaceManager.IsRacing) pinkRival = null;
        }

        private void OnPinkFinished(RivalProfile p, RaceFinishedEvent e)
        {
            var st = State(p.Id);
            if (e.Won)
            {
                st.Wins++;
                var rec = VehicleManager.CreateRecord(pinkCar, 0);
                rec.Nickname = "Pink slip " + pinkCar.DisplayName;
                rec.Mods.AddRange(new[] { new ModEntry(11, 3), new ModEntry(12, 2), new ModEntry(13, 2), new ModEntry(15, 3) });
                rec.Toggles.Add(18); rec.EcuLevel = 1;
                if (garages.AssignToFreeGarage(rec)) { saves.Data.Vehicles.Add(rec); Shard.Big("PINK SLIP WON", p.Name + "'s " + pinkCar.Name + " is yours. It's waiting in " + garages.GarageName(rec.GarageId) + ".", Shard.Purple, 6000); }
                else { money.Earn(pinkCar.BasePrice, 0, "rival", "Pink slip " + pinkCar.Name + " (no garage space, sold)", "pink:" + p.Id + ":" + st.Wins); Shard.Big("PINK SLIP WON", "No garage space, so " + pinkCar.DisplayName + " was sold for $" + pinkCar.BasePrice.ToString("N0") + ".", Shard.Purple, 6000); }
                if (Career.Progression.Instance != null) Career.Progression.Instance.Award(1500, "Pink slip win");
                pinkRival = null;
            }
            else { st.Losses++; pinkLost = true; pinkLostAt = Game.GameTime + 500; }
            saves.Save();
        }

        /// <summary>After a lost pink slip (once the results screen is gone): hand over the car and put the player in another one.</summary>
        private void PinkTick()
        {
            if (!pinkLost || RaceManager.IsRacing || Game.GameTime < pinkLostAt) return;
            pinkLost = false;
            var p = Profiles.FirstOrDefault(x => x.Id == pinkRival); pinkRival = null;
            var vm = Vehicles.VehicleManager.Instance; var rec = saves.Data.Vehicles.FirstOrDefault(v => v.Id == pinkWager);
            if (rec == null || vm == null) return;
            var ped = Game.Player.Character; var pos = ped.Position; float h = ped.Heading;
            var next = saves.Data.Vehicles.Where(v => v.Id != rec.Id).OrderByDescending(v => v.Stats.LastUsedUtc ?? "").FirstOrDefault();
            GTA.UI.Screen.FadeOut(400); Script.Wait(450);
            try
            {
                if (vm.CurrentRecord == rec) vm.Despawn(false);
                saves.Data.Vehicles.Remove(rec);
                if (next != null)
                {
                    var car = vm.Spawn(next, pos + ped.RightVector * 3.5f, h);
                    if (car != null && car.Exists()) ped.SetIntoVehicle(car, VehicleSeat.Driver);
                }
            }
            catch (Exception ex) { Log.Error("Pink slip handover", ex); }
            saves.Save();
            GTA.UI.Screen.FadeIn(400);
            Shard.Big("PINK SLIP LOST", (p != null ? p.Name : "Your rival") + " drives off in your " + (rec.Nickname ?? rec.Model) + ".", Shard.Red, 6000);
        }

        private void OnRaceFinished(RaceFinishedEvent e)
        {
            if (e.RivalId == null) return;
            if (pinkRival != null && e.RivalId == pinkRival) { var pp = Profiles.FirstOrDefault(x => x.Id == pinkRival); if (pp != null) { OnPinkFinished(pp, e); return; } }
            var p = Profiles.FirstOrDefault(x => x.Id == e.RivalId); if (p == null) return;
            var st = State(p.Id);
            if (!e.Won) { st.Losses++; saves.IsDirty = true; Notification.PostTicker("~p~" + p.Name + "~s~: Better luck next time.", true, false); return; }
            st.Wins++;
            money.Earn(Prize(st.Stage), RepPrize(st.Stage), "rival", "Beat " + p.Name + " (stage " + (st.Stage + 1) + ")", "rival:" + p.Id + ":" + st.Stage);
            st.Stage++;
            if (st.Stage >= Stages)
            {
                st.Defeated = true;
                var car = CarFor(p, Stages - 1);
                if (car != null)
                {
                    var rec = VehicleManager.CreateRecord(car, 0);
                    rec.Nickname = p.Name.Split(' ')[0] + "'s " + car.DisplayName;
                    rec.Mods.AddRange(new[] { new ModEntry(11, 3), new ModEntry(12, 2), new ModEntry(13, 2), new ModEntry(15, 2) });
                    rec.Toggles.Add(18);
                    if (garages.AssignToFreeGarage(rec)) { saves.Data.Vehicles.Add(rec); Notification.PostTicker("~p~" + p.Name + "~s~ handed over the keys: ~g~" + rec.Nickname + "~s~ is in " + garages.GarageName(rec.GarageId) + ".", true, false); }
                    else { money.Earn(car.BasePrice / 2, 0, "rival", p.Name + "'s car (no garage space, sold)", "rival-car:" + p.Id); Notification.PostTicker("No garage space for " + p.Name + "'s car, so it was sold for you.", true, false); }
                }
                EventBus.Publish(new RivalDefeatedEvent(p.Id));
            }
            saves.Save();
        }
    }

    public sealed class RivalDefeatedEvent { public string Id; public RivalDefeatedEvent(string id) { Id = id; } }
}
