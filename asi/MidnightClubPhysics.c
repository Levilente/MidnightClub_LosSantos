/* MidnightClubPhysics.asi - native side of Midnight Club: Los Santos' simulation (GTA V Enhanced 2.0.1158).
   Stage 4 (sim_core.h): own tyre forces, wheel spin, engine, clutch, differentials, brakes, ABS, TCS at GTA's physics rate.

   Stage 2 (read-only): hooks GTA's per-wheel tyre-friction and suspension functions (called from the wheel
   physics integrator, 4 times per physics step - Runge-Kutta) and records what they compute for the player's car.
   Nothing is changed: each hook calls the original and copies its outputs into a ring buffer, which the C# mod
   dumps to a file. Hooks are only installed on request (MCP_Install) and only if the code at the expected place
   matches byte for byte; otherwise nothing is touched.

   Functions (RVA, see docs/RE_NOTES.md): 0xF1E8B0 tyre friction, 0xF1DB20 suspension. */
#include <windows.h>
#include <string.h>
#include <stdio.h>
#include <stdint.h>

typedef uint64_t u64;
typedef void (*Fn16)(u64,u64,u64,u64,u64,u64,u64,u64,u64,u64,u64,u64,u64,u64,u64,u64);
typedef void (*Fn12)(u64,u64,u64,u64,u64,u64,u64,u64,u64,u64,u64,u64);
typedef void (*Fn11)(u64,u64,u64,u64,u64,u64,u64,u64,u64,u64,u64);

static const unsigned char SIG_FRICTION[128] = { 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54, 0x56, 0x57, 0x55, 0x53, 0x48, 0x81, 0xec, 0x28, 0x02, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xbc, 0x24, 0x10, 0x02, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xb4, 0x24, 0x00, 0x02, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xac, 0x24, 0xf0, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xa4, 0x24, 0xe0, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x9c, 0x24, 0xd0, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x94, 0x24, 0xc0, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x8c, 0x24, 0xb0, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x84, 0x24, 0xa0, 0x01, 0x00, 0x00, 0x0f, 0x29, 0xbc, 0x24, 0x90, 0x01, 0x00, 0x00, 0x0f, 0x29, 0xb4, 0x24, 0x80, 0x01, 0x00, 0x00, 0xf3, 0x0f, 0x10, 0xbc, 0x24, 0xb0, 0x02, 0x00, 0x00, 0x4c, 0x8b, 0xa4, 0x24, 0x90, 0x02, 0x00, 0x00, 0x41, 0xc7, 0x40, 0x08 };
static const unsigned char SIG_SUSP[128] = { 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54, 0x56, 0x57, 0x55, 0x53, 0x48, 0x81, 0xec, 0x68, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xbc, 0x24, 0x50, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xb4, 0x24, 0x40, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xac, 0x24, 0x30, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xa4, 0x24, 0x20, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x9c, 0x24, 0x10, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x94, 0x24, 0x00, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x8c, 0x24, 0xf0, 0x00, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x84, 0x24, 0xe0, 0x00, 0x00, 0x00, 0x0f, 0x29, 0xbc, 0x24, 0xd0, 0x00, 0x00, 0x00, 0x0f, 0x29, 0xb4, 0x24, 0xc0, 0x00, 0x00, 0x00, 0x4d, 0x89, 0xce, 0x4d, 0x89, 0xc7, 0x49, 0x89, 0xd4, 0x48, 0x89, 0xcf, 0x48, 0x8b, 0x84, 0x24, 0xf8, 0x01, 0x00, 0x00, 0xf3 };

static const unsigned char SIG_STEP[128] = { 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54, 0x56, 0x57, 0x55, 0x53, 0x48, 0x81, 0xec, 0xe8, 0x05, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xbc, 0x24, 0xd0, 0x05, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xb4, 0x24, 0xc0, 0x05, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xac, 0x24, 0xb0, 0x05, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xa4, 0x24, 0xa0, 0x05, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x9c, 0x24, 0x90, 0x05, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x94, 0x24, 0x80, 0x05, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x8c, 0x24, 0x70, 0x05, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x84, 0x24, 0x60, 0x05, 0x00, 0x00, 0x0f, 0x29, 0xbc, 0x24, 0x50, 0x05, 0x00, 0x00, 0x0f, 0x29, 0xb4, 0x24, 0x40, 0x05, 0x00, 0x00, 0x4c, 0x89, 0xce, 0x49, 0x89, 0xcc, 0xf3, 0x44, 0x0f, 0x10, 0x8c, 0x24, 0x80, 0x06, 0x00, 0x00, 0xf3, 0x44, 0x0f, 0x10, 0xb4 };
static Fn16 origFriction; static Fn12 origSusp; static Fn11 origStep;
static volatile LONG installed; static int stepHooked; static char status[256] = "not installed";
static volatile u64 watch[4];

typedef struct { uint32_t tick; uint16_t kind, wheel; float dt, load; float out[4], lin[3], ang[3], spin, wspin, comp, x; } Rec;   /* 64 bytes */
#define CAP 262144
static Rec *ring; static volatile LONG head; static volatile LONG recording;
static volatile LONG calls[2];

static float rf(u64 p, int o); static float asf(u64 v);
/* ---- stage 3: the model (parameters from the C# mod, per player car) ---- */
#include <math.h>
typedef struct { float on, muY, muX, loadSens, By, Cy, Ey, Bx, Cx, Ex, Rb1, Rb2, Rc, fz0, grip, staticLoad, bumpK, scaleMin, scaleMax, pad; } Model;
static volatile Model model;
static volatile LONG scaled;
static float mf(float x, float b, float c, float e) { float bx = b * x; return sinf(c * atanf(bx - e * (bx - atanf(bx)))); }
static float muAt(float base, float fz) { float d = model.fz0 > 1.0f ? (fz - model.fz0) / model.fz0 : 0.0f; if (d < -0.9f) d = -0.9f; if (d > 3.0f) d = 3.0f; float m = base * (1.0f + model.loadSens * d); return m < 0.05f ? 0.05f : m; }
static void tyreModel(u64 wheel, float load, float *out, float *lin, float *ang)
{
    /* GTA's tyre runs on the Magic Formula curve of the fitted compound (peak, peak slip angle, fall-off are written
       into the handling data by the mod); what GTA can't do is load sensitivity per wheel at its own physics rate:
       its friction is linear in the load. Here each wheel's force gets mu(Fz) / mu0 of the real tyre, and the wet factor. */
    (void)wheel;
    if (!(model.on > 0.5f) || load < 1.0f) return;
    float k = muAt(model.muY, load) / (model.muY > 0.05f ? model.muY : 1.0f) * model.grip;
    if (k < model.scaleMin) k = model.scaleMin; if (k > model.scaleMax) k = model.scaleMax;
    out[0] *= k; out[1] *= k; out[2] *= k;
    for (int i = 0; i < 3; i++) { lin[i] *= k; ang[i] *= k; }
    InterlockedIncrement(&scaled);
}
/* own suspension: spring + preload, digressive dampers (bump / rebound, low / high speed), progressive bump stop,
   anti-roll bar from the axle partner's compression. Replaces GTA's spring force (its direction is kept). */
typedef struct { float on, k, pre, cbl, cbh, crl, crh, karb, knee, bumpAt, bumpK, maxF; } Axle;
static volatile Axle axle[2];
static LARGE_INTEGER qf; static LARGE_INTEGER lastT[4]; static float lastPrev[4], dtStep[4] = { 1.0f / 60, 1.0f / 60, 1.0f / 60, 1.0f / 60 };
static volatile LONG suspDone;
static int ownSusp(int i, u64 wheel, u64 other, float *out, float *lin, float *ang, float *loadOut)
{
    const volatile Axle *a = &axle[i < 2 ? 0 : 1];
    if (!(a->on > 0.5f) || a->k < 1.0f) return 0;
    float x = rf(wheel, 0x160), prev = rf(wheel, 0x164);
    if (!qf.QuadPart) QueryPerformanceFrequency(&qf);
    if (prev != lastPrev[i]) { LARGE_INTEGER now; QueryPerformanceCounter(&now); if (lastT[i].QuadPart) { float d = (float)(now.QuadPart - lastT[i].QuadPart) / (float)qf.QuadPart; if (d > 1.0f / 300 && d < 1.0f / 20) dtStep[i] += (d - dtStep[i]) * 0.1f; } lastT[i] = now; lastPrev[i] = prev; }
    float v = (x - prev) / dtStep[i];                                   /* + = compressing */
    if (v > 2.5f || v < -2.5f) return 0;                               /* an impact, not suspension travel: leave it to the game */
    float fs = a->pre + a->k * x;
    float fd;
    if (v >= 0) fd = v < a->knee ? a->cbl * v : a->cbl * a->knee + a->cbh * (v - a->knee);
    else { float u = -v; fd = -(u < a->knee ? a->crl * u : a->crl * a->knee + a->crh * (u - a->knee)); }
    float fb = x > a->bumpAt ? a->bumpK * (x - a->bumpAt) * (x - a->bumpAt) * 50.0f : 0.0f;    /* progressive bump stop */
    float fa = other ? a->karb * (x - rf(other, 0x160)) : 0.0f;
    float f = fs + fd + fb + fa;
    if (f < 0) f = 0; if (f > a->maxF) f = a->maxF;
    float g = fabsf(out[2]);
    if (g < 20.0f) return 0;                                            /* wheel in the air for GTA: leave it */
    float k = f / g;
    out[0] *= k; out[1] *= k; out[2] *= k;
    for (int j = 0; j < 3; j++) { lin[j] *= k; ang[j] *= k; }
    if (loadOut) *loadOut *= k;
    InterlockedIncrement(&suspDone);
    return 1;
}
/* bump rubber + digressive damping: a kerb can't fire a spike several times the static load through the spring */
static void suspModel(float *out, float *lin, float *ang, float *loadOut)
{
    if (!(model.on > 0.5f) || model.staticLoad < 1.0f) return;
    float z = out[2], lim = model.staticLoad * model.bumpK;
    if (z <= lim) return;
    float nz = lim + (z - lim) * 0.35f, k = nz / z;
    out[0] *= k; out[1] *= k; out[2] = nz;
    for (int i = 0; i < 3; i++) { lin[i] *= k; ang[i] *= k; }
    if (loadOut) *loadOut *= k;
}

#include "sim_core.h"

static int whichWheel(u64 w) { for (int i = 0; i < 4; i++) if (watch[i] == w && w) return i; return -1; }
static float rf(u64 p, int o) { return p ? *(float *)(p + o) : 0.0f; }
static float asf(u64 v) { float f; memcpy(&f, &v, 4); return f; }

static void Friction(u64 wheel, u64 inst, u64 outVec, u64 lin, u64 ang, u64 st1, u64 st2, u64 spin, u64 load, u64 a10, u64 a11, u64 a12, u64 a13, u64 ctx, u64 other, u64 dt)
{
    origFriction(wheel, inst, outVec, lin, ang, st1, st2, spin, load, a10, a11, a12, a13, ctx, other, dt);
    InterlockedIncrement(&calls[0]);
    Veh *v = cur;
    if (v && outVec && lin && ang)
    {
        int k = wheelIdx(v, wheel);
        if (k >= 0) { vehTyre(v, k, wheel, inst, (float *)outVec, (float *)lin, (float *)ang, asf(load), st1, st2, a10, a11, a12, asf(dt)); return; }
    }
    int i = whichWheel(wheel);
    if (!stepHooked && i >= 0 && outVec && lin && ang) tyreModel(wheel, asf(load), (float *)outVec, (float *)lin, (float *)ang);
    if (i < 0 || !recording || !ring) return;
    LONG n = InterlockedIncrement(&head) - 1; Rec *r = &ring[n % CAP];
    r->tick = GetTickCount(); r->kind = 1; r->wheel = (uint16_t)i; r->dt = asf(dt); r->load = asf(load);
    for (int k = 0; k < 4; k++) r->out[k] = rf(outVec, 4 * k);
    for (int k = 0; k < 3; k++) { r->lin[k] = rf(lin, 4 * k); r->ang[k] = rf(ang, 4 * k); }
    r->spin = rf(spin, 0); r->wspin = rf(wheel, 0x170); r->comp = rf(wheel, 0x160); r->x = asf(a13);
}

static void Susp(u64 wheel, u64 inst, u64 outVec, u64 lin, u64 ang, u64 loadOut, u64 slot, u64 a8, u64 a9, u64 ctx, u64 other, u64 dt)
{
    origSusp(wheel, inst, outVec, lin, ang, loadOut, slot, a8, a9, ctx, other, dt);
    InterlockedIncrement(&calls[1]);
    Veh *v = cur;
    if (v && outVec && lin && ang)
    {
        int k = wheelIdx(v, wheel);
        if (k >= 0) { vehSusp(v, k, wheel, inst, other, (float *)outVec, (float *)lin, (float *)ang, (float *)loadOut); return; }
    }
    int i = whichWheel(wheel);
    if (!stepHooked && i >= 0 && outVec && lin && ang && !ownSusp(i, wheel, other, (float *)outVec, (float *)lin, (float *)ang, (float *)loadOut)) suspModel((float *)outVec, (float *)lin, (float *)ang, (float *)loadOut);
    if (i < 0 || !recording || !ring) return;
    LONG n = InterlockedIncrement(&head) - 1; Rec *r = &ring[n % CAP];
    r->tick = GetTickCount(); r->kind = 2; r->wheel = (uint16_t)i; r->dt = asf(dt); r->load = rf(loadOut, 0);
    for (int k = 0; k < 4; k++) r->out[k] = rf(outVec, 4 * k);
    for (int k = 0; k < 3; k++) { r->lin[k] = rf(lin, 4 * k); r->ang[k] = rf(ang, 4 * k); }
    r->spin = rf(slot, 0); r->wspin = rf(wheel, 0x170); r->comp = rf(wheel, 0x160); r->x = 0;
}

/* per-vehicle wheel integration step (0xF27F40): after it, our wheel spins / drivetrain for the player's car */
static void Step(u64 ctx, u64 b70, u64 b80, u64 out, u64 wheels, u64 count, u64 a7, u64 gear, u64 dt, u64 a10, u64 a11)
{
    Veh *v = NULL; int nw = (int)(count & 0xFFFFFFFF);
    if (wheels && nw > 0 && nw <= MAXW) v = findByWheels(wheels);   /* rdx is a stack copy of CVehicle+0xB70, the wheel array pointer is the vehicle's own */
    if (v && v->nw != nw) { u64 *wp = (u64 *)wheels; for (int i = 0; i < nw; i++) v->wheel[i] = wp[i]; v->nw = nw; }
    if (v && v->nw == nw) { u64 *wp = (u64 *)wheels; if (wp[0] != v->wheel[0]) for (int i = 0; i < nw; i++) v->wheel[i] = wp[i]; }
    Veh *prev = cur; cur = v;
    origStep(ctx, b70, b80, out, wheels, count, a7, gear, dt, a10, a11);
    cur = prev;
    if (v) vehStep(v, (u64 *)wheels, nw, asf(dt));
}

/* trampoline: the 12 stolen push bytes, then jmp [rip+0] back to target+12 */
static void *makeTrampoline(unsigned char *target)
{
    unsigned char *t = (unsigned char *)VirtualAlloc(NULL, 64, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!t) return NULL;
    memcpy(t, target, 12);
    t[12] = 0xFF; t[13] = 0x25; *(uint32_t *)(t + 14) = 0; *(u64 *)(t + 18) = (u64)(target + 12);
    return t;
}
static int patch(unsigned char *target, void *detour)
{
    unsigned char p[12] = { 0x48, 0xB8, 0,0,0,0,0,0,0,0, 0xFF, 0xE0 };   /* mov rax, detour ; jmp rax */
    *(u64 *)(p + 2) = (u64)detour;
    DWORD old;
    if (!VirtualProtect(target, 12, PAGE_EXECUTE_READWRITE, &old)) return 0;
    memcpy(target, p, 12);
    VirtualProtect(target, 12, old, &old);
    FlushInstructionCache(GetCurrentProcess(), target, 12);
    return 1;
}

static unsigned char *locate(const unsigned char *sig, u64 rva)
{
    unsigned char *base = (unsigned char *)GetModuleHandleA(NULL);
    IMAGE_NT_HEADERS *nt = (IMAGE_NT_HEADERS *)(base + ((IMAGE_DOS_HEADER *)base)->e_lfanew);
    SIZE_T size = nt->OptionalHeader.SizeOfImage;
    if (rva + 128 < size && memcmp(base + rva, sig, 128) == 0) return base + rva;
    unsigned char *hit = NULL;
    for (SIZE_T i = 0x1000; i + 128 < size; i++)
    {
        MEMORY_BASIC_INFORMATION mi;
        if ((i & 0xFFF) == 0 && VirtualQuery(base + i, &mi, sizeof mi) && (mi.State != MEM_COMMIT || (mi.Protect & (PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_READONLY | PAGE_READWRITE | PAGE_EXECUTE)) == 0)) { i += 0xFFF; continue; }
        if (base[i] == sig[0] && memcmp(base + i, sig, 128) == 0) { if (hit) return NULL; hit = base + i; }
    }
    return hit;
}

__declspec(dllexport) int MCP_Install(void)
{
    if (InterlockedCompareExchange(&installed, 1, 0) != 0) return 1;
    unsigned char *f = locate(SIG_FRICTION, 0xF1E8B0), *s = locate(SIG_SUSP, 0xF1DB20), *st = locate(SIG_STEP, 0xF27F40);
    if (!f || !s) { snprintf(status, sizeof status, "code not recognised (friction %p, suspension %p): not hooked", f, s); installed = 0; return 0; }
    origFriction = (Fn16)makeTrampoline(f); origSusp = (Fn12)makeTrampoline(s);
    if (!origFriction || !origSusp || !patch(f, (void *)Friction) || !patch(s, (void *)Susp)) { snprintf(status, sizeof status, "hook failed"); return 0; }
    if (st) { origStep = (Fn11)makeTrampoline(st); if (!origStep || !patch(st, (void *)Step)) { origStep = NULL; st = NULL; } }
    stepHooked = st != NULL;
    snprintf(status, sizeof status, "hooked friction at +0x%llX, suspension at +0x%llX, wheel step %s", (u64)(f - (unsigned char *)GetModuleHandleA(NULL)), (u64)(s - (unsigned char *)GetModuleHandleA(NULL)), st ? "hooked" : "not recognised (no own drivetrain)");
    return 1;
}
__declspec(dllexport) const char *MCP_Status(void) { return status; }
__declspec(dllexport) void MCP_SetModel(const float *p, int n) { if (n > (int)(sizeof(Model) / 4)) n = sizeof(Model) / 4; memcpy((void *)&model, p, (size_t)n * 4); }
__declspec(dllexport) int MCP_DriveAvailable(void) { return stepHooked; }
/* register / update a vehicle (CVehicle address) with its parameters (VParams layout, see sim_core.h) */
__declspec(dllexport) int MCP_VehSet(u64 veh, const float *p, int n)
{
    if (!stepHooked || !veh) return 0;
    if (n > VP_N) n = VP_N;
    Veh *v = findVeh(veh);
    if (v) { memcpy(&v->p, p, (size_t)n * 4); vpeaks(v); v->wkey = *(u64 *)(veh + 0xC30); return 1; }
    for (int i = 0; i < MAXV; i++)
    {
        if (InterlockedCompareExchange(&vehs[i].used, 1, 0) != 0) continue;
        Veh *w = &vehs[i];
        memset((char *)w + sizeof(LONG), 0, sizeof(Veh) - sizeof(LONG));
        w->veh = veh; w->wkey = *(u64 *)(veh + 0xC30); memcpy(&w->p, p, (size_t)n * 4); vpeaks(w);
        w->tcsF = 1; w->engW = (w->p.idle > 100 ? w->p.idle : 800) / RPM_W;
        for (int q = 0; q < MAXW; q++) w->ws[q].pair = -1;
        w->used = 2;
        return 1;
    }
    return 0;
}
__declspec(dllexport) void MCP_VehDrop(u64 veh) { Veh *v = findVeh(veh); if (v) { v->used = 0; } }
__declspec(dllexport) void MCP_VehDropAll(void) { for (int i = 0; i < MAXV; i++) vehs[i].used = 0; }
__declspec(dllexport) int MCP_VehCount(void) { int c = 0; for (int i = 0; i < MAXV; i++) if (vehs[i].used == 2) c++; return c; }
/* state: rpm, tcs, abs, esc, boost, steps, own evaluations, coupling (err ratio, -2 = disabled), suspension ready, wheels,
   spin x10 (10..19), slip ratio x10 (20..29), tyre temp C x10 (30..39), wear x10 (40..49), hot pressure psi x10 (50..59),
   tan slip angle x10 (60..69), tan peak slip angle (70), strut mode (71: 1 own, -1 GTA direction, 0 checking), axis mode (72) */
__declspec(dllexport) int MCP_VehState(u64 veh, float *o, int n)
{
    Veh *v = findVeh(veh); if (!v) return 0;
    float s[100]; memset(s, 0, sizeof s);
    s[0] = v->engW * RPM_W; s[1] = (float)v->tcsAct; s[2] = (float)v->absAct; s[3] = (float)v->escAct; s[4] = v->boost;
    s[5] = (float)v->steps; s[6] = (float)v->own; s[7] = v->bad ? -2.0f : v->angMag > 1e-6 ? (float)(v->angErr / v->angMag) : -1.0f;
    s[8] = (float)v->susReady; s[9] = (float)v->nw;
    for (int i = 0; i < 10 && i < v->nw; i++)
    {
        s[10 + i] = v->ws[i].w; s[20 + i] = v->ws[i].kappa; s[30 + i] = v->ws[i].temp; s[40 + i] = v->ws[i].wear;
        s[50 + i] = v->ws[i].press; s[60 + i] = v->ws[i].ta;
    }
    s[70] = v->aPeakTan; s[71] = v->strutOk ? 1.0f : v->strutBad ? -1.0f : 0.0f; s[72] = (float)axisMode; s[73] = v->bodyMass;
    for (int k = 0; k < 3; k++) { s[74 + k] = v->iGta[k]; s[77 + k] = v->iTgt[k]; }
    s[80] = suspTravel(v, 0); s[81] = suspTravel(v, 1); s[82] = v->x0[0]; s[83] = v->x0[1];
    s[84] = v->revReq ? -1.0f : (float)v->gOwn; s[85] = v->shT;
    for (int i = 0; i < 10 && i < v->nw; i++) s[86 + i] = v->ws[i].btemp;
    if (n > 100) n = 100; memcpy(o, s, (size_t)n * 4); return n;
}
__declspec(dllexport) void MCP_VehDrive(u64 veh, int own, int sel, float thr, float brk) { Veh *v = findVeh(veh); if (v) { v->pedOwn = own; v->sel = sel; v->pThr = thr; v->pBrk = brk; } }
__declspec(dllexport) void MCP_VehShift(u64 veh, int dir) { Veh *v = findVeh(veh); if (v) InterlockedExchange(&v->shiftReq, dir); }
__declspec(dllexport) void MCP_VehTyresNew(u64 veh) { Veh *v = findVeh(veh); if (v) for (int i = 0; i < MAXW; i++) v->ws[i].wear = 0; }
__declspec(dllexport) int MCP_AxisFallbacks(void) { return axisFallback; }
__declspec(dllexport) int MCP_SimDump(const wchar_t *path)
{
    if (!srec) return 0;
    LONG n = srecHead, cnt = n > SREC_CAP ? SREC_CAP : n, start = n > SREC_CAP ? n % SREC_CAP : 0;
    FILE *f = _wfopen(path, L"wb"); if (!f) return 0;
    int sz = (int)sizeof(SRec);
    fwrite("MCSR", 1, 4, f); fwrite(&cnt, 4, 1, f); fwrite(&sz, 4, 1, f);
    for (LONG i = 0; i < cnt; i++) fwrite(&srec[(start + i) % SREC_CAP], sizeof(SRec), 1, f);
    fclose(f); return (int)cnt;
}
__declspec(dllexport) int MCP_Scaled(void) { return scaled; }
__declspec(dllexport) int MCP_SuspDone(void) { return suspDone; }
__declspec(dllexport) void MCP_SetAxle(int which, const float *p, int n) { if (which < 0 || which > 1) return; if (n > (int)(sizeof(Axle) / 4)) n = sizeof(Axle) / 4; memcpy((void *)&axle[which], p, (size_t)n * 4); }
__declspec(dllexport) float MCP_Comp(int i) { return 0; }
__declspec(dllexport) void MCP_SetWheels(u64 a, u64 b, u64 c, u64 d) { watch[0] = a; watch[1] = b; watch[2] = c; watch[3] = d; }
__declspec(dllexport) void MCP_Record(int on)
{
    if (on && !ring) ring = (Rec *)VirtualAlloc(NULL, sizeof(Rec) * CAP, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (on) head = 0;
    recording = on ? 1 : 0;
}
__declspec(dllexport) int MCP_Calls(int which) { return calls[which & 1]; }
__declspec(dllexport) int MCP_Dump(const wchar_t *path)
{
    if (!ring) return 0;
    FILE *f = _wfopen(path, L"wb"); if (!f) return 0;
    LONG n = head; if (n > CAP) n = CAP;
    fwrite("MCPH", 1, 4, f); fwrite(&n, 4, 1, f); fwrite(ring, sizeof(Rec), (size_t)n, f); fclose(f);
    return (int)n;
}

BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID r) { (void)h; (void)r; (void)reason; return TRUE; }
