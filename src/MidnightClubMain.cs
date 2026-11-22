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
        private bool togglePressed;
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
                modes = new GameModeManager(cfg, saves, new CharacterManager(), vehicles);
                hud = new MainHUD(cfg.HudX, cfg.HudY);
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

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (initialized && e.KeyCode == cfg.ToggleKey) togglePressed = true;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!initialized) return;
            try
            {
                if (togglePressed) { togglePressed = false; modes.Toggle(); }
                modes.Tick();
                if (modes.InMidnightClub && cfg.ShowHud) hud.Draw(saves.Data, vehicles);
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
            modes.EmergencyShutdown();
            EventBus.Clear();
        }
    }
}
