using System;

namespace MidnightClub.Physics
{
    public enum Induction { NA, Turbo, Supercharged, Electric }

    /// <summary>Real-world specification of a vehicle: engine (power, torque and where they peak, redline, induction),
    /// gearbox, layout, weight and balance, centre of gravity, aero and tyres.</summary>
    public sealed class CarSpec
    {
        public string Id, Engine = ""; public bool Curated, Bike, Hybrid, Diesel;
        public float Hp, HpRpm, Nm, NmRpm, Redline, Idle = 850f, Boost;   // Boost: factory boost in bar (turbo / SC)
        public Induction Ind; public float Litres; public int Cyl;
        public int Gears; public string Layout; public float FrontDrive;   // share of drive to the front axle
        public float Kg, FrontPct, CgH, Wheelbase = 2.6f, Track = 1.58f, CdA, ClA, AeroFront = 0.45f, TyreR = 0.33f;
        public int Tyre = -1; public int Year; public string Kind;
        public SpecSheet Sheet;
        public string Charger;                 // turbo / supercharger type (sheet), drives spool behaviour              // the vehicle's spec sheet (null: generated from its class)
        public float TrackRear;
        public bool HasGeo; public float GeoFrontY, GeoRearY, GeoAxleZ;   // wheel positions in model space
        public string LayoutLabel { get { return Bike ? "Chain drive" : FrontDrive >= 0.95f ? "FWD" : FrontDrive > 0.05f ? "AWD" : "RWD"; } }
        public string EngineLabel
        {
            get
            {
                if (Ind == Induction.Electric) return "Electric";
                string cfg = Cyl <= 0 ? "" : Cyl == 2 && Engine == "R" ? "2-rotor" : (Engine.Length > 0 ? Engine : Cyl <= 6 && !Bike ? "I" : "V") + Cyl;
                string ind = Ind == Induction.Turbo ? " turbo" : Ind == Induction.Supercharged ? " supercharged" : "";
                return Litres.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " L " + cfg + ind + (Hybrid ? " hybrid" : "") + (Diesel ? " diesel" : "");
            }
        }
    }

}
