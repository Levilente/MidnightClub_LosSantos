/* sim_core.h - the native simulation for every vehicle (player, opponents, traffic, parked), at GTA's physics rate.
   Included by MidnightClubPhysics.c.

   Each vehicle the mod registers (MCP_VehSet, keyed by the CVehicle address) gets its own parameters (tyre, engine,
   gearbox, brakes, differentials, suspension, aero) and state. GTA's per-vehicle wheel step (0xF27F40) tells us
   which vehicle the following wheel evaluations belong to (thread-local), so the friction and suspension hooks
   find the vehicle and wheel directly.

   Tyre (inside GTA's friction evaluation, 4x per step, Runge-Kutta stages): slip from the contact-point velocity
   against the wheel's own spin, Pacejka per direction, load sensitivity, combined slip (normalised slip), sliding
   grip held at ~80 % of peak. Forces enter the body exactly like GTA's own (F/m, I^-1 (r x F) in the body frame):
   lateral at the roll-centre height, longitudinal near the contact patch. Aerodynamic drag and downforce act on the
   centre of mass. Motorbikes keep GTA's lean / camber side force (clamped to our friction circle) and get our
   longitudinal force, wheel spin and brakes.
   After the step: wheel spins in sub-steps (implicit in the tyre stiffness), engine from the real torque curve with
   turbo lag, clutch slip, engine inertia through the gearbox, limited-slip differentials per axle and inter-axle
   couplings, brakes with real lock-up, rolling resistance, ABS, torque-based TCS and brake-based stability control
   (ESC) for everybody. The driver's inputs (player or AI) are read from the vehicle itself. */

#define MAXV 96
#define MAXW 10
#define CURVE_N 48

typedef struct
{
    float on, muY, muX, loadSens, By, Cy, Ey, Bx, Cx, Ex;                      /* 0..9   tyre */
    float wet, rolling, idle, redline, peakNm, maxRpm, engI, eff, launch, boostTau;  /* 10..19 */
    float turbo, als, tyreR, wheelI, brakeCap, handCap, preDiff, powDiff, coastDiff, preC;  /* 20..29 */
    float powC, longAt, tcs, abs, esc, mass, cda, cla, wheelbase, bike;         /* 30..39 */
    float parked, gears, frontShare, brakeBias, hold, power, pad46, pad47, pad48, pad49;  /* 40..49 */
    float susOn, fF, fR, zbl, zbh, zrl, zrh, arbF, arbR, knee;                   /* 50..59 suspension: ride frequency front/rear (Hz), damping ratios, ARB / spring ratio */
    float bumpFrac, bumpK, maxFk, pad63, pad64, pad65, pad66, pad67, pad68, pad69;   /* 60..69 */
    float ratios[12];                                                           /* 70..81  [0] reverse, [g] overall ratio of gear g */
    float driven[MAXW];                                                         /* 82..91  1 = driven wheel */
    float full[CURVE_N], nb[CURVE_N];                                           /* 92..187 full-load torque, off-boost torque (Nm, 0..maxRpm) */
    float ambient, tOpt, tWin, relax, pressF, pressR, pOpt, camF;              /* 188..195 tyre temperature window (C), relaxation length (m), cold pressures / optimum (psi), static camber (rad) */
    float camR, caster, toeF, unsprung, kt, camGain, coldLoss, hotLoss;         /* 196..203 caster, front toe (rad, + = out), unsprung mass per corner (kg), tyre vertical stiffness (N/m), camber recovery, grip loss cold / hot */
    float offroad, record, aeroFront, rcF, rcR, thermal, pad210, pad211;     /* 204..211 grip lost on loose ground (share), record this vehicle (diagnostics) */
} VParams;
#define VP_N ((int)(sizeof(VParams) / 4))

typedef struct
{
    float w;                       /* spin, rad/s, + = forward (GTA stores - at 0x170) */
    float vx, ta, fz, mu;          /* last friction evaluation */
    float kappa, absF, lx, ly;     /* slip ratio, ABS pressure factor, local position (right, forward) of the contact */
    float x0sum; int x0n;          /* static compression measurement */
    int front, init, pair;
    LONG seen;
    /* own wheel-ground contact: the wheel (unsprung mass) on its tyre spring, the body on the suspension */
    float s, sv, x1, ft; int calls, gc, cinit;
    /* tyre: lagged slip (relaxation length), camber, temperature, wear */
    float taL, taRaw, vy, fxL, fyL, gam, temp, wear, muF, press, ftF, surf;
} WSim;

typedef struct
{
    volatile LONG used;            /* 0 free, 1 being written, 2 ready */
    u64 veh, wkey; int nw; u64 wheel[MAXW];   /* wkey = CVehicle+0xC30 (wheel pointer array), what the wheel step gets */
    VParams p; float kPeak, aPeakTan;
    WSim ws[MAXW];
    float engW, tcsF, boost, dt, yaw, vxB, escCut; int tcsAct, absAct, escAct, nGround, pairsDone, susReady;
    float x0[2]; int x0steps;
    double angErr, angMag; LONG angN; int bad;
    double sErr, sMag; LONG sN; int strutOk, strutBad; float M[12]; int Mok; float bodyMass;
    LONG steps, own;
} Veh;
static volatile int axisMode; static volatile LONG axA, axB;   /* which GTA tyre axis is the heading (1 = first, 2 = second), learned near straight ahead */

static Veh vehs[MAXV];
/* diagnostics recorder (one vehicle, one record per physics step) */
typedef struct { float t, dt, mass, speed, yaw, vxB, thr, brk, steer, gear; float w[4][14]; } SRec;
#define SREC_CAP 8000
static SRec *srec; static volatile LONG srecHead; static float srecT;
static __thread Veh *cur;          /* vehicle whose wheel step is running on this thread */
static LONG axisFallback;

static void vpeaks(Veh *v)
{
    float best = 0, at = 0.1f;
    for (int i = 1; i <= 400; i++) { float k = i * 0.001f, f = mf(k, v->p.Bx, v->p.Cx, v->p.Ex); if (f > best) { best = f; at = k; } }
    v->kPeak = at; best = 0; at = 0.1f;
    for (int i = 1; i <= 400; i++) { float a = i * 0.0015f, f = mf(a, v->p.By, v->p.Cy, v->p.Ey); if (f > best) { best = f; at = a; } }
    v->aPeakTan = tanf(at);
}
static float vmu(const Veh *v, float base, float fz)
{
    float fz0 = v->p.mass * 9.81f / (v->nw > 0 ? v->nw : 4);
    float d = fz0 > 1 ? (fz - fz0) / fz0 : 0; if (d < -0.9f) d = -0.9f; if (d > 3) d = 3;
    float m = base * (1 + v->p.loadSens * d); return m < 0.05f ? 0.05f : m;
}
/* combined-slip tyre: one normalised slip magnitude, each direction on its own Magic Formula curve and peak */
static void vtyre(const Veh *v, float kappa, float ta, float fz, float muS, float *fx, float *fy)
{
    float kn = kappa / v->kPeak, an = ta / v->aPeakTan, rho = sqrtf(kn * kn + an * an);
    if (rho < 1e-5f || fz <= 0) { *fx = 0; *fy = 0; return; }
    float mx = vmu(v, v->p.muX, fz) * muS, my = vmu(v, v->p.muY, fz) * muS;
    float sx = mf(rho * v->kPeak, v->p.Bx, v->p.Cx, v->p.Ex), sy = mf(atanf(rho * v->aPeakTan), v->p.By, v->p.Cy, v->p.Ey);
    if (rho > 1.0f) { if (sx < 0.78f) sx = 0.78f; if (sy < 0.82f) sy = 0.82f; }   /* sliding rubber keeps ~80 % of the peak */
    *fx = mx * fz * sx * kn / rho;
    *fy = my * fz * sy * an / rho;
}

static float dot3(const float *a, const float *b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
static void cross3(const float *a, const float *b, float *o) { o[0] = a[1] * b[2] - a[2] * b[1]; o[1] = a[2] * b[0] - a[0] * b[2]; o[2] = a[0] * b[1] - a[1] * b[0]; }
static float len3(const float *a) { return sqrtf(dot3(a, a)); }

/* lever arm of a force at this wheel, as GTA builds it (roll-centre height, half tyre width) */
static void lever(u64 wheel, u64 inst, const float *c, const float *com, float la, float rcOwn, float *rLat, float *rLong)
{
    uint32_t fl = *(uint32_t *)(wheel + 0x204);
    u64 hd = *(u64 *)(wheel + 0x120);
    int idx = rf(wheel, 0x140) < 0 ? 1 : 0;
    float rc = rcOwn >= 0 ? rcOwn : hd ? rf(hd, 0xE8 + 4 * idx) : 0.0f, a = 0, b = 0;
    if (fl & 1) { if (!(fl & 0x100)) b = rc; }
    else if (!(fl & 0x820000))
    {
        a = ((fl & 2) ? 1.0f : -1.0f) * rf(wheel, 0) * rf(wheel, 0x11C) * 0.5f;
        b = rc + (*(unsigned char *)(wheel + 0x20D) ? rf(wheel, 0x14C) : 0.0f);
    }
    const float *R = (const float *)(inst + 0x40), *U = (const float *)(inst + 0x60);
    for (int j = 0; j < 3; j++) { rLat[j] = c[j] + a * R[j] + b * U[j] - com[j]; rLong[j] = c[j] + a * R[j] + b * la * U[j] - com[j]; }
}
static void angOf(u64 wheel, u64 inst, const float *M, const float *t, float *ang)
{
    float tb[3] = { M[0] * t[0] + M[1] * t[1] + M[2] * t[2], M[4] * t[0] + M[5] * t[1] + M[6] * t[2], M[8] * t[0] + M[9] * t[1] + M[10] * t[2] };
    if (*(uint32_t *)(wheel + 0x204) & 0x80) tb[1] = 0;
    const float *S = (*(unsigned char *)(inst + 0x265) & 0x20) ? (const float *)(inst + 0xD0) : (const float *)(inst + 0x30);
    tb[0] *= S[0]; tb[1] *= S[1]; tb[2] *= S[2];
    for (int j = 0; j < 3; j++) ang[j] = M[j] * tb[0] + M[4 + j] * tb[1] + M[8 + j] * tb[2];
}
static float vmin(float dt) { float v = 150.0f * dt; return v < 2.0f ? 2.0f : v > 6.0f ? 6.0f : v; }

static Veh *findVeh(u64 veh) { for (int i = 0; i < MAXV; i++) if (vehs[i].used == 2 && vehs[i].veh == veh) return &vehs[i]; return NULL; }
static Veh *findByWheels(u64 wk) { if (!wk) return NULL; for (int i = 0; i < MAXV; i++) if (vehs[i].used == 2 && vehs[i].wkey == wk) return &vehs[i]; return NULL; }
static int wheelIdx(Veh *v, u64 wheel) { for (int i = 0; i < v->nw; i++) if (v->wheel[i] == wheel) return i; return -1; }

/* ---------------- tyre force for one friction evaluation; 1 = outputs replaced ---------------- */
static int vehTyre(Veh *v, int i, u64 wheel, u64 inst, float *out, float *lin, float *ang, float load, u64 st1, u64 st2, u64 Mp, u64 linV, u64 angV, float grav)
{
    if (v->bad || i < 0 || !inst || !st1 || !st2 || !Mp || !linV || !angV) return 0;
    float mass = rf(inst, 0x13C);
    if (load <= 0.01f * mass * grav || load < 1.0f) return 0;
    WSim *s = &v->ws[i];
    float R = rf(wheel, 0x110); if (R < 0.05f) return 0;
    if (!s->init) { s->w = -rf(wheel, 0x170); s->absF = 1; s->init = 1; }
    const float *F = (const float *)(inst + 0x50), *Rt = (const float *)(inst + 0x40), *U = (const float *)(inst + 0x60);
    float *a1 = (float *)st1, *a2 = (float *)st2, la1 = len3(a1), la2 = len3(a2), fwd[3], side[3];
    if (la1 > 0.9f && la1 < 1.1f && la2 > 0.9f && la2 < 1.1f && fabsf(dot3(a1, a2)) < 0.2f)
    {
        /* GTA's axis order is fixed: learn it while the wheel points nearly straight ahead, then always use it
           (at big steering angles the side axis can be closer to the car's nose than the heading) */
        float d1 = fabsf(dot3(a1, F)), d2 = fabsf(dot3(a2, F));
        if (!axisMode) { if (d1 > 0.98f) InterlockedIncrement(&axA); else if (d2 > 0.98f) InterlockedIncrement(&axB);
                         if (axA > 400 && axA > 20 * axB) axisMode = 1; else if (axB > 400 && axB > 20 * axA) axisMode = 2; }
        float *f = axisMode == 1 ? a1 : axisMode == 2 ? a2 : (d1 >= d2 ? a1 : a2), *g = f == a1 ? a2 : a1;
        if (!axisMode && fabsf(d1 - d2) < 0.5f) return 0;              /* not learned yet and ambiguous: leave it to GTA */
        float sf = dot3(f, F) >= 0 ? 1.0f : -1.0f, sg = dot3(g, Rt) >= 0 ? 1.0f : -1.0f;
        for (int j = 0; j < 3; j++) { fwd[j] = f[j] * sf; side[j] = g[j] * sg; }
        /* the tyre works in the car's own plane: GTA tilts its axes with the contact normal, which on a kerb edge
           points sideways and turns part of the side force into a vertical / sideways shove (kerb snapping) */
        float fu = dot3(fwd, U), fh[3] = { fwd[0] - U[0] * fu, fwd[1] - U[1] * fu, fwd[2] - U[2] * fu }, lf = len3(fh);
        if (lf > 0.3f)
        {
            for (int j = 0; j < 3; j++) fwd[j] = fh[j] / lf;
            cross3(fwd, U, side); float ls = len3(side); if (ls > 0.3f) for (int j = 0; j < 3; j++) side[j] /= ls;
            if (dot3(side, Rt) < 0) for (int j = 0; j < 3; j++) side[j] = -side[j];
        }
    }
    else { InterlockedIncrement(&axisFallback); return 0; }
    float c[3] = { rf(wheel, 0x50), rf(wheel, 0x54), rf(wheel, 0x58) }, com[3] = { rf(inst, 0x70), rf(inst, 0x74), rf(inst, 0x78) };
    float r0[3] = { c[0] - com[0], c[1] - com[1], c[2] - com[2] }, wr[3];
    const float *lv = (const float *)linV, *av = (const float *)angV;
    cross3(av, r0, wr);
    float rel[3] = { lv[0] + wr[0] - rf(wheel, 0x90), lv[1] + wr[1] - rf(wheel, 0x94), lv[2] + wr[2] - rf(wheel, 0x98) };
    float vx = dot3(rel, fwd), vy = dot3(rel, side);
    float den = fabsf(vx), vm = vmin(v->dt); if (den < vm) den = vm;
    float sgn = dot3(r0, Rt) > 0 ? 1.0f : -1.0f;
    float kappa = (s->w * R - vx) / den, ta = vy / den;
    if (s->front) ta -= sgn * v->p.toeF;                                 /* toe: a static slip angle per wheel */
    ta -= 0.1f * s->gam * sgn;                                           /* camber thrust: a leaning tyre pushes towards its lean */
    float bl = (fabsf(vx) + 3.0f) * (v->dt > 0.001f ? v->dt : 0.011f) / (v->p.relax > 0.05f ? v->p.relax : 0.45f); if (bl > 1) bl = 1;
    float taE = s->taL + bl * (ta - s->taL);                             /* relaxation length: side force builds up over the first ~0.5 m */
    /* ground: GTA's own surface value (1 = road, lower = grass / dirt / sand), scaled by how much this tyre minds loose ground */
    float sf0 = rf(wheel, 0x1A4); if (!(sf0 >= 0 && sf0 <= 1.5f)) sf0 = 1; if (sf0 > 1) sf0 = 1;
    float g = 1.0f - (1.0f - sf0) * (v->p.offroad > 0 ? v->p.offroad : 0.6f); if (g < 0.25f) g = 0.25f;
    s->surf = g; v->bodyMass = mass;
    float muS = v->p.wet * g * (s->muF > 0.3f ? s->muF : 1.0f);
    s->taRaw = ta; s->vy = vy; ta = taE;
    float fx, fy; vtyre(v, kappa, ta, load, muS, &fx, &fy);
    /* lateral force enters the body at the suspension's real roll centre (GTA uses 0.3-0.45 m to hide body roll,
       which moves the load transfer into rigid links: an instant, snappy body); longitudinal near the contact patch */
    float rcOwn = s->front ? v->p.rcF : v->p.rcR; if (!(rcOwn > 0.005f && rcOwn < 0.8f)) rcOwn = -1;
    float rLat[3], rLong[3], gLat[3], gLong[3]; lever(wheel, inst, c, com, v->p.longAt, rcOwn, rLat, rLong); lever(wheel, inst, c, com, v->p.longAt, -1, gLat, gLong);
    const float *M = (const float *)Mp;
    float gtaLat = dot3(out, side);
    {   /* check our body coupling against GTA's own force */
        float t[3], pa[3]; cross3(gLat, out, t); angOf(wheel, inst, M, t, pa);
        float e = (pa[0] - ang[0]) * (pa[0] - ang[0]) + (pa[1] - ang[1]) * (pa[1] - ang[1]) + (pa[2] - ang[2]) * (pa[2] - ang[2]);
        v->angErr += sqrtf(e); v->angMag += len3(ang); v->angN++;
        if (v->angN > 3000 && v->angErr > 0.3 * v->angMag) { v->bad = 1; return 0; }
    }
    float FL[3], FY[3], Fw[3], latF;
    if (v->p.bike > 0.5f)
    {   /* motorbike: GTA's lean / camber side force, limited to what the tyre has left next to our longitudinal force */
        float cap = vmu(v, v->p.muY, load) * muS * load, left = cap * cap - fx * fx;
        left = left > 0 ? sqrtf(left) : 0;
        latF = gtaLat > left ? left : gtaLat < -left ? -left : gtaLat;
    }
    else latF = -fy;
    for (int j = 0; j < 3; j++) { FL[j] = fx * fwd[j]; FY[j] = latF * side[j]; Fw[j] = FL[j] + FY[j]; }
    float km = rf(wheel, 0x144) * rf(inst, 0x3C);
    float t1[3], t2[3], t[3], na[3]; cross3(rLong, FL, t1); cross3(rLat, FY, t2);
    for (int j = 0; j < 3; j++) t[j] = t1[j] + t2[j];
    angOf(wheel, inst, M, t, na);
    if (!(fabsf(Fw[0] + Fw[1] + Fw[2] + na[0] + na[1] + na[2]) < 1e8f)) { s->init = 0; return 0; }
    /* aerodynamic drag and downforce on the centre of mass, shared by the wheels on the ground */
    /* aerodynamics: drag on the centre of mass, downforce (or lift) on each axle by the aero balance, so it changes
       the handling at speed and not only the total grip */
    float sp = len3(lv), aero[3] = { 0, 0, 0 }, down[3] = { 0, 0, 0 };
    if (sp > 1.0f && (v->p.cda > 0 || v->p.cla != 0))
    {
        float q = 0.5f * 1.2f * sp, ng = (float)(v->nGround > 0 ? v->nGround : v->nw);
        int nf = 0, nr = 0; for (int j = 0; j < v->nw; j++) { if (v->ws[j].front) nf++; else nr++; }
        float af = v->p.aeroFront > 0.01f && v->p.aeroFront < 0.99f ? v->p.aeroFront : 0.45f;
        float share = s->front ? (nf ? af / nf : 0) : (nr ? (1 - af) / nr : 0);
        for (int j = 0; j < 3; j++) { aero[j] = -q * v->p.cda * lv[j] / ng; down[j] = -q * sp * v->p.cla * share * U[j]; }
        float td[3], ad[3]; cross3(r0, down, td); angOf(wheel, inst, M, td, ad);
        for (int j = 0; j < 3; j++) na[j] += ad[j];
    }
    float im = rf(inst, 0x3C);
    for (int j = 0; j < 3; j++) { out[j] = Fw[j]; lin[j] = Fw[j] * km + (aero[j] + down[j]) * im; ang[j] = na[j]; }
    s->vx = vx; s->ta = ta; s->fz = load; s->mu = muS; s->kappa = kappa; s->fxL = fx; s->fyL = latF;
    if (i == 0) { for (int j = 0; j < 12; j++) v->M[j] = M[j]; v->Mok = 1; }
    s->lx = dot3(r0, Rt); s->ly = dot3(r0, F);
    if (i == 0) { v->yaw = dot3(av, U); v->vxB = dot3(lv, F); }
    InterlockedIncrement(&s->seen);
    InterlockedIncrement(&v->own);
    return 1;
}

/* ---------------- suspension and wheel-ground contact ----------------
   Spring from the ride frequency on the wheel's static load, preload that keeps GTA's own ride height (measured
   over the first seconds), digressive dampers, progressive bump stop, anti-roll bar. Between the suspension and the
   road sits the wheel itself: an unsprung mass on the tyre's vertical spring, integrated after each step. A kerb or
   a bump compresses the tyre first and kicks the wheel up; the body only feels it through the spring and damper,
   and the tyre's grip follows the real tyre load (a hopping wheel loses grip). The force acts along the strut
   (the body's vertical axis), not along a steep kerb-edge contact normal, so kerbs don't snap the car sideways. */
static float susMc(const Veh *v, u64 wheel) { float sh = rf(wheel, 0x13C); if (sh < 0.02f || sh > 0.8f) sh = 1.0f / (v->nw > 0 ? v->nw : 4); return (v->bodyMass > 50 ? v->bodyMass : v->p.mass) * sh; }   /* the body GTA actually simulates */
static float susForce(const Veh *v, u64 wheel, int i, float sS, float vS, float sOther, int hasOther)
{
    int ax = rf(wheel, 0x140) < 0 ? 1 : 0;
    float mc = susMc(v, wheel), f = ax ? v->p.fR : v->p.fF, w = 6.2831853f * f;
    float k = mc * w * w, cc = 2.0f * sqrtf(k * mc), kt = v->p.kt > 1000 ? v->p.kt : 220000.0f;
    /* GTA's travel (CWheel+0x130): at the end of it GTA makes a rigid body contact, which is what bounced the car.
       The static position sits at 30-50 % of that travel (GTA's own ride height when it allows), and our bump stop
       takes the rest progressively so the body never reaches GTA's hard limit. */
    float T = rf(wheel, 0x130); if (!(T > 0.05f && T < 0.8f)) T = 0.25f;
    float d0 = mc * 9.81f / kt, x0 = v->x0[ax] > 0.01f ? v->x0[ax] : 0.4f * T, s0 = x0 - d0;
    if (s0 < 0.3f * T) s0 = 0.3f * T; if (s0 > 0.5f * T) s0 = 0.5f * T;
    float pre = mc * 9.81f - k * s0;
    float fs = pre + k * sS, fd, kn = v->p.knee > 0.01f ? v->p.knee : 0.1f;
    if (vS >= 0) fd = vS < kn ? cc * v->p.zbl * vS : cc * (v->p.zbl * kn + v->p.zbh * (vS - kn));
    else { float u = -vS; fd = -(u < kn ? cc * v->p.zrl * u : cc * (v->p.zrl * kn + v->p.zrh * (u - kn))); }
    float bumpAt = T - d0 - 0.06f; if (bumpAt < s0 + 0.03f) bumpAt = s0 + 0.03f;
    float over = sS - bumpAt, room = T - d0 - 0.01f - bumpAt; if (room < 0.02f) room = 0.02f;
    float fb = over > 0 ? mc * 9.81f * 4.0f * (over / room) * (over / room) + k * 2.0f * over : 0.0f;   /* progressive rubber: 4 g of the corner's weight at the end */
    float fa = hasOther ? k * (ax ? v->p.arbR : v->p.arbF) * (sS - sOther) : 0.0f;
    float fo = fs + fd + fb + fa, mx = mc * 9.81f * (v->p.maxFk > 0 ? v->p.maxFk : 3.5f) + fb;
    if (fo < 0) fo = 0; if (fo > mx) fo = mx;
    (void)i; return fo;
}
static int vehSusp(Veh *v, int i, u64 wheel, u64 inst, u64 other, float *out, float *lin, float *ang, float *loadOut)
{
    if (!(v->p.susOn > 0.5f) || v->p.bike > 0.5f || i < 0) return 0;
    WSim *s = &v->ws[i];
    float x = rf(wheel, 0x160);
    float gz = fabsf(out[2]);
    if (s->calls == 0) s->x1 = x;
    s->calls++;
    if (gz > 20.0f) s->gc = 1;
    if (!v->susReady)
    {
        if (x > 0.005f && x < 0.6f && gz > 20.0f) { s->x0sum += x; s->x0n++; }
        return 0;
    }
    if (!s->cinit || gz < 20.0f) return 0;                       /* no contact for GTA: nothing to push */
    float dx = x - s->x1, sS = s->s + dx; if (sS < 0) sS = 0;   /* the wheel holds its place during the step, the body moves */
    int j = s->pair; float so = j >= 0 && j < v->nw ? v->ws[j].s : 0;
    float F = susForce(v, wheel, i, sS, s->sv, so, j >= 0);
    const float *U = (const float *)(inst + 0x60);
    float c[3] = { rf(wheel, 0x50), rf(wheel, 0x54), rf(wheel, 0x58) }, com[3] = { rf(inst, 0x70), rf(inst, 0x74), rf(inst, 0x78) };
    float r[3] = { c[0] - com[0], c[1] - com[1], c[2] - com[2] };
    if (v->Mok && !v->strutOk && !v->strutBad)
    {   /* check that we can rebuild GTA's own suspension torque before we use our own direction */
        float t[3], pa[3]; cross3(r, out, t); angOf(wheel, inst, v->M, t, pa);
        float e = sqrtf((pa[0] - ang[0]) * (pa[0] - ang[0]) + (pa[1] - ang[1]) * (pa[1] - ang[1]) + (pa[2] - ang[2]) * (pa[2] - ang[2]));
        v->sErr += e; v->sMag += len3(ang); v->sN++;
        if (v->sN > 1500) { if (v->sErr < 0.15 * v->sMag) v->strutOk = 1; else v->strutBad = 1; }
    }
    if (v->strutOk)
    {
        float km = rf(wheel, 0x144) * rf(inst, 0x3C), o[3] = { F * U[0], F * U[1], F * U[2] }, t[3], na[3];
        cross3(r, o, t); angOf(wheel, inst, v->M, t, na);
        if (!(fabsf(o[0] + o[1] + o[2] + na[0] + na[1] + na[2]) < 1e8f)) return 0;
        for (int q = 0; q < 3; q++) { out[q] = o[q]; lin[q] = o[q] * km; ang[q] = na[q]; }
    }
    else
    {
        float sc = F / gz;
        for (int q = 0; q < 3; q++) { out[q] *= sc; lin[q] *= sc; ang[q] *= sc; }
    }
    if (loadOut) *loadOut = s->ftF;                              /* the tyre's grip comes from the tyre's own load (contact patch ~20 ms) */
    return 1;
}
/* after the step: the wheel on its tyre spring against the suspension, in sub-steps */
static void wheelHop(Veh *v, int i, u64 wheel, float dt)
{
    WSim *s = &v->ws[i];
    float x = rf(wheel, 0x160), kt = v->p.kt > 1000 ? v->p.kt : 220000.0f, mu = v->p.unsprung > 5 ? v->p.unsprung : 40.0f;
    kt *= s->press > 5 && v->p.pOpt > 5 ? s->press / v->p.pOpt : 1.0f;
    float mc = susMc(v, wheel);
    if (!s->cinit)
    {
        s->s = x - mc * 9.81f / kt; if (s->s < 0) s->s = 0; s->sv = 0; s->ft = s->ftF = mc * 9.81f; s->cinit = 1; s->calls = 0; s->gc = 0;
        return;
    }
    float xe = s->gc ? x : -0.05f, xs = s->calls > 0 && s->gc ? s->x1 : xe, xr = (xe - xs) / dt;
    float ct = 2.0f * 0.08f * sqrtf(kt * mu), ft = 0;
    int j = s->pair; float so = j >= 0 && j < v->nw ? v->ws[j].s : 0;
    const int m = 10; float h = dt / m;
    for (int k = 0; k < m; k++)
    {
        float xt = xs + (xe - xs) * (k + 1) / m, d = xt - s->s;
        ft = d > 0 ? kt * d + ct * (xr - s->sv) : 0; if (ft < 0) ft = 0;
        float fs = susForce(v, wheel, i, s->s, s->sv, so, j >= 0);
        s->sv += h * (ft - fs) / mu; s->s += h * s->sv;
        if (s->s < 0) { s->s = 0; if (s->sv < 0) s->sv = 0; }
        { float Tm = rf(wheel, 0x130); if (!(Tm > 0.05f && Tm < 0.8f)) Tm = 0.4f; if (s->s > Tm) { s->s = Tm; if (s->sv > 0) s->sv = 0; } }
    }
    s->ft = ft; s->calls = 0; s->gc = 0;
    float a = dt / 0.02f; if (a > 1) a = 1; s->ftF += (ft - s->ftF) * a;
}

static float vcurve(const Veh *v, const float *t, float rpm)
{
    float mx = v->p.maxRpm > 100 ? v->p.maxRpm : 8000, x = rpm / mx * (CURVE_N - 1); if (x < 0) x = 0;
    int i = (int)x; if (i >= CURVE_N - 1) return t[CURVE_N - 1];
    float f = x - i; return t[i] + (t[i + 1] - t[i]) * f;
}
static float vengine(const Veh *v, float rpm, float thr, float cut)
{
    float drag = -(0.05f + 0.15f * rpm / (v->p.redline > 100 ? v->p.redline : 7000)) * v->p.peakNm;
    if (rpm >= v->p.redline) return drag;
    if (rpm < v->p.idle && thr < 0.05f) return 0.0f;
    float full = vcurve(v, v->p.full, rpm);
    if (v->p.turbo > 0.5f) { float nb = vcurve(v, v->p.nb, rpm); full = nb + (full - nb) * v->boost; }
    if (v->p.power > 0.05f) full *= v->p.power;          /* nitrous, slipstream, race catch-up */
    return thr * full * cut + (1 - thr) * drag;
}

#define RPM_W 9.5492966f
static void pairUp(Veh *v)
{
    for (int i = 0; i < v->nw; i++) v->ws[i].pair = -1;
    for (int i = 0; i < v->nw; i++)
    {
        if (v->ws[i].pair >= 0) continue;
        int best = -1; float bd = 0.35f;
        for (int j = 0; j < v->nw; j++)
        {
            if (j == i || v->ws[j].pair >= 0) continue;
            if ((v->ws[i].lx > 0) == (v->ws[j].lx > 0)) continue;
            float d = fabsf(v->ws[i].ly - v->ws[j].ly); if (d < bd) { bd = d; best = j; }
        }
        if (best >= 0) { v->ws[i].pair = best; v->ws[best].pair = i; }
    }
    v->pairsDone = 1;
}

/* ---------------- after GTA's step: wheel spins, engine, clutch, diffs, brakes, assists ---------------- */
static void vehStep(Veh *v, u64 *wp, int nw, float dt)
{
    if (dt <= 0 || dt > 0.1f) return;
    v->dt = dt; v->steps++;
    u64 veh = v->veh; const VParams *p = &v->p;
    if (nw > MAXW) nw = MAXW;
    for (int i = 0; i < nw; i++) v->wheel[i] = wp[i];
    v->nw = nw;
    /* suspension: measure GTA's ride height first */
    if (!v->susReady && ++v->x0steps > 150)
    {
        float s[2] = { 0, 0 }; int n[2] = { 0, 0 };
        for (int i = 0; i < nw; i++) { int ax = rf(wp[i], 0x140) < 0 ? 1 : 0; if (v->ws[i].x0n > 50) { s[ax] += v->ws[i].x0sum / v->ws[i].x0n; n[ax]++; } }
        if (n[0] && n[1]) { v->x0[0] = s[0] / n[0]; v->x0[1] = s[1] / n[1]; v->susReady = 1; }
        else if (v->x0steps > 1500) { v->x0[0] = v->x0[1] = 0.1f; v->susReady = 1; }
    }
    if (v->susReady && p->susOn > 0.5f && p->bike < 0.5f) { for (int i = 0; i < nw; i++) if (wp[i]) wheelHop(v, i, wp[i], dt); }
    else for (int i = 0; i < nw; i++) { v->ws[i].calls = 0; v->ws[i].gc = 0; }
    /* driver inputs, straight from the vehicle */
    float thr = *(float *)(veh + 0x8D8); thr = thr < 0 ? -thr : thr; if (thr > 1) thr = 1;
    float brk = *(float *)(veh + 0x9E8); if (brk < 0) brk = 0; if (brk > 1) brk = 1;
    float hand = (*(unsigned char *)(veh + 0x9EC) & 1) ? 1.0f : 0.0f;
    int gear = *(short *)(veh + 0x882);
    float clutch = *(float *)(veh + 0x8D4);
    float steer = *(float *)(veh + 0x9DC);
    if (p->parked > 0.5f) { thr = 0; brk = 1; hand = 1; }
    int ng = (int)p->gears; if (ng < 1) ng = 1; if (ng > 11) ng = 11;
    float ratio = gear <= 0 ? -p->ratios[0] : p->ratios[gear > ng ? ng : gear];
    float Rg = 0; int nd = 0;
    float R[MAXW], I[MAXW], share[MAXW]; int ok[MAXW], nGround = 0;
    for (int i = 0; i < nw; i++)
    {
        WSim *s = &v->ws[i];
        ok[i] = wp[i] != 0;
        R[i] = ok[i] ? rf(wp[i], 0x110) : 0.33f; if (R[i] < 0.05f) { ok[i] = 0; R[i] = 0.33f; }
        if (ok[i] && !s->init) { s->w = -rf(wp[i], 0x170); s->absF = 1; s->init = 1; }
        s->front = rf(wp[i], 0x140) >= 0;
        if (!s->seen) s->fz = 0; else nGround++;
        s->seen = 0;
        I[i] = p->wheelI > 0.2f ? p->wheelI : 1.2f;
        if (p->driven[i] > 0.5f) { Rg += R[i]; nd++; }
    }
    v->nGround = nGround;
    if (!v->pairsDone && nGround == nw && nw >= 4) pairUp(v);
    /* tyre state per wheel: camber from body roll and caster, relaxation, temperature, pressure, wear */
    {
        float amb = p->ambient > -30 && p->ambient < 60 ? p->ambient : 20.0f;
        for (int i = 0; i < nw; i++)
        {
            WSim *s = &v->ws[i];
            if (s->temp == 0) { s->temp = amb + 5; }
            float sgn = s->lx > 0 ? 1.0f : -1.0f, g0 = s->front ? p->camF : p->camR, roll = 0;
            int j = s->pair;
            if (j >= 0 && j < nw) { float tr = fabsf(s->lx - v->ws[j].lx); if (tr > 0.5f) { float sR = s->lx > 0 ? s->s : v->ws[j].s, sL = s->lx > 0 ? v->ws[j].s : s->s; roll = (sR - sL) / tr; } }
            s->gam = g0 + sgn * (1 - p->camGain) * roll + (s->front ? -sgn * sinf(p->caster) * steer : 0.0f);
            float bl = (fabsf(s->vx) + 3.0f) * dt / (p->relax > 0.05f ? p->relax : 0.45f); if (bl > 1) bl = 1;
            s->taL += bl * (s->taRaw - s->taL);
            if (s->fz <= 0) s->taL *= 0.9f;
            /* heat: sliding power into the tread + rolling hysteresis, cooled by the air */
            float den = fabsf(s->vx) > 2 ? fabsf(s->vx) : 2, slipX = s->kappa * den;
            float pSl = s->fz > 0 ? fabsf(s->fxL * slipX) + fabsf(s->fyL * s->vy) : 0;
            float pRo = s->fz > 0 ? 0.5f * p->rolling * s->fz * fabsf(s->vx) : 0;
            float hc = 18.0f + 2.8f * fabsf(s->vx);
            s->temp += dt * (0.4f * pSl + pRo - hc * (s->temp - amb)) / 7000.0f;
            if (s->temp > 160) s->temp = 160; if (s->temp < amb - 5) s->temp = amb - 5;
            if (!(p->thermal > 0.5f)) s->temp = p->tOpt > 0 ? p->tOpt : 80;   /* temperature simulation off: always at working temperature */
            s->wear += 6e-8f * pSl * dt; if (s->wear > 1) s->wear = 1;
            float pc = s->front ? p->pressF : p->pressR; if (pc < 10) pc = 32;
            s->press = pc * (s->temp + 273.0f) / (amb + 273.0f);
            float tw = p->tWin > 5 ? p->tWin : 40, dT = (s->temp - (p->tOpt > 0 ? p->tOpt : 80)) / tw;
            float muT = dT < 0 ? 1 - p->coldLoss * (dT * dT > 1 ? 1 : dT * dT) : 1 - p->hotLoss * (dT * dT > 2.5f ? 2.5f : dT * dT);
            float po = p->pOpt > 5 ? p->pOpt : 32, dp = (s->press - po) / po;
            float muP = 1 - 0.3f * dp * dp, muW = 1 - 0.25f * s->wear, muC = 1 - 5.0f * s->gam * s->gam;
            float mu = muT * muP * muW * muC; if (mu < 0.5f) mu = 0.5f; if (mu > 1.1f) mu = 1.1f;
            s->muF = mu;
        }
    }
    for (int i = 0; i < nw; i++) share[i] = 0;
    {
        float fs = p->frontShare; int nf = 0, nr = 0;
        for (int i = 0; i < nw; i++) if (p->driven[i] > 0.5f) { if (v->ws[i].front) nf++; else nr++; }
        if (!nf) fs = 0; if (!nr) fs = 1;
        for (int i = 0; i < nw; i++) if (p->driven[i] > 0.5f) share[i] = v->ws[i].front ? fs / nf : (1 - fs) / nr;
    }
    Rg = nd ? Rg / nd : 0.33f;
    float rEff = ratio * Rg / (p->tyreR > 0.1f ? p->tyreR : 0.33f);
    int coupled = fabsf(rEff) > 0.01f && clutch > 0.5f && nd > 0;
    float vAvg = 0; int nv = 0; for (int i = 0; i < nw; i++) if (v->ws[i].fz > 0) { vAvg += v->ws[i].vx; nv++; } if (nv) vAvg /= nv;
    int hold = p->hold > 0.5f && thr < 0.02f && fabsf(vAvg) < 0.6f;
    /* turbo */
    if (p->turbo > 0.5f)
    {
        float want = thr; if (p->als > 0.5f && want < 0.7f) want = 0.7f;
        float tau = want > v->boost ? (p->boostTau > 0.05f ? p->boostTau : 0.5f) : 0.25f;
        v->boost += (want - v->boost) * (dt / tau > 1 ? 1 : dt / tau);
    }
    else v->boost = thr;
    /* stability control: compare the yaw rate with what the steering asks for (limited by grip) */
    float escB[MAXW]; for (int i = 0; i < nw; i++) escB[i] = 0;
    v->escAct = 0; v->escCut = 0;
    if (p->esc > 0.5f && nGround >= 3 && v->vxB > 5.0f && p->bike < 0.5f)
    {
        float L = p->wheelbase > 1 ? p->wheelbase : 2.6f, vx = v->vxB;
        float rRef = vx * tanf(steer) / (L * (1 + 0.0016f * vx * vx)), rMax = 0.9f * p->muY * 9.81f / vx;
        if (rRef > rMax) rRef = rMax; if (rRef < -rMax) rRef = -rMax;
        float r = v->yaw, tol = 0.05f + (10 - p->esc) * 0.035f, e = r - rRef;
        float over = (r * rRef >= 0 && fabsf(r) > fabsf(rRef)) || (r * rRef < 0 && fabsf(r) > 0.1f) ? fabsf(e) - tol : 0;
        float under = (r * rRef >= 0 && fabsf(r) < fabsf(rRef)) ? fabsf(e) - tol * 1.5f : 0;
        float gain = 1.2f + 0.15f * p->esc;
        if (over > 0)
        {   /* brake the outer front: yaw moment against the slide */
            int sideRight = r > 0;
            for (int i = 0; i < nw; i++) if (v->ws[i].front && ((v->ws[i].lx > 0) == sideRight)) escB[i] = over * gain > 0.8f ? 0.8f : over * gain;
            v->escCut = over * 2.0f > 0.8f ? 0.8f : over * 2.0f; v->escAct = 1;
        }
        else if (under > 0)
        {   /* brake the inner rear: helps the car turn in */
            int sideRight = rRef < 0;
            for (int i = 0; i < nw; i++) if (!v->ws[i].front && ((v->ws[i].lx > 0) == sideRight)) escB[i] = under * gain * 0.6f > 0.5f ? 0.5f : under * gain * 0.6f;
            v->escCut = under * 1.5f > 0.6f ? 0.6f : under * 1.5f; v->escAct = 1;
        }
    }
    float thrE = thr * (1 - v->escCut);
    int n = (int)ceilf(dt / 0.001f); if (n < 1) n = 1; if (n > 48) n = 48;
    float h = dt / n;
    int tcsAct = 0, absAct = 0;
    float absLim = v->kPeak * (1.0f + (10 - p->abs) * 0.10f), tcsLim = v->kPeak * (1.0f + (10 - p->tcs) * 0.15f);
    float brakeF = p->brakeCap * p->brakeBias, brakeR = p->brakeCap * (1 - p->brakeBias);
    int nF = 0, nR = 0; for (int i = 0; i < nw; i++) { if (v->ws[i].front) nF++; else nR++; }
    for (int k = 0; k < n; k++)
    {
        float wIn = 0; for (int i = 0; i < nw; i++) wIn += share[i] * v->ws[i].w;
        float rpmW = wIn * rEff * RPM_W, te = 0, Ie = 0;
        if (coupled)
        {
            float target = p->idle + thrE * (p->launch - p->idle);
            if (rpmW < target && rpmW < p->launch)
            {   /* clutch slipping */
                float er = v->engW * RPM_W, want = target > rpmW ? target : rpmW;
                er += (want - er) * (h / 0.12f); v->engW = er / RPM_W;
                te = thrE > 0.02f ? vengine(v, er, thrE, v->tcsF) : (rpmW < p->idle * 0.5f && !hold && brk < 0.05f ? 0.04f * p->peakNm : 0.0f);
                if (te < 0) te = 0;
            }
            else
            {
                v->engW = rpmW / RPM_W; if (v->engW < p->idle / RPM_W) v->engW = p->idle / RPM_W;
                te = vengine(v, rpmW, thrE, v->tcsF);
                Ie = p->engI * rEff * rEff;
            }
        }
        else
        {
            float er = v->engW * RPM_W;
            v->engW += h * vengine(v, er, thrE, 1.0f) / (p->engI > 0.02f ? p->engI : 0.15f);
            if (v->engW * RPM_W < p->idle) v->engW = p->idle / RPM_W;
            if (v->engW * RPM_W > p->redline * 1.02f) v->engW = p->redline * 1.02f / RPM_W;
        }
        float tIn = te * rEff * (te > 0 ? p->eff : 1.0f), Td[MAXW], Iw[MAXW];
        for (int i = 0; i < nw; i++) { Td[i] = tIn * share[i]; Iw[i] = I[i] + Ie * share[i]; }
        /* limited-slip differential per axle: preload + power / coast ramp */
        for (int i = 0; i < nw; i++)
        {
            int j = v->ws[i].pair; if (j <= i || share[i] <= 0 || share[j] <= 0) continue;
            float tAx = Td[i] + Td[j], lim = p->preDiff + (te >= 0 ? p->powDiff : p->coastDiff) * fabsf(tAx);
            float d = v->ws[i].w - v->ws[j].w, ih = Iw[i] * Iw[j] / (Iw[i] + Iw[j]);
            float t = d * ih / h; if (t > lim) t = lim; if (t < -lim) t = -lim;
            Td[i] -= t; Td[j] += t;
        }
        /* inter-axle coupling (AWD centre / tandem axles): each driven axle against the mean */
        if (nd > 2)
        {
            float wm = 0, sm = 0; for (int i = 0; i < nw; i++) if (share[i] > 0) { wm += v->ws[i].w * share[i]; sm += share[i]; }
            if (sm > 0)
            {
                wm /= sm; float lim = p->preC + p->powC * fabsf(tIn);
                for (int i = 0; i < nw; i++)
                {
                    int j = v->ws[i].pair; if (share[i] <= 0 || (j >= 0 && j < i)) continue;
                    float wa = j >= 0 ? 0.5f * (v->ws[i].w + v->ws[j].w) : v->ws[i].w, ia = j >= 0 ? Iw[i] + Iw[j] : Iw[i];
                    float t = (wa - wm) * ia / h * 0.5f, l = lim * (j >= 0 ? share[i] + share[j] : share[i]);
                    if (t > l) t = l; if (t < -l) t = -l;
                    if (j >= 0) { Td[i] -= t * 0.5f; Td[j] -= t * 0.5f; } else Td[i] -= t;
                }
            }
        }
        float kMaxDriven = -10;
        for (int i = 0; i < nw; i++)
        {
            if (!ok[i]) continue;
            WSim *s = &v->ws[i];
            float fx = 0, fy, J = 0, den = 1;
            if (s->fz > 0)
            {
                den = fabsf(s->vx); float vm = vmin(dt); if (den < vm) den = vm;
                float kp = (s->w * R[i] - s->vx) / den, fx2, fy2, e = 0.002f;
                vtyre(v, kp, s->ta, s->fz, s->mu, &fx, &fy);
                vtyre(v, kp + e, s->ta, s->fz, s->mu, &fx2, &fy2);
                J = (fx2 - fx) / e * R[i] / den; if (J < 0) J = 0;
                s->kappa = kp;
                if (share[i] > 0 && kp > kMaxDriven) kMaxDriven = kp;
            }
            float Ieff = Iw[i] + h * J * R[i];
            float wn = s->w + h * (Td[i] - fx * R[i]) / Ieff;
            float pedal = hold ? 0.35f : brk;
            if (p->abs > 0.5f && brk > 0.05f && s->fz > 0 && fabsf(s->vx) > 2.5f)
            {
                if (s->kappa < -absLim) { s->absF -= h * 14.0f; absAct = 1; } else s->absF += h * 7.0f;
                if (s->absF < 0.12f) s->absF = 0.12f; if (s->absF > 1) s->absF = 1;
            }
            else s->absF = 1;
            float bAx = s->front ? (nF ? brakeF / nF : 0) : (nR ? brakeR / nR : 0);
            float tb = (pedal * s->absF + escB[i]) * bAx * R[i] + (!s->front && nR ? hand * p->handCap / nR * R[i] : 0.0f)
                     + p->rolling * s->fz * R[i];
            float db = h * tb / Ieff;
            if (fabsf(wn) <= db) wn = 0; else wn -= (wn > 0 ? db : -db);
            s->w = wn;
        }
        /* traction control: engine torque limited to what the driven tyres can put down (tyre model feed-forward) */
        if (p->tcs > 0.5f && thrE > 0.05f && te > 0 && coupled)
        {
            float unc = vengine(v, v->engW * RPM_W, thrE, 1.0f) * rEff * p->eff, want = 1.0f;
            for (int i = 0; i < nw; i++)
            {
                if (share[i] <= 0 || v->ws[i].fz <= 0 || !ok[i]) continue;
                float fx, fy; vtyre(v, v->kPeak * (1.0f + (10 - p->tcs) * 0.05f) * (rEff >= 0 ? 1.0f : -1.0f), v->ws[i].ta, v->ws[i].fz, v->ws[i].mu, &fx, &fy);
                float need = fabsf(unc * share[i]);
                if (need > 1.0f) { float q = fabsf(fx) * R[i] / need; if (q < want) want = q; }
            }
            if (kMaxDriven > tcsLim * 1.3f) want *= 0.7f;
            if (want < 0.05f) want = 0.05f; if (want > 1) want = 1;
            if (want < 0.97f) tcsAct = 1;
            float tau = 0.015f + (10 - p->tcs) * 0.012f;
            v->tcsF += (want - v->tcsF) * (h / tau > 1 ? 1 : h / tau);
        }
        else v->tcsF = 1;
    }
    for (int i = 0; i < nw; i++)
    {
        if (!ok[i]) continue;
        if (!(fabsf(v->ws[i].w) < 2000.0f)) { v->ws[i].w = -rf(wp[i], 0x170); continue; }
        *(float *)(wp[i] + 0x170) = -v->ws[i].w;
    }
    if (!(v->engW > 0 && v->engW < 2000)) v->engW = p->idle / RPM_W;
    v->tcsAct = tcsAct; v->absAct = absAct;
    if (p->record > 0.5f)
    {
        if (!srec) srec = (SRec *)VirtualAlloc(NULL, sizeof(SRec) * SREC_CAP, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (srec)
        {
            LONG k = InterlockedIncrement(&srecHead) - 1; SRec *r = &srec[k % SREC_CAP];
            srecT += dt;
            r->t = srecT; r->dt = dt; r->mass = v->bodyMass; r->speed = vAvg; r->yaw = v->yaw; r->vxB = v->vxB; r->thr = thr; r->brk = brk; r->steer = steer; r->gear = (float)gear;
            for (int i = 0; i < 4; i++)
            {
                WSim *s = &v->ws[i]; float *q = r->w[i];
                if (i >= nw) { for (int j = 0; j < 14; j++) q[j] = 0; continue; }
                q[0] = s->fz; q[1] = s->ft; q[2] = rf(wp[i], 0x160); q[3] = s->s; q[4] = s->vx; q[5] = s->vy; q[6] = s->ta; q[7] = s->kappa;
                q[8] = s->fxL; q[9] = s->fyL; q[10] = s->muF; q[11] = s->temp; q[12] = s->w; q[13] = rf(wp[i], 0x130);
            }
        }
    }
}
