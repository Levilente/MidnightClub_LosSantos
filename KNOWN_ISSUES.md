# Known issues
1. Game build 1.0.1158.16 is not in Script Hook V's supported list (v3889.0/1158.13), but the user reports mods load fine on it. Phase 1 still awaits its first in-game test.
2. Run Story Mode with BattlEye disabled (Rockstar launcher setting); scripts don't run with BattlEye on.
3. Story Mode autosave while in Midnight Club Mode: missions are blocked so mission autosaves shouldn't happen, but untested. Don't save Story Mode from inside Midnight Club.
4. Story scripts that expect Michael/Franklin/Trevor as the player may misbehave while the Midnight Club ped is active; the mode exits automatically if the game takes the player ped back.
5. The default character's outfit component IDs are unverified on Enhanced; invalid components are skipped.
6. The spawn point (default near LS Customs, Burton) is snapped to the nearest road; untested.
7. Editing the driver while seated: the preview camera may clip into the car on some models.
8. First-time character creation happens standing on the street at the spawn point; traffic is cleared once but may drive in.
9. The registry includes every vehicle the game reports for the allowed classes; some special/mission variants may need hiding via `data\vehicles.ini` (`Enabled=false`).
10. Garage positions are a first pass (public parking lots) and not checked in-game; adjust X/Y/Z in `data\garages.ini` if a marker sits somewhere awkward.
11. Garages are drive-in points for now; walk-in interiors, vehicle display, lighting and decoration come with Phase 5 (Online interiors).
12. Career-tier requirements on garages are shown but not enforced until the career system (Phase 7).
