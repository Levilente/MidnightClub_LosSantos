# Changelog
## 0.15.0 (2026-11-22): UI overhaul
- New Midnight Club UI theme (purple/cyan neon on dark glass, game menu textures): menus, garage, showroom, workshop, race results, phone.
- All button hints now use the game's own instructional-buttons bar (bottom right) with real keyboard/controller glyphs; fixes the broken "b_196"-style text in the garage/showroom hint bars. No more ~INPUT~ tokens in drawn text.
- Welcome screen every time you enter Midnight Club: orbit shot of your car, career card (cash, REP, tier, cars, garages, races), next objective, unread messages / street news, and a front-end menu (Hit the streets / Career & garage / Phone / Back to Story Mode). [HUD] WelcomeScreen=false turns it off.
- Objective box moved to a top-centre pill; hidden while help text, a menu or the phone is up, and during races (no more overlap with button prompts).
- Full-screen screens hide the feed, help text and area/street names.
- Garage interiors: the player and every display car are now forced into the interior's room (FORCE_ROOM_FOR_ENTITY / FORCE_ROOM_FOR_GAME_VIEWPORT) so the script camera renders the interior instead of the void; cleaned up on exit. Log line "Garage room: interior X room Y".
## 0.14.3 (2026-11-22)
- Cockpit cam: driver model hidden (locally, per frame); head-movement G-force from filtered longitudinal acceleration and yaw-rate x speed lateral G (pitch, lean, small head shift); dynamic FOV. [Camera] GForceIntensity scales it (0 = off). Bumper and hood stay rigid with FOV only.
## 0.14.2 (2026-11-22): camera rewrite after first feedback
- Bumper / hood / cockpit: rigidly hard-attached to the car (game camera attach, no lag or jitter), no G-force sway/tilt/roll, no shake; only dynamic FOV. Cockpit lowered ~10 cm to eye level ([Camera] CockpitHeight, HoodHeight to fine-tune).
- Chase cameras rebuilt NFS Unbound-style: low and tight, looking slightly down (car in the lower third), critically damped heading follow (blended a little towards the direction of travel to show drift angle), slight pull-back with speed and filtered acceleration, strong speed FOV, no roll, no G-wobble, shake only with nitrous / above 220 km/h.
## 0.14.1 (2026-11-22): controller pass
- Controller can enter/leave Midnight Club: hold Back/Select (Change View) 1.5 s. A tap still cycles cameras.
- Nitrous is device-aware: Sprint on keyboard, the vehicle boost button (VEH_ROCKET_BOOST) on a controller.
- Quit a race by holding B (Frontend Cancel) as well as Backspace; cinematic camera disabled during races so B doesn't fight it.
- Right stick looks around NFS-style (stick angle = view angle, centres when released); the mouse keeps the spring-back free look.
- Hint bars and tips use GTA input tokens, so they show the right button for keyboard or controller.
## 0.14.0 (2026-11-22): "feels like its own game"
- Title card while Midnight Club loads (logo + gameplay tips).
- Street style system in free roam: near misses, drafting (real slipstream power boost, "slingshot" when you pull out), airtime, drifts; combo multiplier up to x5; banked combos pay REP and fill the special-ability meter.
- Nitrous: workshop kit stages 1-3 (power, burn time, refill), hold Sprint; NOS bar, turbo screen effect, FOV kick and shake through the camera system.
- Special abilities (Midnight Club 3 style), unlocked by career tier and powered by the style meter, Caps Lock / L3+R3: Zone (slow motion, tier 2), Agro (extra power, throws cars aside, tier 3), Roar (shockwave clears the road ahead, tier 4). Phone > Ability to choose.
- Starter objective chain on the HUD (garage, workshop, showroom, first win, style, meet, police escape, rival) with rewards; 13 milestones with cash/REP. Phone > Milestones.
- Optional [World] AlwaysNight (Story time restored when you leave).
## 0.13.0 (2026-11-22): Phase 11 polish + Phase 12 pass
- New Midnight Club speedometer: big digital speed, gear, 20-segment rev bar with shift/redline zones, car name; GTA vehicle/area/street name popups hidden in Midnight Club.
- Cleanup audit: every system that creates persistent entities (races, world simulation, police, vehicles, character) releases them on leaving the mode and on script reload.
- README rewritten (features, controls, config, data files, build, tests). Release zip with the scripts folder layout.
## 0.12.0-phase10 (2026-11-22)
- Midnight Club phone (UP or F6; the game's own phone is disabled in Midnight Club): app grid with Messages, Races, Garage, Dealers, Catalogue, Career, Rivals, Clubs, Street Buzz, Rankings, Calendar, Bank, Properties, Settings. Unread badge.
- Persistent inbox fed by game events: welcome, daily race invitations (with GPS to the start), weekend meet notices, promotions.
- Street Buzz: fictional automotive news written from your notable results (rival upsets, big wins, gold times, citywide escapes, tier-ups).
- Rankings: underground leaderboard of 16 named drivers whose REP grows with the calendar; you're placed by your REP.
## 0.11.0-phase9 (2026-11-22)
- World Simulation Manager: one scheduler and entity budget for the living world (1 Hz scheduling, per-frame only for markers/input), proximity activation, distance cleanup.
- Car meets: 5 spots (Legion Square, Cypress Flats, LSIA, Sandy Shores airfield, Vinewood Bowl) with themes; two active per night (21:00-04:00) or all day at weekends, rotating with the calendar; tuned parked cars and hanging-out NPCs spawn when you're near. E at a meet: show off your car (REP from mods/performance, once per meet per night) or race someone from the meet.
- Spontaneous street races: stopped in traffic, a tuned racer may pull up next to you and rev (more often at night); E within 10 s starts a head-to-head sprint to a point ahead; declining lets them drive off.
- Enthusiast cars: tuned sports/muscle/super cars wander in traffic around you (budget [World] MaxEnthusiastCars).
- Speed traps: 9 cameras on real roads with personal records and REP for beating them.
- [World] TrafficDensity, ParkedDensity, CarMeets, StreetChallenges, SpeedTraps. Rain already lowers grip natively.
## 0.10.0-phase8 (2026-11-22)
- Police pursuits (vehicle-only): patrol cars that see you speeding (trigger lowered by heat and during races) or that you hit start a pursuit. Units chase, box in and ram; drivers carry no weapons, drive-bys disabled; your car still can't explode.
- 4 levels over time/heat: more units, interceptors (police2/3 etc.), helicopter with searchlight from level 3. Unit cap scales with [Police] Difficulty.
- Escape: break line of sight; the evade bar fills while nobody sees you (longer at higher levels); being under a bridge/tunnel/parking deck hides you from the helicopter. Escape pays REP and counts per car. Busted when stopped and boxed in for 5 s: fine scaled by level.
- Heat per car (0-5): rises with pursuits/busts, falls each day, and drops with a new plate or primary respray. Shown in the vehicle menu.
- 60 s cooldown after a pursuit. [Police] Enabled, SpeedTriggerKmh, Difficulty in config.ini.
## 0.9.0-phase7 (2026-11-22)
- Career: 5 tiers by REP (Street Beginner, Street Regular, Underground Competitor, Professional Street Racer, Midnight Club Elite; thresholds in config.ini [Career]). Promotion announcement, tier on the HUD, F6 > Career with progress and unlocks.
- Tier gates: dealership/special-order cars need the matching tier (D/C 1, B 2, A 3, S 4, X 5); 10-car garages tier 2, executive garages tier 4.
- Rivals: Kenji Mori (tuners), Dee 'Blackout' Ramos (muscle), Lexi Cruz (circuits), Nikki Sato (drift), Vance Holloway (highway), Dmitri Volkov (supercars). Unlock by tier, 3 escalating duels each (their car gets faster), win/loss record, cash/REP prizes, their car as the final prize. F6 > Rivals.
- Clubs: Underground Tuners, Muscle Car Society, Supercar Elite, Drift Community, Highway Racers. Independent reputation earned by racing their style; levels 1-5; 5 % / 10 % discount at the tied dealership. F6 > Clubs.
## 0.8.0-phase6 (2026-11-22)
- Racing framework: data-driven races (data\races.json, generated once, editable/extendable): Sprint, Circuit (laps), Time trial (medals), Drift challenge (angle x speed scoring with combo), Head-to-head.
- Routes follow real roads: the game's GPS route between the race's control points is sampled every 20 m and thinned to checkpoints (denser in corners). Start/finish points are verified Online garage entrances.
- Opponent AI: tier-matched cars with random performance mods, racing driver settings, checkpoint-to-checkpoint tasks, mild catch-up, un-stick (never in view). Countdown, native checkpoints, route on the minimap, live position/lap/time HUD, hold Backspace to quit.
- Rewards via TransactionManager (entry fee, payouts by position or medal, REP), race history, best times, per-car races entered/won, RaceFinishedEvent for later career/rival systems.
- Championships: 3 series with entry fee, points (10-8-6-5-4-3-2-1), persistent standings and prizes.
- F6 > Races: list with GPS, championships, history. Race starts on the map (locked ones grey; some races are night-only).
## 0.7.2 (2026-11-22)
- Fix: Benny's roll-up door (lr_prop_supermod_door_01) stayed shut in Story Mode so the workshop couldn't be reached; it is now registered with the door system and held open while you're nearby in Midnight Club, and closed again when you leave the mode.
## 0.7.1-wheellab (2026-11-22)
- DevMode diagnostic "Wheel Lab" (F9 in a Midnight Club car): finds the real CWheel X-offset field on this game build from wheel-bone positions, lists tyre/rim radius candidates and dumps wheel memory to the log; lets you scale tyre radius / rim radius / tyre width / X offset / Y rotation live to see which ones change the wheel's look. Purpose: verify the fitment offsets on Enhanced and find a way to make rim size and tyre profile independently adjustable.
## 0.7.0 (2026-11-22)
- Adjust fitment (workshop): one-time kit, then free per-axle camber (-15..+5 deg), track width (-8..+15 cm per side) and tyre diameter (85-120 %), live preview; also shown on cars parked in garages.
- Tyre compounds: Street, Sport (+5 % grip), Semi-slick (+10 %), Race slick (+16 %), Drift (low grip). Lettered sidewalls as a separate visual option.
- Implementation: CWheel fields (0x008/0x010 Y rotation, 0x030 X offset, 0x110 tyre radius; constants from FiveM VehicleExtraNatives.cpp), validated per wheel before writing and re-applied every frame. Grip via the model handling data only while you drive it, restored afterwards.
- [Fitment] Enabled, CamberSign (flip if camber goes the wrong way).
## 0.6.0 (2026-11-22)
- Phase 5 workshops: 5 LS Customs/Beeker's + Benny's (all Benny's parts). Performance (engine, brakes, transmission, suspension, turbo, Midnight Club ECU tune +6%/stage, drift tyres), body parts, wheels by type, respray (primary/secondary/pearl/wheel), xenons, tyre smoke, tint, livery, repair, build sheet. Live preview while highlighted, reverts if not bought; real game mods with real effects; per-car spend/modification count/build history.
- Camera system (below).
- NFS-style driving cameras: Bumper, Hood, Cockpit, Close chase, Far chase, Action chase (+ Game default). Change View (V) cycles; choice saved per profile.
- G-force reactive (pull-back under acceleration, outward swing and roll in corners, pitch on braking/accelerating), speed + acceleration driven FOV, speed shake, free look that springs back, look behind, wall collision pull-in.
- `[Camera]` in config.ini: Enabled, GForceIntensity, DynamicFovIntensity, ShakeIntensity.
## 0.5.1 (2026-11-22)
- Fix: garage interiors didn't load in Story Mode (interior found but never ready). Midnight Club now loads the GTA Online map state on entry (ON_ENTER_MP, reverted on exit; `[World] LoadOnlineMap`) and un-disables/un-caps the garage interior before pinning it.
- Fix: vehicle class showed as "VEH_CLASS_6"; now localized.
- Fix: "< >" vanished from hint bars (GTA text treats < as a tag); now "LEFT/RIGHT".
## 0.5.0-phase4 (2026-11-22)
- Free starter garage (Davis Lockup, 2 cars) for every profile, including existing ones.
- Midnight Club calendar: own day counter that only advances while playing Midnight Club; drives rotations.
- 6 dealerships + Mosley's used cars, each with identity, map blip, entrance marker and rotating stock (data\\dealerships.ini; extra dealerships can be added with a Classes filter).
- Showroom screen: turntable display, specs, price, promotions (-15% on one class per week), rare badges, 90 s test drive, validated purchase (funds, garage space, duplicate protection), delivery to a garage.
- Used market: condition, odometer, existing mods, project cars, rare finds, sellers; 3-day rotation.
- Special order from the F6 catalogue for any dealership car (+35%), so no car is ever unobtainable. Dealerships menu with stock count and GPS.
## 0.4.0 (2026-11-22): real GTA Online garages
- Replaced the parking-lot garages with 42 real GTA Online garages (2-, 6- and 10-car apartment garages and the 4 executive office garages) at their Online entrances.
- Drive in: the interior loads with every car you keep there parked in its slot; Midnight Club garage screen with camera, car card (stats bars, condition, odometer, value) and controls bar.
- Drive out with any car parked there; driving a car into another garage you own moves it there if there is room.
- Manage menu (M): nickname, plate, favourite, move to another garage. Executive garages: customise floor/walls, lighting and numbering (C) with live preview, paid on apply.
- Swapping cars now happens in garages (DevMode still allows swapping anywhere).
## 0.3.0-phase3 (2026-11-22)
- Economy: TransactionManager with ledger and duplicate protection; HUD money popups; Finances menu.
- Garages: 6 purchasable, upgradable garages (data\\garages.ini), map blips, drive-in markers, store/take out cars, home garages, favourites.
- Car switching moved to garages (DevMode still allows it anywhere).
- Fix: blips/state cleaned up properly on script reload.
## 0.2.1 (2026-11-22)
- Fix: character creator never opened for a new profile (default appearance marked the character as created first).
- Spawned cars are clean (dirt level 0).
- Phase 1 confirmed working in-game by the user (F7 switch, HUD, starter car).
## 0.2.0-phase2 (2026-11-22)
- Character creator, F6 menu system, automatic vehicle registry with own pricing, multiple owned cars (switch/rename/plate/stats), DevMode catalogue.
- Save format: vehicles now also store the model hash (old saves load unchanged).
## 0.1.0-phase1 (2026-11-22)
- Initial core framework: F7 mode switching, independent character, starter car, vehicle-only rules and recovery, independent save system, config, minimal HUD.
