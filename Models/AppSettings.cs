namespace SwitchAudioDevices.Models
{
    public class AppSettings
    {
        // Device IDs excluded from the switcher. Empty = show all.
        public HashSet<string> DisabledDeviceIds { get; set; } = [];
        public bool LaunchAtStartup { get; set; } = false;
    }
}
