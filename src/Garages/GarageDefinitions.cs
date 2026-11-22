using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;

namespace MidnightClub.Garages
{
    public enum GarageCategory { Small, Medium, Large, Premium, Special }

    public sealed class GarageDefinition
    {
        public string Id, Name;
        public GarageCategory Category;
        public long Price;
        public int Capacity, CapacityPerUpgrade, MaxUpgrades;
        public double UpgradeCostFactor;
        public Vector3 Position;
        public float Heading;
        public int CareerTier;

        public int CapacityAt(int level) { return Capacity + CapacityPerUpgrade * level; }
        public long UpgradeCost(int nextLevel) { return (long)(Price * UpgradeCostFactor * nextLevel); }
    }

    /// <summary>Garage list from data\garages.ini (written with defaults on first run, then editable).</summary>
    public static class GarageDefinitions
    {
        public static List<GarageDefinition> Load(string dataDir)
        {
            var file = Path.Combine(dataDir, "garages.ini");
            if (!File.Exists(file)) WriteDefaults(file);
            var s = ScriptSettings.Load(file);
            var list = new List<GarageDefinition>();
            foreach (var id in s.GetAllSectionNames())
            {
                if (!s.GetValue(id, "Enabled", true)) continue;
                var g = new GarageDefinition
                {
                    Id = id,
                    Name = s.GetValue(id, "Name", id),
                    Category = s.GetValue(id, "Category", GarageCategory.Small),
                    Price = s.GetValue(id, "Price", 25000L),
                    Capacity = s.GetValue(id, "Capacity", 2),
                    CapacityPerUpgrade = s.GetValue(id, "CapacityPerUpgrade", 2),
                    MaxUpgrades = s.GetValue(id, "MaxUpgrades", 2),
                    UpgradeCostFactor = s.GetValue(id, "UpgradeCostFactor", 0.35),
                    Position = new Vector3(s.GetValue(id, "X", 0f), s.GetValue(id, "Y", 0f), s.GetValue(id, "Z", 0f)),
                    Heading = s.GetValue(id, "Heading", 0f),
                    CareerTier = s.GetValue(id, "CareerTier", 1)
                };
                if (g.Position != Vector3.Zero) list.Add(g);
            }
            return list;
        }

        private static void WriteDefaults(string file)
        {
            var s = ScriptSettings.Load(file);
            // First-pass locations (public parking lots). Move them by editing X/Y/Z if one is awkward.
            Add(s, "sandy_shores", "Sandy Shores Lot", GarageCategory.Small, 8000, 2, 1, 2, 1737.6f, 3710.2f, 34.1f, 20f, 1);
            Add(s, "airport_longterm", "LSIA Long-Term Parking", GarageCategory.Small, 14000, 2, 2, 2, -796.9f, -2022.8f, 9.2f, 45f, 1);
            Add(s, "paleto_bay", "Paleto Bay Garage", GarageCategory.Medium, 18000, 4, 2, 2, 105.4f, 6613.6f, 32.4f, 315f, 1);
            Add(s, "mirror_park", "Mirror Park Garage", GarageCategory.Medium, 32000, 4, 2, 2, 1032.8f, -765.1f, 58.0f, 330f, 2);
            Add(s, "legion_square", "Legion Square Parking", GarageCategory.Large, 75000, 6, 2, 3, 215.1f, -791.4f, 30.6f, 340f, 2);
            Add(s, "eclipse_towers", "Eclipse Towers Garage", GarageCategory.Premium, 260000, 10, 2, 3, -796.5f, 308.9f, 85.7f, 180f, 3);
            s.Save();
        }

        private static void Add(ScriptSettings s, string id, string name, GarageCategory cat, long price, int cap, int perUpg, int maxUpg, float x, float y, float z, float h, int tier)
        {
            s.SetValue(id, "Name", name); s.SetValue(id, "Category", cat); s.SetValue(id, "Price", price);
            s.SetValue(id, "Capacity", cap); s.SetValue(id, "CapacityPerUpgrade", perUpg); s.SetValue(id, "MaxUpgrades", maxUpg);
            s.SetValue(id, "UpgradeCostFactor", 0.35); s.SetValue(id, "X", x); s.SetValue(id, "Y", y); s.SetValue(id, "Z", z);
            s.SetValue(id, "Heading", h); s.SetValue(id, "CareerTier", tier); s.SetValue(id, "Enabled", true);
        }
    }
}
