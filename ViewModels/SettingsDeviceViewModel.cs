using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SwitchAudioDevices.ViewModels
{
    public class SettingsDeviceViewModel : INotifyPropertyChanged
    {
        private bool _isEnabled;

        public string Id          { get; init; } = string.Empty;
        public string Name        { get; init; } = string.Empty;
        public bool   IsBluetooth { get; init; }

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
