# Development status (2026-11-22)

| Phase | Status |
|---|---|
| 0 Technical research | Done (TECHNICAL_NOTES.md) |
| 1 Core framework | **Working in-game** (user-tested 2026-11-22 on 1.0.1158.16) |
| 2 Character and vehicles | Partly user-tested (registry 615 cars, profile load OK); creator fix in 0.2.1 |
| 3 Economy and garages | **Code complete, compiled, awaiting in-game test** |
| 5 (part) Online garages | **Pulled forward at user request: code complete, awaiting in-game test** |
| 4 Dealerships | **Code complete, compiled, awaiting in-game test** (auctions not done) |
| 5 Workshops + customisation | **Code complete, compiled, awaiting in-game test** |
| Camera system (user request) | **Code complete, compiled, awaiting in-game test** |
| 6 Racing | **Code complete, compiled, awaiting in-game test** |
| 7–12 | Not started |

## Phase 1 contents
- `Core/GameModeManager`: STORY_MODE / ENTERING / MIDNIGHT_CLUB / EXITING / ERROR_RECOVERY state machine; refuses to enter during missions, cutscenes, character switches, while wanted or dead; ignores F7 during transitions.
- Story preservation: the story ped is never modified. Control moves to a separate Midnight Club ped via CHANGE_PLAYER_PED; the story ped (and its vehicle) is hidden and frozen in place, then handed back on exit.
- `Character/CharacterManager`: independent freemode character, default outfit, appearance saved/restored.
- `Vehicles/VehicleManager`: owned-vehicle spawning with mods, colors and plate; explosion/fire/bullet-proof; distance and top-speed stats.
- Vehicle-only rules: exit control blocked; automatic re-seat if the driver ends up on foot; recovery (tow + repair, configurable cost) when the car is wrecked, flipped or sunk.
- Story police disabled inside Midnight Club (placeholder until Phase 8 pursuit system).
- `Persistence/SaveManager`: independent JSON profile, versioned, validated, atomic write + verify, rotating backups, fallback to newest valid backup, duplicate-transaction guard, autosave.
- `UI/MainHUD`: brand, cash, REP, car + speed.
- `Core/ConfigurationManager`: config.ini, missing keys auto-written.

## Tests performed
- Compiles against the installed ScriptHookVDotNet3.dll (SHVDNE 1.1.0.6) with zero warnings.
- Save system: 14/14 automated tests pass (round trip, backup pruning, corruption recovery, invalid-data refusal, duplicate transactions).
- In-game: none yet.

## Phase 2 contents
- `UI/Menu` + `MenuStack`: own Midnight Club styled menu (no external library), keyboard + controller via frontend controls.
- `Character/CharacterCreator`: runs automatically for a new profile (before the first car spawns) and from the F6 menu: sex, two-parent face/skin blend, hair + colours, top/undershirt/arms/legs/shoes with colours, hat, glasses; live preview camera.
- `Vehicles/VehicleRegistry`: built at first entry from `Vehicle.GetAllModels()`: every car, bike and open-wheeler actually present in the installed build (base game, DLC, add-ons), so invalid models are never offered. Per-car make, localized name, class, speed/accel/braking/traction, percentile performance, tier (D–X), career tier, base price. Exported to `data\vehicle_registry.csv`; overrides in `data\vehicles.ini`.
- `Economy/PricingEngine`: own prices (class base × exponential performance curve, rounded, configurable multiplier and clamps) + estimated value with wear/condition.
- Multiple owned cars: switch car (stopped), nickname, custom plate, per-car stats/condition/value.
- F6 menu: My vehicles, Vehicle catalogue (by class), Edit driver, Save, Return to Story Mode. `DevMode=true` in config.ini lets the catalogue add any car for free, for testing.

## Phase 3 contents
- `Economy/TransactionManager`: the only place cash/REP change; validates amounts, never overspends, deduplicates by transaction id, writes a ledger (last 200), raises `MoneyChangedEvent`, saves immediately after purchases.
- `Garages/GarageDefinitions`: `data\garages.ini` (6 defaults: Small → Premium, editable/extendable: position, price, capacity, upgrades, career tier).
- `Garages/GarageManager`: map blips (owned / for sale), drive-in markers, E to enter; buy, capacity upgrades, take a stored car out, set a car's home garage. Car switching now only works at the car's home garage (DevMode bypasses).
- F6 menu: Garages (GPS to any garage), Finances (cash, REP, recent transactions), vehicle home garage + favourite.
- HUD shows +$/-$ changes; tow & repair is charged through the ledger.
- Tests: 10/10 economy tests + 14/14 save tests.

## Online garages (0.4.0)
- `Garages/OnlineGarageCatalog` (generated, MIT data from xnOnlineGarage): 42 garages with entrance, interior spawn, exit and parking slots.
- `Garages/GarageManager`: blips, entrance markers, purchase, drive-in session (interior pin + IPL for executive garages, display cars per slot, slot normalisation), drive-out, entity-set customisation.
- `Garages/GarageScreen`: blocking garage screen with camera interpolation, HUD, manage/customise menus.
- Overrides: `data\online_garages.ini` (Name, Price, Enabled per garage).

## Phase 4 contents (0.5.0)
- `WorldSim/WorldCalendar`: MC day counter, `NewDayEvent`, weekday/night helpers.
- `Dealerships/DealershipDefinitions`: PDM, Southern SA Super Autos, Luxury Autos, Legendary Motorsport, Benny's, LS Car Meet marketplace, Mosley's used cars; ini overrides and custom dealerships.
- `Dealerships/RotationMath` (pure, tested): seeded permutation cycles: every eligible car appears once per cycle, deterministic per (dealer, rotation).
- `Dealerships/DealershipManager`: pools (every eligible car covered by at least one dealership), stock cache, promotions, used listings, purchase flow per spec 7.9, special orders.
- `Dealerships/ShowroomScreen`: showroom UI, test drive, confirm menu.
- Tests: rotation 17/17, economy 10/10, save 14/14.

## 0.6.0 contents
- `Cameras/CameraSystem`: 6 NFS-style cameras + game default; G-force reactions, dynamic FOV, shake, free look, look-behind, wall pull-in; profile-saved mode; other screens call Release().
- `Workshops/WorkshopManager` + `WorkshopScreen`: workshop locations, orbit camera with per-category focus, generic mod lists with live preview/revert (Menu.OnHighlight), toggles, respray, tint, livery, repair, ECU, drift tyres; purchases via TransactionManager; capture/save after every purchase.
- Save format: per-car toggles, tint, livery, ECU level, drift tyres, build history, spend/mod/repair counters; profile camera mode.

## 0.7.0
- `Vehicles/FitmentManager`: stance via CWheel (validated, re-applied per frame, applied to garage display cars too) and tyre-compound grip via handling data (restored when you stop driving the model). Workshop: Adjust fitment, Tyre compound, Lettered sidewalls.

## Reverse engineering in progress
- GTA5_Enhanced.exe code is not scannable on disk (even SHVDNE's working Enhanced patterns aren't found there), so wheel fields must be found at runtime. `Vehicles/WheelLab` does that; results from an in-game run decide the visual wheel size / tyre profile work.

## Phase 6 contents (0.8.0)
- `Racing/RaceData` (+ tests 8/8): definitions, championships, generator, JSON library.
- `Racing/RouteBuilder`: GPS-sampled routes (tries GPS route types 0-3 and remembers the one that works), checkpoint thinning, cache.
- `Racing/RaceSession`: grid, countdown, checkpoints, opponent AI, drift scoring, HUD, cleanup.
- `Racing/RaceManager`: map starts, menus, lifecycle, rewards, history, best times, championships; other location prompts are suppressed while racing.
