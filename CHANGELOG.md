# Changelog
## 0.43.0 (2026-11-28): handling memory fixed at the root
- **The cause of the understeer found** (from the probe's memory dump): GTA keeps biases as front/rear pairs and several tyre-curve values with a precomputed inverse next to them; ScriptHookVDotNet writes only one field. Setting the traction balance to 0.5 halved the front grip, the tuning clamps squeezed the suspension and roll-bar balance, and new tyre curves ran with old inverses. All handling writes now go through a writer that keeps both halves and the inverses right (convention detected per car), and the stock block is restored byte-exact.
- Suspension: lowering per level halved (the car no longer sits down), kerb filter tighter and no longer switched off by kerb contacts (logs how often it corrects).
- Probe findings stored for the next step: wheel spin rate, tyre forces and loads, suspension compression per wheel (CWheel 0x230 bytes), the wheel-physics code region.

## 0.42.4 (2026-11-28)
- Fix: cars floated and slid on the new suspension. The suspension values were written as absolute numbers, but GTA exposes them in its own internal form; they are now scaled from each car's own values by the real ratios (frequency squared for springs, frequency x damping for dampers, roll stiffness, travel).
- Fix: the session starts in the active car's garage (the garage list wasn't loaded yet at spawn time).

## 0.42.2 (2026-11-28)
- Every session starts inside the garage your active car lives in (else the last garage you used, else any garage you own). The old "start in last garage" switch is gone.

## 0.42.1 (2026-11-28): physics probe
- Settings > Driving > **Physics probe (developer)**: groundwork for the full tyre simulation. Saves the game's decrypted code image, the memory offsets ScriptHookVDotNet found for this build, and 20 s of raw wheel / vehicle memory with the driving state next to it (scripts\MidnightClub\probe). Reads only.

## 0.42.0 (2026-11-28): handling fixes, real suspension, kerbs, AI lines, difficulty
- **Understeer and weak brakes fixed**: the combined-slip model cut GTA's single grip value per axle, which also cut braking and front grip whenever the brakes or throttle were on. Now only the driven wheels' measured spin (and a locked handbrake) takes side grip; braking keeps full grip. GTA's wheelspin behaviour is back (stock low-speed traction loss), tyre fall-off halved (GTA scrubs past the peak much more than a real tyre), peak slip angle scaled to GTA's body slip angle, lateral load sensitivity halved, grip scale 2.25, the CoG is never raised much, ABS never drops below 60% pressure, brakes +15%, less speed-steering reduction.
- **Suspension rebuilt from real chassis numbers**: ride frequency by type and era (luxury ~1.1 Hz, sports ~1.6, super ~1.9, +0.15 Hz per level, coilovers +0.25), dampers from real damping ratios, travel and anti-roll stiffness by type, roll stiffness split by layout, spring split by axle loads.
- **No more kerb snapping**: one-frame velocity and roll/pitch spikes from GTA's wheel-on-kerb contacts, far beyond what tyres and springs can do, are filtered out (real impacts are left alone).
- **Objectives** in the top-left corner: the main one with its description, the next ones as titles below.
- **Workshop camera**: no idle spin; right stick (or mouse with a button held) turns and tilts the camera, also when a part has framed it.
- **Leaving a workshop**: the car is lined up facing the way it came in and rolls straight out along that line (no road pathfinding into props); put outside if anything blocks it.
- **AI racing lines**: a checkpoint counts as passed when the AI crosses its gate line anywhere across the road (no more passing in the other lane and U-turning back); near a gate the AI drives a straight line through it towards the next one, and it may use the oncoming lanes and shortcuts.
- **Difficulty** (Novice - Unbeatable) in the Hub settings and the launcher: opponent pace. **Rewards scale like Forza**: +0-40% for difficulty, plus up to +25% for driving with fewer aids; the bonus is shown on the results.

## 0.41.0 (2026-11-28): one physics model - real data simulation
- **Simcade, Arcade and GTA's stock handling are gone: there is only the simulation**, built from every vehicle's real figures. Settings > Driving now has the driver aids instead.
- **Real data for every car and bike**: power and torque (and the rpm where they peak), redline, engine (litres, cylinders, NA / factory turbo / factory supercharger / electric / hybrid), gearbox, drive layout and split, weight, front/rear balance, centre-of-gravity height, wheelbase, CdA and ClA. Hand-entered real figures for the cars the GTA models are based on (Veyron, Sesto Elemento, P1, Huayra, CCX, Enzo, Murciélago, Gallardo, Ford GT, LaFerrari, Huracán, 488, Chiron, C-X75, AMG One, Valkyrie, GT-R, Skyline, AE86, NSX, Supra A80 / A90, 911s, Corvettes, Viper, R8, Vanquish, DB9, Evo X, RX-7, Celica GT-Four, S13, 350Z, S2000, Impreza STI / 22B, RC F, Cayman GT4, Civic Type R, Mustangs, Challenger, Charger, GSX, Camaro, GT500, Firebird, DB5, Miura, Testarossa, Diablo, Countach, F40, McLaren F1, Type 57, Fulvia, Ducati 1098, CBR600, Hayabusa, Diavel, dirt bikes ...); every other vehicle gets a consistent real-world spec generated from its class, era, mass and GTA's own drive figures.
- **Engines simulated from torque curves**: full-load curve through idle, peak torque, peak power and the redline (the power peak lands exactly where the spec says), off-boost curve, turbo lag and spool, supercharger response and drive loss, fuel cut at the redline. Each frame the force the real engine puts on the road (torque x gear x final drive x drivetrain efficiency / tyre radius, with rotating inertia) is what the car gets: GTA's engine is driven to that target, and how GTA responds in each gear is measured while you drive and saved per model (data\physics_calibration.ini).
- **Real gearing**: the final drive puts the power peak at the drag-limited top speed in top gear, ratios spread by era; GTA's gear ranges are re-scaled per gear so every gear ends at the real redline speed. **Race gearbox = every car becomes a close-ratio 6-speed** (the car is rebuilt as it rolls out of the workshop, GTA sets the gear count up at spawn), final drive adjustable; the **Fully adjustable gearbox** arrives with those six ratios and every gear can be tuned.
- **Turbo and supercharger upgrades, 3 stages each**, instead of GTA's single turbo: turbo kit / uprated turbo (more boost, later spool, more lag), supercharger kit / uprated blower (instant boost, drive loss). Never both: fitting one removes the other (a factory-turbo car converted to a supercharger loses its turbo and vice versa). The shop shows the real hp / Nm / boost each stage gives. Anti-lag needs a turbo. Old saves: GTA's turbo becomes turbo stage 1 on NA engines.
- **Upgrades change the real figures**: engine levels, ECU (about +3% NA, +7% and more boost on boosted engines per stage), straight pipe, engine swaps, weight reduction, aero kit (real downforce in kgf at 200 km/h, balance, drag), tyres.
- **Pacejka Magic Formula tyres for every compound**: factory tyres by era and car (bias-ply, touring, ultra-high-performance, semi-slick), Sport, Semi-slick, Race slick, Drift and the new **Drag radial** (huge launch traction, little side grip), plus bike sport / track and off-road knobbly. Peak friction, peak slip angle, fall-off when sliding, load sensitivity, combined slip, wet grip per compound (slicks are hopeless in the rain), rolling resistance.
- **Grip from tyres and aero, suspension that works**: real centre of gravity (height and balance from the wheel positions), real mass and drive split; every frame load transfer fore/aft and side to side, tyre load sensitivity (the axle with the stiffer anti-roll bar loses more grip), aero downforce or lift with its balance, real aero drag (top speed is power against drag). A spinning or locked tyre loses side grip, so **the handbrake really locks the rears** and power oversteer follows the differential (a locked LSD breaks both rears loose). Suspension softer on old and soft-riding cars, each upgrade level stiffer and lower.
- **Wheelies, only when the car can**: the front lifts when the real thrust overcomes the weight on the front axle (CoG height, balance, traction) - sportbikes in the low gears, a dragster on drag radials; a normal car never.
- **Driver aids, each 0 (off) to 10**: traction control (holds the driven wheels near the tyre's peak slip; wheelie control on bikes), ABS (keeps braking slip near peak), stability management (brakes the rotation and cuts power when the car slides). In the Hub settings and the launcher; TCS / ABS / ESC lights on the speedometer while they work.
- **Real data view**: in the garage, the dealerships and the workshop press **R3 / R** to switch the car info from the game ratings to the real figures (power and torque with rpm, weight, balance, CoG, engine, drivetrain, tyres, aero, and the model's 0-100, 0-200, quarter mile, top speed, 100-0 braking and lateral grip) with the torque and power curves.
- Speedometer: real engine rpm, real boost (turbo lag, supercharger) from the engine model.

## 0.40.1 (2026-11-28): straight in from the launcher
- **MidnightClubBoot.asi** (game folder): when the game is started from the Midnight Club launcher, the legal / splash screens and the Enhanced landing page are skipped and the game loads straight into Midnight Club. Started any other way (Steam, Rockstar launcher) the game is untouched. Patch patterns from StraightToStoryMode (Chiheb-Bacha, landing-page patch by alloc8or, GPL-3.0); source in asi\.
- Launcher: no Story Mode button, Midnight Club only.

## 0.40.0 (2026-11-28): text centring, UI budget, PlayStation glyphs
- **Text is vertically centred exactly**: per-font offsets measured in game with the new text calibration card (Settings > Display > Text calibration).
- **Rounded shapes no longer fall apart on busy screens**: a rounded panel now costs one GTA rect (the centre column); its side columns, outlines and highlights are textures, which have no per-frame limit. Toggles, selected rows and chips stay whole.
- **PlayStation button glyphs**: cross / circle / square / triangle, L1-R3, Options, Create. Settings > Display > Controller buttons: Auto (PlayStation glyphs when a DualShock 4 / DualSense is connected, like the game), Xbox or PlayStation. Also in the launcher.

## 0.39.0 (2026-11-27): proper lots and grids
- **Dealership display cars only stand in real parking bays**: when you come near a dealership, the parked cars the map itself places on its car park (GTA's car generators, i.e. the painted bays) are learned as display spots (saved to data\lots.ini, so it's instant next time) and swapped for the current offer. No more cars in doorways, garages or bushes; a dealership without a car park shows its offer only in the showroom. The old guessed spots are gone.
- **Race starting grids on the road**: rows follow the road backwards from the start line (they bend with the road), every car is lined up with the road's heading, two abreast in the lanes going your way (both lanes on a narrow two-way street), each slot checked to be on tarmac and moved towards the lane centre if not. You start on pole.

## 0.38.0 (2026-11-27): real drivetrain, drift physics
- **Less understeer**: tyre peak slip angles brought down to real-tyre values (11-14° instead of 18-24°, which made every car lazy to turn in), rotational inertia capped, a touch more front grip in the balance. simcade.ini is reset once (old file kept as simcade.ini.v1.bak).
- **No artificial assists in Simulation**: the anti-spin yaw damper only runs in Arcade / Simcade; the old "drift sustain" push is gone everywhere.
- **Inverse power** (setting, on by default): GTA cuts drive force when the car is sideways; that cut is given back on the driven axle, scaled by throttle, gear and the slide angle, so drifts are held on the throttle like in real life.
- **Differential model**: acceleration lock (race diff tuning, or the car's stock LSD / open diff) decides power oversteer: a locked rear axle breaks both tyres loose together, an open diff loses drive to the inside wheel in hard corners. Deceleration lock decides whether the car turns in or stays calm when you lift.
- **Real drivetrain** (setting, on by default): every car gets a gearbox fitting its era (4 / 5 / 6 speeds), real ratios, final drive, redline and tyre radius, and a torque curve (NA high-rev, turbo plateau, big-block muscle, classic, diesel-like heavy). GTA's flat-velocity limiter and hard speed cap are moved out of the way: top speed now comes from power against drag. Engine inertia and shift speed per car (old automatics slow, modern dual-clutch quick; transmission upgrades shift faster). The speedometer shows real rpm.
- **Gearbox upgrades**: Race gearbox = final drive (shorter: more force, lower top speed). New **Fully adjustable gearbox** (needs the race gearbox): a ratio for every gear, tuned in Hub > Garage > Tuning setup > Gear ratios.

## 0.37.0 (2026-11-27): new launcher, new logo
- **Launcher 2.0** (MidnightClubLauncher.exe): a modern glass app in the mod's design language: real frosted-glass cards over a city backdrop, light / dark / auto theme (follows the mod's Theme setting, Auto follows Windows), rounded Windows 11 window, Barlow fonts, Lucide icons.
  - **Play**: big Play Midnight Club / Story Mode buttons, system check (GTA V Enhanced, Script Hook V, ScriptHookVDotNet, MidnightClub.dll, settings file) and your career at a glance (cash, REP, story chapter, last played).
  - **Settings** right in the app instead of opening config.ini: Driving, Cameras, Display, World, Police, Controls (click a key and press the new one), Advanced. Toggles, sliders, segmented choices, reset per page; every change is saved to config.ini instantly.
  - **About** page and a mod-folder shortcut.
- Launcher backdrop: the new illustrated LA freeway art (day for the light theme, night for the dark theme), with a readability scrim on the content side.
- **New logo** (Midnight Club Los Santos, by Turbomotive) in the launcher, the Hub, the welcome screen, the loading / starting screen, the race lobby and photo mode.

## 0.36.0 (2026-11-27): new design system (glass, light / dark)
- **Design system** (docs\DESIGN.md, made with the ui-ux-pro-max skill): frosted glass surfaces with hairline borders and highlights, rounded corners everywhere (anti-aliased corner textures), one shape language, five category colours, **light and dark theme** (Settings > Display > Theme: Auto follows the in-game clock).
- **Button prompts** redrawn: real keycaps (keyboard) or coloured A / B / X / Y, bumpers, triggers and d-pad (controller), switching with the last input device, on a glass capsule. Context prompts ("Enter garage", "Browse the offer"...) use the same style instead of GTA's help box.
- **Hub**: segmented tab bar with Q / E keycaps and a colour per tab, glass tiles with colour icon badges and a focus ring, profile capsule (level ring, cash, REP), info and objective cards. Sub-screens: list card with toggles / sliders and a detail card.
- **Every other screen** in the same style: side menus (garages, showrooms...), garage / showroom car card, workshop (icon tab bar, list, purchase card, stat strip with rounded bars), race lobby, race HUD (race card, progress track, standings tower, ghost split), welcome screen, loading screen, toasts, banners (replace GTA's shards), objective capsule, minimap road-name capsule, world labels, PI class badges, camera label.
- The speedometer stays pixel-exact to the reference.

## 0.35.0 (2026-11-27): polish pass
- **Input**: one key press = one step everywhere (Q / E no longer skip two tabs); menus react instantly and repeat while a direction is held; timing uses the real clock, so the slowed-down world behind menus no longer makes them sluggish.
- **No more ghost numbers**: speedometer digits and other textures are drawn from per-use sprite slots and cleared the frame they stop being used. Digit textures carry padding, so the tops and bottoms are no longer cut off.
- **Readable type**: small text is lifted to a legible minimum everywhere; text in rows, tags and tiles is vertically centred.
- **Calmer look**: one accent colour (coral, as on the speedometer), no decorative streaks, neutral tile highlights. Side-panel menus (garages, showrooms...) rebuilt and moved below the screen title, so headers no longer overlap.
- **Free-roam HUD**: the Midnight Club wordmark is gone; level ring, cash and REP / tier in a clean block. Minimap without health, armour and ability bars, with road name and compass heading above it. Banners and toasts wait until menus close.
- **Chase cameras rebuilt**: world-space follow with critically damped heading and smoothed ride height, so body roll, pitch and suspension bob no longer rock the camera; slight pull-back under acceleration, speed FOV. Cockpit cam: braking moves the head forward, throttle back.
- **Garages**: sell cars from the Manage menu (70% of the estimated value).
- **Dealerships**: the current offer is parked on the forecourt with floating price tags; anywhere on the lot you can open the showroom, or stop next to a car to open it on that car. Blips sit on the dealership itself.
- **Workshops**: no markers outside; roll through the door and the game drives you into the bay with a cinematic camera, the shop opens, and afterwards it drives you back out, like the vanilla shops.
- **Workshop layout**: the car sits right of the menu; stats moved to a strip along the bottom so they never cover the car while it turns.
- **Performance upgrades by category**: Engine, Forced induction, Drivetrain, Suspension & chassis, Brakes, Tyres & aero. Race parts sit in their categories instead of a separate list.

## 0.34.0 (2026-11-27): Forza Hub, exact speedometer, camera fixes
- **New Hub, Forza Horizon style**: tabs across the top (RACES · GARAGE · CAREER · WORLD · SETTINGS · EXIT, switch with Q / E or LB / RB) and a scrolling mosaic of big and small tiles per tab, each with an icon, a live info line and a short explanation strip; the garage tile shows a picture of your current car. No more long lists of options on one screen.
- **Sub-screens**: max eight rows with icons, plus a detail panel (big icon, value, what the option does). Settings are split into Driving, Cameras, Display, World and Police pages.
- 70 new line icons (Lucide, ISC licence).
- **Speedometer rebuilt pixel-for-pixel after the reference**: outlined curved rev band with red zone, white fill fading into red, "• RPM" readout, pink gear, big condensed speed digits, KM/H, BOOST box gauge with peak tick and BAR readout, slim NOS bar, ability bar in the same style. All drawn from textures (no GTA font differences).
- **Chase cameras no longer fly away**: the Forza springs are now integrated implicitly (stable at any frame rate and spring setting), layer output is clamped.
- **Bumper, hood and roof cams are rigidly fixed to the body**: no G-force, pitch or shake, only the speed FOV. Cockpit cam: the head now dips under braking and leans back on throttle (was the wrong way round).

## 0.33.0 (2026-11-27): Forza cameras, Forza HUD
- **Cameras rebuilt from the player's Forza Horizon files** (scripts\MidnightClub\camera\CameraSettings.ini + CameraPhysics.xml, editable / replaceable): chase near / far use Forza's heights above ground, radius and target offsets and yaw springs; every enabled CameraPhysics layer runs (G-forces, speed FOV, slip, suspension pitch / roll, pitch angle, airtime, collision impulses, noise, springs).
- New **roof cam**; bumper, hood, roof and cockpit follow the body's pitch **and roll**; bumper / hood pitch from the Forza file (no more looking at the sky); **action cam** moved out and up.
- The Midnight Club camera is active from the first second (no more cycling to "wake it up") and takes the screen back after menus, the lobby, photo mode or the welcome screen. Race intro / finish cinematics no longer fight it.
- **Hitches / HUD flicker fixed**: ambient spawns (street racers, takeovers, police, meets, barn finds) stream their models without freezing the script; slanted UI shapes are single rects (draw budget).
- **New HUD** (Forza Horizon style): curved rev arc with red zone and RPM readout, coral gear, big speed, boost gauge for turbo cars (anti-lag spools faster), slim NOS and ability bars; white / coral race HUD and top-right block.

## 0.32.0 (2026-11-27): deep
- **Handling revamp**: every car gets an era (pre-war, 60s ... modern) that shapes grip, spring and damper softness, travel, body roll, brakes and steering on top of its archetype. Old cars float and pitch, muscle cars are boaty, luxury saloons waft, modern cars sit flat and stop hard. New **driving model** setting: Arcade / Simcade / Simulation.
- **Forza-style tuning** per car (Hub > Garage > Tuning): tyre pressures, final drive, camber, toe, caster, anti-roll bars, springs, ride height, rebound / bump, front / rear downforce, brake balance and pressure, diff acceleration / deceleration lock, AWD centre balance, steering lock, in real units, with presets (grip, drift, drag, rally, touge).
- **Race parts** (workshops > Performance > Race parts): coilovers, anti-roll bars, race diff, race gearbox, race brakes, adjustable aero, three stages of weight reduction, AWD and RWD conversions, anti-lag (exhaust pops, punchier low-rev boost), straight-pipe exhaust (raspier note, a bit more power). Parts unlock the matching tuning pages and raise PI. Engine swaps now add power too.
- **Performance Index & classes** (D / C / B / A / S / X) with badges on the HUD, car lists, lobby and auctions; opponents are matched to your class.
- **Race lobby**: grid preview with every opponent's car and class, side bets, crew toggle, rival time on solo events.
- **Crew mates**: hire a blocker or a drafter; they level up and get faster cars.
- **Series** (Cup and Masters for every discipline): three rounds, points against two rivals, podium ceremony, reward car; series wins are story goals.
- **Hot pursuit races** (police from the green light, +50% reward), **rock crawls** (don't roll it), **seasons** (weekly featured discipline +25% XP and weather mood), **live events** (8-minute challenges for cash and a Wheelspin), **collection rewards**, **auction house** with live bidding, **radio station** setting.

## 0.31.0 (2026-11-27): one career
- **Story**: seven chapters (Rookie Nights → The Midnight Club) whose goals pull in every part of the game: street wins, outruns, tokens, turf, rivals, police, rally medals, off-road wins, drift and gymkhana medals, takeovers, drag and bike wins. Chapters pay cash, REP, a Super Wheelspin and sometimes a car, and make the news. The Hub and the welcome screen always show the next goal.
- **Disciplines**: Street, Drift, Off-road, Drag and Bikes level 1-10 separately from events and activities; levels unlock that discipline's harder, better-paying events and pay rewards (cars at 5 and 10).
- **New events**: trail races (off-road AI), cross country (free route between far checkpoints), hill climbs (with pace notes), gymkhana (donut pillars, close calls, 90 s), bike races (motorcycles only, bike AI). Generated events climb in tiers.
- **Races menu** grouped by discipline, with lock reasons and your best result on each event.
- Licence tests removed.

## 0.30.0 (2026-11-27): everything car games do
- **Rally stages** (orange blips): generated from San Andreas' dirt and back roads, with a co-driver pace-note panel (curve icons with severity 1-6, hairpins, crests, "into"), medal times and ghosts.
- **Drag races** on generated dead-straight roads: shift lights, gear readout, perfect-shift boosts (E / L3 at the redline).
- **Elimination races**: last place is out every 30 seconds; last one standing wins.
- **Licence tests** (Gran Turismo): B-1 launch & stop, B-2 slalom, A-1 braking point, A-2 top speed; bronze / silver / gold targets scale with your car; B and A licences pay out.
- **Wheelspins** (Forza Horizon): every level-up earns one, every fifth a Super Wheelspin with three reels; credits, XP, REP or a car, rarity colours.
- **Car deliveries** (Test Drive Unlimited): drive a client's car across the city against the clock; pay depends on condition and time left.
- **Street takeovers** (GTA VI): night-time crowds shut down an intersection; donuts and drifts in the circle build hype and score.
- **Dynamic weather** with wet roads that cost grip (WET ROADS tag on the HUD).
- **Race intro cinematic**: grid flyover with a title card before every race (skippable).
- **Premium notifications** replace the GTA feed; **race progress strip** shows every racer on the route; **speed lines** past 170 km/h.

## 0.29.1 (2026-11-27)
- Rewind removed (doesn't fit Midnight Club). R / B is back to the normal camera button.

## 0.29.0 (2026-11-27)
- **Fixed**: hub cards, icons and the EXIT tile vanishing. GTA drops script rectangles past a per-frame budget and the vector icons used hundreds; icons and the level ring are now anti-aliased PNG textures drawn by Script Hook V (no budget), gradients are cheaper, and DevMode logs the peak draw count.
- **Start in your last garage** when loading into Midnight Club (Settings toggle).
- **Tuning setup** per car (Hub > Garage): springs, damping, ride height, anti-roll bars, grip balance, brake balance and pressure, gearing, steering lock; Grip / Drift / Drag / Rally presets.
- **Outrun battles**: purple-blip street racers cruise the city; get behind one and flash your lights for a Tokyo Xtreme Racer-style spirit-bar battle.
- **Midnight Tokens**: 40 hidden collectibles, milestone bonuses, tips for sale, a one-off car for all 40.
- **Weekly playlist**: five challenges per real-world week (tokens, outruns, takedowns, jumps, turf, photos...) and a reward car.
- **Takedowns** in races (Burnout-style) with cash bonuses. **Jump distance records.**
- **Races menu**: quick race, race now (fast travel), personal bests, which district a race counts for.
- **Daily login streak**, **career Records** screen, traffic / race traffic / pedestrian density settings, mastery on the HUD.

## 0.27.0 (2026-11-23): the big one
- Shop names no longer reference Vice City / GTA VI: La Mesa Auto Works, Rockford Motorwerks, Runway Tuning LSIA, Southside Speed Lab, Paleto Muscle Garage, Senora Off-Road, Strawberry Lowriders.
- **Rewind**: hold R (controller B) to scrub back up to 12 s; in races opponents, checkpoints and the clock rewind too. Hard crashes show a rewind prompt. Toggle in Settings.
- **Photo mode** (Hub > Garage): orbit camera, FOV, roll, depth of field, 10 filters, time of day, lights, hide driver, shutter that hides the UI for Steam F12 screenshots.
- **Race feel**: start lights, launch control (keep the revs in the green for a perfect launch), slipstream boost behind opponents, new race HUD with standings tower and live gaps, slow-motion finish cinematic, new results screen (count-up rewards, personal best, rewinds used).
- **Ghosts**: your best run on every solo race is saved and raced as a see-through car with live checkpoint splits.
- **Pink slips**: race beaten rivals for their car, with your car on the line.
- **Turf war**: nine districts run by crews. Race wins take control, owned districts pay every night, crews strike back. District toasts when you drive in, map overlay, Hub > Career > Turf war.
- **Route creator** (Hub > Races): drive a route, tap the horn for checkpoints, hold to finish, save as sprint / circuit / time trial / drift and race it.
- **Engine swaps** (workshops, Performance): 11 engine notes with preview.
- **Car mastery**: every car levels 1-10 as you drive and race it; each level pays a bonus and +3 % race winnings in that car.
- **Police**: roadblocks from level 2, spike strips from level 3, NFS-style bounty (time, disabled units, dodged roadblocks) paid on escape, lost when busted. Gas stations fix burst tyres.
- **Endgame**: "The President", leader of the Midnight Club, unlocks at the top tier. Beat him three times to join the club.
- Speed traps have rival street records worth a bonus. Bikes score wheelies and stoppies.

## 0.25.0 (2026-11-23): Midnight Hub
- The bloated phone is replaced by the **Midnight Hub**: a full-screen icon main menu (Races, Garage, Dealers, Career, Social, Explore, Daily, Settings + EXIT). The world slows down and blurs behind it.
- Big vector-icon cards with accent colours, spring selection, shine, live info lines (next race, current car, cash, unread messages) and badges; level ring, cash and REP/tier in the top bar; next objective / daily challenge strip.
- All sub-screens share the same full-screen style (category watermark, breadcrumb, animated selection bar, scrollbar, descriptions).
- New in-game **Settings**: camera, simcade physics, HUD, world and police options adjustable live and saved to the ini.
- **EXIT** replaces "Back to Story Mode" (welcome screen and hub): Exit to Story Mode, Quit game, Cancel.
- Fixed double input in every menu (one action per press).
- **Motorcycles**: new Two Wheel Fury Motorsport bike dealer; bikes keep their own physics and camera.

## 0.24.0 (2026-11-23): launcher
- New MidnightClubLauncher.exe (goes in the GTA V Enhanced folder): dark neon launcher window with PLAY MIDNIGHT CLUB, STORY MODE, SETTINGS (opens config.ini) and MOD FOLDER. Play writes scripts\MidnightClub\autostart.flag and starts the game through Steam (app 3240220; falls back to PlayGTAV.exe).
- Autostart: when the mod finds a fresh autostart.flag on load, it keeps Story Mode hidden (black screen with the Midnight Club title) until the game is ready, then enters Midnight Club directly. The flag is deleted immediately, so normal launches still go to Story Mode.
## 0.23.0 (2026-11-23)
- Vanilla blips: hidden blips are now re-hidden every second (display off + alpha 0), because the game's own scripts keep showing their mission/shop blips again. Restored exactly on leaving.
- Mod shop: everything previews. Pro Builds, toggles (turbo, xenons, tyre smoke), tyre compounds, ECU and nitrous now show on the car / in the stats while highlighted (body parts, wheels and paint already did). The stat card compares against what is actually installed (green/red delta) and only moves after you buy. Leaving a list, switching tabs or exiting puts the car back to its installed state.
- UI: fixed the stripes/seams in gradients, panels and slanted bars (rectangles are now snapped to whole screen pixels, no overlapping strips).
- Cockpit cam: driver model is really hidden now (made invisible while in cockpit view, restored when switching or leaving).
- Chase cams rebuilt again, Driveclub-style: the camera is attached to the car (offset in car space, pointed at the car) so it moves in the same physics step: no jitter at speed. Tighter yaw follow with a little drift angle, capped swing, subtle stretch/squat with throttle/brake, lower and wider with speed, level horizon, very fine high-speed tremor; the road-vibration shake and the roll were removed.
- Fixed "Collection was modified" errors from discoveries revealing places during the scan.
## 0.22.2 (2026-11-23): fix "Blocking script" abort when entering
- Entering Midnight Club could get the script killed by SHVDN's watchdog ("Blocking script ... caused the game to freeze too long"): ON_ENTER_MP (GTA Online map state) blocks the game thread for 4-6 s and the default per-tick limit is 5 s. The mod now yields right before/after it and after every heavy step, logs how long it took, and the road network loads on a worker thread at startup instead of on entry.
- The user's ScriptHookVDotNet.ini ScriptTimeoutThreshold was raised from 5000 to 20000 ms (backup: ScriptHookVDotNet.ini.bak_mc), because the ON_ENTER_MP call alone can exceed 5 s. Alternative: config.ini [World] LoadOnlineMap=false.
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
- Workshops are now GTA VI-style specialty mod shops: La Mesa Auto Works (Retro), Rockford Motorwerks (Luxury), Runway Tuning LSIA (Tuner), Southside Speed Lab (Racing, new location at Hayes Autos), Paleto Muscle Garage (Muscle), Senora Off-Road (Off-road), Strawberry Lowriders (Lowrider, Benny's). Each has a description, -15% on its specialty tab and exclusives (race slicks and ECU stage 3 at Southside Speed Lab; drift compound at Runway Tuning LSIA / Southside Speed Lab).
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
