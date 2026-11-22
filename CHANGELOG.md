# Changelog
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
