using System;
using GTA;
using MidnightClub.Core;
using MidnightClub.Vehicles;

namespace MidnightClub.Economy
{
    /// <summary>Own price model (not GTA Online prices): class base x performance curve, rounded,
    /// clamped and globally scaled from config. Market conditions / rarity get layered on in Phase 3-4.</summary>
    public static class PricingEngine
    {
        public static long ClassBase(VehicleClass c)
        {
            switch (c)
            {
                case VehicleClass.Compacts: return 6000;
                case VehicleClass.Sedans: return 8000;
                case VehicleClass.Motorcycles: return 9000;
                case VehicleClass.Muscle: return 14000;
                case VehicleClass.OffRoad: return 14000;
                case VehicleClass.SUVs: return 16000;
                case VehicleClass.Coupes: return 18000;
                case VehicleClass.SportsClassics: return 30000;
                case VehicleClass.Sports: return 35000;
                case VehicleClass.Super: return 120000;
                case VehicleClass.OpenWheel: return 250000;
                default: return 15000;
            }
        }

        public static long BasePrice(VehicleInfo v, Config cfg)
        {
            // Performance 0..1 → multiplier 0.6 .. ~6 (exponential, so top cars are a real goal).
            double mult = 0.6 * Math.Exp(2.3 * v.Performance);
            double price = ClassBase(v.Class) * mult * cfg.PriceMultiplier;
            price = Math.Max(cfg.MinVehiclePrice, Math.Min(cfg.MaxVehiclePrice, price));
            return Round(price);
        }

        public static long Round(double p)
        {
            long step = p < 20000 ? 250 : p < 200000 ? 1000 : 5000;
            return (long)(Math.Round(p / step) * step);
        }

        /// <summary>Estimated value of an owned car: depreciates with distance, never below 40 % of base.</summary>
        public static long EstimatedValue(VehicleInfo info, double km, float engineHealth, float bodyHealth)
        {
            if (info == null) return 0;
            double wear = Math.Min(0.4, km / 5000.0);
            double condition = Math.Max(0, Math.Min(1, (engineHealth + bodyHealth) / 2000.0));
            double v = info.BasePrice * (1.0 - wear) * (0.75 + 0.25 * condition);
            return Round(Math.Max(info.BasePrice * 0.4, v));
        }
    }
}
