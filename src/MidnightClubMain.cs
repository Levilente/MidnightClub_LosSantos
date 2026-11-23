using System;
using System.IO;
using System.Windows.Forms;
using GTA;
using MidnightClub.Character;
using MidnightClub.Core;
using MidnightClub.Persistence;
using MidnightClub.UI;
using MidnightClub.Vehicles;

namespace MidnightClub
{
    /// <summary>Single SHVDN entry point. Subsystems are plain classes wired together here.</summary>
    [ScriptAttributes(Author = "Midnight Club: Los Santos", SupportURL = "")]
    public sealed class MidnightClubMain : Script
    {
        private readonly Config cfg;
        private readonly SaveManager saves;
        private readonly GameModeManager modes;
        private readonly VehicleManager vehicles;
        private readonly MainHUD hud;
        private readonly MidnightMenu menu;
        private readonly Phone.PhoneManager phone;
        private bool togglePressed, menuPressed, viewHoldFired;
        private int viewHoldStart = -1;
        private readonly bool initialized;

        public MidnightClubMain()
        {
            try
            {
                var dir = Path.Combine(BaseDirectory, "MidnightClub");
                Log.Init(dir);
                Log.Info("Game file version " + Game.FileVersion + ", Enhanced=" + Game.isEnhanced);
                cfg = Config.Load(dir);
                saves = new SaveManager(dir, cfg.BackupCount);
                vehicles = new VehicleManager();
                var registry = new VehicleRegistry();
                modes = new GameModeManager(cfg, saves, new CharacterManager(), vehicles, registry, Path.Combine(dir, "data"));
                Directory.CreateDirectory(Path.Combine(dir, "data"));
                var stack = new MenuStack();
                Racing.RoadGraph.PreloadAsync();
                new Explore.Discovery(saves);
                UI.BlipRegistry.Init();
                var money = new Economy.TransactionManager(saves);
                var garages = new Garages.GarageManager(saves, money, registry, vehicles, modes, stack, Path.Combine(dir, "data"));
                modes.Garages = garages; modes.Money = money;
                garages.StarterGarageId = cfg.StarterGarage;
                var calendar = new WorldSim.WorldCalendar(saves);
                new Cameras.CameraSystem(saves, vehicles, cfg);
                new Workshops.WorkshopManager(saves, money, registry, vehicles, stack);
                new FitmentManager(vehicles, cfg);
                new WorldSim.GasStations(vehicles, saves);
                new Simcade(vehicles, registry, cfg, Path.Combine(dir, "data"));
                new WheelLab(vehicles, cfg);
                var dealers = new Dealerships.DealershipManager(saves, money, registry, vehicles, garages, calendar, stack, Path.Combine(dir, "data"));
                var races = new Racing.RaceManager(saves, money, registry, vehicles, calendar, stack, Path.Combine(dir, "data"));
                new Career.CareerManager(saves, cfg);
                new Career.Progression(saves, money, garages, calendar);
                var rivals = new Rivals.RivalManager(saves, money, registry, races, garages, stack);
                var clubs = new Factions.ClubManager(saves, registry, vehicles);
                new Police.PursuitManager(saves, money, vehicles, cfg);
                new Explore.Turf(saves, money);
                new Career.Mastery(saves, money, vehicles);
                new WorldSim.WorldSimulationManager(saves, money, registry, vehicles, calendar, races, stack, cfg);
                menu = new MidnightMenu(stack, modes, saves, registry, vehicles, cfg) { Garages = garages, Dealers = dealers, Races = races, RivalMgr = rivals, ClubMgr = clubs };
                phone = new Phone.PhoneManager(saves, stack, menu, calendar, races, rivals.RivalsMenu, clubs.ClubsMenu);
                new Gameplay.StyleSystem(saves, money, vehicles);
                var abilities = new Gameplay.Abilities(saves, vehicles);
                new Gameplay.Rewind(vehicles, cfg) { Blocked = () => stack.IsOpen || phone.IsVisible || Gameplay.PhotoMode.Running || (Racing.RaceSession.Active != null && (Racing.RaceSession.Active.Player.Finished || !Racing.RaceSession.Active.Running)) };
                var objectives = new Gameplay.Objectives(saves, money) { stackOpen = () => stack.IsOpen || phone.IsOpen };
                phone.MilestonesMenu = objectives.MilestonesMenu; phone.AbilityMenu = abilities.AbilityMenu;
                EventBus.Subscribe<ModeChangedEvent>(e => { menu.Close(); if (e.To == GameMode.MIDNIGHT_CLUB && cfg.ShowWelcome) welcomePending = Game.GameTime + 1500; });
                welcomeCalendar = calendar;
                hud = new MainHUD(cfg.HudX, cfg.HudY);
                // Launcher: "Play Midnight Club" writes autostart.flag -> boot straight into Midnight Club
                try
                {
                    var flag = Path.Combine(dir, "autostart.flag");
                    if (File.Exists(flag))
                    {
                        bool fresh = (DateTime.UtcNow - File.GetLastWriteTimeUtc(flag)).TotalMinutes < 20;
                        File.Delete(flag);
                        if (fresh) { autostart = true; autostartSince = -1; Log.Info("Autostart requested by the launcher"); }
                    }
                }
                catch (Exception ex) { Log.Error("Autostart flag", ex); }
                Interval = 0;
                Tick += OnTick;
                KeyDown += OnKeyDown;
                Aborted += OnAborted;
                initialized = true;
            }
            catch (Exception ex)
            {
                Log.Error("Initialization failed", ex);
            }
        }

        private bool autostart; private int autostartSince = -1;

        /// <summary>Keeps Story Mode hidden (black screen + title) until the game is ready, then enters Midnight Club.</summary>
        private void AutostartTick()
        {
            GTA.UI.Screen.FadeOut(0);
            GTA.Native.Function.Call(GTA.Native.Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
            GTA.Native.Function.Call(GTA.Native.Hash.THEFEED_HIDE_THIS_FRAME);
            UI.Ds.Rect(0f, 0f, 1f, 1f, UI.Ds.Bg);
            UI.Ds.Logo(0.5f, 0.4f, 2.2f, 1f, GTA.UI.Alignment.Center, true);
            UI.Ds.T("STARTING", 0.5f, 0.6f, 0.3f, UI.Ds.Muted, UI.Ds.Display, GTA.UI.Alignment.Center);
            var p = Game.Player.Character;
            bool ready = p != null && p.Exists() && !p.IsDead && !GTA.Native.Function.Call<bool>(GTA.Native.Hash.GET_IS_LOADING_SCREEN_ACTIVE)
                         && !GTA.Native.Function.Call<bool>(GTA.Native.Hash.IS_PLAYER_SWITCH_IN_PROGRESS) && !Game.IsCutsceneActive;
            if (!ready) { autostartSince = -1; return; }
            if (autostartSince < 0) autostartSince = Game.GameTime;
            if (Game.GameTime - autostartSince < 1500) return;
            autostart = false;
            Log.Info("Autostart: entering Midnight Club");
            modes.Enter();
            if (!modes.InMidnightClub) GTA.UI.Screen.FadeIn(500);
        }

        private int welcomePending;
        private WorldSim.WorldCalendar welcomeCalendar;

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!initialized) return;
            if (e.KeyCode == cfg.ToggleKey) togglePressed = true;
            else if (e.KeyCode == cfg.MenuKey) menuPressed = true;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!initialized) return;
            try
            {
                if (autostart) { AutostartTick(); return; }
                if (togglePressed) { togglePressed = false; modes.Toggle(); }
                if (menuPressed) { menuPressed = false; if (modes.InMidnightClub) phone.Toggle(); }
                // Controller: hold Back/Select (Change View) for 1.5 s to enter/leave Midnight Club.
                if (Game.LastInputMethod == InputMethod.GamePad && Game.IsControlPressed(GTA.Control.NextCamera))
                {
                    if (viewHoldStart < 0) viewHoldStart = Game.GameTime;
                    else if (!viewHoldFired && Game.GameTime - viewHoldStart > 1500) { viewHoldFired = true; modes.Toggle(); }
                }
                else { viewHoldStart = -1; viewHoldFired = false; }
                modes.Tick();
                if (welcomePending > 0 && modes.InMidnightClub && Game.GameTime > welcomePending && !menu.IsOpen && !phone.IsOpen && !Racing.RaceManager.IsRacing && Game.Player.Character.IsInVehicle())
                {
                    welcomePending = 0;
                    var c = WelcomeScreen.Show(saves.Data, welcomeCalendar.DayName);
                    if (c == WelcomeScreen.Choice.Menu) menu.Toggle();
                    else if (c == WelcomeScreen.Choice.Phone) phone.Toggle();
                    else if (c == WelcomeScreen.Choice.Story) phone.OpenExit();
                }
                else if (!modes.InMidnightClub) welcomePending = 0;
                if (modes.InMidnightClub) menu.Tick();
                if (modes.InMidnightClub) Shard.Tick();
                if (modes.InMidnightClub && cfg.ShowHud && !phone.IsVisible && !menu.IsOpen) hud.Draw(saves.Data, vehicles);
            }
            catch (Exception ex)
            {
                Log.Error("Tick failed", ex);
                modes.EmergencyShutdown();
            }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            if (!initialized) return;
            Log.Info("Script aborted (reload or game exit)");
            Phone.PhoneManager.ResetEffects();
            modes.EmergencyShutdown();
            EventBus.Clear();
        }
    }
}
