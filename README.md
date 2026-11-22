# Midnight Club: Los Santos

A total conversion for **GTA V Enhanced** Story Mode. Press **F7** and Story Mode turns into an independent, Midnight Club-style street-racing game with its own driver, money, REP, cars, garages, career, rivals, police and living car culture. Press F7 again and Story Mode is exactly as you left it.

## Requirements
- GTA V Enhanced (tested build 1.0.1158.16), BattlEye off for Story Mode
- Script Hook V + ASI loader, Script Hook V .NET Enhanced 1.1.0.6+ (ScriptHookVDotNet.asi, ScriptHookVDotNet3.dll, MinHook.x64.dll)

## Install
Copy `MidnightClub.dll` into `<GTA V Enhanced>\scripts\`. No game files are modified.
First run creates `scripts\MidnightClub\` (`config.ini`, `MidnightClub.log`, `saves\`, `data\`).

## Controls
| Key | Action |
|---|---|
| F7 (controller: hold Back/Select) | Enter / leave Midnight Club |
| UP or F6 | Midnight Club phone |
| E | Interact: garages, dealerships, workshops, race starts, car meets, street challenges |
| V | Cycle cameras (bumper, hood, cockpit, close/far/action chase, game default) |
| Menus | Arrows, Enter, Backspace/Esc; Left/Right change values |
| Garage screen | Left/Right car, Enter drive out, M manage, C customise (executive garages) |
| Showroom | Left/Right browse, Enter buy, T test drive |
| Racing | Hold Backspace / B to quit |
| Sprint on keyboard / vehicle boost button on controller | Nitrous (with a kit) |
| Caps Lock / L3+R3 | Special ability (Zone, Agro, Roar) when the meter is full |
| F9 (DevMode) | Wheel Lab diagnostic |

## What's in it
- **Mode switch** with a state machine; the Story character is never touched (control moves to a separate driver); the Online map state is used while in Midnight Club.
- **Driver**: character creator, vehicle-only gameplay, tow/repair recovery instead of wrecks.
- **Cars**: automatic registry of every car in your build (incl. DLC/add-ons) with own pricing and tiers; multiple owned cars with stats, heat and build history.
- **Garages**: 42 real GTA Online garages (2/6/10-car and executive) with interiors, a garage screen, free starter garage, executive garage styling.
- **Dealerships**: PDM, Southern SA Super Autos, Luxury Autos, Legendary Motorsport, Benny's, LS Car Meet marketplace, Mosley's used cars; calendar-driven rotating stock (every car comes round), promotions, test drives, special orders.
- **Workshops**: LS Customs, Beeker's, Benny's: performance, ECU tune, body, wheels, paint, lights, tint, livery, repair; adjustable fitment (camber, track width, tyre diameter); tyre compounds.
- **Cameras**: NFS-style G-force reactive cameras with dynamic FOV.
- **Racing**: sprint, circuit, time trial, drift, head-to-head on GPS-plotted real roads, AI opponents, championships, history, best times.
- **Career**: 5 tiers, 6 rivals with 3 duels each, 5 clubs with independent reputation and dealership discounts.
- **Police**: vehicle-only pursuits (no shooting), 4 levels, helicopter, evade/cover, busts, per-car heat.
- **Living world**: car meets, spontaneous street races at lights, enthusiast traffic, speed traps.
- **Street style, nitrous, abilities**: near misses, drafting, air and drifts fill a meter for Zone / Agro / Roar; nitrous kits.
- **New-game flow**: title card, starter objectives, milestones.
- **Phone**: messages, race invites, Street Buzz news, rankings, calendar and every menu as apps.
- **Saves**: independent, versioned, validated, atomic with backups; Story saves are never written.

## Configuration
`config.ini` sections: General (keys, DevMode), Career (start cash/car/garage, tier REP), Character, Gameplay, HUD, Save, Vehicles, Economy, World (Online map, traffic, meets, challenges, traps), Camera, Fitment, Police.
Data files (editable): `data\online_garages.ini`, `data\dealerships.ini`, `data\vehicles.ini`, `data\races.json` (+ `vehicle_registry.csv` export).

## Build
`mcs -target:library -sdk:4.8 -unsafe -optimize+ -r:ScriptHookVDotNet3.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Runtime.Serialization.dll -r:System.Core.dll -out:build/MidnightClub.dll -recurse:'src/*.cs'`
Tests (no game needed): `tests/SaveTests.cs`, `EconomyTests.cs`, `RotationTests.cs`, `RaceDataTests.cs`.

Docs: DEVELOPMENT_STATUS.md, CHANGELOG.md, KNOWN_ISSUES.md, TECHNICAL_NOTES.md, IMPLEMENTATION_ROADMAP.md, TESTING.md, THIRD_PARTY_NOTICES.md.
