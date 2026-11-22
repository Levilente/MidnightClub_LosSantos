using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;
using MidnightClub.Vehicles;

namespace MidnightClub.Dealerships
{
    public sealed class DealershipDefinition
    {
        public string Id, Name, Tagline;
        public Vector3 Position;
        public BlipSprite Sprite; public BlipColor Color;
        public int MinStock, MaxStock, RotationDays;
        public double PriceFactor = 1.0;
        public Func<VehicleInfo, bool> Filter;
        public bool IsUsedMarket;
    }

    /// <summary>Built-in dealership identities; locations, stock sizes, rotation and price factor are
    /// overridable in data\dealerships.ini. New dealerships can be added there with a "Classes" filter.</summary>
    public static class DealershipDefinitions
    {
        private static bool In(VehicleInfo v, params VehicleClass[] c) { return Array.IndexOf(c, v.Class) >= 0; }

        public static List<DealershipDefinition> Defaults()
        {
            return new List<DealershipDefinition>
            {
                new DealershipDefinition { Id = "pdm", Name = "Premium Deluxe Motorsport", Tagline = "Simeon's affordable rides. Every legend starts somewhere.",
                    Position = new Vector3(-31.0f, -1090.5f, 26.4f), Sprite = BlipSprite.SimeonCarShowroom, Color = BlipColor.White, MinStock = 8, MaxStock = 12, RotationDays = 7,
                    Filter = v => !v.Bennys && !v.Tuner && v.BasePrice <= 45000 && In(v, VehicleClass.Compacts, VehicleClass.Sedans, VehicleClass.SUVs, VehicleClass.Coupes, VehicleClass.Muscle, VehicleClass.SportsClassics) },
                new DealershipDefinition { Id = "southern", Name = "Southern San Andreas Super Autos", Tagline = "Something for every street. Sports, muscle, off-road and bikes.",
                    Position = new Vector3(812.0f, -1050.0f, 27.0f), Sprite = BlipSprite.CarShowroom, Color = BlipColor.Blue, MinStock = 15, MaxStock = 20, RotationDays = 7,
                    Filter = v => !v.Bennys && v.BasePrice >= 12000 && v.BasePrice <= 160000 && In(v, VehicleClass.Sports, VehicleClass.Muscle, VehicleClass.OffRoad, VehicleClass.Compacts, VehicleClass.Motorcycles, VehicleClass.SportsClassics, VehicleClass.Coupes) },
                new DealershipDefinition { Id = "luxury", Name = "Luxury Autos", Tagline = "Rockford Hills. Exclusive pieces, by appointment only.",
                    Position = new Vector3(-803.0f, -223.0f, 37.2f), Sprite = BlipSprite.LuxuryAutos, Color = BlipColor.Yellow2, MinStock = 2, MaxStock = 3, RotationDays = 7,
                    Filter = v => !v.Bennys && v.BasePrice >= 70000 && In(v, VehicleClass.SUVs, VehicleClass.Coupes, VehicleClass.Sedans, VehicleClass.Sports, VehicleClass.SportsClassics) },
                new DealershipDefinition { Id = "legendary", Name = "Legendary Motorsport", Tagline = "Supercars and high-performance machines.",
                    Position = new Vector3(-1255.0f, -366.0f, 36.9f), Sprite = BlipSprite.SportsCar, Color = BlipColor.Red, MinStock = 5, MaxStock = 8, RotationDays = 7,
                    Filter = v => !v.Bennys && v.BasePrice >= 110000 && In(v, VehicleClass.Super, VehicleClass.Sports, VehicleClass.SportsClassics, VehicleClass.OpenWheel) },
                new DealershipDefinition { Id = "bennys", Name = "Benny's Original Motor Works", Tagline = "Custom conversions, lowriders and special variants.",
                    Position = new Vector3(-205.6f, -1305.0f, 31.0f), Sprite = BlipSprite.Bennys, Color = BlipColor.Yellow2, MinStock = 4, MaxStock = 6, RotationDays = 7,
                    Filter = v => v.Bennys },
                new DealershipDefinition { Id = "carmeet", Name = "LS Car Meet Marketplace", Tagline = "Tuners, drift builds and rare finds from the community.",
                    Position = new Vector3(778.9f, -1867.4f, 29.3f), Sprite = BlipSprite.LSCarMeet, Color = BlipColor.Green, MinStock = 3, MaxStock = 6, RotationDays = 5,
                    Filter = v => v.Tuner || v.Drift || (v.Tier == "X" && In(v, VehicleClass.Sports, VehicleClass.Super)) },
                new DealershipDefinition { Id = "used", Name = "Mosley's Used Cars", Tagline = "Pre-owned, as seen. Project cars and the odd hidden gem.",
                    Position = new Vector3(-38.6f, -1672.4f, 29.4f), Sprite = BlipSprite.RetroSportsCar, Color = BlipColor.White, MinStock = 6, MaxStock = 8, RotationDays = 3,
                    IsUsedMarket = true, Filter = v => v.BasePrice <= 400000 },
            };
        }

        public static List<DealershipDefinition> Load(string dataDir)
        {
            var file = Path.Combine(dataDir, "dealerships.ini");
            bool write = !File.Exists(file);
            var s = ScriptSettings.Load(file);
            var result = new List<DealershipDefinition>();
            foreach (var d in Defaults())
            {
                if (write)
                {
                    s.SetValue(d.Id, "Name", d.Name); s.SetValue(d.Id, "X", d.Position.X); s.SetValue(d.Id, "Y", d.Position.Y); s.SetValue(d.Id, "Z", d.Position.Z);
                    s.SetValue(d.Id, "MinStock", d.MinStock); s.SetValue(d.Id, "MaxStock", d.MaxStock); s.SetValue(d.Id, "RotationDays", d.RotationDays);
                    s.SetValue(d.Id, "PriceFactor", d.PriceFactor); s.SetValue(d.Id, "Enabled", true);
                }
                if (!s.GetValue(d.Id, "Enabled", true)) continue;
                d.Name = s.GetValue(d.Id, "Name", d.Name);
                d.Position = new Vector3(s.GetValue(d.Id, "X", d.Position.X), s.GetValue(d.Id, "Y", d.Position.Y), s.GetValue(d.Id, "Z", d.Position.Z));
                d.MinStock = Math.Max(1, s.GetValue(d.Id, "MinStock", d.MinStock));
                d.MaxStock = Math.Max(d.MinStock, s.GetValue(d.Id, "MaxStock", d.MaxStock));
                d.RotationDays = Math.Max(1, s.GetValue(d.Id, "RotationDays", d.RotationDays));
                d.PriceFactor = s.GetValue(d.Id, "PriceFactor", d.PriceFactor);
                result.Add(d);
            }
            // Extra community dealerships: any section that isn't built in, filtered by class list.
            var known = new HashSet<string>(result.ConvertAll(x => x.Id));
            foreach (var d in Defaults()) known.Add(d.Id);
            foreach (var id in s.GetAllSectionNames())
            {
                if (known.Contains(id) || !s.GetValue(id, "Enabled", true)) continue;
                var classes = new HashSet<VehicleClass>();
                foreach (var c in s.GetValue(id, "Classes", "Sports").Split(','))
                {
                    VehicleClass vc; if (Enum.TryParse(c.Trim(), true, out vc)) classes.Add(vc);
                }
                long min = s.GetValue(id, "MinPrice", 0L), max = s.GetValue(id, "MaxPrice", long.MaxValue);
                result.Add(new DealershipDefinition
                {
                    Id = id, Name = s.GetValue(id, "Name", id), Tagline = s.GetValue(id, "Tagline", ""),
                    Position = new Vector3(s.GetValue(id, "X", 0f), s.GetValue(id, "Y", 0f), s.GetValue(id, "Z", 0f)),
                    Sprite = BlipSprite.CarShowroom, Color = BlipColor.White,
                    MinStock = s.GetValue(id, "MinStock", 5), MaxStock = s.GetValue(id, "MaxStock", 8), RotationDays = s.GetValue(id, "RotationDays", 7),
                    PriceFactor = s.GetValue(id, "PriceFactor", 1.0),
                    Filter = v => classes.Contains(v.Class) && v.BasePrice >= min && v.BasePrice <= max
                });
            }
            if (write) try { s.Save(); } catch { }
            return result;
        }
    }
}
