using System;
using MidnightClub.Core;
using MidnightClub.Persistence;

namespace MidnightClub.Economy
{
    public sealed class MoneyChangedEvent
    {
        public long CashDelta, RepDelta; public string Description;
        public MoneyChangedEvent(long cash, long rep, string desc) { CashDelta = cash; RepDelta = rep; Description = desc; }
    }

    /// <summary>The only place Midnight Club cash/REP change. Every change is validated, deduplicated by
    /// transaction id (so a retried action or reloaded save can't pay twice), written to the ledger and saved
    /// immediately for purchases.</summary>
    public sealed class TransactionManager
    {
        private readonly SaveManager saves;
        public TransactionManager(SaveManager saves) { this.saves = saves; }

        public long Cash { get { return saves.Data == null ? 0 : saves.Data.Cash; } }
        public long Rep { get { return saves.Data == null ? 0 : saves.Data.Rep; } }
        public bool CanAfford(long amount) { return saves.Data != null && amount >= 0 && saves.Data.Cash >= amount; }

        /// <summary>Spend cash. transactionId must be unique per real-world action (null = no dedupe, e.g. repairs).</summary>
        public bool TrySpend(long amount, string category, string description, string transactionId, bool saveNow = true)
        {
            var d = saves.Data;
            if (d == null || amount < 0) return false;
            if (d.Cash < amount) return false;
            if (transactionId != null && !saves.TryBeginTransaction(transactionId)) { Log.Warn("Duplicate transaction blocked: " + transactionId); return false; }
            d.Cash -= amount;
            Record(category, description, -amount, 0);
            if (saveNow) saves.Save();
            return true;
        }

        /// <summary>Charge up to the given amount (never below zero); returns what was actually charged.</summary>
        public long ChargeUpTo(long amount, string category, string description)
        {
            var d = saves.Data; if (d == null || amount <= 0) return 0;
            long c = Math.Min(d.Cash, amount);
            d.Cash -= c;
            Record(category, description, -c, 0);
            return c;
        }

        public bool Earn(long cash, long rep, string category, string description, string transactionId)
        {
            var d = saves.Data;
            if (d == null || cash < 0 || rep < 0) return false;
            if (transactionId != null && !saves.TryBeginTransaction(transactionId)) { Log.Warn("Duplicate reward blocked: " + transactionId); return false; }
            d.Cash += cash; d.Rep += rep;
            Record(category, description, cash, rep);
            return true;
        }

        private void Record(string category, string description, long cash, long rep)
        {
            var d = saves.Data;
            d.Ledger.Add(new LedgerEntry { Utc = DateTime.UtcNow.ToString("o"), Category = category, Description = description, Cash = cash, Rep = rep, BalanceAfter = d.Cash });
            saves.IsDirty = true;
            Log.Info("Transaction [" + category + "] " + description + ": cash " + cash + ", rep " + rep + " → $" + d.Cash);
            EventBus.Publish(new MoneyChangedEvent(cash, rep, description));
        }
    }
}
