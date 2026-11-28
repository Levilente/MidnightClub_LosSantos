/* sim_core.h - stage 4 of the native simulation: our own tyre forces, wheel spin, engine, clutch, differentials,
   brakes, ABS and traction control, all at GTA's physics rate. Included by MidnightClubPhysics.c.

   Inside GTA's per-wheel friction evaluation (4x per physics step, Runge-Kutta) the original function still runs
   (it builds the tyre's axes and GTA's effects state), then its force is replaced by ours:
     - slip from the contact-point velocity (body velocity + angular velocity x lever - ground velocity) of the
       Runge-Kutta stage, against the wheel's own spin
     - Pacejka Magic Formula per direction, load sensitivity, combined slip by normalised slip (friction ellipse)
     - the force goes into the body exactly the way GTA does it (F/m, and I^-1 (r x F) in the body frame), lateral
       force at GTA's roll-centre height, longitudinal force at the contact patch (so squat, dive and wheelies happen)
   After the physics step the wheel spins are integrated in sub-steps of ~1 ms (implicit in the tyre's longitudinal
   stiffness, so it stays stable at any speed): engine torque through gearbox and clutch, open / limited-slip /
   locked differentials and the AWD centre coupling, brakes with real lock-up, ABS and traction control. The result
   is written back into the wheel, so GTA's sounds, smoke, rpm and gear changes follow the real wheel speeds. */

/* ---------------- parameters from the C# side ---------------- */
typedef struct
{
    float on, thr, brake, hand, clutch, ratio, tyreR, eff, engI, wheelI;            /* 0..9  */
    float brakeF, brakeR, handF, frontShare, preF, powF, coastF, preR, powR, coastR; /* 10..19 */
    float preC, powC, idle, redline, peakNm, launch, tcs, abs, hold, maxRpm;         /* 20..29 */
    float longAt, pad1, pad2, pad3;                                                   /* 30..33 */
} Drive;
static volatile Drive drv;
#define CURVE_N 48
static volatile float curve[CURVE_N];                 /* full-load torque (Nm) 0..maxRpm at the current boost */

/* ---------------- per-wheel state ---------------- */
typedef struct
{
    float w;                 /* spin, rad/s, + = forward (GTA stores - at 0x170) */
    float vx, ta, fz, mu, r; /* last friction evaluation: longitudinal velocity, tan(slip angle), load, mu scale, radius */
    float kappa, absF;       /* slip ratio (for the UI / C#), ABS pressure factor */
    LONG seen;               /* friction evaluations since the last step */
    int init;
} WheelSim;
static WheelSim ws[4];
static float engW = 80.0f, tcsF = 1.0f; static LONG steps, ownCount, axisFallback;
static double angErr, angMag; static LONG angN; static volatile int coupleBad;
static float kPeak = 0.12f, aPeakTan = 0.14f;         /* peak slip ratio / tan(peak slip angle) of the fitted compound */
static int tcsOn, absOn; static volatile int tcsLive, absLive;

static void peaks(void)
{
    float best = 0, at = 0.1f;
    for (int i = 1; i <= 400; i++) { float k = i * 0.001f, f = mf(k, model.Bx, model.Cx, model.Ex); if (f > best) { best = f; at = k; } }
    kPeak = at; best = 0; at = 0.1f;
    for (int i = 1; i <= 400; i++) { float a = i * 0.0015f, f = mf(a, model.By, model.Cy, model.Ey); if (f > best) { best = f; at = a; } }
    aPeakTan = tanf(at);
}

/* combined-slip tyre: normalised slip (kappa / kappa_peak, tan alpha / tan alpha_peak) gives one slip magnitude,
   each direction keeps its own Magic Formula curve and peak friction. fx along the wheel's heading, fy along the
   slip direction (the caller turns it against the sliding velocity). */
static void tyre(float kappa, float ta, float fz, float muS, float *fx, float *fy)
{
    float kn = kappa / kPeak, an = ta / aPeakTan, rho = sqrtf(kn * kn + an * an);
    if (rho < 1e-5f || fz <= 0) { *fx = 0; *fy = 0; return; }
    float mx = muAt(model.muX, fz) * muS, my = muAt(model.muY, fz) * muS;
    float sx = mf(rho * kPeak, model.Bx, model.Cx, model.Ex), sy = mf(atanf(rho * aPeakTan), model.By, model.Cy, model.Ey);
    /* past the peak a real tyre keeps most of its grip (rubber sliding on asphalt ~75-85 % of peak); the fitted
       curves are only valid up to a few times the peak slip, beyond that the sliding friction is held */
    if (rho > 1.0f) { float fl = 0.78f, fy0 = 0.82f; if (sx < fl) sx = fl; if (sy < fy0) sy = fy0; }
    *fx = mx * fz * sx * kn / rho;
    *fy = my * fz * sy * an / rho;
}

static float dot3(const float *a, const float *b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
static void cross3(const float *a, const float *b, float *o) { o[0] = a[1] * b[2] - a[2] * b[1]; o[1] = a[2] * b[0] - a[0] * b[2]; o[2] = a[0] * b[1] - a[1] * b[0]; }
static float len3(const float *a) { return sqrtf(dot3(a, a)); }

/* lever arm of a force at this wheel, as GTA builds it (roll-centre height, half tyre width) */
static void lever(u64 wheel, u64 inst, float *c, float *com, float *rLat, float *rLong)
{
    uint32_t fl = *(uint32_t *)(wheel + 0x204);
    u64 hd = *(u64 *)(wheel + 0x120);
    int idx = rf(wheel, 0x140) < 0 ? 1 : 0;
    float rc = hd ? rf(hd, 0xE8 + 4 * idx) : 0.0f;
    float a = 0, b = 0;
    if (fl & 1) { if (!(fl & 0x100)) b = rc; }
    else if (!(fl & 0x820000))
    {
        a = ((fl & 2) ? 1.0f : -1.0f) * rf(wheel, 0) * rf(wheel, 0x11C) * 0.5f;
        b = rc + (*(unsigned char *)(wheel + 0x20D) ? rf(wheel, 0x14C) : 0.0f);
    }
    const float *R = (const float *)(inst + 0x40), *U = (const float *)(inst + 0x60);
    float la = drv.longAt;                            /* share of the roll-centre height kept for longitudinal force (anti-squat / anti-dive) */
    for (int j = 0; j < 3; j++)
    {
        rLat[j] = c[j] + a * R[j] + b * U[j] - com[j];
        rLong[j] = c[j] + a * R[j] + b * la * U[j] - com[j];
    }
}
/* angular acceleration of the body from a torque, the way GTA does it: I^-1 in the body frame */
static void angOf(u64 wheel, u64 inst, const float *M, const float *t, float *ang)
{
    float tb[3] = { M[0] * t[0] + M[1] * t[1] + M[2] * t[2], M[4] * t[0] + M[5] * t[1] + M[6] * t[2], M[8] * t[0] + M[9] * t[1] + M[10] * t[2] };
    if (*(uint32_t *)(wheel + 0x204) & 0x80) tb[1] = 0;
    const float *S = (*(unsigned char *)(inst + 0x265) & 0x20) ? (const float *)(inst + 0xD0) : (const float *)(inst + 0x30);
    tb[0] *= S[0]; tb[1] *= S[1]; tb[2] *= S[2];
    for (int j = 0; j < 3; j++) ang[j] = M[j] * tb[0] + M[4 + j] * tb[1] + M[8 + j] * tb[2];
}

static float vmin(float dt) { float v = 150.0f * dt; return v < 2.0f ? 2.0f : v > 6.0f ? 6.0f : v; }
static volatile float stepDt = 1.0f / 60;

/* our force for one friction evaluation; returns 1 when the outputs were replaced */
static int ownTyre(int i, u64 wheel, u64 inst, float *out, float *lin, float *ang, float load, u64 st1, u64 st2, u64 Mp, u64 linV, u64 angV, float grav)
{
    if (!(drv.on > 0.5f) || !(model.on > 0.5f) || i < 0 || i > 3 || !inst || !st1 || !st2 || !Mp || !linV || !angV || coupleBad) return 0;
    if (angN > 4000 && angErr > 0.3 * angMag) { coupleBad = 1; return 0; }   /* our body coupling doesn't match GTA's: stay on the scaled GTA force */
    float mass = rf(inst, 0x13C);
    if (load <= 0.01f * mass * grav || load < 1.0f) return 0;           /* GTA skipped this wheel (no contact) */
    WheelSim *s = &ws[i];
    float R = rf(wheel, 0x110); if (R < 0.05f) return 0;
    if (!s->init) { s->w = -rf(wheel, 0x170); s->absF = 1; s->init = 1; }
    const float *F = (const float *)(inst + 0x50), *Rt = (const float *)(inst + 0x40);
    float *a1 = (float *)st1, *a2 = (float *)st2;
    float la1 = len3(a1), la2 = len3(a2);
    float fwd[3], side[3];
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
    float den = fabsf(vx); float vm = vmin(stepDt); if (den < vm) den = vm;
    float kappa = (s->w * R - vx) / den, ta = vy / den;
    float g = rf(wheel, 0x1A8); if (!(g > 0.3f)) g = 1.0f; if (g > 1.2f) g = 1.2f;
    float muS = model.grip * g;
    float fx, fy; tyre(kappa, ta, load, muS, &fx, &fy);
    /* validation of the body coupling against GTA's own force (once per evaluation, cheap) */
    float rLat[3], rLong[3]; lever(wheel, inst, c, com, rLat, rLong);
    const float *M = (const float *)Mp;
    {
        float t[3], pa[3]; cross3(rLat, out, t); angOf(wheel, inst, M, t, pa);
        float e = (pa[0] - ang[0]) * (pa[0] - ang[0]) + (pa[1] - ang[1]) * (pa[1] - ang[1]) + (pa[2] - ang[2]) * (pa[2] - ang[2]);
        angErr += sqrtf(e); angMag += len3(ang); angN++;
    }
    float FL[3], FY[3], Fw[3];
    for (int j = 0; j < 3; j++) { FL[j] = fx * fwd[j]; FY[j] = -fy * side[j]; Fw[j] = FL[j] + FY[j]; }
    float km = rf(wheel, 0x144) * rf(inst, 0x3C);
    float t1[3], t2[3], t[3]; cross3(rLong, FL, t1); cross3(rLat, FY, t2);
    for (int j = 0; j < 3; j++) t[j] = t1[j] + t2[j];
    float na[3]; angOf(wheel, inst, M, t, na);
    if (!(fabsf(Fw[0] + Fw[1] + Fw[2] + na[0] + na[1] + na[2]) < 1e8f)) { s->init = 0; return 0; }   /* NaN / runaway guard */
    for (int j = 0; j < 3; j++) { out[j] = Fw[j]; lin[j] = Fw[j] * km; ang[j] = na[j]; }
    s->vx = vx; s->ta = ta; s->fz = load; s->mu = muS; s->r = R; s->kappa = kappa;
    InterlockedIncrement(&s->seen);
    InterlockedIncrement(&ownCount);
    return 1;
}

static float curveAt(float rpm)
{
    float mx = drv.maxRpm > 100 ? drv.maxRpm : 8000;
    float x = rpm / mx * (CURVE_N - 1); if (x < 0) x = 0;
    int i = (int)x; if (i >= CURVE_N - 1) return curve[CURVE_N - 1];
    float f = x - i; return curve[i] + (curve[i + 1] - curve[i]) * f;
}
/* engine torque at an rpm and throttle: full-load curve, closed-throttle drag, fuel cut at the redline */
static float engTorque(float rpm, float thr, float cut)
{
    float drag = -(0.05f + 0.15f * rpm / (drv.redline > 100 ? drv.redline : 7000)) * drv.peakNm;
    if (rpm >= drv.redline) return drag;
    if (rpm < drv.idle && thr < 0.05f) return 0.0f;
    return thr * curveAt(rpm) * cut + (1 - thr) * drag;     /* TCS cuts the combustion torque, not the engine braking */
}

#define RPM_W 9.5492966f   /* rad/s -> rpm */
static void driveStep(u64 *wp, float dt)
{
    if (!(drv.on > 0.5f) || !(model.on > 0.5f) || dt <= 0 || dt > 0.1f) return;
    stepDt = dt; tcsOn = drv.tcs > 0.5f; absOn = drv.abs > 0.5f;
    int n = (int)ceilf(dt / 0.001f); if (n < 1) n = 1; if (n > 64) n = 64;
    float h = dt / n;
    float ratio = drv.ratio, Rreal = drv.tyreR > 0.1f ? drv.tyreR : 0.33f;
    float share[4]; float fs = drv.frontShare; if (fs < 0) fs = 0; if (fs > 1) fs = 1;
    share[0] = share[1] = fs * 0.5f; share[2] = share[3] = (1 - fs) * 0.5f;
    float R[4], I[4]; int ok[4];
    for (int i = 0; i < 4; i++)
    {
        ok[i] = wp[i] != 0;
        R[i] = ok[i] ? rf(wp[i], 0x110) : 0.33f; if (R[i] < 0.05f) { ok[i] = 0; R[i] = 0.33f; }
        if (ok[i] && !ws[i].init) { ws[i].w = -rf(wp[i], 0x170); ws[i].absF = 1; ws[i].init = 1; }
        if (!ws[i].seen) ws[i].fz = 0;              /* not on the ground this step */
        ws[i].seen = 0;
        I[i] = drv.wheelI > 0.2f ? drv.wheelI : 1.2f;
    }
    float Rg = 0; float sw = 0; for (int i = 0; i < 4; i++) if (share[i] > 0) { Rg += R[i] * share[i]; sw += share[i]; }
    Rg = sw > 0 ? Rg / sw : 0.33f;
    float rEff = ratio * Rg / Rreal;               /* the real overall ratio on GTA's wheel radius */
    float thr = drv.thr, brk = drv.brake, hand = drv.hand;
    int coupled = fabsf(rEff) > 0.01f && drv.clutch > 0.5f;
    float vAvg = 0; int nv = 0; for (int i = 0; i < 4; i++) if (ws[i].fz > 0) { vAvg += ws[i].vx; nv++; } if (nv) vAvg /= nv;
    int hold = drv.hold > 0.5f && thr < 0.02f && fabsf(vAvg) < 0.6f;
    int tcsAct = 0, absAct = 0;
    float tcsLim = kPeak * (1.0f + (10 - drv.tcs) * 0.15f), absLim = kPeak * (1.0f + (10 - drv.abs) * 0.10f);
    for (int k = 0; k < n; k++)
    {
        /* ---- engine and clutch */
        float wIn = 0; for (int i = 0; i < 4; i++) wIn += share[i] * ws[i].w;
        float rpmW = wIn * rEff * RPM_W;               /* rpm the wheels ask for (+ in the driving direction) */
        float te = 0, Ie = 0;
        if (coupled)
        {
            float target = drv.idle + thr * (drv.launch - drv.idle);   /* the driver's foot sets the clutch rpm, TCS only trims the torque */
            if (rpmW < target && rpmW < drv.launch)
            {   /* clutch slipping (launch / crawling): engine at its own speed, full engine torque through the clutch */
                float er = engW * RPM_W; float want = target > rpmW ? target : rpmW;
                er += (want - er) * (h / 0.12f); engW = er / RPM_W;
                te = thr > 0.02f ? engTorque(er, thr, tcsF) : (rpmW < drv.idle * 0.5f ? 0.04f * drv.peakNm : 0.0f);
                if (te < 0) te = 0;
            }
            else
            {
                engW = rpmW / RPM_W; if (engW < drv.idle / RPM_W) engW = drv.idle / RPM_W;
                te = engTorque(rpmW, thr, tcsF);
                Ie = drv.engI * rEff * rEff;
            }
        }
        else
        {   /* neutral / clutch open: the engine revs on its own */
            float er = engW * RPM_W, t = engTorque(er, thr, 1.0f);
            engW += h * t / (drv.engI > 0.02f ? drv.engI : 0.15f);
            if (engW * RPM_W < drv.idle) engW = drv.idle / RPM_W;
            if (engW * RPM_W > drv.redline * 1.02f) engW = drv.redline * 1.02f / RPM_W;
        }
        float tIn = te * rEff * (te > 0 ? drv.eff : 1.0f);   /* torque at the differential output (both axles together) */
        float Td[4], Ie_i[4];
        for (int i = 0; i < 4; i++) { Td[i] = tIn * share[i]; Ie_i[i] = I[i] + Ie * share[i]; }
        /* ---- differentials: clutch-type LSD per axle (preload + ramp x input torque), AWD centre coupling */
        for (int ax = 0; ax < 2; ax++)
        {
            int a = ax * 2, b = a + 1; float tAx = Td[a] + Td[b];
            if (share[a] <= 0) continue;
            float pre = ax ? drv.preR : drv.preF, ramp = te >= 0 ? (ax ? drv.powR : drv.powF) : (ax ? drv.coastR : drv.coastF);
            float lim = pre + ramp * fabsf(tAx);
            float d = ws[a].w - ws[b].w, ih = Ie_i[a] * Ie_i[b] / (Ie_i[a] + Ie_i[b]);
            float t = d * ih / h; if (t > lim) t = lim; if (t < -lim) t = -lim;
            Td[a] -= t; Td[b] += t;
        }
        if (share[0] > 0 && share[2] > 0)
        {
            float tf = Td[0] + Td[1], tr = Td[2] + Td[3];
            float lim = drv.preC + drv.powC * fabsf(tf + tr);
            float d = 0.5f * (ws[0].w + ws[1].w) - 0.5f * (ws[2].w + ws[3].w);
            float iF = Ie_i[0] + Ie_i[1], iR = Ie_i[2] + Ie_i[3], ih = iF * iR / (iF + iR);
            float t = d * ih / h * 0.5f; if (t > lim) t = lim; if (t < -lim) t = -lim;
            Td[0] -= t * 0.5f; Td[1] -= t * 0.5f; Td[2] += t * 0.5f; Td[3] += t * 0.5f;
        }
        /* ---- each wheel: drive - tyre - brake, implicit in the tyre's longitudinal stiffness */
        float kMaxDriven = -10;
        for (int i = 0; i < 4; i++)
        {
            if (!ok[i]) continue;
            WheelSim *s = &ws[i];
            float fx = 0, fy, J = 0, den = 1;
            if (s->fz > 0)
            {
                den = fabsf(s->vx); float vm = vmin(dt); if (den < vm) den = vm;
                float kp = (s->w * R[i] - s->vx) / den;
                tyre(kp, s->ta, s->fz, s->mu, &fx, &fy);
                float e = 0.002f, fx2, fy2; tyre(kp + e, s->ta, s->fz, s->mu, &fx2, &fy2);
                J = (fx2 - fx) / e * R[i] / den;           /* dFx / d(spin) */
                if (J < 0) J = 0;
                s->kappa = kp;
                if (share[i] > 0 && kp > kMaxDriven) kMaxDriven = kp;
            }
            float Ieff = Ie_i[i] + h * J * R[i];
            float wn = s->w + h * (Td[i] - fx * R[i]) / Ieff;
            /* brakes (with ABS) and handbrake: friction torque, the wheel locks when it can stop it */
            float bf = i < 2 ? drv.brakeF : drv.brakeR;
            float pedal = hold ? 0.35f : brk;
            if (absOn && brk > 0.05f && s->fz > 0 && fabsf(s->vx) > 2.5f)
            {
                if (s->kappa < -absLim) { s->absF -= h * 14.0f; absAct = 1; } else s->absF += h * 7.0f;
                if (s->absF < 0.12f) s->absF = 0.12f; if (s->absF > 1) s->absF = 1;
            }
            else s->absF = 1;
            float tb = pedal * bf * 0.5f * R[i] * s->absF + (i >= 2 ? hand * drv.handF * 0.5f * R[i] : 0.0f);
            float db = h * tb / Ieff;
            if (fabsf(wn) <= db) wn = 0; else wn -= (wn > 0 ? db : -db);
            s->w = wn;
        }
        /* ---- traction control: limits the engine torque to what the driven tyres can put down at the target slip
           (feed-forward from the tyre model, like a modern torque-based TCS), plus feedback if a wheel still flares */
        if (tcsOn && thr > 0.05f && te > 0 && coupled)
        {
            float unc = engTorque(engW * RPM_W, thr, 1.0f) * rEff * drv.eff, want = 1.0f;
            for (int i = 0; i < 4; i++)
            {
                if (share[i] <= 0 || ws[i].fz <= 0 || !ok[i]) continue;
                float fx, fy; tyre(kPeak * (1.0f + (10 - drv.tcs) * 0.05f) * (rEff >= 0 ? 1.0f : -1.0f), ws[i].ta, ws[i].fz, ws[i].mu, &fx, &fy);
                float need = fabsf(unc * share[i]);
                if (need > 1.0f) { float k2 = fabsf(fx) * R[i] / need; if (k2 < want) want = k2; }
            }
            if (kMaxDriven > tcsLim * 1.3f) want *= 0.7f;
            if (want < 0.05f) want = 0.05f; if (want > 1) want = 1;
            if (want < 0.97f) tcsAct = 1;
            float tau = 0.015f + (10 - drv.tcs) * 0.012f;
            tcsF += (want - tcsF) * (h / tau > 1 ? 1 : h / tau);
        }
        else tcsF = 1;
    }
    for (int i = 0; i < 4; i++)
    {
        if (!ok[i]) continue;
        if (!(fabsf(ws[i].w) < 2000.0f)) { ws[i].w = -rf(wp[i], 0x170); continue; }
        *(float *)(wp[i] + 0x170) = -ws[i].w;
    }
    if (!(engW > 0 && engW < 2000)) engW = drv.idle / RPM_W;
    tcsLive = tcsAct; absLive = absAct;
    InterlockedIncrement(&steps);
}
