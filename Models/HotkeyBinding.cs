using System.Windows.Input;

namespace AudioPilot.Models
{
    /// <summary>
    /// A serialisable hotkey combination stored in AppSettings.
    /// Modifiers uses Win32 MOD_ flags: Ctrl=0x0002, Alt=0x0001, Shift=0x0004, Win=0x0008.
    /// </summary>
    public class HotkeyBinding
    {
        public uint Modifiers  { get; set; }
        public uint VirtualKey { get; set; }

        public bool IsSet => VirtualKey != 0;

        public string DisplayText
        {
            get
            {
                if (!IsSet) return "Not set";

                var parts = new List<string>();
                if ((Modifiers & 0x0002) != 0) parts.Add("Ctrl");
                if ((Modifiers & 0x0001) != 0) parts.Add("Alt");
                if ((Modifiers & 0x0004) != 0) parts.Add("Shift");
                if ((Modifiers & 0x0008) != 0) parts.Add("Win");

                var wpfKey  = KeyInterop.KeyFromVirtualKey((int)VirtualKey);
                var keyName = wpfKey.ToString();

                // D0-D9 → 0-9
                if (keyName.Length == 2 && keyName[0] == 'D' && char.IsDigit(keyName[1]))
                    keyName = keyName[1..];

                parts.Add(keyName);
                return string.Join("+", parts);
            }
        }
    }
}
