# Midnight Club: Los Santos (GTA V Enhanced total conversion)

Press **F7** in Story Mode to switch into Midnight Club Mode, an independent street-racing game with its own character, money, REP, cars and save. Press F7 again to go back to Story Mode exactly where you left it.

## Requirements
- GTA V **Enhanced**, a build supported by Script Hook V (see KNOWN_ISSUES.md: 1.0.1158.16 is **not** supported yet)
- Script Hook V + ASI loader (dinput8.dll / xinput1_4.dll)
- Script Hook V .NET Enhanced 1.1.0.6+ (ScriptHookVDotNet.asi, ScriptHookVDotNet3.dll, MinHook.x64.dll)

## Install
Copy `MidnightClub.dll` into `<GTA V Enhanced>\scripts\`. No game files are modified.
On first run the mod creates `scripts\MidnightClub\` with `config.ini`, `MidnightClub.log` and `saves\`.

## Controls (Phase 1)
- **F7**: enter / leave Midnight Club Mode (configurable: `[General] ToggleKey`)
- **V** (Change View): cycle Midnight Club cameras (Bumper, Hood, Cockpit, Close/Far/Action chase, Game default). Mouse/right stick looks around, Look Behind works.
- **F6**: Midnight Club menu (`[General] MenuKey`). Arrows navigate, Left/Right change values, Enter selects, Backspace/Esc goes back.
- **E** at a garage entrance: buy it, or drive in. Garage screen: Left/Right choose car, Enter drive out, M manage, C customise (executive garages), Backspace leave in your current car.
- **E** at a dealership: showroom. Left/Right browse, Enter buy, T test drive, Backspace leave.
- F6 > Vehicle catalogue: special order any dealership car (+35%).
- `[General] DevMode=true`: catalogue can add any car for free (testing).

## Build
`mcs -target:library -sdk:4.8 -optimize+ -r:ScriptHookVDotNet3.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Runtime.Serialization.dll -r:System.Core.dll -out:build/MidnightClub.dll -recurse:'src/*.cs'`
(or any C# compiler targeting .NET Framework 4.8 with a reference to ScriptHookVDotNet3.dll).
Save-system tests: `tests/SaveTests.cs` (runs without the game).
