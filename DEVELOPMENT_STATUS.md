# Development status (2026-11-22)

| Phase | Status |
|---|---|
| 0 Technical research | Done (TECHNICAL_NOTES.md) |
| 1 Core framework | **Code complete, compiled, awaiting first in-game test** |
| 2–12 | Not started |

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
