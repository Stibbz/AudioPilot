using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SwitchAudioDevices.Models
{
    public class AudioDevice : INotifyPropertyChanged
    {
        private bool _isDefault;
        private bool _isConnecting;

        // ── Init-only identity ──────────────────────────────────────────────────
        public string Id               { get; init; } = string.Empty;
        public string Name             { get; init; } = string.Empty;
        public bool   IsBluetooth      { get; init; }
        public bool   IsBluetoothConnected { get; init; }
        public ulong  BluetoothAddress { get; init; }

        // ── Observable state ────────────────────────────────────────────────────

        /// <summary>True when this device is the Windows default audio output.</summary>
        public bool IsDefault
        {
            get => _isDefault;
            set { if (_isDefault == value) return; _isDefault = value; OnPropertyChanged(); }
        }

        /// <summary>True while a Bluetooth connection attempt is in progress.</summary>
        public bool IsConnecting
        {
            get => _isConnecting;
            set { if (_isConnecting == value) return; _isConnecting = value; OnPropertyChanged(); }
        }

        private bool _isConnectionFailed;
        /// <summary>True for a few seconds after a connection attempt times out.</summary>
        public bool IsConnectionFailed
        {
            get => _isConnectionFailed;
            set { if (_isConnectionFailed == value) return; _isConnectionFailed = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
