using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioPilot.ViewModels
{
    public class SettingsDeviceViewModel : INotifyPropertyChanged
    {
        private bool _isEnabled;

        public string Id          { get; init; } = string.Empty;
        public string Name        { get; init; } = string.Empty;
        public bool   IsBluetooth { get; init; }

        /// <summary>Segoe MDL2 Assets glyph for this device type.</summary>
        public string DeviceIconGlyph
        {
            get
            {
                if (IsBluetooth) return "\uE702";
                var n = Name.ToLowerInvariant();
                if (n.Contains("headphone") || n.Contains("headset") ||
                    n.Contains("earphone")  || n.Contains("airpod")  ||
                    n.Contains("buds")      || n.Contains("ear "))
                    return "\uE7EF";
                return "\uE767";
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
