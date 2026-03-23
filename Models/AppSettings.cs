namespace SwitchAudioDevices.Models
{
    public class AppSettings
    {
        // Device IDs excluded from the switcher. Empty = show all.
        public HashSet<string> DisabledDeviceIds { get; set; } = [];
        public bool LaunchAtStartup { get; set; } = false;

        /// <summary>Number of device cards visible before scrolling (default 3).</summary>
        public int ItemsToShow { get; set; } = 3;
    }
}
