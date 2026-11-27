/* MidnightClubBoot.asi - straight into Midnight Club: Los Santos, ONLY when the game was started from the
   Midnight Club launcher (the launcher writes scripts\MidnightClub\autostart.flag). Started any other way, the game
   is left completely untouched (landing page, legal and splash screens as normal).

   When the flag is there: skips the legal and splash screens and the Enhanced landing page, so the game loads
   Story Mode directly and the mod takes over from there.

   Patch patterns: landing page patch by "Unknown Modder" (alloc8or), legal/splash patches adapted from ChaosModV,
   as used in StraightToStoryMode by Chiheb-Bacha (GPL-3.0). This file is GPL-3.0 as well. */
#include <windows.h>
#include <string.h>
#include <stdio.h>

static BYTE *modBase; static SIZE_T modSize;

static int parse(const char *pat, int *bytes, int max)
{
    int n = 0;
    while (*pat && n < max)
    {
        while (*pat == ' ') pat++;
        if (!*pat) break;
        if (*pat == '?') { bytes[n++] = -1; pat++; if (*pat == '?') pat++; }
        else { char h[3] = { pat[0], pat[1], 0 }; bytes[n++] = (int)strtol(h, NULL, 16); pat += 2; }
    }
    return n;
}

static BYTE *find(const char *pattern)
{
    int pb[64]; int n = parse(pattern, pb, 64);
    for (SIZE_T i = 0; i + n < modSize; i++)
    {
        int ok = 1;
        for (int j = 0; j < n; j++) if (pb[j] >= 0 && modBase[i + j] != (BYTE)pb[j]) { ok = 0; break; }
        if (ok) return modBase + i;
    }
    return NULL;
}

static void write(BYTE *at, const char *bytes, SIZE_T len)
{
    DWORD old;
    if (!at) return;
    if (VirtualProtect(at, len, PAGE_EXECUTE_READWRITE, &old)) { memcpy(at, bytes, len); VirtualProtect(at, len, old, &old); }
}

static void logline(const char *dir, const char *msg)
{
    char p[MAX_PATH]; FILE *f;
    snprintf(p, sizeof p, "%s\\scripts\\MidnightClub\\boot.log", dir);
    f = fopen(p, "a"); if (f) { fprintf(f, "%s\n", msg); fclose(f); }
}

static void run(void)
{
    char exe[MAX_PATH], dir[MAX_PATH], flag[MAX_PATH]; char *slash; IMAGE_DOS_HEADER *dos; IMAGE_NT_HEADERS *nt;
    GetModuleFileNameA(NULL, exe, MAX_PATH);
    strcpy(dir, exe); slash = strrchr(dir, '\\'); if (slash) *slash = 0;
    if (_stricmp(slash ? slash + 1 : exe, "GTA5_Enhanced.exe") != 0) return;          // Enhanced only
    snprintf(flag, sizeof flag, "%s\\scripts\\MidnightClub\\autostart.flag", dir);
    {   /* not started from the launcher (or a stale flag from a crashed start): leave the game alone */
        WIN32_FILE_ATTRIBUTE_DATA fa; FILETIME now; ULARGE_INTEGER a, b;
        if (!GetFileAttributesExA(flag, GetFileExInfoStandard, &fa)) return;
        GetSystemTimeAsFileTime(&now);
        a.LowPart = fa.ftLastWriteTime.dwLowDateTime; a.HighPart = fa.ftLastWriteTime.dwHighDateTime;
        b.LowPart = now.dwLowDateTime; b.HighPart = now.dwHighDateTime;
        if (b.QuadPart > a.QuadPart && b.QuadPart - a.QuadPart > 20ULL * 60ULL * 10000000ULL) return;
    }
    modBase = (BYTE *)GetModuleHandleA(NULL);
    dos = (IMAGE_DOS_HEADER *)modBase; nt = (IMAGE_NT_HEADERS *)(modBase + dos->e_lfanew);
    modSize = nt->OptionalHeader.SizeOfImage;
    {
        BYTE *splash = find("0F 85 A9 00 00 00 48 8D 54");
        BYTE *legal = find("00 E9 ? 05 00 00 E8 ? 07");
        BYTE *landing = find("E8 ? ? ? ? 8B 0D ? ? ? ? 83 C1 ? 84 C0");
        if (splash) write(splash - 0x0D, "\xE9\x86\x01\x00\x00\x90", 6);
        if (legal) write(legal - 0x0F, "\x90\x90\x90\x90\x90\x90", 6);
        if (landing)
        {
            BYTE *call = landing + 1;
            BYTE *fn = call + 4 + *(INT32 *)call;
            write(fn, "\x31\xC0\xC3", 3);                                              // landing page -> "not needed"
        }
        {
            char msg[160];
            snprintf(msg, sizeof msg, "Midnight Club boot: splash %s, legal %s, landing page %s", splash ? "skipped" : "not found", legal ? "skipped" : "not found", landing ? "skipped" : "not found");
            logline(dir, msg);
        }
    }
}

BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID r)
{
    (void)h; (void)r;
    if (reason == DLL_PROCESS_ATTACH) run();
    return TRUE;
}
