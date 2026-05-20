using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioPilot.Models
{
    public class AudioDevice : INotifyPropertyChanged
    {
        private bool  _isDefault;
        private bool  _isConnecting;
        private bool  _isConnectionFailed;
        private float _volume;

        // ── Init-only identity ──────────────────────────────────────────────────
        public string Id                   { get; init; } = string.Empty;
        public string Name                 { get; init; } = string.Empty;
        public bool   IsBluetooth          { get; init; }
        public bool   IsBluetoothConnected { get; init; }
        public ulong  BluetoothAddress     { get; init; }

        // ── Computed ─────────────────────────────────────────────────────────────

        /// <summary>Segoe MDL2 Assets glyph for this device type.</summary>
        public string DeviceIconGlyph
        {
            get
            {
                if (IsBluetooth) return "\uE702"; // Bluetooth
                var n = Name.ToLowerInvariant();
                if (n.Contains("headphone") || n.Contains("headset") ||
                    n.Contains("earphone")  || n.Contains("airpod")  ||
                    n.Contains("buds")      || n.Contains("ear "))
                    return "\uE7EF"; // Headphones
                return "\uE772"; // Speaker (default)
            }
        }

        // ── Observable state ────────────────────────────────────────────────────

        public bool IsDefault
        {
            get => _isDefault;
            set { if (_isDefault == value) return; _isDefault = value; OnPropertyChanged(); }
        }

        public bool IsConnecting
        {
            get => _isConnecting;
            set { if (_isConnecting == value) return; _isConnecting = value; OnPropertyChanged(); }
        }

        public bool IsConnectionFailed
        {
            get => _isConnectionFailed;
            set { if (_isConnectionFailed == value) return; _isConnectionFailed = value; OnPropertyChanged(); }
        }

        /// <summary>Master volume level scalar 0.0–1.0. Changes are propagated to WASAPI by MainViewModel.</summary>
        public float Volume
        {
            get => _volume;
            set
            {
                var clamped = Math.Clamp(value, 0f, 1f);
                if (Math.Abs(_volume - clamped) < 0.001f) return;
                _volume = clamped;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
