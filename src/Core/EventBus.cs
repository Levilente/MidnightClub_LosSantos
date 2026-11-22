using System;
using System.Collections.Generic;

namespace MidnightClub.Core
{
    /// <summary>Tiny synchronous publish/subscribe bus. All subsystems talk through it
    /// instead of referencing each other directly.</summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, List<Delegate>> handlers = new Dictionary<Type, List<Delegate>>();

        public static void Subscribe<T>(Action<T> handler)
        {
            List<Delegate> list;
            if (!handlers.TryGetValue(typeof(T), out list)) { list = new List<Delegate>(); handlers[typeof(T)] = list; }
            if (!list.Contains(handler)) list.Add(handler);
        }

        public static void Unsubscribe<T>(Action<T> handler)
        {
            List<Delegate> list;
            if (handlers.TryGetValue(typeof(T), out list)) list.Remove(handler);
        }

        public static void Publish<T>(T evt)
        {
            List<Delegate> list;
            if (!handlers.TryGetValue(typeof(T), out list)) return;
            foreach (var d in list.ToArray())
            {
                try { ((Action<T>)d)(evt); }
                catch (Exception ex) { Log.Error("Event handler for " + typeof(T).Name + " failed", ex); }
            }
        }

        public static void Clear() { handlers.Clear(); }
    }

    public enum GameMode { STORY_MODE, ENTERING_MIDNIGHT_CLUB, MIDNIGHT_CLUB, EXITING_MIDNIGHT_CLUB, ERROR_RECOVERY }

    public sealed class ModeChangedEvent
    {
        public GameMode From; public GameMode To;
        public ModeChangedEvent(GameMode from, GameMode to) { From = from; To = to; }
    }

    /// <summary>Raised once per tick while in Midnight Club mode (after core restrictions).</summary>
    public sealed class MidnightTickEvent { public static readonly MidnightTickEvent Instance = new MidnightTickEvent(); }

    /// <summary>Raised right before the Midnight Club profile is written to disk.</summary>
    public sealed class BeforeSaveEvent { public static readonly BeforeSaveEvent Instance = new BeforeSaveEvent(); }

    public sealed class GarageEnteredEvent { }
    public sealed class ShowroomOpenedEvent { }
    public sealed class WorkshopPurchaseEvent { public string What; public WorkshopPurchaseEvent(string w) { What = w; } }
    public sealed class MeetShowoffEvent { }
    public sealed class StyleEvent { public string Name; public int Points; public StyleEvent(string n, int p) { Name = n; Points = p; } }
}
