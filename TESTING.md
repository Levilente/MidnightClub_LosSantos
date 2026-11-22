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

## Racing (0.8.0, next)
See the section added with the racing release.
