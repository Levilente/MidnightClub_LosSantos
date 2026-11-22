using GTA;
using MidnightClub.Core;
using MidnightClub.Persistence;

namespace MidnightClub.WorldSim
{
    public sealed class NewDayEvent { public int Day; public NewDayEvent(int d) { Day = d; } }

    /// <summary>Midnight Club's own day counter. Advances when the in-game clock passes midnight while in
    /// Midnight Club Mode and is stored in the profile, so rotations can't be rerolled by reloading or by
    /// changing the time in Story Mode.</summary>
    public sealed class WorldCalendar
    {
        private readonly SaveManager saves;
        private int nextCheck;
        public static readonly string[] DayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        public WorldCalendar(SaveManager saves)
        {
            this.saves = saves;
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To == GameMode.MIDNIGHT_CLUB && saves.Data != null) saves.Data.LastClockHour = GTA.Chrono.GameClock.Hour; });
        }

        public int Day { get { return saves.Data == null ? 0 : saves.Data.CalendarDay; } }
        public string DayName { get { return DayNames[Day % 7]; } }
        public bool IsWeekend { get { return Day % 7 >= 5; } }
        public int Hour { get { return GTA.Chrono.GameClock.Hour; } }
        public bool IsNight { get { int h = Hour; return h >= 21 || h < 5; } }

        private void Tick()
        {
            if (Game.GameTime < nextCheck || saves.Data == null) return;
            nextCheck = Game.GameTime + 1000;            // cheap: once per second
            int h = GTA.Chrono.GameClock.Hour;
            int last = saves.Data.LastClockHour;
            saves.Data.LastClockHour = h;
            if (last >= 0 && h < last)                    // wrapped past midnight
            {
                saves.Data.CalendarDay++;
                saves.IsDirty = true;
                Log.Info("New Midnight Club day " + saves.Data.CalendarDay + " (" + DayName + ")");
                EventBus.Publish(new NewDayEvent(saves.Data.CalendarDay));
            }
        }
    }
}
