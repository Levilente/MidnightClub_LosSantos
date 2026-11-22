using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;

namespace MidnightClub.Garages
{
    public enum GarageCategory { Small, Medium, Large, Premium, Special }

    public struct Spot
    {
        public Vector3 Position; public float Heading;
        public Spot(float[] a) { Position = new Vector3(a[0], a[1], a[2]); Heading = a.Length > 3 ? a[3] : 0f; }
    }

    public sealed class GarageDefinition
    {
        public string Id, Name, Kind, Ipl;
        public GarageCategory Category;
        public long Price;
        public float Radius;
        public Spot Entrance, ExitMarker, SpawnIn, SpawnOut;
        public List<Spot> Slots = new List<Spot>();
        public Vector3 Position { get { return Entrance.Position; } }
        public int Capacity { get { return Slots.Count; } }
        public bool Customizable { get { return Kind == "ceo"; } }
        public string KindLabel
        {
            get { return Kind == "low" ? "2-car garage" : Kind == "mid" ? "6-car garage" : Kind == "high" ? "10-car garage" : Kind == "ceo" ? "Executive office garage" : "Garage"; }
        }
    }

    /// <summary>Real GTA Online garages (built-in catalog) with per-garage overrides in data\online_garages.ini.</summary>
    public static class GarageDefinitions
    {
        public static List<GarageDefinition> Load(string dataDir)
        {
            var file = Path.Combine(dataDir, "online_garages.ini");
            var s = ScriptSettings.Load(file);
            bool write = !File.Exists(file);
            var list = new List<GarageDefinition>();
            foreach (var d in OnlineGarageCatalog.All)
            {
                if (write)
                {
                    s.SetValue(d.Id, "Name", d.Name); s.SetValue(d.Id, "Price", d.Price); s.SetValue(d.Id, "Enabled", true);
                }
                if (!s.GetValue(d.Id, "Enabled", true)) continue;
                var g = new GarageDefinition
                {
                    Id = d.Id, Kind = d.Kind, Ipl = d.Ipl,
                    Name = s.GetValue(d.Id, "Name", d.Name),
                    Price = s.GetValue(d.Id, "Price", d.Price),
                    Radius = d.Radius,
                    Entrance = new Spot(d.Entrance), ExitMarker = new Spot(d.ExitMarker), SpawnIn = new Spot(d.SpawnIn), SpawnOut = new Spot(d.SpawnOut),
                    Category = d.Kind == "low" ? GarageCategory.Small : d.Kind == "mid" ? GarageCategory.Medium : d.Kind == "high" ? GarageCategory.Large : GarageCategory.Premium
                };
                foreach (var sl in d.Slots) g.Slots.Add(new Spot(sl));
                list.Add(g);
            }
            if (write) try { s.Save(); } catch { }
            return list;
        }
    }
}
