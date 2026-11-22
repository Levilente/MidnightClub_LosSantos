using System.IO;
using System.Windows.Forms;
using GTA;

namespace MidnightClub.Core
{
    /// <summary>Reads scripts\MidnightClub\config.ini (human-readable). Missing keys are written back with defaults.</summary>
    public sealed class Config
    {
        public Keys ToggleKey = Keys.F7;
        public int StartingCash = 15000;
        public string StartingVehicle = "futo";
        public string CharacterModel = "mp_m_freemode_01";
        public float SpawnX = -365.4f, SpawnY = -131.8f, SpawnZ = 37.9f;
        public bool BlockVehicleExit = true;
        public bool DisableStoryPolice = true;
        public bool PlayerInvincible = true;
        public bool ShowHud = true;
        public float HudX = 0.985f, HudY = 0.04f;
        public int AutoSaveSeconds = 120;
        public int BackupCount = 5;
        public float RecoveryEngineThreshold = 50f;
        public int RecoveryCost = 500;
        public int TransitionFadeMs = 800;

        public static Config Load(string dir)
        {
            var file = Path.Combine(dir, "config.ini");
            var s = ScriptSettings.Load(file);
            var c = new Config();
            c.ToggleKey = Get(s, "General", "ToggleKey", c.ToggleKey);
            c.StartingCash = Get(s, "Career", "StartingCash", c.StartingCash);
            c.StartingVehicle = Get(s, "Career", "StartingVehicle", c.StartingVehicle);
            c.CharacterModel = Get(s, "Character", "Model", c.CharacterModel);
            c.SpawnX = Get(s, "Character", "SpawnX", c.SpawnX);
            c.SpawnY = Get(s, "Character", "SpawnY", c.SpawnY);
            c.SpawnZ = Get(s, "Character", "SpawnZ", c.SpawnZ);
            c.BlockVehicleExit = Get(s, "Gameplay", "BlockVehicleExit", c.BlockVehicleExit);
            c.DisableStoryPolice = Get(s, "Gameplay", "DisableStoryPolice", c.DisableStoryPolice);
            c.PlayerInvincible = Get(s, "Gameplay", "PlayerInvincible", c.PlayerInvincible);
            c.RecoveryEngineThreshold = Get(s, "Gameplay", "RecoveryEngineThreshold", c.RecoveryEngineThreshold);
            c.RecoveryCost = Get(s, "Gameplay", "RecoveryCost", c.RecoveryCost);
            c.TransitionFadeMs = Get(s, "Gameplay", "TransitionFadeMs", c.TransitionFadeMs);
            c.ShowHud = Get(s, "HUD", "Show", c.ShowHud);
            c.HudX = Get(s, "HUD", "X", c.HudX);
            c.HudY = Get(s, "HUD", "Y", c.HudY);
            c.AutoSaveSeconds = Get(s, "Save", "AutoSaveSeconds", c.AutoSaveSeconds);
            c.BackupCount = Get(s, "Save", "BackupCount", c.BackupCount);
            try { s.Save(); } catch { }
            return c;
        }

        private static T Get<T>(ScriptSettings s, string section, string key, T def)
        {
            if (!s.ContainsKey(section, key)) { s.SetValue(section, key, def); return def; }
            return s.GetValue(section, key, def);
        }
    }
}
