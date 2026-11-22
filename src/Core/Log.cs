using System;
using System.IO;

namespace MidnightClub.Core
{
    public static class Log
    {
        private static string path;
        private static readonly object sync = new object();

        public static void Init(string directory)
        {
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "MidnightClub.log");
            try { if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024) File.Delete(path); } catch { }
            Info("==== Midnight Club: Los Santos " + Version.Text + " starting ====");
        }

        public static void Info(string msg) { Write("INFO ", msg); }
        public static void Warn(string msg) { Write("WARN ", msg); }
        public static void Error(string msg, Exception ex = null) { Write("ERROR", ex == null ? msg : msg + ": " + ex); }

        private static void Write(string level, string msg)
        {
            if (path == null) return;
            lock (sync)
            {
                try { File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + msg + Environment.NewLine); }
                catch { }
            }
        }
    }

    public static class Version
    {
        public const string Text = "0.2.1-phase2";
        public const int SaveFormat = 1;
    }
}
