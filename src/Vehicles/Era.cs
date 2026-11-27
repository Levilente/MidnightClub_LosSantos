using System;
using System.Collections.Generic;
using GTA;

namespace MidnightClub.Vehicles
{
    /// <summary>When a car was built, roughly, and what that means for how it drives: pre-war and 60s cars float on
    /// soft springs, roll and pitch, brake late and steer slowly; 70s-80s cars are a little tighter; 90s tuners feel
    /// direct but analogue; modern cars sit flat with sticky tyres and strong brakes. Applied on top of the archetype.</summary>
    public static class Era
    {
        private static readonly Dictionary<string, int> Years = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // pre-war / 50s
            { "btype", 1930 }, { "btype2", 1930 }, { "btype3", 1930 }, { "ztype", 1938 }, { "hotknife", 1934 }, { "hustler", 1935 }, { "clique", 1938 }, { "roosevelt", 1928 }, { "roosevelt2", 1928 },
            { "stirlinggt", 1956 }, { "slamvan", 1956 }, { "hermes", 1955 },
            // 60s
            { "stinger", 1964 }, { "stingergt", 1964 }, { "jb700", 1964 }, { "jb7002", 1964 }, { "casco", 1962 }, { "coquette2", 1963 }, { "coquette3", 1969 }, { "feltzer3", 1960 }, { "mamba", 1965 },
            { "monroe", 1967 }, { "peyote", 1962 }, { "peyote2", 1962 }, { "swinger", 1968 }, { "tornado", 1960 }, { "tornado2", 1960 }, { "tornado5", 1960 }, { "michelli", 1964 }, { "gt500", 1967 },
            { "chino", 1964 }, { "impaler", 1968 }, { "nightshade", 1966 }, { "voodoo", 1963 }, { "voodoo2", 1963 }, { "ellie", 1967 }, { "vamos", 1968 }, { "tulip", 1968 }, { "issi3", 1965 },
            { "weevil", 1960 }, { "brioso2", 1960 }, { "winky", 1960 }, { "savanna", 1967 },
            // 70s
            { "z190", 1972 }, { "torero", 1974 }, { "ardent", 1977 }, { "manana", 1971 }, { "pigalle", 1974 }, { "retinue", 1974 }, { "retinue2", 1974 }, { "viseris", 1975 }, { "fagaloa", 1975 },
            { "cheburek", 1975 }, { "dynasty", 1977 }, { "zion3", 1978 }, { "nebula", 1975 }, { "toreador", 1976 }, { "blade", 1972 }, { "buccaneer", 1972 }, { "buccaneer2", 1972 }, { "dukes", 1970 },
            { "picador", 1972 }, { "sabregt", 1970 }, { "sabregt2", 1970 }, { "stalion", 1972 }, { "tampa", 1970 }, { "vigero", 1970 }, { "virgo", 1971 }, { "virgo2", 1971 }, { "yosemite", 1975 },
            { "gauntlet3", 1971 }, { "deviant", 1971 }, { "warrener", 1972 }, { "warrener2", 1972 }, { "emperor", 1978 }, { "glendale", 1976 }, { "regina", 1975 }, { "bfinjection", 1970 },
            { "kalahari", 1970 }, { "bodhi2", 1975 }, { "riata", 1978 }, { "dloader", 1972 }, { "beetle", 1970 }, { "rapidgt4", 1974 }, { "dominator9", 1974 },
            // 80s
            { "cheetah2", 1983 }, { "infernus2", 1985 }, { "turismo2", 1986 }, { "rapidgt3", 1985 }, { "savestra", 1984 }, { "deluxo", 1983 }, { "futo", 1986 }, { "futo2", 1986 }, { "sentinel3", 1985 },
            { "faction", 1984 }, { "ruiner", 1984 }, { "blista2", 1985 }, { "blista3", 1985 }, { "asbo", 1987 }, { "kanjosj", 1985 }, { "postlude", 1987 }, { "rebel", 1985 }, { "rebel2", 1985 },
            { "dukes3", 1984 }, { "zion2", 1988 }, { "tampa3", 1985 },
            // 90s
            { "zr350", 1992 }, { "jester3", 1995 }, { "jester4", 1996 }, { "calico", 1996 }, { "remus", 1994 }, { "dominator7", 1993 }, { "comet2", 1995 }, { "comet3", 1995 }, { "comet4", 1990 },
            { "sultan2", 1997 }, { "rt3000", 1995 }, { "previon", 1993 }, { "banshee", 1994 }, { "sheava", 1995 }, { "gp1", 1994 }, { "kanjo", 1993 }, { "primo", 1994 }, { "stanier", 1995 },
            { "washington", 1994 }, { "sandking", 1995 }, { "sandking2", 1995 }, { "dominator2", 1995 }, { "elegy", 1998 }, { "euros", 2000 }, { "cypher", 2018 }, { "vectre", 2019 },
            // 2000s
            { "sultan", 2006 }, { "sultanrs", 2006 }, { "kuruma", 2008 }, { "penumbra", 2005 }, { "tailgater2", 2005 }, { "elegy2", 2008 }, { "banshee2", 2008 }, { "infernus", 2006 },
            { "cheetah", 2004 }, { "entityxf", 2006 }, { "adder", 2008 }, { "bullet", 2006 }, { "vacca", 2006 }, { "dominator", 2008 }, { "gauntlet", 2008 }, { "tempesta", 2009 }, { "club", 2003 },
        };

        public static int Year(VehicleInfo info)
        {
            if (info == null) return 2005;
            int y; if (Years.TryGetValue(info.Id, out y)) return y;
            switch (info.Class)
            {
                case VehicleClass.SportsClassics: return 1972;
                case VehicleClass.Muscle: return 1972;
                case VehicleClass.Sedans: case VehicleClass.Coupes: case VehicleClass.Compacts: case VehicleClass.SUVs: case VehicleClass.OffRoad: case VehicleClass.Vans: return 2006;
                case VehicleClass.Super: return 2016;
                default: return 2012;
            }
        }

        public static string Label(int year) { return year < 1950 ? "Pre-war" : (year / 10 * 10) + "s"; }

        /// <summary>{grip, spring, damping, travel, antiroll, brakes, steerLock, hiSpeedSteer, inertia, comRise, stability}</summary>
        public static float[] Factors(int year)
        {
            if (year < 1950) return new[] { 0.82f, 0.58f, 0.50f, 1.40f, 0.35f, 0.70f, 0.90f, 0.82f, 1.25f, 0.05f, 0.55f };
            if (year < 1970) return new[] { 0.86f, 0.64f, 0.55f, 1.33f, 0.42f, 0.76f, 0.93f, 0.86f, 1.20f, 0.04f, 0.65f };
            if (year < 1980) return new[] { 0.89f, 0.72f, 0.62f, 1.25f, 0.50f, 0.82f, 0.95f, 0.90f, 1.16f, 0.03f, 0.75f };
            if (year < 1990) return new[] { 0.93f, 0.82f, 0.72f, 1.14f, 0.65f, 0.88f, 0.98f, 0.94f, 1.10f, 0.02f, 0.85f };
            if (year < 2000) return new[] { 0.97f, 0.92f, 0.86f, 1.05f, 0.82f, 0.95f, 1.00f, 0.98f, 1.04f, 0.01f, 0.95f };
            if (year < 2012) return new[] { 1.00f, 0.98f, 0.96f, 1.00f, 0.95f, 1.00f, 1.00f, 1.00f, 1.00f, 0.00f, 1.00f };
            return new[] { 1.04f, 1.06f, 1.04f, 0.95f, 1.08f, 1.07f, 1.00f, 1.03f, 0.97f, -0.01f, 1.10f };
        }
    }
}
