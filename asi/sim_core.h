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
    LONG steps, own;
} Veh;

static Veh vehs[MAXV];
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
static void lever(u64 wheel, u64 inst, const float *c, const float *com, float la, float *rLat, float *rLong)
{
    uint32_t fl = *(uint32_t *)(wheel + 0x204);
    u64 hd = *(u64 *)(wheel + 0x120);
    int idx = rf(wheel, 0x140) < 0 ? 1 : 0;
    float rc = hd ? rf(hd, 0xE8 + 4 * idx) : 0.0f, a = 0, b = 0;
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
        float *f = fabsf(dot3(a1, F)) >= fabsf(dot3(a2, F)) ? a1 : a2, *g = f == a1 ? a2 : a1;
        float sf = dot3(f, F) >= 0 ? 1.0f : -1.0f, sg = dot3(g, Rt) >= 0 ? 1.0f : -1.0f;
        for (int j = 0; j < 3; j++) { fwd[j] = f[j] * sf; side[j] = g[j] * sg; }
    }
    else { InterlockedIncrement(&axisFallback); return 0; }
    float c[3] = { rf(wheel, 0x50), rf(wheel, 0x54), rf(wheel, 0x58) }, com[3] = { rf(inst, 0x70), rf(inst, 0x74), rf(inst, 0x78) };
    float r0[3] = { c[0] - com[0], c[1] - com[1], c[2] - com[2] }, wr[3];
    const float *lv = (const float *)linV, *av = (const float *)angV;
    cross3(av, r0, wr);
    float rel[3] = { lv[0] + wr[0] - rf(wheel, 0x90), lv[1] + wr[1] - rf(wheel, 0x94), lv[2] + wr[2] - rf(wheel, 0x98) };
    float vx = dot3(rel, fwd), vy = dot3(rel, side);
    float den = fabsf(vx), vm = vmin(v->dt); if (den < vm) den = vm;
    float kappa = (s->w * R - vx) / den, ta = vy / den;
    float g = rf(wheel, 0x1A8); if (!(g > 0.3f)) g = 1.0f; if (g > 1.2f) g = 1.2f;
    float muS = v->p.wet * g;
    float fx, fy; vtyre(v, kappa, ta, load, muS, &fx, &fy);
    float rLat[3], rLong[3]; lever(wheel, inst, c, com, v->p.longAt, rLat, rLong);
    const float *M = (const float *)Mp;
    float gtaLat = dot3(out, side);
    {   /* check our body coupling against GTA's own force */
        float t[3], pa[3]; cross3(rLat, out, t); angOf(wheel, inst, M, t, pa);
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
    float sp = len3(lv), aero[3] = { 0, 0, 0 };
    if (sp > 1.0f && (v->p.cda > 0 || v->p.cla != 0))
    {
        float q = 0.5f * 1.2f * sp, ng = (float)(v->nGround > 0 ? v->nGround : v->nw);
        for (int j = 0; j < 3; j++) aero[j] = (-q * v->p.cda * lv[j] - q * sp * v->p.cla * U[j]) / ng;
    }
    float im = rf(inst, 0x3C);
    for (int j = 0; j < 3; j++) { out[j] = Fw[j]; lin[j] = Fw[j] * km + aero[j] * im; ang[j] = na[j]; }
    s->vx = vx; s->ta = ta; s->fz = load; s->mu = muS; s->kappa = kappa;
    s->lx = dot3(r0, Rt); s->ly = dot3(r0, F);
    if (i == 0) { v->yaw = dot3(av, U); v->vxB = dot3(lv, F); }
    InterlockedIncrement(&s->seen);
    InterlockedIncrement(&v->own);
    return 1;
}

/* ---------------- suspension: spring from the ride frequency on the wheel's static load, preload that keeps
   GTA's own ride height (measured over the first seconds), digressive dampers, bump stop, anti-roll bar ---------- */
static int vehSusp(Veh *v, int i, u64 wheel, u64 other, float *out, float *lin, float *ang, float *loadOut)
{
    if (!(v->p.susOn > 0.5f) || v->p.bike > 0.5f || i < 0) return 0;
    WSim *s = &v->ws[i];
    float x = rf(wheel, 0x160), prev = rf(wheel, 0x164);
    int ax = rf(wheel, 0x140) < 0 ? 1 : 0;
    if (!v->susReady)
    {
        if (x > 0.005f && x < 0.6f && fabsf(out[2]) > 20.0f) { s->x0sum += x; s->x0n++; }
        return 0;
    }
    float share = rf(wheel, 0x13C); if (share < 0.02f || share > 0.8f) share = 1.0f / v->nw;
    float mc = v->p.mass * share, f = ax ? v->p.fR : v->p.fF, w = 6.2831853f * f;
    float k = mc * w * w, cc = 2.0f * sqrtf(k * mc), x0 = v->x0[ax] > 0.01f ? v->x0[ax] : 0.1f;
    float pre = mc * 9.81f - k * x0;
    float dt = v->dt > 0.001f ? v->dt : 1.0f / 60;
    float vel = (x - prev) / dt;
    if (vel > 2.5f || vel < -2.5f) return 0;                      /* an impact, not suspension travel */
    float fs = pre + k * x, fd, kn = v->p.knee > 0.01f ? v->p.knee : 0.1f;
    if (vel >= 0) fd = vel < kn ? cc * v->p.zbl * vel : cc * (v->p.zbl * kn + v->p.zbh * (vel - kn));
    else { float u = -vel; fd = -(u < kn ? cc * v->p.zrl * u : cc * (v->p.zrl * kn + v->p.zrh * (u - kn))); }
    float bumpAt = x0 * (v->p.bumpFrac > 0.5f ? v->p.bumpFrac : 1.7f); if (bumpAt > 0.22f) bumpAt = 0.22f;
    float fb = x > bumpAt ? k * (v->p.bumpK > 0 ? v->p.bumpK : 4.0f) * (x - bumpAt) * (x - bumpAt) * 50.0f : 0.0f;
    float fa = other ? k * (ax ? v->p.arbR : v->p.arbF) * (x - rf(other, 0x160)) : 0.0f;
    float fo = fs + fd + fb + fa, mx = mc * 9.81f * (v->p.maxFk > 0 ? v->p.maxFk : 3.5f);
    if (fo < 0) fo = 0; if (fo > mx) fo = mx;
    float gz = fabsf(out[2]);
    if (gz < 20.0f) return 0;
    float sc = fo / gz;
    for (int j = 0; j < 3; j++) { out[j] *= sc; lin[j] *= sc; ang[j] *= sc; }
    if (loadOut) *loadOut *= sc;
    return 1;
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
}
