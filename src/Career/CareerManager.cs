using System;
using GTA;
using GTA.Native;
using GTA.UI;
using MidnightClub.Core;
using MidnightClub.Economy;
using MidnightClub.Persistence;

namespace MidnightClub.Career
{
    public sealed class TierUpEvent { public int Tier; public TierUpEvent(int t) { Tier = t; } }

    /// <summary>Career tiers driven by REP (thresholds configurable). Gates car tiers at dealerships and
    /// garage classes, announces promotions and exposes the tier to every other system.</summary>
    public sealed class CareerManager
    {
        public static CareerManager Instance { get; private set; }
        public static readonly string[] TierNames = { "", "Street Beginner", "Street Regular", "Underground Competitor", "Professional Street Racer", "Midnight Club Elite" };
        public static readonly string[] TierUnlocks =
        {
            "", "Basic street races, first garage, basic tuning.",
            "Harder races, better cars, larger garages, the Street Beginner Cup.",
            "Club challenges, tougher rivals, Underground Series, A-tier cars.",
            "High-level competitions, S-tier cars, executive garages.",
            "Elite championships, X-tier cars, the Midnight Club Invitational."
        };
        private readonly SaveManager saves;
        private readonly long[] thresholds;

        public CareerManager(SaveManager saves, Config cfg)
        {
            this.saves = saves; Instance = this;
            thresholds = new long[] { 0, 0, cfg.TierRep2, cfg.TierRep3, cfg.TierRep4, cfg.TierRep5 };
            EventBus.Subscribe<MoneyChangedEvent>(e => { if (e.RepDelta > 0) Check(); });
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To == GameMode.MIDNIGHT_CLUB) Check(); });
        }

        public int Tier
        {
            get
            {
                long rep = saves.Data == null ? 0 : saves.Data.Rep;
                int t = 1; for (int i = 2; i <= 5; i++) if (rep >= thresholds[i]) t = i;
                return t;
            }
        }
        public string TierName { get { return TierNames[Tier]; } }
        public long NextThreshold { get { int t = Tier; return t >= 5 ? 0 : thresholds[t + 1]; } }
        public long Threshold(int tier) { return thresholds[Math.Max(1, Math.Min(5, tier))]; }
        public float ProgressToNext
        {
            get { int t = Tier; if (t >= 5) return 1f; long a = thresholds[t], b = thresholds[t + 1]; return Math.Max(0f, Math.Min(1f, (saves.Data.Rep - a) / (float)(b - a))); }
        }

        private void Check()
        {
            if (saves.Data == null) return;
            int t = Tier;
            if (t <= saves.Data.CareerTierNotified) return;
            saves.Data.CareerTierNotified = t; saves.IsDirty = true;
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "RANK_UP", "HUD_AWARDS", true);
            Screen.ShowSubtitle("~p~TIER " + t + ": " + TierNames[t].ToUpperInvariant() + "~s~~n~" + TierUnlocks[t], 9000);
            Log.Info("Career tier " + t);
            EventBus.Publish(new TierUpEvent(t));
        }
    }
}
