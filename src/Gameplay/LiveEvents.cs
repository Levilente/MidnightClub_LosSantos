using System;
using GTA;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;
using MidnightClub.Racing;
using MidnightClub.UI;

namespace MidnightClub.Gameplay
{
    /// <summary>Seasons (Forza Horizon): every real-world week is Summer, Autumn, Winter or Spring, each with its own
    /// weather mood and a featured discipline worth +25% discipline XP.</summary>
    public static class Season
    {
        private static readonly string[] Names = { "Summer", "Autumn", "Winter", "Spring" };
        public static int Index { get { return Career.Progression.WeekNumber % 4; } }
        public static string Name { get { return Names[Index]; } }
        public static Discipline Featured { get { return new[] { Discipline.Street, Discipline.Offroad, Discipline.Drift, Discipline.Drag }[Index]; } }
        public static float XpMult(Discipline d) { return d == Featured || (Index == 3 && d == Discipline.Bikes) ? 1.25f : 1f; }
        /// <summary>Extra weight for a weather type (index into Weather.Types) this season.</summary>
        public static int WeatherBias(int type)
        {
            switch (Index)
            {
                case 0: return type == 0 ? 25 : type == 1 ? 10 : 0;
                case 1: return type == 4 ? 20 : type == 3 ? 15 : type == 5 ? 5 : 0;
                case 2: return type == 7 ? 20 : type == 3 ? 15 : type == 2 ? 10 : 0;
                default: return type == 1 ? 15 : type == 6 ? 10 : type == 4 ? 5 : 0;
            }
        }
        public static string Label { get { return Name.ToUpperInvariant() + " SEASON  ·  " + Career.Disciplines.Name(Featured).ToUpperInvariant() + (Index == 3 ? " & BIKES" : "") + " +25% XP"; } }
    }

    /// <summary>Live events (Forzathon-style): every ~18 minutes an eight-minute challenge pops up for everyone in the
    /// world: earn style points, win an outrun, set a trap record, land a big jump... Done in time: cash and a Wheelspin.</summary>
    public sealed class LiveEvents
    {
        private sealed class Ev { public string Kind, Text; public long Target; public bool Max; }
        private static readonly Ev[] Pool =
        {
            new Ev { Kind = "style", Text = "Earn 8,000 style points", Target = 8000 }, new Ev { Kind = "km", Text = "Drive 8 km", Target = 8 },
            new Ev { Kind = "outrun", Text = "Win an outrun battle", Target = 1 }, new Ev { Kind = "trap", Text = "Set a speed trap record", Target = 1 },
            new Ev { Kind = "zone", Text = "Earn a skill zone star", Target = 1 }, new Ev { Kind = "win", Text = "Win any race", Target = 1 },
            new Ev { Kind = "takedown", Text = "Take down 2 opponents", Target = 2 }, new Ev { Kind = "jump", Text = "Jump 40 m", Target = 40, Max = true },
            new Ev { Kind = "token", Text = "Find a Midnight Token", Target = 1 },
        };
        public static LiveEvents Instance;
        private readonly SaveManager saves; private readonly TransactionManager money;
        private Ev cur; private long progress; private int ends, next; private readonly Random rnd = new Random();

        public LiveEvents(SaveManager saves, TransactionManager money)
        {
            this.saves = saves; this.money = money; Instance = this;
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e => { cur = null; next = Game.GameTime + 240000; });
        }

        public void Track(string kind, long amount, bool max)
        {
            if (cur == null || cur.Kind != kind) return;
            progress = max || cur.Max ? Math.Max(progress, amount) : progress + amount;
            if (progress >= cur.Target)
            {
                money.Earn(8000, 150, "live", "Live event: " + cur.Text, null);
                saves.Data.Wheelspins++; saves.IsDirty = true;
                Shard.Mid("LIVE EVENT COMPLETE", cur.Text.ToUpperInvariant() + "  ·  +$8,000  ·  WHEELSPIN", Shard.Gold, 4000);
                cur = null; next = Game.GameTime + 18 * 60000;
            }
        }

        private void Tick()
        {
            int now = Game.GameTime;
            if (cur == null)
            {
                if (now < next || RaceManager.IsRacing) return;
                cur = Pool[rnd.Next(Pool.Length)]; progress = 0; ends = now + 8 * 60000;
                Toast.Post("~y~LIVE EVENT~s~: " + cur.Text + " in the next 8 minutes. Prize: $8,000 and a Wheelspin.", true);
                return;
            }
            int left = ends - now;
            if (left <= 0) { Toast.Post("~y~Live event~s~ over. Another one soon."); cur = null; next = now + 18 * 60000; return; }
            if (RaceManager.IsRacing || (Phone.PhoneManager.Instance != null && Phone.PhoneManager.Instance.IsVisible)) return;
            float w = Ds.Px(0.2f), x = 0.985f - w, y = 0.18f;
            Ds.Rect(x, y, w, 0.062f, Ds.A(Ds.Bg, 0.82f)); Ds.Rect(x, y, Ds.Px(0.0035f), 0.062f, Ds.Amber);
            Ds.T("LIVE EVENT  ·  " + (left / 60000) + ":" + (left / 1000 % 60).ToString("00"), x + Ds.Px(0.012f), y + 0.006f, 0.22f, Ds.Amber, Ds.Display);
            Ds.T(Ds.Fit(cur.Text, 0.3f, w - Ds.Px(0.02f)), x + Ds.Px(0.012f), y + 0.024f, 0.3f, Ds.Text);
            Ds.Rect(x + Ds.Px(0.012f), y + 0.052f, w - Ds.Px(0.024f), 0.003f, Ds.Stroke);
            Ds.Rect(x + Ds.Px(0.012f), y + 0.052f, (w - Ds.Px(0.024f)) * Math.Min(1f, progress / (float)cur.Target), 0.003f, Ds.Amber);
        }
    }
}
