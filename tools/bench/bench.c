/* Full-vehicle test bench for the native suspension/tyre model.
   "GTA layer": rigid body (heave, roll, pitch, yaw, planar), probe compression x per wheel, rigid stop at the wheel's real travel.
   "Our layer": sim_core.h susForce / wheelHop / vtyre exactly as in the game. */
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <math.h>
#include <stdlib.h>
typedef uint64_t u64; typedef int LONG;
#define InterlockedIncrement(p) (++*(p))
#define VirtualAlloc(a,b,c,d) calloc(1,b)
#define MEM_COMMIT 0
#define MEM_RESERVE 0
#define PAGE_READWRITE 0
static float rf(u64 p, int o) { return p ? *(float *)(p + o) : 0.0f; }
static float mf(float x, float b, float c, float e) { float bx = b * x; return sinf(c * atanf(bx - e * (bx - atanf(bx)))); }
#include "../../asi/sim_core.h"

typedef struct { const char *name; float m, wb, tr, hcg, frontPct, fF, fR, zbl, zbh, zrl, zrh, knee, arbF, arbR, rcF, rcR, travel, rigid, x0, yawDI, rollK, R, unspr, kt, maxSteer; int rwd; float antiF, antiR; } Car;
static unsigned char wm[4][0x230];
static Veh *v;
static Car C;
/* state */
static double u, vy, r, z, vz, ph, p, th, q, w[4], taL[4];
static double sumRoll, maxRoll; static int rigidHits; static float xMax[4];
static float steerCmd, brakeCmd, thrCmd, hbCmd, zg[4];
static float lastAy, lastAx, lastFz[4];

static void setup(void)
{
    /* what the game keeps for the car's whole life: the learned travel and its histogram */
    float keepL0 = vehs[0].tLearn[0], keepL1 = vehs[0].tLearn[1]; int kn0 = vehs[0].clampN[0], kn1 = vehs[0].clampN[1];
    v = &vehs[0]; memset(v, 0, sizeof *v); v->tLearn[0] = keepL0; v->tLearn[1] = keepL1; v->clampN[0] = kn0; v->clampN[1] = kn1; v->nw = 4; v->susReady = 1; v->used = 2;
    VParams *P = &v->p;
    P->susOn = 1; P->mass = C.m; P->fF = C.fF; P->fR = C.fR; P->zbl = C.zbl; P->zbh = C.zbh; P->zrl = C.zrl; P->zrh = C.zrh; P->knee = C.knee;
    P->arbF = C.arbF; P->arbR = C.arbR; P->maxFk = 3.5f; P->kt = C.kt; P->unsprung = C.unspr; P->travel = C.travel; P->rcF = C.rcF; P->rcR = C.rcR; P->antiF = C.antiF; P->antiR = C.antiR;
    /* touring tyre */
    P->muY = 0.92f; P->muX = 0.96f; P->loadSens = -0.12f; P->By = 7.211f; P->Cy = 1.5f; P->Ey = -1.32f; P->Bx = 13.383f; P->Cx = 1.65f; P->Ex = 0.34f; P->wet = 1;
    vpeaks(v);
    v->x0[0] = v->x0[1] = C.x0;
    float a = C.wb * (1 - C.frontPct), b = C.wb * C.frontPct;   /* CoM to front / rear axle */
    for (int i = 0; i < 4; i++)
    {
        memset(wm[i], 0, sizeof wm[i]);
        int front = i < 2; float share = (front ? C.frontPct : 1 - C.frontPct) * 0.5f;
        *(float *)(wm[i] + 0x13C) = share; *(float *)(wm[i] + 0x140) = front ? 1.0f : -1.0f; *(float *)(wm[i] + 0x130) = 0.67f; *(float *)(wm[i] + 0x160) = C.x0;
        v->wheel[i] = (u64)wm[i]; v->ws[i].front = front; v->ws[i].pair = i ^ 1; v->ws[i].lx = (i & 1 ? 1 : -1) * C.tr * 0.5f; v->ws[i].ly = front ? a : -b; v->ws[i].init = 1;
        v->bodyMass = C.m;
    }
    u = vy = r = z = vz = ph = p = th = q = 0; for (int i = 0; i < 4; i++) { w[i] = 0; taL[i] = 0; zg[i] = 0; xMax[i] = 0; }
    rigidHits = 0; steerCmd = brakeCmd = thrCmd = hbCmd = 0;
    for (int i = 0; i < 4; i++) wheelHop(v, i, v->wheel[i], 0.01f);
}
/* one step of dt: returns lateral g */
static void step(float dt)
{
    float a = C.wb * (1 - C.frontPct), b = C.wb * C.frontPct, t2 = C.tr * 0.5f;
    float X[4] = { a, a, -b, -b }, Y[4] = { t2, -t2, t2, -t2 };       /* x forward, y left; wheel 0 = front left */
    float Iz = C.m * C.yawDI * (0.5f * C.wb) * (0.5f * C.wb), Ix = C.m * (C.rollK * C.tr) * (C.rollK * C.tr), Iy = 0.93f * Iz;
    float Fz_[4], Fx[4], Fy[4], Fs[4];
    for (int i = 0; i < 4; i++)
    {
        /* GTA probe: compression from body height at the corner and the ground */
        float zi = (float)(z + Y[i] * ph - X[i] * th);
        float x = C.x0 + (zg[i] + 0.0001f * ((rand() & 1023) / 1023.0f - 0.5f) - zi), rig = 0;   /* real roads: a tenth of a millimetre of texture */
        float vzi = (float)(vz + Y[i] * p - X[i] * q);
        if (x > C.rigid) { rig = 4e6f * (x - C.rigid) - 3e4f * vzi; if (rig < 0) rig = 0; rigidHits++; x = C.rigid; }
        if (x > xMax[i]) xMax[i] = x;
        WSim *s = &v->ws[i];
        int contact = x > 0;
        if (contact) watchStop(v, s, i < 2 ? 0 : 1, x);
        if (s->calls == 0) s->x1 = *(float *)(wm[i] + 0x160);
        *(float *)(wm[i] + 0x160) = contact ? x : 0; s->gc = contact; s->calls = 1;
        float so = v->ws[i ^ 1].s;
        float sS = s->s + (x - s->x1); if (sS < 0) sS = 0;
        Fs[i] = contact ? susForce(v, v->wheel[i], i, sS, s->sv, so, 1) : 0;
        Fs[i] += rig;
        wheelHop(v, i, v->wheel[i], dt);
        float load = s->ftF + rig; Fz_[i] = load;
        /* tyre */
        float vxw0 = (float)(u - r * Y[i]), vyw0 = (float)(vy + r * X[i]);
        float d = i < 2 ? steerCmd : 0, cd = cosf(d), sd = sinf(d);
        float vxw = vxw0 * cd + vyw0 * sd, vyw = -vxw0 * sd + vyw0 * cd;
        float den = fabsf(vxw) > 1 ? fabsf(vxw) : 1;
        float taRaw = vyw / den, bl = (fabsf(vxw) + 3.0f) * dt / 0.35f; if (bl > 1) bl = 1;
        taL[i] += (taRaw - taL[i]) * bl;
        float kappa = (float)((w[i] * C.R - vxw) / den);
        float fx, fy; vtyre(v, kappa, (float)taL[i], load > 0 ? load : 0, 1.0f, &fx, &fy);
        float fyw = -fy;
        /* wheel spin */
        float Tq = 0;
        if (C.rwd && i >= 2) Tq += thrCmd * 1800.0f * 0.5f;
        float bt = brakeCmd * (i < 2 ? 3200.0f : 1700.0f) + (i >= 2 ? hbCmd * 4000.0f : 0);
        float Iw = 1.2f;
        double wn = w[i] + dt * (Tq - fx * C.R) / Iw;
        double sgn = wn > 0 ? 1 : wn < 0 ? -1 : 0, br = bt * dt / Iw;
        if (fabs(wn) <= br) wn = 0; else wn -= sgn * br;
        w[i] = wn;
        Fx[i] = fx * cd - fyw * sd; Fy[i] = fx * sd + fyw * cd;
    }
    float SFx = 0, SFy = 0, Mz = 0, Mx = 0, My = 0, SFs = 0;
    for (int i = 0; i < 4; i++)
    {
        float hrc = i < 2 ? C.rcF : C.rcR;
        SFx += Fx[i]; SFy += Fy[i]; SFs += Fs[i];
        Mz += X[i] * Fy[i] - Y[i] * Fx[i];
        Mx += Y[i] * Fs[i] + (C.hcg - hrc) * Fy[i];
        float A = i < 2 ? v->p.antiF : v->p.antiR, hApp = A > 0.01f ? A * C.hcg : 0.25f * hrc;
        My += (hApp - C.hcg) * Fx[i] - X[i] * Fs[i];
    }
    float drag = 0.5f * 1.2f * 0.8f * (float)(u * fabs(u));
    double du = (SFx - drag) / C.m + vy * r, dv = SFy / C.m - u * r;
    v->vxB = (float)u; lastAy = SFy / C.m; lastAx = (SFx - drag) / C.m;
    u += dt * du; vy += dt * dv; r += dt * Mz / Iz;
    vz += dt * (SFs / C.m - 9.81); z += dt * vz;
    p += dt * Mx / Ix; ph += dt * p;
    q += dt * My / Iy; th += dt * q;
    for (int i = 0; i < 4; i++) lastFz[i] = Fz_[i];
}
static void freeRoll(void) { for (int i = 0; i < 4; i++) w[i] = u / C.R; }
#define DT (1.0f / 120)
static void settle(float spd) { u = spd; freeRoll(); for (int k = 0; k < 360; k++) { step(DT); if (spd > 0) freeRoll(); } }
static float deg(double a) { return (float)(a * 57.29578); }

static void run(int verbose)
{
    setup();
    printf("\n=== %s  (%.0f kg, ride %.2f/%.2f Hz, rigid stop %.3f m) ===\n", C.name, C.m, C.fF, C.fR, C.rigid);
    /* A: static */
    settle(0);
    float xs[4]; for (int i = 0; i < 4; i++) xs[i] = *(float *)(wm[i] + 0x160);
    printf("static x F %.3f R %.3f (%.0f%% / %.0f%% of rigid), ride vs GTA %+.1f/%+.1f cm, room to stop %.0f/%.0f mm\n", xs[0], xs[2], 100 * xs[0] / C.rigid, 100 * xs[2] / C.rigid,
        100 * (C.x0 - xs[0]), 100 * (C.x0 - xs[2]), 1000 * (C.rigid - xs[0]), 1000 * (C.rigid - xs[2]));
    /* B: steady cornering ramp at 20 m/s */
    setup(); settle(20); rigidHits = 0;
    float rg = 0; int nrg = 0; float ayMax = 0;
    for (int k = 0; k < 120 * 8; k++)
    {
        float t = k * DT; steerCmd = 0.14f * t / 8.0f * C.maxSteer / 0.6f;
        if (u < 20) thrCmd = 0.3f; else thrCmd = 0.1f;
        step(DT);
        if (fabsf(lastAy) > ayMax) ayMax = fabsf(lastAy);
        if (fabsf(lastAy) > 3 && fabsf(lastAy) < 7) { rg += deg(ph) / (lastAy / 9.81f); nrg++; }
    }
    printf("cornering: roll gradient %.2f deg/g, max lateral %.2f g, rigid contacts %d\n", nrg ? rg / nrg : 0, ayMax / 9.81f, rigidHits);
    /* C: braking from 25 m/s */
    setup(); settle(25); rigidHits = 0; thrCmd = 0;
    float pg = 0; int npg = 0; float pmax = 0, axMax = 0;
    for (int k = 0; k < 120 * 3 && u > 2; k++)
    {
        brakeCmd = k < 12 ? k / 12.0f : 1.0f; step(DT);
        if (deg(th) > pmax) pmax = deg(th);
        if (-lastAx > axMax) axMax = -lastAx;
        if (k > 120 && -lastAx > 5) { pg += deg(th) / (-lastAx / 9.81f); npg++; }
    }
    brakeCmd = 0;
    printf("braking: pitch gradient %.2f deg/g, peak pitch %.2f deg at %.2f g, front load %.0f N/corner, rigid contacts %d\n", npg ? pg / npg : 0, pmax, axMax / 9.81f, lastFz[0], rigidHits);
    /* D: step steer at 20 m/s */
    setup(); settle(20); rigidHits = 0;
    float rollPk = 0, rollEnd = 0, yrPk = 0, yrEnd = 0, t90 = -1;
    for (int k = 0; k < 120 * 4; k++)
    {
        steerCmd = k < 6 ? 0.035f * k / 6.0f * C.maxSteer / 0.6f : 0.035f * C.maxSteer / 0.6f; thrCmd = 0.12f;
        step(DT);
        float rd = fabsf(deg(ph)); if (rd > rollPk) rollPk = rd; if (fabsf((float)r) > yrPk) yrPk = fabsf((float)r);
        if (k == 120 * 4 - 1) { rollEnd = rd; yrEnd = fabsf((float)r); }
    }
    setup(); settle(20);
    for (int k = 0; k < 120 * 4; k++) { steerCmd = k < 6 ? 0.035f * k / 6.0f * C.maxSteer / 0.6f : 0.035f * C.maxSteer / 0.6f; thrCmd = 0.12f; step(DT); if (t90 < 0 && fabsf(deg(ph)) > 0.9f * rollEnd) t90 = k * DT; }
    printf("step steer: roll %.2f deg, overshoot %.0f%%, 90%% after %.2f s; yaw rate overshoot %.0f%%; rigid contacts %d\n", rollEnd, 100 * (rollPk / rollEnd - 1), t90, 100 * (yrPk / yrEnd - 1), rigidHits);
    /* E: left-side kerb 8 cm at 15 m/s */
    setup(); settle(15); rigidHits = 0;
    float rpk = 0, settleT = 0;
    for (int k = 0; k < 120 * 3; k++)
    {
        float t = k * DT, x0 = 1.0f, len = 1.5f;
        float sF = (t * 15 - x0), sR = (t * 15 - x0 - C.wb);
        zg[0] = sF > 0 && sF < len ? 0.08f : 0; zg[2] = sR > 0 && sR < len ? 0.08f : 0; zg[1] = zg[3] = 0;
        step(DT); freeRoll();
        float rd = fabsf(deg(ph)); if (rd > rpk) rpk = rd; if (rd > 0.15f) settleT = t;
    }
    printf("kerb (8 cm, one side, 15 m/s): peak roll %.2f deg, settled (<0.15 deg) after %.2f s, rigid contacts %d, max x %.3f/%.3f\n", rpk, settleT - (1.0f + 1.5f + C.wb) / 15, rigidHits, xMax[0], xMax[2]);
    /* F: sharp crest both sides 6 cm */
    setup(); settle(20); rigidHits = 0; float hpk = 0;
    for (int k = 0; k < 120 * 2; k++)
    {
        float t = k * DT; float sF = t * 20 - 2, sR = sF - C.wb;
        float bF = sF > 0 && sF < 0.6f ? 0.06f * sinf(sF / 0.6f * 3.14159f) : 0, bR = sR > 0 && sR < 0.6f ? 0.06f * sinf(sR / 0.6f * 3.14159f) : 0;
        zg[0] = zg[1] = bF; zg[2] = zg[3] = bR; step(DT); freeRoll();
        if (fabsf((float)z) > hpk) hpk = fabsf((float)z);
    }
    printf("pothole crest (6 cm, 20 m/s): body heave peak %.1f cm, rigid contacts %d\n", hpk * 100, rigidHits);
    /* G: scandi flick vs plain turn-in, 22 m/s, lift off */
    for (int flick = 0; flick < 2; flick++)
    {
        setup(); settle(22); rigidHits = 0; float bPk = 0, rPk = 0;
        for (int k = 0; k < 120 * 3; k++)
        {
            float t = k * DT, S = C.maxSteer;
            if (flick) steerCmd = t < 0.45f ? -0.35f * S * (t < 0.15f ? t / 0.15f : 1) : t < 0.6f ? -0.35f * S + (t - 0.45f) / 0.15f * 0.75f * S : 0.4f * S;
            else steerCmd = t < 0.45f ? 0 : t < 0.6f ? (t - 0.45f) / 0.15f * 0.4f * S : 0.4f * S;
            thrCmd = 0; brakeCmd = (t > 0.35f && t < 0.6f) ? 0.35f : 0;
            {   /* the driver catches the slide: opposite lock against the body slip once the car has rotated */
                float beta = (float)atan2(vy, fabs(u) > 1 ? u : 1);
                if (t > 0.6f && fabsf(beta) > 0.12f) { float c = 1.1f * beta; if (c > S) c = S; if (c < -S) c = -S; steerCmd = c; thrCmd = 0.25f; }
            }
            step(DT);
            float beta = deg(atan2(vy, fabs(u) > 1 ? u : 1)); if (fabsf(beta) > bPk && t > 0.45f) bPk = fabsf(beta); if (fabsf((float)r) > rPk && t > 0.45f) rPk = fabsf((float)r);
        }
        if (flick) printf("learned travel %.3f/%.3f\n", v->tLearn[0], v->tLearn[1]);
        printf("%s: peak body slip %.1f deg, peak yaw rate %.0f deg/s, slip at the end %.1f deg (%s), speed after %.1f m/s, rigid contacts %d\n", flick ? "scandi flick + catch" : "plain turn-in + catch", bPk, deg(rPk),
            deg(atan2(vy, fabs(u) > 1 ? u : 1)), fabs(atan2(vy, fabs(u) > 1 ? u : 1)) < 0.2 ? "caught" : "spun", u, rigidHits);
    }
}
int main(int argc, char **argv)
{
    /* name m wb tr hcg front fF fR zbl zbh zrl zrh knee arbF arbR rcF rcR travel rigid x0 yawDI rollK R unspr kt steer rwd */
    float rig = argc > 1 ? atof(argv[1]) : 0.255f, trav = argc > 2 ? atof(argv[2]) : 0.305f;
    Car cars[] = {
        { "Jubilee (SUV)", 2660, 2.92f, 1.65f, 0.68f, 0.52f, 1.15f, 1.15f, 0.55f, 0.44f, 0.85f, 0.65f, 0.15f, 0.6f * 1.5f * 0.57f, 0.6f * 1.5f * 0.43f, 0.15f, 0.28f, 0, 0, 0.20f, 1.12f, 0.42f, 0.38f, 60, 330000, 0.6f, 1, 0.2f, 0.15f },
        { "Elegy RH8 (Sports)", 1600, 2.65f, 1.55f, 0.50f, 0.55f, 1.6f, 1.6f, 0.55f, 0.44f, 0.85f, 0.65f, 0.15f, 0.75f * 1.5f * 0.57f, 0.75f * 1.5f * 0.43f, 0.07f, 0.11f, 0, 0, 0.17f, 1.0f, 0.36f, 0.33f, 40, 220000, 0.6f, 1, 0.3f, 0.2f },
        { "Futo (Coupe, 1985)", 950, 2.40f, 1.36f, 0.48f, 0.53f, 1.35f * 0.93f, 1.35f * 0.93f, 0.55f, 0.44f, 0.85f, 0.65f, 0.15f, 0.55f * 1.5f * 0.57f, 0.55f * 1.5f * 0.43f, 0.06f, 0.12f, 0, 0, 0.17f, 1.0f, 0.38f, 0.30f, 35, 220000, 0.6f, 1, 0.1f, 0.1f },
    };
    for (int i = 0; i < 3; i++) { memset(&vehs[0], 0, sizeof vehs[0]); C = cars[i]; C.rigid = rig; C.travel = trav; run(0); }
}
