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
10. Executive office garages load their interior with REQUEST_IPL; if Story Mode doesn't stream them, the log says "No interior found" and they need the MP map switch (to be tested).
11. The garage-screen camera uses a fixed offset from each car; in the 2-car garage it may clip a wall.
12. Clubhouse, warehouse, facility and storage-bay garages from the source data are not included yet (need extra IPLs).
13. Dealership positions are first-pass and snapped to the nearest road (Southern SA Super Autos and Legendary Motorsport have no in-game building; they're placed at plausible spots).
14. Showroom cars are shown where you parked, not inside the showroom buildings.
15. Auctions (spec §8) aren't implemented yet.
16. Luxury Autos only shows 2-3 cars a week, so a full cycle takes many weeks; special orders cover the gap.
17. The Online map state (ON_ENTER_MP) is loaded while in Midnight Club and restored on exit; if Story Mode ever looks wrong after leaving, set `[World] LoadOnlineMap=false` and report it.
18. ECU tune uses SET_VEHICLE_CHEAT_POWER_INCREASE, re-applied every frame for the driven car; effect size untested.
19. Mounted cameras use the "bonnet" bone / head bone; on some models the hood or cockpit position may need tuning.
20. Free look reads mouse/right-stick input each frame; sensitivity untested.
21. Workshop entrances are first-pass LS Customs door positions; the car is tuned where you stop, not inside the shop.
22. Fitment writes wheel memory at offsets taken from FiveM (Legacy); another SHVDN mod reports they are the same on Enhanced, but this is untested here. If a wheel reads implausible values the car is left alone and the log says so.
23. Visual wheel/rim size (scaling the wheel model) and an independent visual tyre profile are not available: FiveM finds the wheel draw size by scanning the executable, and none of those patterns exist in GTA5_Enhanced.exe 1.0.1158.16. Tyre diameter changes the physical tyre (ride height, arch fill), not the wheel model.
24. Tyre compound grip edits the model's shared handling data while you drive; traffic cars of the same model nearby get the same grip meanwhile.
25. If camber leans the wrong way, set [Fitment] CamberSign=-1.
26. Car-meet spot coordinates are first-pass (Vinewood Bowl and Sandy Shores airfield especially); cars are laid out in a grid around the spot and may need moving.

- 0.18.0: shard banner colour indices (purple/gold/blue) are best guesses; if a banner is invisible or oddly coloured, report it.
- 0.18.0: existing saves start the exploration from the starter set too (already-used workshops/dealers must be rediscovered by driving near them).
