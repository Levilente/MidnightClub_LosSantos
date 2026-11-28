using System;
using System.Drawing;
using GTA;
using GTA.Native;
using GTA.UI;
using Font = GTA.UI.Font;

namespace MidnightClub.UI
{
    /// <summary>Title card shown while Midnight Club loads (drawn over the fade).</summary>
    public static class Splash
    {
        private static readonly string[] Tips =
        {
            "Near misses, drafting, air and drifts build your style meter. A full meter powers your special ability.",
            "Police remember your car. A new plate or a respray at LS Customs cools it down.",
            "Car meets run at night and all weekend. Show off your build for REP.",
            "Stopped at a light? Someone might want to race. Press E when they rev.",
            "Every car in the game comes round at a dealership eventually. Or special-order it from the phone.",
            "Drive into a garage you own and the car you arrived in parks there.",
            "Rivals hand over their car after you beat them three times. Then it's pink slips.",
            "Race parts at workshops unlock real tuning: springs, anti-roll bars, diff, gearing, aero, brakes. Tune in Hub > Garage > Tuning.",
            "Old cars float and roll, muscle cars wallow, modern cars sit flat. Pick the right tool for the job.",
            "Anti-lag pops and crackles off throttle; a straight pipe makes any engine sound angry.",
            "Hire a crew mate in Hub > Social > Crew: blockers ram your chasers, drafters give you a tow.",
            "Series: three rounds, points, a podium and a reward car. Hub > Races > Race events > Series.",
            "Side bets in the race lobby: win and it doubles, podium and you get it back.",
            "Your story lives in Midnight Hub > Career > Story. Every chapter mixes street, dirt, drift, drag and bikes.",
            "Five disciplines level up separately. Higher levels open harder, better-paying events of that kind.",
            "Off-road events need ground clearance: trail races, rally stages with pace notes, cross country and hill climbs.",
            "Gymkhana: donuts around the glowing pillars score big. Close calls at speed add more.",
            "Every level-up earns a Wheelspin. Every fifth level, a Super Wheelspin.",
            "Purple blips are street racers. Get right behind one and flash your lights for an outrun battle.",
            "Tune every car in Midnight Hub > Garage > Tuning setup: springs, ride height, grip and brake balance, gearing.",
            "40 Midnight Tokens are hidden on the roads. Find them all for a one-off car.",
            "Win races that start in a district to take it from its crew. Your turf pays every night.",
            "Hold the revs in the green during the countdown for a perfect launch.",
            "Tuck in behind an opponent to catch their slipstream.",
            "Build your own races: Midnight Hub > Races > Route creator.",
            "Beat your best time and it becomes the ghost you race next time.",
            "Escape the police to cash in your bounty. Get busted and it's gone.",
            "Every car levels up as you drive it. Higher mastery, bigger race winnings.",
            "Nitrous: Sprint on keyboard, the boost button on a controller, once a workshop has fitted a kit. On a controller, hold Back/Select to enter or leave Midnight Club."
        };

        public static void Show(int ms)
        {
            string tip = Tips[new Random().Next(Tips.Length)];
            int end = Game.GameTime + ms;
            int start = Game.GameTime;
            while (Game.GameTime < end)
            {
                float t = (Game.GameTime - start) / (float)ms, e = Ds.EaseOut(Math.Min(1f, (Game.GameTime - start) / 600f));
                Ds.RectPhys(0f, 0f, 1f, 1f, Ds.Bg);
                var glow = Th.Light ? Color.FromArgb(255, 255, 214, 222) : Color.FromArgb(255, 70, 14, 30);
                Ds.VGrad(0f, 0.25f, 1f, 0.25f, Ds.A(Ds.Bg, 0f), Ds.A(glow, 0.9f), 12);
                Ds.VGrad(0f, 0.5f, 1f, 0.25f, Ds.A(glow, 0.9f), Ds.A(Ds.Bg, 0f), 12);
                Ds.Logo(0.5f, 0.38f - (1f - e) * 0.02f, 2.2f, e, Alignment.Center, true);
                float tw2 = Math.Min(0.5f, Ds.Width(tip, 0.32f)) + Ds.Px(Th.Px(60f)), th2 = Th.Px(64f);
                Kit.Glass(0.5f - tw2 / 2f, 0.62f, tw2, th2, Th.R_Lg, e);
                Textures.Draw("li_sparkles", 0.5f - tw2 / 2f + Ds.Px(Th.Px(28f)), 0.62f + th2 / 2f, Th.Px(22f), Ds.A(Th.Accent, e));
                Ds.TM(Ds.Fit(tip, 0.32f, 0.5f), 0.5f + Ds.Px(Th.Px(12f)), 0.62f + th2 / 2f, 0.32f, Ds.A(Th.Text, e), Ds.Body, Alignment.Center);
                float bw = 0.22f;
                Ds.Streaks(0f, 0.3f, 1f, 0.4f, Ds.Violet, 0.6f * e, 22, 0.7f);
                Kit.Bar(0.5f - bw / 2, 0.88f, bw, Th.Px(6f), Ds.EaseInOut(t), Th.Accent);
                Ds.T("LOADING", 0.5f, 0.892f, 0.24f, Ds.Faint, Ds.Body, Alignment.Center);
                Script.Wait(0);
            }
        }

        private static void T(string s, float x, float y, float scale, Color c, Font f)
        { MidnightClub.UI.Ds.Txt(s, x, y, MidnightClub.UI.Ds.Eff(scale), c, f, Alignment.Center, false, true, 0.6f); }
    }
}
