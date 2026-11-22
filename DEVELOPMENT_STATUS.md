# Development status (2026-11-22)

| Phase | Status |
|---|---|
| 0 Technical research | Done (TECHNICAL_NOTES.md) |
| 1 Core framework | **Working in-game** (user-tested 2026-11-22 on 1.0.1158.16) |
| 2 Character and vehicles | **Code complete, compiled, awaiting in-game test** |
| 3–12 | Not started |

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
