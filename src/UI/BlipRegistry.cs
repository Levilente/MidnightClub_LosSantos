using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using MidnightClub.Core;

namespace MidnightClub.UI
{
    /// <summary>Midnight Club owns the map: every blip the mod creates is registered here; all other blips (Story
    /// missions, vanilla shops, LS Customs...) are hidden while in Midnight Club and restored on the way out.</summary>
    public static class BlipRegistry
    {
        private static readonly HashSet<int> ours = new HashSet<int>();
        private static readonly Dictionary<int, BlipDisplayType> hidden = new Dictionary<int, BlipDisplayType>();
        private static readonly Dictionary<int, int> alphas = new Dictionary<int, int>();
        private static int next; private static bool active;

        public static Blip Own(Blip b) { if (b != null) ours.Add(b.Handle); return b; }

        public static void Init()
        {
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To == GameMode.MIDNIGHT_CLUB) { active = true; next = 0; } else if (e.To == GameMode.STORY_MODE || e.To == GameMode.EXITING_MIDNIGHT_CLUB) RestoreAll(); });
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
        }

        private static void Tick()
        {
            if (!active || Game.GameTime < next) return;
            next = Game.GameTime + 1000;
            try
            {
                foreach (var b in World.GetAllBlips())
                {
                    if (b == null || !b.Exists()) continue;
                    int h = b.Handle;
                    if (ours.Contains(h)) continue;
                    var s = b.Sprite;
                    if (s == BlipSprite.Waypoint || s == BlipSprite.Player || s == BlipSprite.North || s == BlipSprite.PoliceOfficer || s == BlipSprite.PoliceCar || s == BlipSprite.PoliceHelicopterAnimated) continue;
                    // the game's scripts keep re-showing their blips, so this is re-applied every second
                    if (!hidden.ContainsKey(h)) { hidden[h] = b.DisplayType; alphas[h] = b.Alpha; }
                    if (b.DisplayType != BlipDisplayType.NoDisplay) b.DisplayType = BlipDisplayType.NoDisplay;
                    if (b.Alpha != 0) b.Alpha = 0;
                }
            }
            catch (Exception ex) { Log.Error("Blip hiding", ex); active = false; }
        }

        private static void RestoreAll()
        {
            active = false;
            foreach (var kv in hidden)
                try { var b = new Blip(kv.Key); if (b.Exists()) { b.DisplayType = kv.Value; int a; if (alphas.TryGetValue(kv.Key, out a)) b.Alpha = a; } } catch { }
            hidden.Clear(); alphas.Clear();
        }
    }
}
