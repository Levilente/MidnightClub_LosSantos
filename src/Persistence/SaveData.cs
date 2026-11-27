using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MidnightClub.Persistence
{
    [DataContract]
    public sealed class SaveData
    {
        [DataMember] public int FormatVersion;
        [DataMember] public string ModVersion;
        [DataMember] public string ProfileId;
        [DataMember] public long Revision;
        [DataMember] public string SavedAtUtc;

        [DataMember] public CharacterData Character = new CharacterData();
        [DataMember] public long Cash;
        [DataMember] public long Rep;
        [DataMember] public List<OwnedVehicle> Vehicles = new List<OwnedVehicle>();
        [DataMember] public string CurrentVehicleId;
        [DataMember] public float PosX, PosY, PosZ, Heading;
        [DataMember] public bool HasPosition;
        [DataMember] public List<string> ProcessedTransactions = new List<string>();
        [DataMember] public int CameraMode = 3;        // CloseChase
        [DataMember] public int CalendarDay;          // Midnight Club in-game days elapsed (only advances while playing MC)
        [DataMember] public int LastClockHour = -1;
        [DataMember] public List<string> SoldStock = new List<string>();   // "dealer:rotation:modelhash" / "used:rotation:slot"
        [DataMember] public int RacesStarted;
        [DataMember] public int CareerTierNotified = 1;
        [DataMember] public int Pursuits, PursuitEscapes, Busts;
        [DataMember] public List<KeyValue> SpeedTrapRecords = new List<KeyValue>();   // trap id -> km/h
        [DataMember] public List<string> MeetShowoffs = new List<string>();            // "meetId:day" already rewarded
        [DataMember] public int StreetRacesWon;
        [DataMember] public int PhotosTaken;
        [DataMember] public bool ClubMember;
        [DataMember] public string LastGarageId;
        [DataMember] public int LastLoginDay, LoginStreak;
        [DataMember] public float BestJumpM;
        [DataMember] public int Wheelspins, SuperWheelspins;
        [DataMember] public string CrewName, CrewRole; [DataMember] public int CrewRaces;
        [DataMember] public List<KeyValue> DisciplineXp = new List<KeyValue>();
        [DataMember] public int StoryChapter; [DataMember] public List<KeyValue> StoryCounters = new List<KeyValue>();
        [DataMember] public List<KeyValue> Licenses = new List<KeyValue>();   // test index -> medal 1..3
        [DataMember] public int WeeklyId; [DataMember] public List<KeyValue> WeeklyProgress = new List<KeyValue>(); [DataMember] public List<string> WeeklyDone = new List<string>();
        [DataMember] public List<KeyValue> Turf = new List<KeyValue>();   // district id -> control 0..100
        [DataMember] public List<PhoneMessage> Messages = new List<PhoneMessage>();
        [DataMember] public List<NewsItem> News = new List<NewsItem>();
        [DataMember] public int LastInviteDay = -1;
        [DataMember] public List<string> ObjectivesDone = new List<string>();
        [DataMember] public List<string> MilestonesDone = new List<string>();
        [DataMember] public string SelectedAbility;
        [DataMember] public long StylePointsTotal;
        [DataMember] public float BestSpeedKmh;
        [DataMember] public List<RivalState> Rivals = new List<RivalState>();
        [DataMember] public List<KeyValue> ClubRep = new List<KeyValue>();
        [DataMember] public List<RaceResult> RaceHistory = new List<RaceResult>();
        [DataMember] public List<KeyValue> BestTimes = new List<KeyValue>();      // raceId -> ms
        [DataMember] public List<ChampionshipProgress> Championships = new List<ChampionshipProgress>();
        [DataMember] public List<OwnedGarage> Garages = new List<OwnedGarage>();
        [DataMember] public List<LedgerEntry> Ledger = new List<LedgerEntry>();
        [DataMember] public List<KeyValue> Extensions = new List<KeyValue>();
        // progression (0.18)
        [DataMember] public long Xp;
        [DataMember] public int LevelRewarded = 1;
        [DataMember] public List<string> Discovered = new List<string>();
        [DataMember] public string RoadBits;
        [DataMember] public List<int> RoadMilestones = new List<int>();
        [DataMember] public List<KeyValue> SkillRecords = new List<KeyValue>();   // zone id -> best value
        [DataMember] public List<string> BarnFound = new List<string>();
        [DataMember] public List<string> PendingCars = new List<string>();
        [DataMember] public int DailyDay = -1;
        [DataMember] public List<KeyValue> Daily = new List<KeyValue>();          // challenge id -> progress
        [DataMember] public List<string> DailyDone = new List<string>();
        [DataMember] public float KmDriven;
    }

    [DataContract]
    public sealed class CharacterData
    {
        [DataMember] public string Model;
        [DataMember] public bool Created;
        [DataMember] public int[] Drawables, Textures, Palettes;
        [DataMember] public int[] PropIndex, PropTexture;
        [DataMember] public int HeadShape1, HeadShape2, HeadSkin1, HeadSkin2;
        [DataMember] public float ShapeMix, SkinMix;
        [DataMember] public int HairColor, HairHighlight;
    }

    [DataContract]
    public sealed class OwnedVehicle
    {
        [DataMember] public string Id;
        [DataMember] public string Model;
        [DataMember] public int ModelHash;
        [DataMember] public string Plate;
        [DataMember] public string Nickname;
        [DataMember] public int PrimaryColor, SecondaryColor, PearlColor, WheelColor, WheelType;
        [DataMember] public List<ModEntry> Mods = new List<ModEntry>();
        [DataMember] public List<int> Toggles = new List<int>();      // turbo 18, tyre smoke 20, xenon 22
        [DataMember] public int WindowTint = -1, Livery = -1;
        [DataMember] public int EcuLevel;
        [DataMember] public string EngineSound;                       // engine swap: audio game object name (null = stock)
        [DataMember] public int[] Tune;                                // tuning setup, -5..+5 per setting (see Vehicles.Tuning)
        [DataMember] public bool DriftTyres;
        [DataMember] public bool FitKit;                             // adjustable fitment kit bought
        [DataMember] public float CamberF, CamberR;                  // degrees, negative = top of wheel in
        [DataMember] public float TrackF, TrackR;                    // cm wider (+) / narrower (-) per side
        [DataMember] public float TyreF, TyreR;                      // tyre diameter multiplier, 0 = stock (1.0)
        [DataMember] public int TyreCompound;                        // 0 street, 1 sport, 2 semi-slick, 3 race slick, 4 drift
        [DataMember] public bool LetteredTyres;
        [DataMember] public float Heat;                               // police attention on this car (0-5)
        [DataMember] public int NosLevel;                             // 0 none, 1-3 nitrous kit
        [DataMember] public List<string> BuildHistory = new List<string>();
        [DataMember] public float EngineHealth = 1000f, BodyHealth = 1000f;
        [DataMember] public long PurchasePrice;
        [DataMember] public string PurchasedAtUtc;
        [DataMember] public string GarageId;     // home garage (null = no garage yet)
        [DataMember] public bool Favorite;
        [DataMember] public int ParkingSlot;     // 0 = none, n = slot n-1 in the home garage
        [DataMember] public VehicleStats Stats = new VehicleStats();
    }

    [DataContract]
    public sealed class VehicleStats
    {
        [DataMember] public double DistanceKm;
        [DataMember] public float TopSpeedKmh;
        [DataMember] public int Recoveries;
        [DataMember] public int RacesEntered, RacesWon, PoliceEscapes;
        [DataMember] public string LastUsedUtc;
        [DataMember] public long CustomizationSpent;
        [DataMember] public int ModificationCount;
        [DataMember] public int Repairs;
    }

    [DataContract] public sealed class ModEntry { [DataMember] public int Type; [DataMember] public int Index; public ModEntry() { } public ModEntry(int t, int i) { Type = t; Index = i; } }
    [DataContract] public sealed class KeyValue { [DataMember] public string Key; [DataMember] public string Value; }

    [DataContract] public sealed class OwnedGarage
    {
        [DataMember] public string Id;
        [DataMember] public int UpgradeLevel;
        [DataMember] public int Decor, Lighting, Numbering;   // CEO garage customisation (1-based, 0 = default)
        [DataMember] public string PurchasedAtUtc;
    }

    [DataContract] public sealed class LedgerEntry
    {
        [DataMember] public string Utc;
        [DataMember] public string Category;
        [DataMember] public string Description;
        [DataMember] public long Cash;
        [DataMember] public long Rep;
        [DataMember] public long BalanceAfter;
    }

    [DataContract] public sealed class RaceResult
    {
        [DataMember] public string RaceId, Name, Type, Utc, Car;
        [DataMember] public int Position, Of;      // 0 = DNF
        [DataMember] public long TimeMs, Cash, Rep, Score;
        [DataMember] public string Medal;
    }

    [DataContract] public sealed class ChampionshipProgress
    {
        [DataMember] public string Id;
        [DataMember] public int Round;              // index of the next race
        [DataMember] public bool Finished;
        [DataMember] public List<KeyValue> Points = new List<KeyValue>();   // driver -> points
    }

    [DataContract] public sealed class RivalState
    {
        [DataMember] public string Id;
        [DataMember] public int Stage, Wins, Losses;
        [DataMember] public bool Defeated;
    }

    [DataContract] public sealed class PhoneMessage
    {
        [DataMember] public string From, Subject, Body, Utc, Action;   // Action: "gps:x,y" / "race:id" / "" 
        [DataMember] public bool Read;
    }

    [DataContract] public sealed class NewsItem
    {
        [DataMember] public string Headline, Body, Utc; [DataMember] public int Day;
    }
}
