# Testing checklist (for the next play session)

Tick items off or note what happened. Logs: `scripts\MidnightClub\MidnightClub.log` and `ScriptHookVDotNet.log`; Claude can read them from the game folder.

## Already confirmed in-game
- [x] F7 switch into/out of Midnight Club, HUD, starter Futo, profile save/load
- [x] Vehicle registry (615 cars), garage screen UI (car card, stats bars)

## Garages (0.4.0–0.5.1)
- [ ] Davis Lockup (free starter garage) shows on the map; drive in with E
- [ ] **Interior now loads** (0.5.1: Online map state + un-disabled interior). Before it was a void.
- [ ] Garage screen: LEFT/RIGHT, Enter drive out, M manage, Backspace leave; car appears outside at the exit point
- [ ] Buy a second garage (Los Santos Airport Storage / Rancho Projects, $13,000), drive a car into it → it moves there
- [ ] Executive (CEO) garage: does its interior load? (log line "Garage interior … ready=")
- [ ] Story Mode looks normal after leaving Midnight Club (Online map state is restored on exit)

## Dealerships (0.5.0)
- [ ] Blips for PDM, Southern SA, Luxury Autos, Legendary, Benny's, LS Car Meet, Mosley's; markers reachable by car
- [ ] Showroom: turntable car, specs, LEFT/RIGHT, T test drive (90 s, Backspace ends), Enter buy → delivered to a garage
- [ ] Mosley's used cars: condition/odometer/mods shown, bought car keeps them
- [ ] F6 > Vehicle catalogue > a car > Special order (+35 %)
- [ ] F6 > Dealerships list + GPS
- [ ] Note: DevMode is currently ON in config.ini (needed for Wheel Lab); it lets the catalogue add cars for free

## Workshops (0.6.0) and Benny's door (0.7.2)
- [ ] LS Customs (Burton / LSIA / La Mesa / Route 68 / Beeker's): E in the marker → workshop screen
- [ ] Highlighting a part previews it live; backing out without buying reverts it
- [ ] Performance upgrades feel real (engine, transmission, turbo, ECU stages)
- [ ] **Benny's roll-up door opens** when you get near it in Midnight Club; Benny's workshop shows Benny's parts

## Cameras (0.6.0)
- [ ] V cycles: Bumper, Hood, Cockpit, Close chase, Far chase, Action chase, Game default
- [ ] G-force feel (pull back on throttle, swing/roll in corners), FOV opens with speed, shake at high speed
- [ ] Mouse look springs back; Look Behind works; chase cam doesn't go through walls
- [ ] Hood / bumper / cockpit positions look right on a few different cars
- Tuning knobs: config.ini `[Camera]` GForceIntensity, DynamicFovIntensity, ShakeIntensity

## Fitment / tyres (0.7.0) and Wheel Lab (0.7.1)
- [ ] Workshop > Adjust fitment: buy kit, then camber / track / tyre diameter change the car live (front & rear separately)
- [ ] Camber leans the expected way (if not: `[Fitment] CamberSign=-1`)
- [ ] Tyre compounds: grip difference noticeable (Street vs Race slick), Drift slides
- [ ] Lettered sidewalls visible
- [ ] **Wheel Lab: in a Midnight Club car press F9.** PgUp/PgDn pick a field, +/- scale it, Backspace resets. For each of the 5 fields write down what visibly happens (whole wheel bigger? only tyre sidewall? wheel moves out? nothing?). Screenshots help. This decides whether separate visual rim size / tyre profile is possible.

## Racing (0.8.0)
- [ ] Red/blue/yellow race-car blips; checkered marker at the start; E opens the race card
- [ ] **Route plotting**: after Start the screen fades briefly while the route is built. Does a route appear (checkpoints + minimap line)? If not, the log has a "Route ..." line; tell me.
- [ ] Countdown 3-2-1-GO, opponents start and follow the checkpoints, they don't get stuck forever
- [ ] Position/lap/time HUD makes sense; finishing shows results with cash/REP
- [ ] Time trial gives a medal; drift challenge scores while sliding (combo resets on crash)
- [ ] Hold Backspace quits a race; opponents are removed
- [ ] F6 > Races: GPS works, championships can be entered (Street Beginner Cup needs 300 REP)
- [ ] Opponent difficulty: too easy / too hard?

## Career, rivals, clubs (0.9.0)
- [ ] HUD shows the career tier next to REP; F6 > Career shows progress
- [ ] Dealership purchase of a higher-tier car is refused with a clear message
- [ ] F6 > Rivals: Kenji and Blackout available at tier 1; "Challenge" moves you to the start line and the duel begins with one purple-blip opponent
- [ ] Winning advances the stage and pays out; losing counts a loss; after 3 wins their car shows up in a garage
- [ ] Nikki (drift) duel: beating her score counts as a win
- [ ] F6 > Clubs: racing in a muscle car raises Muscle Car Society rep; level-up message; discount appears at Southern SA (level 2+)

## Police (0.10.0)
- [ ] Speed past a police car (> ~115 km/h) or bump one: "POLICE PURSUIT" + red HUD bar at the top
- [ ] Units chase and ram but never shoot; level rises over time; helicopter at level 3 with searchlight
- [ ] Losing them: evade bar fills when out of sight; hiding under a bridge/tunnel shows "UNDER COVER"
- [ ] Getting boxed in while stopped for 5 s = BUSTED + fine
- [ ] F6 > My vehicles > a car shows Police escapes and Police heat; new plate lowers heat
- [ ] Too aggressive / too easy? (config [Police] Difficulty, SpeedTriggerKmh)

## Living world (0.11.0)
- [ ] At night (or weekends) purple car-meet blips; a notification lists tonight's meets
- [ ] Driving to a meet spawns parked tuned cars + people; leaving despawns them; layout looks OK (not inside walls)
- [ ] E at the meet: Show off (REP once per night), Race someone (1-on-1 sprint)
- [ ] Waiting at a red light: sometimes a racer pulls up and revs; E accepts a street race
- [ ] Tuned enthusiast cars appear in traffic now and then
- [ ] Yellow camera blips = speed traps; passing one fast shows your speed and records

## Phone (0.12.0)
- [ ] UP (or F6) opens the Midnight Club phone instead of the GTA phone; arrows move, Enter opens an app, Backspace closes
- [ ] Every app opens its screen; backing out returns to the phone home
- [ ] Messages: welcome + a race invite with "Set GPS"; unread badge; new message sound/notification
- [ ] Street Buzz fills up after notable results; Rankings shows your place

## Game feel (0.14.0)
- [ ] F7 shows the MIDNIGHT CLUB title card with a tip while loading
- [ ] Objective panel top-left; completing one pays and shows the next
- [ ] Near misses / drafting / air / drift popups at the bottom; combo multiplier; REP when the combo ends
- [ ] Workshop > Performance > Nitrous kit; hold Shift: boost, blue NOS bar drains and refills, screen effect
- [ ] At tier 2 Zone unlocks: meter full -> Caps Lock = slow motion; Agro (tier 3) and Roar (tier 4) feel right / not too wild
- [ ] Phone > Milestones / Ability
