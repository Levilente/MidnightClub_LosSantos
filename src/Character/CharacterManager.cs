using System;
using GTA;
using GTA.Math;
using GTA.Native;
using MidnightClub.Core;
using MidnightClub.Persistence;

namespace MidnightClub.Character
{
    /// <summary>Everything needed to put the Story Mode player back exactly as it was.</summary>
    public sealed class StorySnapshot
    {
        public Ped StoryPed;
        public Vehicle StoryVehicle;
        public VehicleSeat StorySeat = VehicleSeat.Driver;
        public bool StoryVehicleWasPersistent;
        public Vector3 Position;
        public float Heading;
        public int WantedLevel;
        public int MaxWantedLevel;
        public bool PedWasPersistent;
    }

    /// <summary>Creates the independent Midnight Club character and hands player control between it
    /// and the untouched Story Mode ped via CHANGE_PLAYER_PED (the same mechanism the character switch uses),
    /// so the story character keeps its own weapons, clothes and state.</summary>
    public sealed class CharacterManager
    {
        private const int ComponentCount = 12, PropSlots = 8;
        public Ped MidnightPed { get; private set; }

        public StorySnapshot CaptureStory()
        {
            var ped = Game.Player.Character;
            var s = new StorySnapshot
            {
                StoryPed = ped,
                Position = ped.Position,
                Heading = ped.Heading,
                WantedLevel = Game.Player.WantedLevel,
                MaxWantedLevel = Game.MaxWantedLevel,
                PedWasPersistent = ped.IsPersistent
            };
            var v = ped.CurrentVehicle;
            if (v != null && v.Exists())
            {
                s.StoryVehicle = v;
                s.StoryVehicleWasPersistent = v.IsPersistent;
                s.StorySeat = ped.SeatIndex;
                v.IsPersistent = true;   // keep it from being cleaned up while we are away
            }
            return s;
        }

        /// <summary>Hide and freeze the story ped where it stands.</summary>
        public void ParkStoryPed(StorySnapshot s)
        {
            var p = s.StoryPed;
            if (p == null || !p.Exists()) return;
            p.IsPersistent = true;
            Function.Call(Hash.SET_ENTITY_INVINCIBLE, p, true);
            Function.Call(Hash.SET_ENTITY_VISIBLE, p, false, false);
            Function.Call(Hash.SET_ENTITY_COLLISION, p, false, false);
            Function.Call(Hash.FREEZE_ENTITY_POSITION, p, true);
            if (s.StoryVehicle != null && s.StoryVehicle.Exists())
                Function.Call(Hash.FREEZE_ENTITY_POSITION, s.StoryVehicle, true);
        }

        public void UnparkStoryPed(StorySnapshot s)
        {
            var p = s.StoryPed;
            if (p == null || !p.Exists()) return;
            Function.Call(Hash.FREEZE_ENTITY_POSITION, p, false);
            Function.Call(Hash.SET_ENTITY_COLLISION, p, true, true);
            Function.Call(Hash.SET_ENTITY_VISIBLE, p, true, false);
            Function.Call(Hash.SET_ENTITY_INVINCIBLE, p, false);
            p.IsPersistent = s.PedWasPersistent;
            if (s.StoryVehicle != null && s.StoryVehicle.Exists())
                Function.Call(Hash.FREEZE_ENTITY_POSITION, s.StoryVehicle, false);
        }

        public Ped SpawnMidnightPed(CharacterData data, string defaultModel, Vector3 pos, float heading)
        {
            var model = new Model(string.IsNullOrEmpty(data.Model) ? defaultModel : data.Model);
            if (!model.IsValid || !model.IsInCdImage) { Log.Warn("Character model invalid, falling back to mp_m_freemode_01"); model = new Model("mp_m_freemode_01"); }
            if (!model.Request(5000)) throw new Exception("Character model failed to load: " + model.Hash);

            var ped = World.CreatePed(model, pos, heading);
            model.MarkAsNoLongerNeeded();
            if (ped == null || !ped.Exists()) throw new Exception("World.CreatePed returned nothing");
            ped.IsPersistent = true;

            if (!data.Created) CreateDefaultAppearance(ped, data);
            ApplyAppearance(ped, data);
            data.Model = model.Hash == new Model("mp_f_freemode_01").Hash ? "mp_f_freemode_01" : (string.IsNullOrEmpty(data.Model) ? defaultModel : data.Model);
            MidnightPed = ped;
            return ped;
        }

        public void TakeControl(Ped ped)
        {
            Function.Call(Hash.CHANGE_PLAYER_PED, Game.Player, ped, false, false);
        }

        private static void CreateDefaultAppearance(Ped ped, CharacterData d)
        {
            Function.Call(Hash.SET_PED_DEFAULT_COMPONENT_VARIATION, ped);
            // A neutral freemode head; proper character creator comes in a later phase.
            d.HeadShape1 = 4; d.HeadShape2 = 21; d.HeadSkin1 = 4; d.HeadSkin2 = 21; d.ShapeMix = 0.5f; d.SkinMix = 0.5f;
            d.HairColor = 2; d.HairHighlight = 2;
            Function.Call(Hash.SET_PED_HEAD_BLEND_DATA, ped, d.HeadShape1, d.HeadShape2, 0, d.HeadSkin1, d.HeadSkin2, 0, d.ShapeMix, d.SkinMix, 0f, false);
            // Street-racer outfit for the default male freemode ped: hair, jacket, jeans, sneakers.
            SetComp(ped, 2, 12, 0); SetComp(ped, 11, 4, 0); SetComp(ped, 8, 15, 0); SetComp(ped, 3, 4, 0);
            SetComp(ped, 4, 4, 0); SetComp(ped, 6, 1, 0);
            Capture(ped, d);
            d.Created = true;
        }

        private static void SetComp(Ped ped, int comp, int drawable, int texture)
        {
            if (Function.Call<bool>(Hash.IS_PED_COMPONENT_VARIATION_VALID, ped, comp, drawable, texture))
                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, ped, comp, drawable, texture, 0);
        }

        public static void Capture(Ped ped, CharacterData d)
        {
            d.Drawables = new int[ComponentCount]; d.Textures = new int[ComponentCount]; d.Palettes = new int[ComponentCount];
            for (int i = 0; i < ComponentCount; i++)
            {
                d.Drawables[i] = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, ped, i);
                d.Textures[i] = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, ped, i);
                d.Palettes[i] = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, ped, i);
            }
            d.PropIndex = new int[PropSlots]; d.PropTexture = new int[PropSlots];
            for (int i = 0; i < PropSlots; i++)
            {
                d.PropIndex[i] = Function.Call<int>(Hash.GET_PED_PROP_INDEX, ped, i);
                d.PropTexture[i] = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, ped, i);
            }
        }

        private static void ApplyAppearance(Ped ped, CharacterData d)
        {
            Function.Call(Hash.SET_PED_HEAD_BLEND_DATA, ped, d.HeadShape1, d.HeadShape2, 0, d.HeadSkin1, d.HeadSkin2, 0, d.ShapeMix, d.SkinMix, 0f, false);
            Function.Call(Hash.SET_PED_HAIR_TINT, ped, d.HairColor, d.HairHighlight);
            if (d.Drawables != null)
                for (int i = 0; i < ComponentCount && i < d.Drawables.Length; i++)
                    Function.Call(Hash.SET_PED_COMPONENT_VARIATION, ped, i, d.Drawables[i], d.Textures[i], d.Palettes[i]);
            if (d.PropIndex != null)
                for (int i = 0; i < PropSlots && i < d.PropIndex.Length; i++)
                    if (d.PropIndex[i] >= 0) Function.Call(Hash.SET_PED_PROP_INDEX, ped, i, d.PropIndex[i], d.PropTexture[i], true, 0);
        }

        /// <summary>Settings that make the Midnight Club driver a "car-only" character.</summary>
        public void ApplyDriverSettings(Ped ped, bool invincible)
        {
            Function.Call(Hash.SET_PED_CAN_BE_KNOCKED_OFF_VEHICLE, ped, 1);           // never
            Function.Call(Hash.SET_PED_CONFIG_FLAG, ped, 32, false);                   // no flying through windscreen
            ped.CanBeDraggedOutOfVehicle = false;
            ped.IsInvincible = invincible;
            ped.Weapons.RemoveAll();
        }

        public void DeleteMidnightPed()
        {
            if (MidnightPed != null && MidnightPed.Exists() && MidnightPed != Game.Player.Character) MidnightPed.Delete();
            MidnightPed = null;
        }
    }
}
