# Technical notes (Phase 0 feasibility)

## Environment (inspected 2026-11-22)
- GTA V **Enhanced**, `GTA5_Enhanced.exe` 1.0.1158.16 (Steam, F:\SteamLibrary).
- Script Hook V v3889.0/1158.13: supports Enhanced 1.0.1013.17/29/33/34 and 1.0.1158.13 only → **does not support .16**.
- Script Hook V .NET Enhanced 3.9.0.6 (1.1.0.6) + MinHook: framework chosen (v3 API, .NET Framework 4.8). Includes a patch enabling MP vehicles in SP, which matters for the vehicle registry.
- ASI loaders: dinput8.dll (legacy) and xinput1_4.dll ("GTA V ENHANCED ASI LOADER").
- No other script mods, no `mods` folder (no OpenIV/RPF modding in use).

## Architecture decisions
- One SHVDN script class (`MidnightClubMain`) wires plain C# subsystems; they communicate through `EventBus` (ModeChanged, MidnightTick, BeforeSave). Later phases subscribe instead of adding more Script classes, so there is one tick and one place for ordering.
- Player swap uses `CHANGE_PLAYER_PED` instead of `Player.ChangeModel`, so the story character's weapons/outfit/state are never touched.
- Saves: `DataContractJsonSerializer` (built into .NET 4.8, no extra DLLs). No dictionaries in save data (lists only), for serializer portability.
- All natives used are verified to exist in SHVDNE's `Hash` enum; signatures follow the public native DB.

## Feature classification (spec §5 / §32)
| Feature | Class | Status |
|---|---|---|
| Mode switch, independent character/save | Custom | Feasible, implemented, untested |
| MP vehicles in SP | Available via SHVDNE patch | Feasible but untested |
| Benny's, LS Car Meet, HSW, Online garages | Need IPL/interior loading (e.g. via community "Online interiors" mods) | Experimental; interior *functionality* (shops) must be custom-built |
| HSW performance upgrades | Enhanced-only mod kit content | Experimental |
| Dealership showrooms | Custom (spawned display cars at exterior/interior locations) | Feasible |
| Opponent race AI | Custom on TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE / mission tasks + CAIHandlingInfo (SHVDNE) | Feasible |
| Vehicle-only police | Custom (wanted system disabled, own pursuit AI) | Feasible |
| Custom phone UI | Custom scaleform/menu | Feasible |
