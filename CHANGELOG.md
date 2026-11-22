# Changelog
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
