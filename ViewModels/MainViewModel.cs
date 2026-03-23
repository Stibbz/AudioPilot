using SwitchAudioDevices.Models;
using SwitchAudioDevices.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace SwitchAudioDevices.ViewModels
{
    public class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }

    public class RelayCommand<T>(Action<T?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute(parameter is T t ? t : default);
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly AudioService    _audioService;
        private readonly SettingsService _settingsService;
        private bool _launchAtStartup;
        private bool _isSettingsOpen;

        public ObservableCollection<AudioDevice>            Devices        { get; } = [];
        public ObservableCollection<SettingsDeviceViewModel> SettingsDevices { get; } = [];

        public bool IsSettingsOpen
        {
            get => _isSettingsOpen;
            set { _isSettingsOpen = value; OnPropertyChanged(); }
        }

        public bool LaunchAtStartup
        {
            get => _launchAtStartup;
            set
            {
                if (_launchAtStartup == value) return;
                _launchAtStartup = value;
                OnPropertyChanged();
                _settingsService.SetLaunchAtStartup(value);
            }
        }

        // ── Commands ────────────────────────────────────────────────────────────

        /// <summary>
        /// Primary action on a device card.
        /// • Regular / BT-connected  → set as default output.
        /// • BT-disconnected         → initiate Bluetooth connection, then set default.
        /// </summary>
        public ICommand DeviceActionCommand    { get; }
        public ICommand RefreshCommand         { get; }
        public ICommand ToggleSettingsDeviceCommand { get; }
        public ICommand ToggleStartupCommand   { get; }

        public MainViewModel(AudioService audioService, SettingsService settingsService)
        {
            _audioService    = audioService;
            _settingsService = settingsService;
            _launchAtStartup = settingsService.StartupShortcutExists();

            DeviceActionCommand         = new RelayCommand<string>(id => _ = DeviceActionAsync(id));
            RefreshCommand              = new RelayCommand(LoadDevices);
            ToggleSettingsDeviceCommand = new RelayCommand<string>(ToggleSettingsDevice);
            ToggleStartupCommand        = new RelayCommand(() => LaunchAtStartup = !LaunchAtStartup);

            LoadDevices();
        }

        // ── Data loading ────────────────────────────────────────────────────────

        public void LoadDevices()
        {
            Devices.Clear();
            foreach (var ep in _audioService.GetPlaybackEndpoints(_settingsService.Settings))
                Devices.Add(new AudioDevice
                {
                    Id                   = ep.Id,
                    Name                 = ep.Name,
                    IsDefault            = ep.IsDefault,
                    IsBluetooth          = ep.IsBluetooth,
                    IsBluetoothConnected = ep.IsBluetoothConnected,
                    BluetoothAddress     = ep.BluetoothAddress
                });
        }

        public void LoadSettingsDevices()
        {
            SettingsDevices.Clear();
            foreach (var ep in _audioService.GetAllEndpoints())
                SettingsDevices.Add(new SettingsDeviceViewModel
                {
                    Id          = ep.Id,
                    Name        = ep.Name,
                    IsBluetooth = ep.IsBluetooth,
                    IsEnabled   = _settingsService.IsDeviceEnabled(ep.Id)
                });
        }

        // ── Device action ───────────────────────────────────────────────────────

        private async Task DeviceActionAsync(string? deviceId)
        {
            if (deviceId == null) return;
            var device = Devices.FirstOrDefault(d => d.Id == deviceId);
            if (device == null || device.IsConnecting) return;

            if (device.IsBluetooth && !device.IsBluetoothConnected)
                await ConnectBluetoothAsync(device);
            else
                SwitchDefault(deviceId);
        }

        private void SwitchDefault(string deviceId)
        {
            _audioService.SetDefaultDevice(deviceId);
            foreach (var d in Devices) d.IsDefault = d.Id == deviceId;
        }

        /// <summary>
        /// Calls BluetoothSetServiceState to enable A2DP/HFP, then polls the audio
        /// endpoint state every 600 ms until it becomes Active (connected) or 15 s
        /// elapse.  On success the device is automatically set as the default output.
        /// </summary>
        private async Task ConnectBluetoothAsync(AudioDevice device)
        {
            device.IsConnecting      = true;
            device.IsConnectionFailed = false;

            // Yield to let WPF paint the "Connecting…" badge before we call the
            // potentially-blocking Win32 API on the thread pool.
            await Task.Yield();

            bool started = await Task.Run(() => _audioService.ConnectBluetoothDevice(device.BluetoothAddress));

            if (!started)
            {
                device.IsConnecting = false;
                await ShowConnectionFailed(device);
                return;
            }

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(600);

                var fresh = await Task.Run(() => _audioService.GetAllEndpoints());
                if (fresh.FirstOrDefault(d => d.Id == device.Id)?.IsBluetoothConnected == true)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        _audioService.SetDefaultDevice(device.Id);
                        LoadDevices();
                    });
                    return;
                }
            }

            // Timed out
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                device.IsConnecting = false;
                LoadDevices();
            });

            await ShowConnectionFailed(device);
        }

        private static async Task ShowConnectionFailed(AudioDevice device)
        {
            device.IsConnectionFailed = true;
            await Task.Delay(4000);
            device.IsConnectionFailed = false;
        }

        private void ToggleSettingsDevice(string? deviceId)
        {
            if (deviceId == null) return;
            var item = SettingsDevices.FirstOrDefault(d => d.Id == deviceId);
            if (item == null) return;
            item.IsEnabled = !item.IsEnabled;
            _settingsService.SetDeviceEnabled(deviceId, item.IsEnabled);
            LoadDevices();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
