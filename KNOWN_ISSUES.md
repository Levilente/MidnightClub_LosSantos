# Known issues
1. **BLOCKER: game build 1.0.1158.16 is newer than Script Hook V** (latest SHV v3889.0/1158.13, 15 Jul 2026). No script mods load until dev-c.com releases an update. Phase 1 is untested in-game until then.
2. Run Story Mode with BattlEye disabled (Rockstar launcher setting); scripts don't run with BattlEye on.
3. Story Mode autosave while in Midnight Club Mode: missions are blocked so mission autosaves shouldn't happen, but untested. Don't save Story Mode from inside Midnight Club.
4. Story scripts that expect Michael/Franklin/Trevor as the player may misbehave while the Midnight Club ped is active; the mode exits automatically if the game takes the player ped back.
5. The default character's outfit component IDs are unverified on Enhanced; invalid components are skipped.
6. The spawn point (default near LS Customs, Burton) is snapped to the nearest road; untested.
