using System;
using System.IO;

namespace Sunwall
{
    // A small plain-text log, so a machine without a debugger still reports what the
    // app did. Lives next to the user's other local app data.
    public static class Log
    {
        static string filePath;
        static bool ready;

        public static string FilePath { get { return filePath == null ? "(no log)" : filePath; } }

        public static void Init()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sunwall");
                Directory.CreateDirectory(dir);
                filePath = Path.Combine(dir, "log.txt");
                File.WriteAllText(filePath, "Sunwall log" + Environment.NewLine
                    + Environment.NewLine);
                ready = true;
            }
            catch (Exception) { ready = false; }
        }

        public static void Write(string message)
        {
            if (!ready) return;
            try
            {
                File.AppendAllText(filePath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}
