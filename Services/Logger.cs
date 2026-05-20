using System.IO;

namespace SwitchAudioDevices.Services
{
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(
            AppContext.BaseDirectory, "SwitchAudioDevices.log");

        private static readonly object _lock = new();

        public static void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            System.Diagnostics.Debug.WriteLine(line);
            try
            {
                lock (_lock)
                    File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch { }
        }

        public static void LogSeparator()
            => Log("--- session start ---");
    }
}
