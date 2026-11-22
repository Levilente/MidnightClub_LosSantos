using System;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using MidnightClub.Character;
using MidnightClub.Persistence;
using MidnightClub.Vehicles;

namespace MidnightClub.Core
{
    /// <summary>State machine: STORY_MODE -> ENTERING -> MIDNIGHT_CLUB -> EXITING -> STORY_MODE,
    /// with ERROR_RECOVERY restoring Story Mode if any transition step fails.</summary>
    public sealed class GameModeManager
    {
        public GameMode State { get; private set; } = GameMode.STORY_MODE;
        public bool InMidnightClub { get { return State == GameMode.MIDNIGHT_CLUB; } }

        private readonly Config cfg;
        private readonly SaveManager saves;
        private readonly CharacterManager characters;
        private readonly VehicleManager vehicles;
        private StorySnapshot story;
        private int lastAutoSave;
        private int onFootSince = -1;

        public GameModeManager(Config cfg, SaveManager saves, CharacterManager characters, VehicleManager vehicles)
        {
            this.cfg = cfg; this.saves = saves; this.characters = characters; this.vehicles = vehicles;
        }

        private void SetState(GameMode next)
        {
            var prev = State; State = next;
            Log.Info("Mode " + prev + " -> " + next);
            EventBus.Publish(new ModeChangedEvent(prev, next));
        }

        public void Toggle()
        {
            if (State == GameMode.STORY_MODE) Enter();
            else if (State == GameMode.MIDNIGHT_CLUB) Exit();
            // Any other state = a transition is already running; ignore the key (prevents double init).
        }

        private string WhyCannotEnter()
        {
            var p = Game.Player.Character;
            if (Game.IsCutsceneActive) return "a cutscene is playing";
            if (Game.IsMissionActive) return "a Story Mode mission is active";
            if (Function.Call<bool>(Hash.IS_PLAYER_SWITCH_IN_PROGRESS)) return "a character switch is in progress";
            if (p == null || !p.Exists() || p.IsDead || Game.Player.IsDead) return "the player is not alive";
            if (Game.Player.WantedLevel > 0) return "you are wanted by the police";
            return null;
        }

        // ---------------------------------------------------------------- enter
        public void Enter()
        {
            var reason = WhyCannotEnter();
            if (reason != null) { Notification.PostTicker("~r~Midnight Club~s~ unavailable: " + reason + ".", true, false); return; }

            SetState(GameMode.ENTERING_MIDNIGHT_CLUB);
            try
            {
                Screen.FadeOut(cfg.TransitionFadeMs);
                Script.Wait(cfg.TransitionFadeMs + 100);

                story = characters.CaptureStory();
                Log.Info("Story snapshot: ped " + story.StoryPed.Handle + " at " + story.Position + (story.StoryVehicle != null ? " in vehicle " + story.StoryVehicle.Handle : ""));

                bool isNew = !saves.Load();
                if (isNew) saves.CreateNew(NewProfile());
                var data = saves.Data;

                Vector3 pos; float heading;
                if (data.HasPosition) { pos = new Vector3(data.PosX, data.PosY, data.PosZ); heading = data.Heading; }
                else
                {
                    pos = World.GetNextPositionOnStreetWithHeading(new Vector3(cfg.SpawnX, cfg.SpawnY, cfg.SpawnZ), out heading, false);
                    if (pos == Vector3.Zero) { pos = new Vector3(cfg.SpawnX, cfg.SpawnY, cfg.SpawnZ); heading = 0f; }
                }

                // Load the area around the Midnight Club position before spawning there.
                Function.Call(Hash.NEW_LOAD_SCENE_START_SPHERE, pos.X, pos.Y, pos.Z, 120f, 0);
                int t0 = Game.GameTime;
                while (!Function.Call<bool>(Hash.IS_NEW_LOAD_SCENE_LOADED) && Game.GameTime - t0 < 6000) Script.Wait(50);
                Function.Call(Hash.NEW_LOAD_SCENE_STOP);

                var ped = characters.SpawnMidnightPed(data.Character, cfg.CharacterModel, pos, heading);
                Game.Player.WantedLevel = 0;
                characters.TakeControl(ped);
                Script.Wait(0);
                characters.ParkStoryPed(story);

                var rec = CurrentRecord(data);
                var car = vehicles.Spawn(rec, pos, heading);
                ped.SetIntoVehicle(car, VehicleSeat.Driver);
                characters.ApplyDriverSettings(ped, cfg.PlayerInvincible);
                car.IsEngineRunning = true;
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, car, true, true, false);

                if (cfg.DisableStoryPolice) { Game.MaxWantedLevel = 0; }
                Function.Call(Hash.SET_GAMEPLAY_CAM_RELATIVE_HEADING, 0f);

                saves.Save();
                lastAutoSave = Game.GameTime;
                SetState(GameMode.MIDNIGHT_CLUB);
                Screen.FadeIn(cfg.TransitionFadeMs);
                Notification.PostTicker(isNew ? "~p~MIDNIGHT CLUB~s~: welcome to the streets. Press ~b~" + cfg.ToggleKey + "~s~ to return to Story Mode."
                                              : "~p~MIDNIGHT CLUB~s~: welcome back.", true, false);
            }
            catch (Exception ex)
            {
                Log.Error("Entering Midnight Club failed", ex);
                Recover("Could not start Midnight Club (see scripts\\MidnightClub\\MidnightClub.log).");
            }
        }

        private SaveData NewProfile()
        {
            var d = new SaveData { Cash = cfg.StartingCash, Rep = 0 };
            d.Character.Model = cfg.CharacterModel;
            var model = VehicleManager.IsSpawnable(cfg.StartingVehicle) ? cfg.StartingVehicle : "futo";
            var starter = VehicleManager.CreateRecord(model, 0);
            starter.Nickname = "Starter";
            d.Vehicles.Add(starter);
            d.CurrentVehicleId = starter.Id;
            Log.Info("Created new Midnight Club profile with starter " + model);
            return d;
        }

        private static OwnedVehicle CurrentRecord(SaveData d)
        {
            var r = d.Vehicles.Find(v => v.Id == d.CurrentVehicleId);
            if (r == null && d.Vehicles.Count > 0) { r = d.Vehicles[0]; d.CurrentVehicleId = r.Id; }
            if (r == null) { r = VehicleManager.CreateRecord("futo", 0); d.Vehicles.Add(r); d.CurrentVehicleId = r.Id; }
            return r;
        }

        // ---------------------------------------------------------------- exit
        public void Exit()
        {
            if (State != GameMode.MIDNIGHT_CLUB) return;
            SetState(GameMode.EXITING_MIDNIGHT_CLUB);
            try
            {
                SnapshotProfile();
                saves.Save();
                Screen.FadeOut(cfg.TransitionFadeMs);
                Script.Wait(cfg.TransitionFadeMs + 100);
                RestoreStory();
                SetState(GameMode.STORY_MODE);
                Screen.FadeIn(cfg.TransitionFadeMs);
            }
            catch (Exception ex)
            {
                Log.Error("Exiting Midnight Club failed", ex);
                Recover("Midnight Club exit hit an error; Story Mode was restored.");
            }
        }

        private void SnapshotProfile()
        {
            var d = saves.Data; if (d == null) return;
            vehicles.Capture();
            var p = Game.Player.Character;
            if (p != null && p.Exists() && p == characters.MidnightPed)
            {
                var pos = p.Position;
                d.PosX = pos.X; d.PosY = pos.Y; d.PosZ = pos.Z; d.Heading = p.Heading; d.HasPosition = true;
                CharacterManager.Capture(p, d.Character);
            }
        }

        /// <summary>Puts Story Mode back. Safe to call from any state; never throws.</summary>
        private void RestoreStory()
        {
            try { vehicles.Despawn(false); } catch (Exception ex) { Log.Error("Despawn vehicle", ex); }
            if (story != null)
            {
                try
                {
                    characters.UnparkStoryPed(story);
                    if (story.StoryPed != null && story.StoryPed.Exists())
                    {
                        characters.TakeControl(story.StoryPed);
                        if (story.StoryVehicle != null && story.StoryVehicle.Exists())
                        {
                            if (!story.StoryPed.IsInVehicle(story.StoryVehicle)) story.StoryPed.SetIntoVehicle(story.StoryVehicle, story.StorySeat);
                            story.StoryVehicle.IsPersistent = story.StoryVehicleWasPersistent;
                        }
                    }
                    else Log.Error("Story ped no longer exists; the game will respawn the player.");
                }
                catch (Exception ex) { Log.Error("Restoring story ped", ex); }
                try { Game.MaxWantedLevel = story.MaxWantedLevel > 0 ? story.MaxWantedLevel : 5; Game.Player.WantedLevel = 0; } catch { }
            }
            try { characters.DeleteMidnightPed(); } catch (Exception ex) { Log.Error("Delete MC ped", ex); }
            story = null;
            onFootSince = -1;
        }

        private void Recover(string message)
        {
            SetState(GameMode.ERROR_RECOVERY);
            RestoreStory();
            SetState(GameMode.STORY_MODE);
            try { Screen.FadeIn(500); Notification.PostTicker("~r~" + message, true, false); } catch { }
        }

        /// <summary>Called when scripts are reloaded/aborted or the game shuts down: no waiting allowed.</summary>
        public void EmergencyShutdown()
        {
            if (State == GameMode.STORY_MODE) return;
            Log.Warn("Emergency shutdown while in " + State);
            try { if (State == GameMode.MIDNIGHT_CLUB) { SnapshotProfile(); saves.Save(); } } catch { }
            RestoreStory();
            try { if (Screen.IsFadedOut || Screen.IsFadingOut) Screen.FadeIn(0); } catch { }
            State = GameMode.STORY_MODE;
        }

        // ---------------------------------------------------------------- per tick
        public void Tick()
        {
            if (State != GameMode.MIDNIGHT_CLUB) return;
            var ped = Game.Player.Character;

            if (ped == null || !ped.Exists() || ped != characters.MidnightPed)
            {   // Something (e.g. a story script) took the player ped away from us.
                Log.Warn("Player ped changed externally; leaving Midnight Club.");
                Recover("Midnight Club was interrupted by the game and returned to Story Mode.");
                return;
            }
            if (ped.IsDead) { Recover("Your driver was knocked out. Returned to Story Mode."); return; }

            ApplyRestrictions();
            vehicles.UpdateStats();
            CheckVehicle(ped);

            if (cfg.AutoSaveSeconds > 0 && Game.GameTime - lastAutoSave > cfg.AutoSaveSeconds * 1000)
            {
                lastAutoSave = Game.GameTime;
                SnapshotProfile();
                saves.Save();
            }
            EventBus.Publish(MidnightTickEvent.Instance);
        }

        private void ApplyRestrictions()
        {
            if (cfg.BlockVehicleExit) Game.DisableControlThisFrame(Control.VehicleExit);
            Game.DisableControlThisFrame(Control.CharacterWheel);
            Game.DisableControlThisFrame(Control.SelectWeapon);
            Game.DisableControlThisFrame(Control.VehicleAim);
            Game.DisableControlThisFrame(Control.VehicleAttack);
            Game.DisableControlThisFrame(Control.VehicleAttack2);
            Game.DisableControlThisFrame(Control.VehiclePassengerAim);
            Game.DisableControlThisFrame(Control.VehiclePassengerAttack);
            if (cfg.DisableStoryPolice && Game.Player.WantedLevel > 0) Game.Player.WantedLevel = 0;
        }

        private void CheckVehicle(Ped ped)
        {
            if (vehicles.NeedsRecovery(cfg.RecoveryEngineThreshold)) { RecoverVehicle("Your car needed a tow"); return; }
            var car = vehicles.Current;
            if (!ped.IsInVehicle(car))
            {
                if (onFootSince < 0) onFootSince = Game.GameTime;
                // Small grace period so scripted interactions (garages, later phases) can move the ped.
                if (Game.GameTime - onFootSince > 1500) { ped.SetIntoVehicle(car, VehicleSeat.Driver); onFootSince = -1; }
            }
            else onFootSince = -1;

            if (car.IsUpsideDown && car.Speed < 1f)
            {
                if (flipSince < 0) flipSince = Game.GameTime;
                else if (Game.GameTime - flipSince > 3000) { RecoverVehicle("Flipped car recovered"); flipSince = -1; }
            }
            else flipSince = -1;
        }
        private int flipSince = -1;

        private void RecoverVehicle(string why)
        {
            var d = saves.Data; var rec = vehicles.CurrentRecord;
            var ped = Game.Player.Character;
            try
            {
                Screen.FadeOut(400); Script.Wait(450);
                var near = vehicles.Current != null && vehicles.Current.Exists() ? vehicles.Current.Position : ped.Position;
                float heading;
                var pos = World.GetNextPositionOnStreetWithHeading(near, out heading, false);
                if (pos == Vector3.Zero) { pos = near; heading = ped.Heading; }

                rec.EngineHealth = 1000f; rec.BodyHealth = 1000f; rec.Stats.Recoveries++;
                vehicles.Capture();  // keep mods/colors; health reset below
                rec.EngineHealth = 1000f; rec.BodyHealth = 1000f;
                var car = vehicles.Spawn(rec, pos, heading);
                ped.SetIntoVehicle(car, VehicleSeat.Driver);
                car.IsEngineRunning = true;

                long cost = Math.Min(d.Cash, cfg.RecoveryCost);
                d.Cash -= cost; saves.IsDirty = true;
                Screen.FadeIn(400);
                Notification.PostTicker("~p~MIDNIGHT CLUB~s~: " + why + ". Repair & recovery: ~r~-$" + cost, true, false);
                Log.Info("Vehicle recovered (" + why + "), charged " + cost);
            }
            catch (Exception ex)
            {
                Log.Error("Vehicle recovery failed", ex);
                Recover("Vehicle recovery failed; returned to Story Mode.");
            }
        }
    }
}
