using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using MidnightClub.Core;

namespace MidnightClub.Persistence
{
    /// <summary>Independent Midnight Club save. Lives only in scripts\MidnightClub\saves and never
    /// touches GTA V Story Mode save files.</summary>
    public sealed class SaveManager
    {
        private readonly string dir, mainPath;
        private readonly int backupCount;
        private static readonly DataContractJsonSerializer serializer =
            new DataContractJsonSerializer(typeof(SaveData), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public SaveData Data { get; private set; }
        public bool IsDirty { get; set; }

        public SaveManager(string baseDir, int backups)
        {
            dir = Path.Combine(baseDir, "saves");
            Directory.CreateDirectory(dir);
            mainPath = Path.Combine(dir, "profile.json");
            backupCount = Math.Max(1, backups);
        }

        /// <summary>Loads the profile, falling back to the newest valid backup. Returns false if a new profile was created.</summary>
        public bool Load()
        {
            var candidates = new[] { mainPath }.Concat(
                Directory.GetFiles(dir, "profile.*.bak").OrderByDescending(f => File.GetLastWriteTimeUtc(f)));
            foreach (var file in candidates)
            {
                if (!File.Exists(file)) continue;
                try
                {
                    SaveData d;
                    using (var fs = File.OpenRead(file)) d = (SaveData)serializer.ReadObject(fs);
                    if (!Validate(d)) { Log.Warn("Save failed validation: " + file); continue; }
                    Migrate(d);
                    Data = d;
                    if (file != mainPath) { Log.Warn("Main save unreadable, recovered from backup " + file); IsDirty = true; }
                    Log.Info("Loaded profile rev " + d.Revision + " from " + Path.GetFileName(file));
                    return true;
                }
                catch (Exception ex) { Log.Error("Could not read save " + file, ex); }
            }
            Data = null;
            return false;
        }

        public void CreateNew(SaveData fresh)
        {
            fresh.FormatVersion = Core.Version.SaveFormat;
            fresh.ProfileId = Guid.NewGuid().ToString("N");
            Data = fresh;
            IsDirty = true;
        }

        private static bool Validate(SaveData d)
        {
            if (d == null || d.FormatVersion <= 0 || d.FormatVersion > Core.Version.SaveFormat) return false;
            if (d.Cash < 0 || d.Rep < 0) return false;
            if (d.Vehicles == null || d.Character == null) return false;
            return true;
        }

        private static void Migrate(SaveData d)
        {
            // Future format upgrades go here: if (d.FormatVersion == 1) { ...; d.FormatVersion = 2; }
            foreach (var v in d.Vehicles) { if (v.Mods == null) v.Mods = new System.Collections.Generic.List<ModEntry>(); if (v.Parts == null) v.Parts = new System.Collections.Generic.List<string>(); if (v.Stats == null) v.Stats = new VehicleStats(); if (v.Toggles == null) v.Toggles = new System.Collections.Generic.List<int>(); if (v.BuildHistory == null) v.BuildHistory = new System.Collections.Generic.List<string>(); if (v.DriftTyres && v.TyreCompound == 0) { v.TyreCompound = 4; v.DriftTyres = false; } }
            if (d.WeeklyProgress == null) d.WeeklyProgress = new System.Collections.Generic.List<KeyValue>();
            if (d.WeeklyDone == null) d.WeeklyDone = new System.Collections.Generic.List<string>();
            if (d.Licenses == null) d.Licenses = new System.Collections.Generic.List<KeyValue>();
            if (d.DisciplineXp == null) d.DisciplineXp = new System.Collections.Generic.List<KeyValue>();
            if (d.StoryCounters == null) d.StoryCounters = new System.Collections.Generic.List<KeyValue>();
            if (d.SeriesWon == null) d.SeriesWon = new System.Collections.Generic.List<string>();
            if (d.CollectionRewards == null) d.CollectionRewards = new System.Collections.Generic.List<string>();
            if (d.Turf == null) d.Turf = new System.Collections.Generic.List<KeyValue>();
            if (d.SpeedTrapRecords == null) d.SpeedTrapRecords = new System.Collections.Generic.List<KeyValue>();
            if (d.MeetShowoffs == null) d.MeetShowoffs = new System.Collections.Generic.List<string>();
            if (d.Messages == null) d.Messages = new System.Collections.Generic.List<PhoneMessage>();
            if (d.News == null) d.News = new System.Collections.Generic.List<NewsItem>();
            if (d.ObjectivesDone == null) d.ObjectivesDone = new System.Collections.Generic.List<string>();
            if (d.MilestonesDone == null) d.MilestonesDone = new System.Collections.Generic.List<string>();
            if (d.Rivals == null) d.Rivals = new System.Collections.Generic.List<RivalState>();
            if (d.ClubRep == null) d.ClubRep = new System.Collections.Generic.List<KeyValue>();
            if (d.CareerTierNotified <= 0) d.CareerTierNotified = 1;
            if (d.RaceHistory == null) d.RaceHistory = new System.Collections.Generic.List<RaceResult>();
            if (d.BestTimes == null) d.BestTimes = new System.Collections.Generic.List<KeyValue>();
            if (d.Championships == null) d.Championships = new System.Collections.Generic.List<ChampionshipProgress>();
            if (d.SoldStock == null) d.SoldStock = new System.Collections.Generic.List<string>();
            if (d.Garages == null) d.Garages = new System.Collections.Generic.List<OwnedGarage>();
            if (d.Ledger == null) d.Ledger = new System.Collections.Generic.List<LedgerEntry>();
            if (d.ProcessedTransactions == null) d.ProcessedTransactions = new System.Collections.Generic.List<string>();
            if (d.Extensions == null) d.Extensions = new System.Collections.Generic.List<KeyValue>();
            if (d.Discovered == null) d.Discovered = new System.Collections.Generic.List<string>();
            if (d.RoadMilestones == null) d.RoadMilestones = new System.Collections.Generic.List<int>();
            if (d.SkillRecords == null) d.SkillRecords = new System.Collections.Generic.List<KeyValue>();
            if (d.BarnFound == null) d.BarnFound = new System.Collections.Generic.List<string>();
            if (d.PendingCars == null) d.PendingCars = new System.Collections.Generic.List<string>();
            if (d.Daily == null) d.Daily = new System.Collections.Generic.List<KeyValue>();
            if (d.DailyDone == null) d.DailyDone = new System.Collections.Generic.List<string>();
            if (d.LevelRewarded < 1) d.LevelRewarded = 1;
        }

        /// <summary>Atomic write: serialize to .tmp, then swap in, keeping a rotating backup of the previous file.</summary>
        public bool Save()
        {
            if (Data == null) return false;
            try
            {
                EventBus.Publish(BeforeSaveEvent.Instance);
                Data.Revision++;
                Data.ModVersion = Core.Version.Text;
                Data.SavedAtUtc = DateTime.UtcNow.ToString("o");
                if (Data.Messages.Count > 50) Data.Messages.RemoveRange(0, Data.Messages.Count - 50);
                if (Data.News.Count > 30) Data.News.RemoveRange(0, Data.News.Count - 30);
                if (Data.MeetShowoffs.Count > 60) Data.MeetShowoffs.RemoveRange(0, Data.MeetShowoffs.Count - 60);
                if (Data.RaceHistory.Count > 200) Data.RaceHistory.RemoveRange(0, Data.RaceHistory.Count - 200);
                if (Data.SoldStock.Count > 300) Data.SoldStock.RemoveRange(0, Data.SoldStock.Count - 300);
                if (Data.Ledger.Count > 200) Data.Ledger.RemoveRange(0, Data.Ledger.Count - 200);
                if (Data.ProcessedTransactions.Count > 500)
                    Data.ProcessedTransactions.RemoveRange(0, Data.ProcessedTransactions.Count - 500);

                var tmp = mainPath + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    serializer.WriteObject(fs, Data);
                    fs.Flush(true);
                }
                // Verify what we wrote can be read back before replacing the good file.
                using (var fs = File.OpenRead(tmp)) if (!Validate((SaveData)serializer.ReadObject(fs))) throw new IOException("Written save failed validation");

                if (File.Exists(mainPath))
                {
                    var bak = Path.Combine(dir, "profile." + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".bak");
                    File.Replace(tmp, mainPath, bak, true);
                    PruneBackups();
                }
                else File.Move(tmp, mainPath);

                IsDirty = false;
                return true;
            }
            catch (Exception ex) { Log.Error("Save failed", ex); return false; }
        }

        private void PruneBackups()
        {
            foreach (var old in Directory.GetFiles(dir, "profile.*.bak").OrderByDescending(f => File.GetLastWriteTimeUtc(f)).Skip(backupCount))
                try { File.Delete(old); } catch { }
        }

        /// <summary>Duplicate-transaction guard for purchases, rewards etc.</summary>
        public bool TryBeginTransaction(string transactionId)
        {
            if (Data == null || Data.ProcessedTransactions.Contains(transactionId)) return false;
            Data.ProcessedTransactions.Add(transactionId);
            IsDirty = true;
            return true;
        }
    }
}
