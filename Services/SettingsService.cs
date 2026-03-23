using SwitchAudioDevices.Models;
using System.IO;
using System.Text.Json;

namespace SwitchAudioDevices.Services
{
    public class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SwitchAudioDevices", "settings.json");

        private static readonly string ShortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "SwitchAudioDevices.lnk");

        public AppSettings Settings { get; private set; } = new();

        public SettingsService() => Load();

        private void Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
            }
            catch { Settings = new(); }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath,
                    JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public bool IsDeviceEnabled(string deviceId) => !Settings.DisabledDeviceIds.Contains(deviceId);

        public void SetDeviceEnabled(string deviceId, bool enabled)
        {
            if (enabled) Settings.DisabledDeviceIds.Remove(deviceId);
            else Settings.DisabledDeviceIds.Add(deviceId);
            Save();
        }

        public bool StartupShortcutExists() => File.Exists(ShortcutPath);

        public void SetLaunchAtStartup(bool enable)
        {
            Settings.LaunchAtStartup = enable;
            Save();
            if (enable) CreateStartupShortcut();
            else RemoveStartupShortcut();
        }

        private void CreateStartupShortcut()
        {
            try
            {
                var exePath = Environment.ProcessPath
                    ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                    ?? string.Empty;
                if (string.IsNullOrEmpty(exePath)) return;

                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                dynamic shortcut = shell.CreateShortcut(ShortcutPath);
                shortcut.TargetPath = exePath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
                shortcut.Description = "Audio Switcher";
                shortcut.Save();
            }
            catch { }
        }

        private static void RemoveStartupShortcut()
        {
            try { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); }
            catch { }
        }
    }
}
