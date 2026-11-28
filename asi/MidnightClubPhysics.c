/* MidnightClubPhysics.asi - native side of Midnight Club: Los Santos' simulation (GTA V Enhanced 2.0.1158).

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

static const unsigned char SIG_FRICTION[128] = { 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54, 0x56, 0x57, 0x55, 0x53, 0x48, 0x81, 0xec, 0x28, 0x02, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xbc, 0x24, 0x10, 0x02, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xb4, 0x24, 0x00, 0x02, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xac, 0x24, 0xf0, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xa4, 0x24, 0xe0, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x9c, 0x24, 0xd0, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x94, 0x24, 0xc0, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x8c, 0x24, 0xb0, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x84, 0x24, 0xa0, 0x01, 0x00, 0x00, 0x0f, 0x29, 0xbc, 0x24, 0x90, 0x01, 0x00, 0x00, 0x0f, 0x29, 0xb4, 0x24, 0x80, 0x01, 0x00, 0x00, 0xf3, 0x0f, 0x10, 0xbc, 0x24, 0xb0, 0x02, 0x00, 0x00, 0x4c, 0x8b, 0xa4, 0x24, 0x90, 0x02, 0x00, 0x00, 0x41, 0xc7, 0x40, 0x08 };
static const unsigned char SIG_SUSP[128] = { 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54, 0x56, 0x57, 0x55, 0x53, 0x48, 0x81, 0xec, 0x68, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xbc, 0x24, 0x50, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xb4, 0x24, 0x40, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xac, 0x24, 0x30, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0xa4, 0x24, 0x20, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x9c, 0x24, 0x10, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x94, 0x24, 0x00, 0x01, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x8c, 0x24, 0xf0, 0x00, 0x00, 0x00, 0x44, 0x0f, 0x29, 0x84, 0x24, 0xe0, 0x00, 0x00, 0x00, 0x0f, 0x29, 0xbc, 0x24, 0xd0, 0x00, 0x00, 0x00, 0x0f, 0x29, 0xb4, 0x24, 0xc0, 0x00, 0x00, 0x00, 0x4d, 0x89, 0xce, 0x4d, 0x89, 0xc7, 0x49, 0x89, 0xd4, 0x48, 0x89, 0xcf, 0x48, 0x8b, 0x84, 0x24, 0xf8, 0x01, 0x00, 0x00, 0xf3 };

static Fn16 origFriction; static Fn12 origSusp;
static volatile LONG installed; static char status[256] = "not installed";
static volatile u64 watch[4];

typedef struct { uint32_t tick; uint16_t kind, wheel; float dt, load; float out[4], lin[3], ang[3], spin, wspin, comp, x; } Rec;   /* 64 bytes */
#define CAP 262144
static Rec *ring; static volatile LONG head; static volatile LONG recording;
static volatile LONG calls[2];

static int whichWheel(u64 w) { for (int i = 0; i < 4; i++) if (watch[i] == w && w) return i; return -1; }
static float rf(u64 p, int o) { return p ? *(float *)(p + o) : 0.0f; }
static float asf(u64 v) { float f; memcpy(&f, &v, 4); return f; }

static void Friction(u64 wheel, u64 inst, u64 outVec, u64 lin, u64 ang, u64 st1, u64 st2, u64 spin, u64 load, u64 a10, u64 a11, u64 a12, u64 a13, u64 ctx, u64 other, u64 dt)
{
    origFriction(wheel, inst, outVec, lin, ang, st1, st2, spin, load, a10, a11, a12, a13, ctx, other, dt);
    InterlockedIncrement(&calls[0]);
    int i = whichWheel(wheel);
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
    int i = whichWheel(wheel);
    if (i < 0 || !recording || !ring) return;
    LONG n = InterlockedIncrement(&head) - 1; Rec *r = &ring[n % CAP];
    r->tick = GetTickCount(); r->kind = 2; r->wheel = (uint16_t)i; r->dt = asf(dt); r->load = rf(loadOut, 0);
    for (int k = 0; k < 4; k++) r->out[k] = rf(outVec, 4 * k);
    for (int k = 0; k < 3; k++) { r->lin[k] = rf(lin, 4 * k); r->ang[k] = rf(ang, 4 * k); }
    r->spin = rf(slot, 0); r->wspin = rf(wheel, 0x170); r->comp = rf(wheel, 0x160); r->x = 0;
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
    unsigned char *f = locate(SIG_FRICTION, 0xF1E8B0), *s = locate(SIG_SUSP, 0xF1DB20);
    if (!f || !s) { snprintf(status, sizeof status, "code not recognised (friction %p, suspension %p): not hooked", f, s); installed = 0; return 0; }
    origFriction = (Fn16)makeTrampoline(f); origSusp = (Fn12)makeTrampoline(s);
    if (!origFriction || !origSusp || !patch(f, (void *)Friction) || !patch(s, (void *)Susp)) { snprintf(status, sizeof status, "hook failed"); return 0; }
    snprintf(status, sizeof status, "hooked friction at +0x%llX, suspension at +0x%llX", (u64)(f - (unsigned char *)GetModuleHandleA(NULL)), (u64)(s - (unsigned char *)GetModuleHandleA(NULL)));
    return 1;
}
__declspec(dllexport) const char *MCP_Status(void) { return status; }
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
