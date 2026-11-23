# Changelog
## 0.22.1 (2026-11-23): drive into the mod shops for real
- No more teleport: the shop doors are opened. Door props between the street and the bay are found automatically; known roller-door models are opened through the game's door system (animated, held open); any door that doesn't move, or an unknown gate-shaped prop, is made passable (hidden, no collision) while you're nearby, and restored when you leave the area or Midnight Club.
- Drive into the bay and stop: the shop opens by itself after a moment (or press Context). After closing it you drive out yourself; it won't reopen until you've left the bay.
- Log lines "Shop gate at <shop>: ..." show what was found per shop.
## 0.22.0 (2026-11-23): fixes + AAA chase cameras
- FIX: mod shops in LS Customs buildings could not be entered (Story Mode keeps their doors shut without the vanilla shop scripts). The marker is now on the driveway in front of the door; entering moves the car inside behind a fade and brings it back out (nose out) afterwards.
- FIX: vanilla map blips (Story missions, LS Customs, shops...) are hidden in Midnight Club and restored when you leave; every Midnight Club blip is registered and stays.
- NEW: drive through a gas station to repair and clean your car (Midnight Club classic). 27 stations plus any fuel blips the game has; per-station cooldown.
- FIX: after a pursuit ends, police units stand down for real: blips off, sirens off, tasks cleared, they drive away (deleted if out of sight); wanted level cleared.
- Cameras: chase cams reworked Driveclub + NFS Unbound style: rotationally lazy but positionally tight follow (the car swings and slides on screen), camera slides to the outside in corners, squats closer under braking and stretches back under acceleration, lowers and widens with speed (stronger FOV curve), gentle lean into corners and pitch under braking, velocity look-ahead, fine micro-shake plus the game's road-vibration shake at speed (stronger with nitrous). Mounted cams unchanged (FOV only).
## 0.21.0 (2026-11-22): GTA VI-style physics and mod shops
- Brake-to-drift removed.
- Simcade retuned towards GTA VI (GTA IV weight + GTA V accessibility, leaning realistic): softer springs and dampers, more suspension travel, more body roll (anti-roll and roll centres), more rotational inertia, less sticky tyres with a wider, more progressive slip angle, more wheelspin on RWD/muscle cars, longer braking with nose dive; arcade helpers cut back (light anti-spin, little drift sustain, downforce only on supercars). Muscle and classic cars wallow, modern supercars stay tight. New per-model values Spring, Damping, Travel, AntiRoll, Inertia in simcade.ini; [Simcade] Weight in config.ini scales the weight layer. simcade.ini is reset once (old file kept as simcade.ini.v1.bak).
- Workshops are now GTA VI-style specialty mod shops: Coastal Auto Care (Retro), Euroform Automotive (Luxury), Vice Auto Customs (Tuner), Whisler Motorsport (Racing, new location at Hayes Autos), Capri Classic (Muscle), Wasteland Off-Road (Off-road), Lowballers Customs (Lowrider, Benny's). Each has a description, -15% on its specialty tab and exclusives (race slicks and ECU stage 3 at Whisler; drift compound at Vice Auto Customs / Whisler).
- Mod shop menu restructured into tabs PRO BUILDS / COSMETICS / BODYWORK / PERFORMANCE / WHEELS / INTERIOR (LB/RB or Q/E). Pro Builds: ready-made packages (Street, Performance + a specialty build per shop) 10% cheaper than the parts. New: plates, interior parts (trim, dash, dials, seats, steering wheel, shifter...), interior and dashboard colours, interior camera.
- New GTA VI-style shop UI: shop name and tab bar top left, "TAB / PART" breadcrumb, list with a white highlighted row, part panel bottom left (price, name, n / m, owned / not owned, EXCLUSIVE / SPECIALTY tag), stat card bottom right (Speed, Acceleration, Asphalt handling, Off-road handling, Strength, Road / Off-road rating) with live green/red preview of the highlighted part, cash top right, shop card on arrival.
## 0.20.1 (2026-11-22): NFS-style brake-to-drift
- Grip cars stay simcade-grippy, but a quick brake tap while turning at speed (released back onto the throttle, or brake + throttle together) kicks the car into a controlled drift: the rear lets go, the nose swings in, steering sets the angle (more lock = wider, counter-steer = coming out), throttle holds it and keeps your entry speed up. Lift off, straighten up or brake hard to grip up again. Anti-spin allows the drift angle meanwhile.
- Per-car strength (simcade.ini "BrakeToDrift": drift/tuner cars stronger, heavy cars weaker); config.ini [Simcade] BrakeToDrift=true/false and BrakeToDriftStrength.
## 0.20.0 (2026-11-22): per-car simcade physics
- New Simcade system (Vehicles/Simcade.cs). Every car gets its own profile at runtime, derived from its stock handling (mass, drivetrain split, power-to-weight, grip curve), class and tags: archetype (Super, AWD, Sports, Tuner, Drift, Muscle, Classic, FWD, Street, Heavy, Rally) + per-model personality. Written to data\simcade.ini (one section per model, every value editable).
- While you drive it, that model's handling is retuned (grip, slip angle, low-speed wheelspin, brakes, lower centre of mass; tyre compounds stack on top) and restored afterwards.
- Live assists: speed-sensitive steering, anti-spin yaw damping past the car's spin angle (drift cars allow much more), drift sustain on throttle, speed-squared downforce, airborne pitch/roll settling for flat landings.
- Garage and showroom show the car's handling archetype.
- config.ini [Simcade]: Enabled, Intensity (0-1.5), SteeringAssist, StabilityAssist.
## 0.19.0 (2026-11-22): premium showroom, location markers, no stacked locations
- Showroom rebuilt: the dealership's whole current offer is parked in an arc on a quiet, studio-lit stage (LSIA apron), each car on a glowing floor ring. Left/right flies the camera smoothly to the next car (no respawn, no HUD flash); right stick / mouse orbits, triggers / mouse wheel zoom, slow showcase drift when idle. Sold cars stay with a red ring. Test drive starts at the dealership and returns to the stage.
- New in-world location markers for dealerships, garages, workshops and races: pulsing ring on the real ground, soft light column, floating rotating icon, and a 3D label (name + action / price / lock reason) when close.
- Race starts/finishes that sat on garage entrances, dealerships or workshops are moved along the road to a spot 90 m clear (and race starts are kept apart), so markers and prompts no longer stack.
## 0.18.0 (2026-11-22): progression, exploration, road-network routing
- FIX: races failed with "Route leg not found" (the in-game GPS route is unreliable from scripts on Enhanced). Routes now come from the game's full road network (67,454 path nodes, embedded in the DLL) with A* routing: road-following checkpoints anywhere on the map, no teleporting behind a fade. The old GPS method remains only as a fallback.
- Driver LEVEL (1-50) from XP for everything: races (by position), rivals, escapes, meets, upgrades, style, discoveries, skill zones, road discovery, daily challenges. Level rewards: cash every level, cars at 5/10/15/20/25/35/50 (Sultan, Elegy RH8, Jester, Banshee 900R, Comet, Zentorno, Entity XF; delivered later if garages are full), barn-find rumours at 3/7/12/17/23/30. HUD shows level + XP bar and +XP popups; level-ups use the game's own animated shard banner.
- Exploration: workshops, dealerships, garages for sale, races and speed traps start hidden (except LSC La Mesa, PDM, the used market and the first four races). Driving near one discovers it (banner + XP) and adds its blip; purple ? marks show undiscovered places within ~450 m. Phone race invites reveal that race.
- Road discovery %: every road node you drive counts (saved); rewards at 1/5/10/25/50/75/100%.
- Skill zones built from the road network: 7 speed zones (average speed, 1-3 stars) and 10 drift zones (drift score), with start chevrons, live HUD, records and XP for new stars.
- Barn finds: 6 hidden classics on remote dead-end dirt roads; a level reward gives a rumour (search area on the map); get within a few metres of the dusty car to claim it.
- Daily challenges: 3 per Midnight Club day (win, finish races, km, top speed, style, zone star, discovery, escape, speed trap), with cash + XP.
- Phone: new Progress (level, next rewards, roads %, locations found, zone stars, barn finds) and Daily apps.
- Tier-ups, milestones, speed-trap records and discoveries use the game's own shard banners instead of subtitles/feed.
## 0.17.0 (2026-11-22): street-racing design language + garage fix
- FIX: you could get stuck free-roaming inside a garage interior (after a script reload/crash in the garage, or a save made inside). Every second the mod now checks for this and puts you outside in your car; positions inside garage interiors are never saved, and a saved position inside one spawns you outside.
- Design language v3: no rounded/pixel shapes or pixel icons any more. Everything is sharp and slanted: parallelogram selection bars with a light sweep, slanted tags for kickers/chips/badges, racing stripes, animated speed streaks, slanted rev-bar segments, gear badge, NOS/ability bars, objective and pursuit panels.
- Phone v3: slim dark-glass device with neon edge; big clock, cash/REP/tier tags; numbered app list in large condensed type with live info per app (unread, open races, cars, days to new stock, rivals beaten, tier, rank, cash, goals, ability); accent-coloured slanted selection that springs between rows; staggered entrance; apps open inside the phone with a tag + big title + stripes header and the same slanted list.
## 0.16.0 (2026-11-22): new design system, new phone
- Design system (UI/Design.cs): palette, condensed display + body typography, rounded shapes (anti-stepped corners), gradients, easing/spring motion, text fitting with ellipsis, MIDNIGHT CLUB wordmark. Pricedown removed everywhere.
- Vector icon set (16 app icons + UI glyphs) drawn from rectangles on a 12x12 grid: crisp, no textures needed.
- Phone rebuilt: real phone body with notch, status bar and home indicator; animated night-city wallpaper; big clock, cash/REP/tier chips; 4x4 icon grid with per-app accent colours, spring highlight, staggered entrance, pulsing badges; next-objective / street-buzz card. Slides up/down. Apps now open INSIDE the phone (header, animated list, scrollbar, description card) instead of a side menu. Leaving an app via an action closes the phone; Back returns to the home screen.
- Side menus redesigned: kicker + big condensed title, animated violet selection, adjust chevrons, scrollbar, description card, soft left backdrop, slide-in.
- HUD redesigned: wordmark, rolling cash counter, REP + tier, animated +$ / +REP deltas; new speedometer (big speed, gear badge that turns red at the limiter, 24-segment rev bar), NOS and ability bars under it. HUD hides while the phone or a menu is open (no more overlap).
- Pursuit HUD, objective pill, style popups (pop-in animation, combo chip), loading screen and welcome screen restyled to match.
## 0.15.1 (2026-11-22): NFS-style car screens
- Studio lighting in garage, showroom and workshop: overhead key + camera-side key spot, purple/cyan rim lights, warm front/rear fills, drawn every frame. Headlights off on display cars (no glare). [HUD] StudioLight scales it (1.0 default).
- Shallow depth of field focused on the car.
- Camera framing: car sits centred in the upper part of the screen (garage/showroom) or right of the menu (workshop); wider, lower shot scaled by car length.
- New layout: no more big boxes over the car. Small title top-left over a soft fade, NFS-style bottom strip (kicker, big car name, make/class/tier, four compact stats, value/price and details on the right), native button bar.
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
