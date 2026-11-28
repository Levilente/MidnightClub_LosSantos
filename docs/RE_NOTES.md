# GTA V Enhanced (2.0.1158.16) - physics reverse-engineering notes

Source: in-game probe (Settings > Driving > Physics probe): decrypted image dump, SHVDN offsets, 20 s raw wheel/vehicle memory.
The .exe on disk has an encrypted .text (entropy 7.99); .pdata is readable and gives function boundaries.
RVAs below are relative to the image base.

## CHandlingData (pairs and inverses)
- Biases are pairs summing to 2 (AWD drive bias, brake, traction, suspension, anti-roll) or 1 (RWD/FWD drive bias 0/1):
  drive 0x48/0x4C, brake 0x74/0x78, traction 0xB0/0xB4, suspension 0xD4/0xD8, anti-roll 0xE0/0xE4.
- 0x88 traction max, 0x8C = 1/max. 0x90 traction min, 0x94 = 1/(max-min). 0x98 lateral (radians), 0x9C = 1/lateral.
  0xA0 spring delta max, 0xA4 = 1/delta.
- Written through src/Physics/HandlingMem.cs (keeps the pairs/inverses), restored byte-exact.

## CVehicle
- 0x880 current gear (short), 0x886 high gear, 0x88C float[] gear ratios ([0] reverse, [g] gear g; stock 6-speed 3.333 1.949 1.392 1.095 0.946 0.9).
- 0x8BC drive max flat velocity (m/s), 0x8C0 max velocity (= flat x 1.2). Top speed in gear g = [0x8C0] / ratio[g].
- 0x960 handling pointer, 0xC30 wheel pointer array, 0xC38 wheel count, 0xB70/0xB80 vectors used by the wheel job.

## CWheel (0x230 bytes)
- 0x40/0x50/0x60 contact positions, 0x70 contact normal, 0xA0..0xA8 tyre force (world; z ~ load), 0xB0/0xC0 velocities,
  0x110 tyre radius, 0x13C static load share, 0x160 suspension compression, 0x16C rotation angle, 0x170 spin (rad/s, negative = forward),
  0x178 tyre temperature, 0x1A8 grip factor (0.65-1.0), 0x1D0/0x1D4 drive/brake torque inputs, 0x200 dynamic flags.

## Code
- 0xF27CF0: vehicle wheel job (rcx = job ctx {ctx, CVehicle*, out*, float dt}); reads wheels, gear ratio, calls 0xF27F40.
- 0xF27F40: per-vehicle wheel/transmission integration per physics step; calls 0xF28AE0 four times and 0xF299B0.
- 0xF299B0 -> 0xF22830: wheel spin integration (writes 0x16C, 0x170, 0x174, 0x1B8, 0x1C0, 0x1C4).
- 0xF28AE0 -> 0xF1E8B0 -> 0xF20010: candidates for tyre / suspension force (write 0xA0..).
- 0xF27F40 integrates the wheel/suspension state with **Runge-Kutta 4**: it calls 0xF28AE0 four times (k1..k4, each into a
  0x110-byte stack struct at rsp+0x430/0x320/0x210/0x100, the next call gets the previous struct as input) and combines
  them (k1 + 2k2 + 2k3 + k4) x dt/6; the per-wheel result is accumulated into CWheel+0xA0..0xA8. Then 0xF299B0 integrates wheel spin.
- So 0xF28AE0 = derivative evaluation: all wheel forces (suspension + tyre) for a given state. This is the function the
  native model replaces: same inputs, same 0x110-byte output layout (to be mapped field by field).
- Next: read 0xF28AE0 / 0xF20010 to find the tyre-force calculation and its inputs; hook point for the native model is 0xF27F40 (per vehicle, per step).

## Stage 2 result (hook recording, Elegy RH8, 15 s)
- Hooks at +0xF1E8B0 (friction) and +0xF1DB20 (suspension) installed fine; each called 4x per wheel per physics step (RK4).
- Friction(wheel, inst, outForce, linAcc, angAcc, st1, st2, spinAcc, load, ?, ?, ?, slot, ctx, otherWheel, gravity):
  outForce = tyre force at the contact, world, N (up to ~10 kN); linAcc = force / mass (m/s2); angAcc = angular acceleration
  contribution (rad/s2, world); spinAcc = wheel spin acceleration (rad/s2); load = suspension load from Susp (N, median 4.1 kN
  on a 1740 kg car); last arg = 9.8 (gravity), not dt.
- Susp(wheel, inst, outForce, linAcc, angAcc, loadOut, slot, ?, ?, ctx, otherWheel, gravity): outForce mostly vertical (N),
  loadOut = wheel load (N) handed to Friction.
- Next: map the per-wheel slip state (candidates CWheel 0x1C4, 0x1CC, 0x1D0, 0x1B8, 0x1C0) and the contact frame, then
  stage 3: replace the outputs with the Pacejka model (force -> lin = F/m, ang = scaled consistently).
