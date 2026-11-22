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
        [DataMember] public List<KeyValue> Extensions = new List<KeyValue>();
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
        [DataMember] public float EngineHealth = 1000f, BodyHealth = 1000f;
        [DataMember] public long PurchasePrice;
        [DataMember] public string PurchasedAtUtc;
        [DataMember] public string GarageId;
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
    }

    [DataContract] public sealed class ModEntry { [DataMember] public int Type; [DataMember] public int Index; public ModEntry() { } public ModEntry(int t, int i) { Type = t; Index = i; } }
    [DataContract] public sealed class KeyValue { [DataMember] public string Key; [DataMember] public string Value; }
}
