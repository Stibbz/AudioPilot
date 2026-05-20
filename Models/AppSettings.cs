namespace AudioPilot.Models
{
    public class AppSettings
    {
        // Device IDs excluded from the switcher. Empty = show all.
        public HashSet<string> DisabledDeviceIds { get; set; } = [];
        public bool LaunchAtStartup { get; set; } = false;

        public HotkeyBinding? HotkeyNext { get; set; }
        public HotkeyBinding? HotkeyPrev { get; set; }
    }
}
