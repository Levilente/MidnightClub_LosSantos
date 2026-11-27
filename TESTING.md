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

## Controller (0.14.1)
- [ ] Hold Back/Select 1.5 s in Story Mode enters Midnight Club; again leaves. A short tap changes the camera
- [ ] Hint bars show controller buttons when using a pad (and keys with keyboard)
- [ ] Nitrous on the controller boost button (which button is it on your pad?)
- [ ] Right stick look feels OK; holding B quits a race

## Cameras v2 (0.14.2-0.14.3)
- [ ] Bumper/hood: rigid, no wobble, only FOV opens with speed
- [ ] Cockpit: eye height OK ([Camera] CockpitHeight to adjust), driver hidden, head leans the right way in corners and pitches on throttle/brake
- [ ] Chase cams closer to NFS Unbound now? What still feels off (distance, height, lag, FOV)?

## UI overhaul (0.15.0)
- [ ] Entering Midnight Club shows the welcome screen (orbiting car, career card, menu). Each menu entry works; B/Backspace = hit the streets.
- [ ] Garage (Davis Lockup and an Online garage): the interior is visible and the cars are there (not the void). Send the log line "Garage room: ..." if not.
- [ ] Bottom-right button bar shows correct glyphs on keyboard and on controller in garage, showroom, test drive, menus, phone, race results.
- [ ] Objective pill (top centre) never covers help text / prompts.
- [ ] 0.15.1: garage/showroom/workshop car is well lit (not blown out, not dark) and not covered by UI. If too bright/dark: [HUD] StudioLight=0.6 / 1.5.

## Progression & exploration (0.18.0)
- [ ] Start a race (e.g. r01): the log shows "Route r01: N road nodes, ..." and checkpoints follow the roads. No teleport/fade before the race.
- [ ] Log shows "Road graph: 67454 nodes ..." and "Skill zones: 17, barn finds: 6".
- [ ] Only LSC La Mesa, PDM, the used market and races r01-r04 have blips at first; purple ? marks appear near hidden places; driving close discovers them (banner, XP, blip appears).
- [ ] HUD top right: LV + XP bar, +XP popups. Level-up shows the big shard banner with the reward.
- [ ] Phone > Progress and Phone > Daily work; daily challenges complete with a banner.
- [ ] Drive into a speed zone (stopwatch blip) / drift zone (steering wheel blip) in the right direction: live HUD, result banner with stars.
- [ ] Reach level 3: barn find rumour area on the map; find the dusty car; it is delivered to a garage.

## Showroom & markers (0.19.0)
- [ ] Showroom: all stock cars in an arc, camera glides between them, right stick/mouse orbit, triggers/wheel zoom, no flicker when switching. Buy and test drive work; after a test drive you are back on the stage.
- [ ] Is the LSIA stage clear (no plane/traffic driving through)? If something is in the way, tell me and I'll move the stage.
- [ ] Markers at dealerships/garages/workshops/races: ring on the ground (not floating/buried), icon, label when close.
- [ ] Log: "Moved N race points away from garages/dealerships/workshops"; no race marker on top of a garage any more.

## Simcade (0.20.0)
- [ ] Log: "Simcade profile <model>: <Archetype> ..." when you get in a car; data\simcade.ini gets a section for it.
- [ ] Compare a drift car, a muscle car, a supercar and an FWD hatch: each should feel different (muscle: wheelspin; drift: holds long angles; super: planted at speed; FWD: safe).
- [ ] High speed: steering calmer, no twitchy spins; slides past the limit get caught (less for drift cars).
- [ ] Jumps land flatter. If anything feels wrong, [Simcade] Intensity=0.5 or Enabled=false, and tell me which car.
- [ ] 0.20.1 brake-to-drift: at 60+ km/h, steer and tap brake then gas (or brake+gas): car drifts, steering changes the angle, gas holds speed; lifting/straightening ends it. Tell me if it's too strong/weak (BrakeToDriftStrength).

## GTA VI physics & mod shops (0.21.0)
- [ ] Driving: cars feel heavier, roll and pitch more, brake longer; muscle/classics wallow, supercars stay planted. Too much? [Simcade] Weight=0.6. Log line "simcade.ini reset ..." appears once.
- [ ] Mod shop: tabs switch with LB/RB (Q/E); stat card shows green/red preview while browsing parts; Pro Builds apply; interior tab camera looks at the dash.
- [ ] New shop Southside Speed Lab at Hayes Autos (Strawberry): marker on the driveway, reachable by car.

## 0.22.0
- [ ] La Mesa Auto Works / Euroform / Runway Tuning LSIA: marker on the driveway, entering works, you come out facing the street.
- [ ] Map: no Franklin/Michael/Trevor mission blips or vanilla LSC blips in Midnight Club; they come back in Story Mode.
- [ ] Drive through a gas station with a damaged car: "REPAIRED".
- [ ] After escaping/being busted, police cars stop chasing and switch off sirens.
- [ ] Chase cams feel fast (FOV, swing, squat/stretch, vibration). Too much? [Camera] GForceIntensity / ShakeIntensity / DynamicFovIntensity.

## Midnight Hub (0.25.0)
- [ ] Phone button (Up arrow / D-pad up) opens the hub; world slows + blurs; closing restores normal speed.
- [ ] Arrow navigation across the 4x2 grid and to EXIT; one move per press (no double input anywhere).
- [ ] Each card opens its sub-screen; Back returns to the hub; Back again closes it.
- [ ] Settings changes apply live and survive a restart (MidnightClub.ini).
- [ ] EXIT → Exit to Story Mode / Quit game work; welcome screen EXIT opens the same confirmation.
- [ ] Bike dealer (Two Wheel Fury, Del Perro) lists motorcycles; buying + riding works.

## 0.27.0
- [ ] Hold R / B while driving: rewind effect, car goes back, carries on with old speed on release. In a race the opponents and the clock rewind too.
- [ ] Hub > Garage > Photo mode: orbit with mouse/right stick, W/S zoom, filters, DOF, time of day; Enter/A shoots; Esc/B exits and the normal camera returns.
- [ ] Race countdown: start lights + rev gauge; green zone at GO gives "PERFECT LAUNCH".
- [ ] Following an opponent closely: SLIPSTREAM tag and speed gain.
- [ ] Standings tower on the left with gaps; finish cinematic; new results screen.
- [ ] Solo race twice: second run shows the ghost car and split deltas.
- [ ] Beaten rival > PINK SLIP RACE (needs 2+ cars): win gives their car, losing removes yours and puts you in another car.
- [ ] District toast when driving into a new district; winning a race there raises control; Hub > Career > Turf war.
- [ ] Hub > Races > Route creator: horn drops checkpoints, holding the horn opens the save menu, "Save and race now" works.
- [ ] Workshop > Performance > Engine swap: hovering changes the engine note, Back restores it.
- [ ] Pursuit level 2+: roadblock ahead; level 3+: spike strip; bounty on the HUD, paid on escape.

## 0.29.0
- [ ] Midnight Hub: all 8 cards have their icon, background and accent bar; EXIT tile visible; level ring smooth.
- [ ] Loading into Midnight Club puts you inside your last garage (garage screen); leaving works normally.
- [ ] Hub > Garage > Tuning setup: sliders change the car's feel while driving; presets load.
- [ ] Purple "Street racer" blips: drive right behind one, press H (D-pad right on controller) → countdown → spirit bars.
- [ ] Glowing purple orbs on roads (Midnight Tokens) collect when driven through; Hub > Explore > Midnight Tokens.
- [ ] Hub > Daily > Weekly playlist shows 5 challenges.
- [ ] Ramming an opponent off the road in a race shows TAKEDOWN.

## 0.30.0
- [ ] Orange rally blips appear a few seconds after entering; a rally shows the pace-note panel with curve arrows.
- [ ] Green drag blips: shift lights, E at the redline gives PERFECT SHIFT.
- [ ] Elimination races (Races list): countdown banner, last place removed.
- [ ] Hub > Races > Licence tests: B-1 box stop, B-2 cones, results card with medal.
- [ ] Level up → Hub > Career > Wheelspins: reel animation, prize paid.
- [ ] Hub > Explore > Car deliveries: pick-up swaps you into the client's car, drop-off returns your own car.
- [ ] At night: orange "Street takeover" blip, crowd, hype meter.
- [ ] Weather changes every few minutes; rain shows WET ROADS and the car slides more.
- [ ] Race intro: camera sweep + title card, Enter skips.
- [ ] Notifications appear as dark cards above the minimap instead of GTA's feed.

## 0.31.0
- [ ] Hub > Career > Story shows chapter 1 goals; the Hub strip and welcome screen show the next goal; winning a street race counts.
- [ ] Hub > Career > Disciplines: levels rise after events; level-up shard.
- [ ] Hub > Races > Race events: groups (Street / Drift / Off-road / Drag / Bikes); locked events say why.
- [ ] Trail race (orange) has off-road AI cars; cross country has no road routing; hill climb shows pace notes.
- [ ] Gymkhana: glowing pillars, DONUT shard after a full 360 around one, 90 s timer, medal at the end.
- [ ] Bike race refuses to start in a car, works on a motorcycle with bike opponents.

## 0.32.0
- [ ] Drive a 60s/70s car (Tornado, Sabre GT, Stinger): soft, wallowy, rolls; a modern supercar: flat and grippy. Log shows "Simcade profile <car> (1970s)".
- [ ] Settings > Driving model cycles Arcade / Simcade / Simulation.
- [ ] Workshop > Performance > Race parts: buy coilovers → Hub > Garage > Tuning > Springs unlocks; sliders change the feel.
- [ ] Anti-lag (needs turbo): pops off throttle. Straight pipe: raspier note.
- [ ] Class badge next to the car name on the HUD; PI changes after upgrades.
- [ ] Race now → lobby with grid, side bet (left/right), crew (X).
- [ ] Hub > Social > Crew: hire; crew car joins races (blue blip).
- [ ] Races > Series: enter the Street Cup, three rounds, podium at the end.
- [ ] "(Hot Pursuit)" races: police at the green light.
- [ ] Live event card top right after a few minutes; Season shown in the Hub top bar.
- [ ] Dealers > Auction house: bidding works, car delivered when you win.
- [ ] Settings > Radio station: station set when getting in a car.

## 0.33.0
- [ ] Enter Midnight Club: the chase cam is active immediately (no cycling needed). Cycle: Bumper, Hood, Roof, Cockpit, Chase near, Chase far, Action, Game.
- [ ] Hood / roof cams lean with the body in corners; bumper cam looks at the road.
- [ ] Chase near/far: FOV opens with speed, camera swings and rolls with lateral G, pulls back under acceleration; matches the Forza feel.
- [ ] After the welcome screen / lobby / photo mode the custom cam comes back by itself.
- [ ] No HUD blinking or hitches when street racers / police spawn.
- [ ] New speedometer: rev arc, coral gear, BOOST row on turbo cars, NOS row with a kit.
