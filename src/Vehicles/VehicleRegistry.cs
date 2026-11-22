using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GTA;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.Economy;

namespace MidnightClub.Vehicles
{
    public enum VehicleSource { Native, AddOn }

    public sealed class VehicleInfo
    {
        public int Hash;
        public string Id;            // unique internal id = lower-case model name
        public string DisplayName, Make;
        public VehicleClass Class;
        public float MaxSpeed, Acceleration, Braking, Traction;
        public float Performance;    // 0..1, relative to every car in the registry
        public string Tier;          // D, C, B, A, S, X
        public long BasePrice;
        public bool Enabled = true;
        public bool DealershipEligible = true;
        public int RequiredCareerTier = 1;
        public VehicleSource Source;
        public string Name { get { return string.IsNullOrEmpty(Make) ? DisplayName : Make + " " + DisplayName; } }
    }

    /// <summary>Centralized vehicle database built from the vehicles actually present in the installed
    /// game build (base game, all DLC, and add-on cars), so invalid models are never offered.
    /// data\vehicles.ini can override price / enabled / dealership / tier per model.</summary>
    public sealed class VehicleRegistry
    {
        private readonly Dictionary<int, VehicleInfo> byHash = new Dictionary<int, VehicleInfo>();
        private readonly Dictionary<string, VehicleInfo> byId = new Dictionary<string, VehicleInfo>(StringComparer.OrdinalIgnoreCase);
        public IEnumerable<VehicleInfo> All { get { return byHash.Values; } }
        public int Count { get { return byHash.Count; } }
        public bool IsBuilt { get; private set; }

        private static readonly HashSet<VehicleClass> DefaultClasses = new HashSet<VehicleClass>
        {
            VehicleClass.Compacts, VehicleClass.Sedans, VehicleClass.SUVs, VehicleClass.Coupes, VehicleClass.Muscle,
            VehicleClass.SportsClassics, VehicleClass.Sports, VehicleClass.Super, VehicleClass.Motorcycles,
            VehicleClass.OffRoad, VehicleClass.OpenWheel
        };

        public void Build(string dataDir, Config cfg)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            byHash.Clear(); byId.Clear();
            var classes = new HashSet<VehicleClass>(DefaultClasses);
            if (!cfg.IncludeMotorcycles) classes.Remove(VehicleClass.Motorcycles);

            foreach (var vh in Vehicle.GetAllModels())
            {
                try
                {
                    var model = new Model((int)vh);
                    if (!model.IsValid || !model.IsInCdImage || !model.IsVehicle) continue;
                    var cls = Vehicle.GetModelClass(model);
                    if (!classes.Contains(cls)) continue;

                    var info = new VehicleInfo { Hash = model.Hash, Class = cls };
                    bool known = Enum.IsDefined(typeof(VehicleHash), vh);
                    info.Source = known ? VehicleSource.Native : VehicleSource.AddOn;
                    string label = Function.Call<string>(Hash.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL, model.Hash) ?? "";
                    info.Id = (known ? vh.ToString() : label).ToLowerInvariant();
                    if (string.IsNullOrEmpty(info.Id) || info.Id == "carnotfound") info.Id = "0x" + model.Hash.ToString("X8");
                    info.DisplayName = Localize(label, info.Id);
                    info.Make = Localize(Function.Call<string>(Hash.GET_MAKE_NAME_FROM_VEHICLE_MODEL, model.Hash), "");
                    info.MaxSpeed = Function.Call<float>(Hash.GET_VEHICLE_MODEL_ESTIMATED_MAX_SPEED, model.Hash);
                    info.Acceleration = Function.Call<float>(Hash.GET_VEHICLE_MODEL_ACCELERATION, model.Hash);
                    info.Braking = Function.Call<float>(Hash.GET_VEHICLE_MODEL_MAX_BRAKING, model.Hash);
                    info.Traction = Function.Call<float>(Hash.GET_VEHICLE_MODEL_MAX_TRACTION, model.Hash);
                    if (byId.ContainsKey(info.Id)) info.Id += "_" + model.Hash.ToString("X8");
                    byHash[info.Hash] = info; byId[info.Id] = info;
                }
                catch (Exception ex) { Log.Warn("Registry skipped " + vh + ": " + ex.Message); }
            }

            ComputePerformance();
            foreach (var v in byHash.Values) v.BasePrice = PricingEngine.BasePrice(v, cfg);
            ApplyOverrides(Path.Combine(dataDir, "vehicles.ini"));
            Export(Path.Combine(dataDir, "vehicle_registry.csv"));
            IsBuilt = true;
            Log.Info("Vehicle registry: " + byHash.Count + " vehicles (" + byHash.Values.Count(v => v.Source == VehicleSource.AddOn) + " not in SHVDN's list: newer DLC or add-on) in " + sw.ElapsedMilliseconds + " ms");
        }

        private static string Localize(string label, string fallback)
        {
            if (string.IsNullOrEmpty(label) || label == "CARNOTFOUND") return fallback;
            if (Function.Call<bool>(Hash.DOES_TEXT_LABEL_EXIST, label))
            {
                var s = Game.GetLocalizedString(label);
                if (!string.IsNullOrEmpty(s) && s != "NULL") return s;
            }
            return label.Substring(0, 1) + label.Substring(1).ToLowerInvariant();
        }

        /// <summary>Performance = weighted percentile of speed/accel/braking/traction across the registry,
        /// so it stays meaningful whatever cars (DLC/add-ons) are installed.</summary>
        private void ComputePerformance()
        {
            var list = byHash.Values.ToList();
            if (list.Count == 0) return;
            Func<Func<VehicleInfo, float>, Dictionary<VehicleInfo, float>> pct = sel =>
            {
                var sorted = list.OrderBy(sel).ToList();
                var d = new Dictionary<VehicleInfo, float>();
                for (int i = 0; i < sorted.Count; i++) d[sorted[i]] = sorted.Count == 1 ? 1f : i / (float)(sorted.Count - 1);
                return d;
            };
            var sp = pct(v => v.MaxSpeed); var ac = pct(v => v.Acceleration); var br = pct(v => v.Braking); var tr = pct(v => v.Traction);
            foreach (var v in list)
            {
                v.Performance = 0.35f * sp[v] + 0.35f * ac[v] + 0.1f * br[v] + 0.2f * tr[v];
                v.Tier = v.Performance >= 0.92f ? "X" : v.Performance >= 0.78f ? "S" : v.Performance >= 0.6f ? "A" : v.Performance >= 0.4f ? "B" : v.Performance >= 0.2f ? "C" : "D";
                v.RequiredCareerTier = v.Tier == "X" ? 5 : v.Tier == "S" ? 4 : v.Tier == "A" ? 3 : v.Tier == "B" ? 2 : 1;
            }
        }

        private void ApplyOverrides(string file)
        {
            var s = ScriptSettings.Load(file);
            if (!File.Exists(file))
            {
                s.SetValue("futo", "Price", 9500);
                s.SetValue("futo", "Comment", "Example override. Section = model name. Keys: Price, Enabled, Dealership, CareerTier");
                try { s.Save(); } catch { }
            }
            int n = 0;
            foreach (var section in s.GetAllSectionNames())
            {
                VehicleInfo v;
                if (!byId.TryGetValue(section, out v)) continue;
                v.BasePrice = s.GetValue(section, "Price", v.BasePrice);
                v.Enabled = s.GetValue(section, "Enabled", v.Enabled);
                v.DealershipEligible = s.GetValue(section, "Dealership", v.DealershipEligible);
                v.RequiredCareerTier = s.GetValue(section, "CareerTier", v.RequiredCareerTier);
                n++;
            }
            if (n > 0) Log.Info("Applied " + n + " vehicle overrides");
        }

        private void Export(string file)
        {
            try
            {
                var sb = new StringBuilder("id,name,make,class,tier,performance,price,max_speed_kmh,acceleration,braking,traction,source\n");
                foreach (var v in byHash.Values.OrderBy(v => v.Class).ThenBy(v => v.BasePrice))
                    sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0},\"{1}\",\"{2}\",{3},{4},{5:0.000},{6},{7:0},{8:0.000},{9:0.000},{10:0.000},{11}\n",
                        v.Id, v.DisplayName, v.Make, v.Class, v.Tier, v.Performance, v.BasePrice, v.MaxSpeed * 3.6f, v.Acceleration, v.Braking, v.Traction, v.Source);
                File.WriteAllText(file, sb.ToString());
            }
            catch (Exception ex) { Log.Warn("Registry export failed: " + ex.Message); }
        }

        public VehicleInfo Get(int hash) { VehicleInfo v; return byHash.TryGetValue(hash, out v) ? v : null; }
        public VehicleInfo Get(string id) { VehicleInfo v; return id != null && byId.TryGetValue(id, out v) ? v : null; }
    }
}
