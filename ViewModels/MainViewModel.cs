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
        private bool   _launchAtStartup;
        private bool   _isSettingsOpen;
        private string _statusMessage = "";
        private bool   _statusIsError;
        private CancellationTokenSource? _statusCts;

        public ObservableCollection<AudioDevice>             Devices         { get; } = [];
        public ObservableCollection<SettingsDeviceViewModel> SettingsDevices { get; } = [];

        public bool IsSettingsOpen
        {
            get => _isSettingsOpen;
            set { _isSettingsOpen = value; OnPropertyChanged(); }
        }

        // ── Status bar ──────────────────────────────────────────────────────────

        /// <summary>Non-empty while there is something worth showing in the footer.</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                _statusMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasStatus));
            }
        }

        /// <summary>True when the status is an error (drives red colouring in XAML).</summary>
        public bool StatusIsError
        {
            get => _statusIsError;
            private set { _statusIsError = value; OnPropertyChanged(); }
        }

        public bool HasStatus => !string.IsNullOrEmpty(_statusMessage);

        /// <summary>
        /// Shows <paramref name="message"/> in the footer.
        /// Pass <paramref name="autoCloseMs"/> = 0 to keep it until explicitly cleared.
        /// </summary>
        public void SetStatus(string message, bool isError = false, int autoCloseMs = 4000)
        {
            _statusCts?.Cancel();
            StatusIsError  = isError;
            StatusMessage  = message;

            if (autoCloseMs <= 0) return;

            _statusCts = new CancellationTokenSource();
            var token  = _statusCts.Token;
            _ = Task.Delay(autoCloseMs, token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = "";
                    StatusIsError = false;
                });
            }, TaskScheduler.Default);
        }

        public void ClearStatus()
        {
            _statusCts?.Cancel();
            StatusMessage = "";
            StatusIsError = false;
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

        public ICommand DeviceActionCommand         { get; }
        public ICommand RefreshCommand              { get; }
        public ICommand ToggleSettingsDeviceCommand { get; }
        public ICommand ToggleStartupCommand        { get; }

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
            // Unsubscribe from old devices before clearing
            foreach (var d in Devices)
                d.PropertyChanged -= OnDevicePropertyChanged;

            Devices.Clear();
            foreach (var ep in _audioService.GetPlaybackEndpoints(_settingsService.Settings))
            {
                var device = new AudioDevice
                {
                    Id                   = ep.Id,
                    Name                 = ep.Name,
                    IsDefault            = ep.IsDefault,
                    IsBluetooth          = ep.IsBluetooth,
                    IsBluetoothConnected = ep.IsBluetoothConnected,
                    BluetoothAddress     = ep.BluetoothAddress,
                    Volume               = ep.Volume
                };
                device.PropertyChanged += OnDevicePropertyChanged;
                Devices.Add(device);
            }
        }

        private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AudioDevice.Volume) && sender is AudioDevice d)
                _audioService.SetDeviceVolume(d.Id, d.Volume);
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

        // ── Cycle device (hotkey) ────────────────────────────────────────────────

        /// <summary>
        /// Switches to the next enabled, connected device in the list.
        /// Disconnected Bluetooth devices are skipped.
        /// </summary>
        public void CycleDevice()
        {
            var active = Devices.Where(d => !d.IsBluetooth || d.IsBluetoothConnected).ToList();
            if (active.Count < 2) return;

            var currentIndex = active.FindIndex(d => d.IsDefault);
            var nextIndex    = (currentIndex + 1) % active.Count;
            var next         = active[nextIndex];

            SwitchDefault(next.Id);
            LoadDevices();
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

        private async Task ConnectBluetoothAsync(AudioDevice device)
        {
            device.IsConnecting       = true;
            device.IsConnectionFailed = false;
            SetStatus($"Connecting to {device.Name}…", isError: false, autoCloseMs: 0);

            await Task.Yield();

            bool started = await Task.Run(() =>
                _audioService.ConnectBluetoothDevice(device.BluetoothAddress, device.Name));

            if (!started)
            {
                // Bluetooth stack couldn't even find / reach the device — fail immediately.
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    device.IsConnecting       = false;
                    device.IsConnectionFailed = true;
                    LoadDevices();
                    SetStatus($"Could not reach {device.Name} — make sure it is powered on", isError: true, autoCloseMs: 6000);
                });
                await Task.Delay(4000);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    device.IsConnectionFailed = false);
                return;
            }

            // Poll until the device shows up as connected (up to 15 s).
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
                        SetStatus($"Connected to {device.Name}", isError: false, autoCloseMs: 3000);
                    });
                    return;
                }
            }

            // Timed out.
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                device.IsConnecting       = false;
                device.IsConnectionFailed = true;
                LoadDevices();
                SetStatus($"Could not connect to {device.Name}", isError: true, autoCloseMs: 6000);
            });
            await Task.Delay(4000);
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                device.IsConnectionFailed = false);
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
