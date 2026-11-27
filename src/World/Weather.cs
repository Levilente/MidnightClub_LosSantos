using System;
using GTA;
using GTA.Native;
using MidnightClub.Core;

namespace MidnightClub.WorldSim
{
    /// <summary>Dynamic weather (Forza Horizon-style): the sky changes every few minutes with smooth transitions,
    /// and wet roads cost grip. Roads get wet while it rains and dry out slowly afterwards; the grip loss feeds the
    /// simcade handling layer.</summary>
    public sealed class Weather
    {
        private static readonly string[] Types = { "EXTRASUNNY", "CLEAR", "CLOUDS", "OVERCAST", "RAIN", "THUNDER", "CLEARING", "FOGGY", "SMOG" };
        private static readonly string[] Names = { "Sunny", "Clear", "Cloudy", "Overcast", "Rain", "Thunderstorm", "Clearing", "Fog", "Smog" };
        // rough Markov chain: weights for the next state
        private static readonly int[][] Next =
        {
            new[] { 30, 40, 25, 0, 0, 0, 0, 0, 5 }, new[] { 25, 35, 30, 5, 0, 0, 0, 3, 2 }, new[] { 5, 25, 30, 25, 10, 0, 0, 5, 0 },
            new[] { 0, 5, 25, 25, 35, 10, 0, 0, 0 }, new[] { 0, 0, 0, 10, 35, 20, 35, 0, 0 }, new[] { 0, 0, 0, 0, 50, 20, 30, 0, 0 },
            new[] { 10, 40, 40, 10, 0, 0, 0, 0, 0 }, new[] { 10, 40, 40, 10, 0, 0, 0, 0, 0 }, new[] { 30, 50, 20, 0, 0, 0, 0, 0, 0 },
        };
        public static Weather Instance; public static float Wetness { get; private set; }
        /// <summary>Grip multiplier for the handling layer (1 = dry).</summary>
        public static float GripMult { get { return 1f - 0.16f * Wetness; } }
        private readonly Config cfg; private readonly Random rnd = new Random();
        private int cur = 1, nextChange;

        public Weather(Config cfg)
        {
            this.cfg = cfg; Instance = this;
            EventBus.Subscribe<MidnightTickEvent>(e => { try { Tick(); } catch (Exception ex) { Log.Error("Weather", ex); } });
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To == GameMode.MIDNIGHT_CLUB) nextChange = Game.GameTime + 20000; else Wetness = 0f; });
        }

        public string Current { get { return Names[cur]; } }

        private void Tick()
        {
            float rain = Function.Call<float>(Hash.GET_RAIN_LEVEL);
            Wetness = rain > 0.05f ? Math.Min(1f, Wetness + Game.LastFrameTime / 40f * (0.5f + rain)) : Math.Max(0f, Wetness - Game.LastFrameTime / 150f);
            if (!cfg.DynamicWeather || Racing.RaceManager.IsRacing) return;
            if (Game.GameTime < nextChange) return;
            nextChange = Game.GameTime + 360000 + rnd.Next(240000);
            int total = 0; foreach (int w in Next[cur]) total += w;
            int r = rnd.Next(total), n = 0;
            for (int i = 0; i < Types.Length; i++) { r -= Next[cur][i]; if (r < 0) { n = i; break; } }
            if (n == cur) return;
            cur = n;
            Function.Call(Hash.SET_WEATHER_TYPE_OVERTIME_PERSIST, Types[n], 45f);
            if (n == 4 || n == 5) UI.Toast.Post("~b~Weather~s~: " + Names[n].ToLowerInvariant() + " moving in. Wet roads mean less grip.");
            else if (n == 6) UI.Toast.Post("~b~Weather~s~: clearing up. The roads will dry out soon.");
            else if (n == 7) UI.Toast.Post("~b~Weather~s~: fog rolling in.");
        }
    }
}
