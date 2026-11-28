using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using GTA;
using GTA.Math;
using GTA.Native;

namespace MidnightClub.Physics
{
    /// <summary>Developer probe for the full tyre simulation: (1) dumps the game's code as it is in memory (the exe on
    /// disk is encrypted, the loaded one isn't) so the wheel physics can be analysed offline; (2) records 20 s of the
    /// raw wheel and vehicle memory of the car you drive, with the known state (speed, steering, throttle, brake, ...)
    /// next to it, so the unknown wheel fields (rotation speed, load, slip ...) can be found by correlation; (3) writes
    /// the offsets ScriptHookVDotNet itself found for this build. Everything goes to scripts\MidnightClub\probe\.
    /// Reading only: nothing in the game is changed.</summary>
    public static class Probe
    {
        [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();

        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern int MCP_Install();
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr MCP_Status();
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern void MCP_SetWheels(long a, long b, long c, long d);
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern void MCP_Record(int on);
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl)] private static extern int MCP_Calls(int which);
        [DllImport("MidnightClubPhysics.asi", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private static extern int MCP_Dump(string path);

        private static bool hookRec; private static int hookEnd;
        /// <summary>Stage 2: installs the native read-only hooks and records 15 s of GTA's own tyre and suspension
        /// results for the player's car (together with a normal drive recording).</summary>
        public static void StartHook(string baseDir)
        {
            var car = Game.Player.Character.CurrentVehicle;
            if (car == null || !car.Exists()) { UI.Toast.Post("Native physics test: get in a car first."); return; }
            try
            {
                int ok = MCP_Install();
                string st = Marshal.PtrToStringAnsi(MCP_Status());
                Core.Log.Info("Native physics: " + st);
                if (ok == 0) { Status = "Native: " + st; UI.Toast.Post("Native physics test: " + st); return; }
                var w = car.Wheels.GetAllWheels(); long[] a = new long[4];
                for (int i = 0; i < 4 && i < w.Length; i++) a[i] = w[i].MemoryAddress.ToInt64();
                MCP_SetWheels(a[0], a[1], a[2], a[3]);
                MCP_Record(1); hookRec = true; hookEnd = Game.GameTime + 15000;
                skipCode = true; Start(baseDir); skipCode = false;
                UI.Toast.Post("Native physics test: hooked, recording 15 s. Drive hard: throttle, brakes, corners, a slide.");
            }
            catch (DllNotFoundException) { UI.Toast.Post("Native physics test: MidnightClubPhysics.asi is missing from the game folder."); }
            catch (Exception ex) { Core.Log.Error("native hook", ex); }
        }
        private static bool skipCode;

        public static bool Recording { get; private set; }
        public static string Status = "";
        private static BinaryWriter rec; private static int recEnd, frames; private static string dir;
        private const int WheelBytes = 0x400, VehBytes = 0x2000;

        public static string BaseDir;
        public static void Start(string baseDir)
        {
            if (Recording) return;
            var car = Game.Player.Character.CurrentVehicle;
            if (car == null || !car.Exists()) { Status = "Get in a car first"; UI.Toast.Post("Physics probe: get in a car first."); return; }
            dir = Path.Combine(baseDir, "probe"); Directory.CreateDirectory(dir);
            try { Offsets(Path.Combine(dir, "offsets.txt")); } catch (Exception ex) { Core.Log.Error("probe offsets", ex); }
            if (!skipCode) try { DumpCode(Path.Combine(dir, "code.bin")); } catch (Exception ex) { Core.Log.Error("probe dump", ex); }
            rec = new BinaryWriter(File.Create(Path.Combine(dir, "drive.bin")));
            rec.Write(0x4D435052); rec.Write(WheelBytes); rec.Write(VehBytes);
            recEnd = Game.GameTime + 20000; frames = 0; Recording = true;
            UI.Toast.Post("Physics probe: recording 20 s. Drive hard: full throttle, hard braking, steering, a slide, the handbrake.");
        }

        /// <summary>Per frame while recording.</summary>
        public static void Tick()
        {
            if (hookRec && Game.GameTime > hookEnd)
            {
                hookRec = false;
                try
                {
                    MCP_Record(0);
                    int n = MCP_Dump(Path.Combine(dir, "hook.bin"));
                    string msg = "Native physics test done: " + n + " records, GTA called friction " + MCP_Calls(0) + "x and suspension " + MCP_Calls(1) + "x so far.";
                    Core.Log.Info(msg); UI.Toast.Post(msg + " Tell Claude."); Status = "Native test done";
                }
                catch (Exception ex) { Core.Log.Error("native dump", ex); }
            }
            if (!Recording) return;
            var car = Game.Player.Character.CurrentVehicle;
            if (car == null || !car.Exists() || Game.GameTime > recEnd) { Stop(); return; }
            try
            {
                var h = GetCurrentProcess();
                var wheels = car.Wheels.GetAllWheels();
                rec.Write(0x46524D45); rec.Write(Game.GameTime); rec.Write(Game.LastFrameTime);
                var v = car.Velocity; var w = car.WorldRotationVelocity; var f = car.ForwardVector; var r = car.RightVector; var u = car.UpVector;
                foreach (var x in new[] { v.X, v.Y, v.Z, w.X, w.Y, w.Z, f.X, f.Y, f.Z, r.X, r.Y, r.Z, u.X, u.Y, u.Z, car.Speed, car.WheelSpeed, car.CurrentRPM, car.Throttle, car.SteeringAngle,
                    Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, (int)Control.VehicleAccelerate), Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, (int)Control.VehicleBrake),
                    Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, (int)Control.VehicleHandbrake) ? 1f : 0f, car.HandlingData.Mass, car.HeightAboveGround })
                    rec.Write(x);
                rec.Write(car.CurrentGear); rec.Write(wheels.Length);
                rec.Write(car.MemoryAddress.ToInt64());
                rec.Write(Read(h, car.MemoryAddress, VehBytes));
                foreach (var wh in wheels)
                {
                    rec.Write((int)wh.BoneId); rec.Write(wh.MemoryAddress.ToInt64());
                    var p = wh.LastContactPosition; rec.Write(p.X); rec.Write(p.Y); rec.Write(p.Z); rec.Write(wh.IsTouchingSurface ? 1 : 0);
                    rec.Write(Read(h, wh.MemoryAddress, WheelBytes));
                }
                frames++;
                Status = "Recording " + ((recEnd - Game.GameTime) / 1000 + 1) + " s";
            }
            catch (Exception ex) { Core.Log.Error("probe frame", ex); Stop(); }
        }

        private static void Stop()
        {
            Recording = false;
            try { rec.Write(0x454E4421); rec.Close(); } catch { }
            Status = "Done: " + frames + " frames";
            UI.Toast.Post("Physics probe done (" + frames + " frames). Files in scripts\\MidnightClub\\probe - tell Claude.");
            Core.Log.Info("Physics probe recorded " + frames + " frames");
        }

        private static byte[] Read(IntPtr h, IntPtr addr, int n)
        {
            var b = new byte[n]; IntPtr got;
            if (addr == IntPtr.Zero || !ReadProcessMemory(h, addr, b, (IntPtr)n, out got)) Array.Clear(b, 0, n);
            return b;
        }

        /// <summary>The loaded image of GTA5_Enhanced.exe (decrypted code), read safely page range by page range.</summary>
        private static void DumpCode(string path)
        {
            var mod = Process.GetCurrentProcess().MainModule;
            long baseAddr = mod.BaseAddress.ToInt64(); int size = mod.ModuleMemorySize;
            var h = GetCurrentProcess();
            using (var fs = new BinaryWriter(File.Create(path)))
            {
                fs.Write(0x4D434443); fs.Write(baseAddr); fs.Write(size);
                const int chunk = 0x10000; var buf = new byte[chunk]; int bad = 0;
                for (long off = 0; off < size; off += chunk)
                {
                    int n = (int)Math.Min(chunk, size - off); IntPtr got;
                    if (!ReadProcessMemory(h, new IntPtr(baseAddr + off), buf, (IntPtr)n, out got) || got.ToInt64() != n)
                    {
                        // retry page by page so one guarded page doesn't blank 64 KB
                        for (int p = 0; p < n; p += 0x1000)
                        {
                            var pb = new byte[0x1000];
                            if (!ReadProcessMemory(h, new IntPtr(baseAddr + off + p), pb, (IntPtr)0x1000, out got)) { bad++; Array.Clear(pb, 0, pb.Length); }
                            Array.Copy(pb, 0, buf, p, Math.Min(0x1000, n - p));
                        }
                    }
                    fs.Write(buf, 0, n);
                }
                Core.Log.Info("Probe: dumped " + (size / 1048576) + " MB of the game image at 0x" + baseAddr.ToString("X") + " (" + bad + " unreadable pages)");
            }
        }

        /// <summary>Every offset ScriptHookVDotNet found for this game build (its NativeMemory class is public).</summary>
        private static void Offsets(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("game base 0x" + Process.GetCurrentProcess().MainModule.BaseAddress.ToInt64().ToString("X"));
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type nm = null; try { nm = asm.GetType("SHVDN.NativeMemory"); } catch { }
                if (nm == null) continue;
                var types = new List<Type> { nm }; types.AddRange(nm.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
                foreach (var t in types)
                {
                    foreach (var p in t.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (p.PropertyType != typeof(int) && p.PropertyType != typeof(IntPtr) && p.PropertyType != typeof(ulong) && p.PropertyType != typeof(long) && p.PropertyType != typeof(uint)) continue;
                        try { var v = p.GetValue(null, null); sb.AppendLine(t.Name + "." + p.Name + " = " + (v is int ? "0x" + ((int)v).ToString("X") : v is IntPtr ? "0x" + ((IntPtr)v).ToInt64().ToString("X") : v.ToString())); } catch { }
                    }
                    foreach (var fl in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        object v; try { v = fl.GetValue(null); } catch { continue; }
                        if (v is int) sb.AppendLine(t.Name + "." + fl.Name + " = 0x" + ((int)v).ToString("X"));
                        else if (v is IntPtr) sb.AppendLine(t.Name + "." + fl.Name + " = 0x" + ((IntPtr)v).ToInt64().ToString("X"));
                        else if (v is ulong || v is long || v is uint) sb.AppendLine(t.Name + "." + fl.Name + " = " + v);
                        else if (v is Pointer) { unsafe { sb.AppendLine(t.Name + "." + fl.Name + " = 0x" + ((long)Pointer.Unbox(v)).ToString("X")); } }
                    }
                }
            }
            File.WriteAllText(path, sb.ToString());
        }
    }
}
