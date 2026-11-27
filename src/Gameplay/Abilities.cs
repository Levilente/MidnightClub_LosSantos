using System;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Cameras;
using MidnightClub.Career;
using MidnightClub.Core;
using MidnightClub.Persistence;
using MidnightClub.Vehicles;
using Font = GTA.UI.Font;

namespace MidnightClub.Gameplay
{
    /// <summary>Nitrous (workshop kit, Sprint key) and Midnight Club special abilities on the ability key
    /// (Caps Lock / L3+R3): Zone (slow motion), Agro (ram through traffic), Roar (shockwave that clears the road).
    /// Abilities unlock with career tiers and run on the style meter.</summary>
    public sealed class Abilities
    {
        public static readonly string[] Names = { "Zone", "Agro", "Roar" };
        public static readonly int[] UnlockTier = { 2, 3, 4 };
        private readonly SaveManager saves;
        private readonly VehicleManager vehicles;
        private float nos = 1f; private bool nosOn, fxNos;
        public static float NosFill { get { return Instance != null ? Instance.nos : 0f; } }
        public static bool NosActive { get { return Instance != null && Instance.nosOn; } }
        private string active; private float activeLeft; private bool fxZone;

        public static Abilities Instance;
        public Abilities(SaveManager saves, VehicleManager vehicles)
        {
            this.saves = saves; this.vehicles = vehicles; Instance = this;
            EventBus.Subscribe<MidnightTickEvent>(e => Tick());
            EventBus.Subscribe<ModeChangedEvent>(e => { if (e.To != GameMode.MIDNIGHT_CLUB) StopAll(); });
            EventBus.Subscribe<Career.TierUpEvent>(e =>
            {
                for (int i = 0; i < Names.Length; i++) if (UnlockTier[i] == e.Tier)
                    { saves.Data.SelectedAbility = Names[i]; UI.Toast.Post("~p~New special ability: " + Names[i].ToUpperInvariant() + "~s~. Press Caps Lock when the meter is full."); }
            });
        }

        private int Tier { get { return CareerManager.Instance != null ? CareerManager.Instance.Tier : 1; } }
        public bool Unlocked(string name) { int i = Array.IndexOf(Names, name); return i >= 0 && Tier >= UnlockTier[i]; }
        public string Selected
        {
            get { var s = saves.Data.SelectedAbility; if (s != null && Unlocked(s)) return s; return Names.LastOrDefault(Unlocked); }
            set { saves.Data.SelectedAbility = value; saves.IsDirty = true; }
        }

        public UI.Menu AbilityMenu()
        {
            var m = new UI.Menu("Special ability", "Fill the meter with style, press Caps Lock");
            m.OnOpen = () =>
            {
                m.Items.Clear();
                for (int i = 0; i < Names.Length; i++)
                {
                    string n = Names[i]; int t = UnlockTier[i];
                    string desc = n == "Zone" ? "Slow motion for 6 s: thread through traffic." : n == "Agro" ? "7 s of extra power; cars in front get thrown aside." : "A shockwave that blasts traffic out of the road ahead.";
                    m.Add(new UI.MenuItem(n, () => { if (Unlocked(n)) Selected = n; }) { Enabled = Unlocked(n), Value = () => !Unlocked(n) ? "Tier " + t : Selected == n ? "~g~SELECTED" : "", Description = () => desc });
                }
            };
            return m;
        }

        private void StopAll()
        {
            Game.TimeScale = 1f; active = null;
            if (fxZone) { Screen.StopEffect(ScreenEffect.FocusIn); fxZone = false; }
            if (fxNos) { Screen.StopEffect(ScreenEffect.RaceTurbo); fxNos = false; }
        }

        private void Tick()
        {
            var car = vehicles.Current; var rec = vehicles.CurrentRecord;
            if (car == null || !car.Exists() || rec == null) { if (active != null || fxNos) StopAll(); return; }
            float dt = Game.LastFrameTime / Math.Max(0.1f, Game.TimeScale);   // real time, even in slow motion
            Nitrous(car, rec, dt);
            Special(car, dt);
            if (StyleSystem.Instance != null && StyleSystem.Instance.Slipstreaming && !nosOn)
                Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, car, 1.12f + 0.06f * rec.EcuLevel);
            DrawBars(rec);
        }

        // ------------------------------------------------------------------ nitrous
        private void Nitrous(Vehicle car, OwnedVehicle rec, float dt)
        {
            if (rec.NosLevel <= 0) return;
            var nosControl = Game.LastInputMethod == InputMethod.GamePad ? Control.VehicleRocketBoost : Control.Sprint;
            Game.DisableControlThisFrame(Control.VehicleRocketBoost);
            bool want = Game.IsControlPressed(nosControl) && car.Speed > 3f && !car.IsInAir;
            float burn = 1f / (3.5f + rec.NosLevel * 1.5f);                 // full tank lasts 5 / 6.5 / 8 s
            if (want && nos > 0.02f)
            {
                nosOn = true;
                nos = Math.Max(0f, nos - burn * dt);
                Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, car, 1.6f + 0.35f * rec.NosLevel + 0.06f * rec.EcuLevel);
                car.ApplyForceRelative(new Vector3(0f, 0.3f + 0.15f * rec.NosLevel, 0f), Vector3.Zero, ForceType.ExternalForce);
                CameraSystem.BoostFov = 12f; CameraSystem.BoostShake = 0.6f;
                if (!fxNos) { Screen.StartEffect(ScreenEffect.RaceTurbo, 0, true); fxNos = true; Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Boost", "DLC_IE_Stunt_Race_Sounds", true); }
            }
            else
            {
                nosOn = false;
                nos = Math.Min(1f, nos + dt / (14f - rec.NosLevel * 2f));       // refills in 12 / 10 / 8 s
                if (fxNos) { Screen.StopEffect(ScreenEffect.RaceTurbo); fxNos = false; }
            }
        }

        // ------------------------------------------------------------------ special abilities
        private void Special(Vehicle car, float dt)
        {
            var style = StyleSystem.Instance; if (style == null) return;
            string sel = Selected;
            bool press = UI.Input.JustPressed(Control.SpecialAbilityPC) || UI.Input.JustPressed(Control.SpecialAbility);
            if (active == null && press && sel != null)
            {
                if (style.Meter < 0.999f) { UI.Toast.Post(sel + " isn't charged yet: drive with style."); }
                else
                {
                    active = sel; activeLeft = sel == "Zone" ? 6f : sel == "Agro" ? 7f : 0.5f; style.Meter = 0f;
                    Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Hit_Out", "PLAYER_SWITCH_CUSTOM_SOUNDSET", true);
                    if (sel == "Roar") Roar(car);
                }
            }
            if (active == null) return;
            activeLeft -= dt;
            switch (active)
            {
                case "Zone":
                    Game.TimeScale = 0.45f;
                    if (!fxZone) { Screen.StartEffect(ScreenEffect.FocusIn, 0, true); fxZone = true; }
                    break;
                case "Agro":
                    Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, car, 1.35f);
                    CameraSystem.BoostShake = 0.4f;
                    foreach (var o in World.GetNearbyVehicles(car.Position, 5.5f))
                    {
                        if (o == car || !o.Exists()) continue;
                        var off = car.GetPositionOffset(o.Position);
                        if (off.Y < 0f) continue;
                        var push = Vector3.Normalize(o.Position - car.Position) * (6f + car.Speed * 0.4f) + new Vector3(0, 0, 2.5f);
                        o.ApplyForce(push, Vector3.Zero, ForceType.ExternalImpulse);
                    }
                    break;
            }
            if (activeLeft <= 0f) { Game.TimeScale = 1f; if (fxZone) { Screen.StopEffect(ScreenEffect.FocusIn); fxZone = false; } active = null; }
        }

        private static void Roar(Vehicle car)
        {
            CameraSystem.BoostShake = 1.5f;
            foreach (var o in World.GetNearbyVehicles(car.Position, 35f))
            {
                if (o == car || !o.Exists()) continue;
                var off = car.GetPositionOffset(o.Position);
                if (off.Y < -2f) continue;                                     // only ahead and beside
                var dir = Vector3.Normalize(o.Position - car.Position);
                float strength = 18f * (1f - Math.Min(1f, off.Length() / 35f)) + 6f;
                o.ApplyForce(dir * strength + new Vector3(0, 0, 3f), Vector3.Zero, ForceType.ExternalImpulse);
            }
        }

        private void DrawBars(OwnedVehicle rec)
        {
            // NOS is drawn by the speedometer; this is the special-ability meter row under it (same style)
            var style = StyleSystem.Instance;
            if (style != null && Selected != null && UI.MainHUD.RowY > 0f)
            {
                float x = UI.MainHUD.RowX, w = UI.MainHUD.RowW, y = UI.MainHUD.RowY;
                bool full = style.Meter >= 0.999f;
                var c = full ? UI.Ds.Mix(UI.Ds.Amber, Color.White, UI.Ds.Pulse(2f) * 0.5f) : Color.White;
                UI.Ds.T((active ?? Selected).ToUpperInvariant() + (full && active == null ? "  READY" : ""), x, y - 0.012f, 0.3f, full ? UI.Ds.Amber : Color.FromArgb(230, 255, 255, 255), UI.Ds.Display);
                UI.Ds.Rect(x + UI.Ds.Px(0.065f), y, w - UI.Ds.Px(0.065f), 0.006f, Color.FromArgb(70, 255, 255, 255));
                if (style.Meter > 0.01f) UI.Ds.Rect(x + UI.Ds.Px(0.065f), y, (w - UI.Ds.Px(0.065f)) * style.Meter, 0.006f, c);
            }
            UI.MainHUD.RowY = 0f;
        }

        private static void Rect(float x, float y, float w, float h, Color c) { Function.Call(Hash.DRAW_RECT, x + w / 2f, y + h / 2f, w, h, c.R, c.G, c.B, c.A, false); }
        private static void Txt(string s, float x, float y, Alignment a)
        { new TextElement(s, new PointF(x * 1280f, y * 720f), 0.24f, Color.White, Font.ChaletLondon, a, true, true).ScaledDraw(); }
    }
}
