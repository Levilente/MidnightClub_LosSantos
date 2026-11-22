using System;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Core;
using MidnightClub.Persistence;
using MidnightClub.UI;

namespace MidnightClub.Character
{
    /// <summary>Blocking character creator (runs inside a transition or from the Midnight Club menu).
    /// Edits the live Midnight Club ped and writes the result to CharacterData.</summary>
    public sealed class CharacterCreator
    {
        private readonly CharacterManager characters;
        private Camera cam;

        public CharacterCreator(CharacterManager characters) { this.characters = characters; }

        public void Run(CharacterData data, bool firstTime)
        {
            var ped = characters.MidnightPed;
            if (ped == null || !ped.Exists()) return;
            if (Cameras.CameraSystem.Instance != null) Cameras.CameraSystem.Instance.Release();
            bool frozeHere = !ped.IsInVehicle();
            if (frozeHere) Function.Call(Hash.FREEZE_ENTITY_POSITION, ped, true);
            if (data.Drawables == null) CharacterManager.Capture(ped, data);

            var stack = new MenuStack();
            bool done = false;
            var menu = new Menu("Midnight Club", firstTime ? "Create your driver" : "Edit your driver");
            menu.OnBack = () => { if (!firstTime) done = true; else Screen.ShowSubtitle("Select ~p~Hit the streets~s~ to finish.", 2000); };
            Build(menu, data, () => done = true);
            stack.Open(menu);

            SetupCamera(ped);
            try
            {
                while (!done)
                {
                    ped = characters.MidnightPed;
                    if (ped == null || !ped.Exists()) break;
                    Game.DisableAllControlsThisFrame();
                    foreach (var c in new[] { Control.FrontendUp, Control.FrontendDown, Control.FrontendLeft, Control.FrontendRight, Control.FrontendAccept, Control.FrontendCancel })
                        Game.EnableControlThisFrame(c);
                    Hud.HideComponentThisFrame(HudComponent.HelpText);
                    stack.Tick();
                    Script.Wait(0);
                }
            }
            finally
            {
                ped = characters.MidnightPed;
                if (ped != null && ped.Exists())
                {
                    CharacterManager.Capture(ped, data);
                    if (frozeHere) Function.Call(Hash.FREEZE_ENTITY_POSITION, ped, false);
                }
                data.Created = true;
                DestroyCamera();
            }
        }

        private void Build(Menu m, CharacterData d, Action finish)
        {
            Func<Ped> P = () => characters.MidnightPed;
            m.Add(new MenuItem("Sex") { Value = () => CharacterManager.IsFemale(P()) ? "Female" : "Male",
                OnAdjust = dir => { characters.ChangeSex(d, !CharacterManager.IsFemale(P())); CharacterManager.Capture(P(), d); SetupCamera(P()); } });
            m.Add(Blend("Face (parent 1)", () => d.HeadShape1, v => d.HeadShape1 = v, 46, d));
            m.Add(Blend("Face (parent 2)", () => d.HeadShape2, v => d.HeadShape2 = v, 46, d));
            m.Add(new MenuItem("Resemblance") { Value = () => Math.Round(d.ShapeMix * 100) + "%",
                OnAdjust = dir => { d.ShapeMix = Clamp01(d.ShapeMix + dir * 0.1f); ApplyBlend(d); } });
            m.Add(Blend("Skin (parent 1)", () => d.HeadSkin1, v => d.HeadSkin1 = v, 46, d));
            m.Add(Blend("Skin (parent 2)", () => d.HeadSkin2, v => d.HeadSkin2 = v, 46, d));
            m.Add(new MenuItem("Skin mix") { Value = () => Math.Round(d.SkinMix * 100) + "%",
                OnAdjust = dir => { d.SkinMix = Clamp01(d.SkinMix + dir * 0.1f); ApplyBlend(d); } });
            m.Add(Component("Hair", 2, false, d));
            m.Add(new MenuItem("Hair colour") { Value = () => (d.HairColor + 1).ToString(),
                OnAdjust = dir => { d.HairColor = Wrap(d.HairColor + dir, HairTints()); Function.Call(Hash.SET_PED_HAIR_TINT, P(), d.HairColor, d.HairHighlight); } });
            m.Add(new MenuItem("Hair highlight") { Value = () => (d.HairHighlight + 1).ToString(),
                OnAdjust = dir => { d.HairHighlight = Wrap(d.HairHighlight + dir, HairTints()); Function.Call(Hash.SET_PED_HAIR_TINT, P(), d.HairColor, d.HairHighlight); } });
            m.Add(Component("Top", 11, false, d)); m.Add(Component("Top colour", 11, true, d));
            m.Add(Component("Undershirt", 8, false, d)); m.Add(Component("Undershirt colour", 8, true, d));
            m.Add(Component("Arms", 3, false, d));
            m.Add(Component("Legs", 4, false, d)); m.Add(Component("Legs colour", 4, true, d));
            m.Add(Component("Shoes", 6, false, d)); m.Add(Component("Shoes colour", 6, true, d));
            m.Add(Prop("Hat", 0)); m.Add(Prop("Glasses", 1));
            m.Add(new MenuItem("~p~Hit the streets", finish) { Description = () => "Save your driver and continue." });
        }

        private int HairTints() { int n = Function.Call<int>(Hash.GET_NUM_PED_HAIR_TINTS); return n > 0 ? n : 64; }

        private MenuItem Blend(string label, Func<int> get, Action<int> set, int count, CharacterData d)
        {
            return new MenuItem(label) { Value = () => (get() + 1).ToString(), OnAdjust = dir => { set(Wrap(get() + dir, count)); ApplyBlend(d); } };
        }

        private void ApplyBlend(CharacterData d)
        {
            Function.Call(Hash.SET_PED_HEAD_BLEND_DATA, characters.MidnightPed, d.HeadShape1, d.HeadShape2, 0, d.HeadSkin1, d.HeadSkin2, 0, d.ShapeMix, d.SkinMix, 0f, false);
        }

        /// <summary>Clothing component: style (drawable) or colour (texture). Invalid combinations are skipped.</summary>
        private MenuItem Component(string label, int comp, bool texture, CharacterData d)
        {
            return new MenuItem(label)
            {
                Value = () =>
                {
                    var p = characters.MidnightPed;
                    int dr = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, p, comp);
                    if (!texture) return (dr + 1) + "/" + Function.Call<int>(Hash.GET_NUMBER_OF_PED_DRAWABLE_VARIATIONS, p, comp);
                    return (Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, p, comp) + 1) + "/" + Math.Max(1, Function.Call<int>(Hash.GET_NUMBER_OF_PED_TEXTURE_VARIATIONS, p, comp, dr));
                },
                OnAdjust = dir =>
                {
                    var p = characters.MidnightPed;
                    int dr = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, p, comp);
                    int tx = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, p, comp);
                    if (texture)
                    {
                        int n = Math.Max(1, Function.Call<int>(Hash.GET_NUMBER_OF_PED_TEXTURE_VARIATIONS, p, comp, dr));
                        tx = Wrap(tx + dir, n);
                        CharacterManager.SetComp(p, comp, dr, tx);
                        return;
                    }
                    int count = Function.Call<int>(Hash.GET_NUMBER_OF_PED_DRAWABLE_VARIATIONS, p, comp);
                    for (int tries = 0; tries < count; tries++)   // skip drawables that are invalid for this model
                    {
                        dr = Wrap(dr + dir, count);
                        if (Function.Call<bool>(Hash.IS_PED_COMPONENT_VARIATION_VALID, p, comp, dr, 0)) { CharacterManager.SetComp(p, comp, dr, 0); break; }
                    }
                }
            };
        }

        private MenuItem Prop(string label, int slot)
        {
            return new MenuItem(label)
            {
                Value = () => { int i = Function.Call<int>(Hash.GET_PED_PROP_INDEX, characters.MidnightPed, slot); return i < 0 ? "None" : (i + 1).ToString(); },
                OnAdjust = dir =>
                {
                    var p = characters.MidnightPed;
                    int count = Function.Call<int>(Hash.GET_NUMBER_OF_PED_PROP_DRAWABLE_VARIATIONS, p, slot);
                    int i = Function.Call<int>(Hash.GET_PED_PROP_INDEX, p, slot) + 1;   // 0 = none
                    i = Wrap(i + dir, count + 1) - 1;
                    if (i < 0) Function.Call(Hash.CLEAR_PED_PROP, p, slot, 0);
                    else Function.Call(Hash.SET_PED_PROP_INDEX, p, slot, i, 0, true, 0);
                }
            };
        }

        private void SetupCamera(Ped ped)
        {
            DestroyCamera();
            var pos = ped.GetOffsetPosition(new Vector3(0.6f, 1.9f, 0.45f));
            cam = Camera.Create(ScriptedCameraNameHash.DefaultScriptedCamera, pos, Vector3.Zero, 40f, true, EulerRotationOrder.YXZ);
            cam.PointAt(ped, new Vector3(0f, 0f, 0.25f));
            cam.IsActive = true;
            Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false, 0);
        }

        public void DestroyCamera()
        {
            if (cam == null) return;
            Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0);
            cam.IsActive = false; cam.Delete(); cam = null;
        }

        private static int Wrap(int v, int n) { if (n <= 0) return 0; return ((v % n) + n) % n; }
        private static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    }
}
