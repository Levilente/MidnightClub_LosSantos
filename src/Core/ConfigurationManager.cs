using System;
using System.IO;
using System.Windows.Forms;
using GTA;

namespace MidnightClub.Core
{
    /// <summary>Reads scripts\MidnightClub\config.ini (human-readable). Missing keys are written back with defaults.</summary>
    public sealed class Config
    {
        public Keys ToggleKey = Keys.F7;
        public Keys MenuKey = Keys.F6;
        public bool DevMode = false;
        public bool IncludeMotorcycles = true;
        public double PriceMultiplier = 1.0;
        public long MinVehiclePrice = 4000, MaxVehiclePrice = 3000000;
        public int StartingCash = 15000;
        public string StartingVehicle = "futo";
        public string StarterGarage = "davis_lockup";
        public string CharacterModel = "mp_m_freemode_01";
        public float SpawnX = -365.4f, SpawnY = -131.8f, SpawnZ = 37.9f;
        public bool BlockVehicleExit = true;
        public bool DisableStoryPolice = true;
        public bool PlayerInvincible = true;
        public bool ShowHud = true;
        public bool ShowWelcome = true;
        public bool LoadOnlineMap = true;
        public bool CustomCameras = true;
        public bool FitmentEnabled = true;
        public bool SimcadeEnabled = true; public float SimcadeIntensity = 1f, SimcadeSteerAssist = 1f, SimcadeStability = 1f, SimcadeWeight = 1f;
        public bool PoliceEnabled = true;
        public bool AlwaysNight = false;
        public float TrafficDensity = 1f, ParkedDensity = 1f;
        public int MaxEnthusiasts = 4;
        public bool StreetChallenges = true, CarMeets = true, SpeedTraps = true;
        public bool PhotoMode = true, StartInGarage = true;
        public float PedDensity = 1f, RaceTraffic = 0.8f;
        public bool DynamicWeather = true, SpeedEffects = true;
        public string UiTheme = "Auto";
        public bool RealDrivetrain = true, InversePower = true;
        public string RadioStation = "";
        public float PoliceSpeedKmh = 115f, PoliceDifficulty = 1f;
        public long TierRep2 = 1000, TierRep3 = 3500, TierRep4 = 9000, TierRep5 = 20000;
        public float CamberSign = 1f;
        public float CameraGForce = 1.0f, CameraFovScale = 1.0f, CameraShake = 1.0f, CockpitCamHeight = -0.03f, HoodCamHeight = 0f;
        public float HudX = 0.985f, HudY = 0.04f;
        public int AutoSaveSeconds = 120;
        public int BackupCount = 5;
        public float RecoveryEngineThreshold = 50f;
        public int RecoveryCost = 500;
        public int TransitionFadeMs = 800;

        public static string FilePath;
        public static Config Current;
        /// <summary>Writes one setting back to config.ini (used by the in-game Settings menu).</summary>
        public static void Save(string section, string key, object value)
        {
            try
            {
                var s = ScriptSettings.Load(FilePath);
                s.SetValue(section, key, value is float ? ((float)value).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : value.ToString());
                s.Save();
            }
            catch (Exception ex) { Log.Error("Saving setting " + section + "/" + key, ex); }
        }

        public static Config Load(string dir)
        {
            var file = Path.Combine(dir, "config.ini"); FilePath = file;
            var s = ScriptSettings.Load(file);
            var c = new Config(); Current = c;
            c.ToggleKey = Get(s, "General", "ToggleKey", c.ToggleKey);
            c.MenuKey = Get(s, "General", "MenuKey", c.MenuKey);
            c.DevMode = Get(s, "General", "DevMode", c.DevMode);
            c.IncludeMotorcycles = Get(s, "Vehicles", "IncludeMotorcycles", c.IncludeMotorcycles);
            c.PriceMultiplier = Get(s, "Economy", "PriceMultiplier", c.PriceMultiplier);
            c.MinVehiclePrice = Get(s, "Economy", "MinVehiclePrice", c.MinVehiclePrice);
            c.MaxVehiclePrice = Get(s, "Economy", "MaxVehiclePrice", c.MaxVehiclePrice);
            c.StartingCash = Get(s, "Career", "StartingCash", c.StartingCash);
            c.StartingVehicle = Get(s, "Career", "StartingVehicle", c.StartingVehicle);
            c.StarterGarage = Get(s, "Career", "StarterGarage", c.StarterGarage);
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
            c.ShowWelcome = Get(s, "HUD", "WelcomeScreen", c.ShowWelcome);
            UI.Studio.Brightness = Get(s, "HUD", "StudioLight", 1f);
            c.LoadOnlineMap = Get(s, "World", "LoadOnlineMap", c.LoadOnlineMap);
            c.CustomCameras = Get(s, "Camera", "Enabled", c.CustomCameras);
            c.FitmentEnabled = Get(s, "Fitment", "Enabled", c.FitmentEnabled);
            c.SimcadeEnabled = Get(s, "Simcade", "Enabled", c.SimcadeEnabled);
            c.SimcadeIntensity = Get(s, "Simcade", "Intensity", c.SimcadeIntensity);
            c.SimcadeSteerAssist = Get(s, "Simcade", "SteeringAssist", c.SimcadeSteerAssist);
            c.SimcadeStability = Get(s, "Simcade", "StabilityAssist", c.SimcadeStability);
            c.SimcadeWeight = Get(s, "Simcade", "Weight", c.SimcadeWeight);
            c.RealDrivetrain = Get(s, "Simcade", "RealDrivetrain", c.RealDrivetrain); c.InversePower = Get(s, "Simcade", "InversePower", c.InversePower);
            c.PoliceEnabled = Get(s, "Police", "Enabled", c.PoliceEnabled);
            c.AlwaysNight = Get(s, "World", "AlwaysNight", c.AlwaysNight);
            c.TrafficDensity = Get(s, "World", "TrafficDensity", c.TrafficDensity); c.ParkedDensity = Get(s, "World", "ParkedDensity", c.ParkedDensity);
            c.MaxEnthusiasts = Get(s, "World", "MaxEnthusiastCars", c.MaxEnthusiasts);
            c.StreetChallenges = Get(s, "World", "StreetChallenges", c.StreetChallenges); c.CarMeets = Get(s, "World", "CarMeets", c.CarMeets); c.SpeedTraps = Get(s, "World", "SpeedTraps", c.SpeedTraps);
            c.StartInGarage = Get(s, "Gameplay", "StartInGarage", c.StartInGarage);
            c.PedDensity = Get(s, "World", "PedDensity", c.PedDensity); c.DynamicWeather = Get(s, "World", "DynamicWeather", c.DynamicWeather); c.SpeedEffects = Get(s, "HUD", "SpeedEffects", c.SpeedEffects); c.UiTheme = s.GetValue("HUD", "Theme", c.UiTheme) ?? "Auto"; UI.Kit.ButtonStyle = Get(s, "HUD", "ButtonStyle", "Auto"); UI.Th.Mode = c.UiTheme == "Dark" ? UI.UiMode.Dark : c.UiTheme == "Light" ? UI.UiMode.Light : UI.UiMode.Auto; c.RadioStation = s.GetValue("World", "RadioStation", c.RadioStation) ?? ""; c.RaceTraffic = Get(s, "World", "RaceTraffic", c.RaceTraffic);
            c.PoliceSpeedKmh = Get(s, "Police", "SpeedTriggerKmh", c.PoliceSpeedKmh);
            c.PoliceDifficulty = Get(s, "Police", "Difficulty", c.PoliceDifficulty);
            c.TierRep2 = Get(s, "Career", "Tier2Rep", c.TierRep2); c.TierRep3 = Get(s, "Career", "Tier3Rep", c.TierRep3);
            c.TierRep4 = Get(s, "Career", "Tier4Rep", c.TierRep4); c.TierRep5 = Get(s, "Career", "Tier5Rep", c.TierRep5);
            c.CamberSign = Get(s, "Fitment", "CamberSign", c.CamberSign);
            c.CameraGForce = Get(s, "Camera", "GForceIntensity", c.CameraGForce);
            c.CameraFovScale = Get(s, "Camera", "DynamicFovIntensity", c.CameraFovScale);
            c.CameraShake = Get(s, "Camera", "ShakeIntensity", c.CameraShake);
            c.CockpitCamHeight = Get(s, "Camera", "CockpitHeight", c.CockpitCamHeight);
            c.HoodCamHeight = Get(s, "Camera", "HoodHeight", c.HoodCamHeight);
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
