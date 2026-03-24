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

        /// <summary>
        /// Async version: the WASAPI/BT enumeration runs on a thread-pool thread so the
        /// UI stays responsive during navigation. The ObservableCollection is updated back
        /// on the calling (UI) thread after the IO completes.
        /// </summary>
        public async Task LoadSettingsDevicesAsync()
        {
            var endpoints = await Task.Run(() => _audioService.GetAllEndpoints());
            SettingsDevices.Clear();
            foreach (var ep in endpoints)
                SettingsDevices.Add(new SettingsDeviceViewModel
                {
                    Id          = ep.Id,
                    Name        = ep.Name,
                    IsBluetooth = ep.IsBluetooth,
                    IsEnabled   = _settingsService.IsDeviceEnabled(ep.Id)
                });
        }

        public async Task LoadDevicesAsync()
        {
            var endpoints = await Task.Run(() =>
                _audioService.GetPlaybackEndpoints(_settingsService.Settings));

            foreach (var d in Devices) d.PropertyChanged -= OnDevicePropertyChanged;
            Devices.Clear();
            foreach (var ep in endpoints)
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

        // ── Hotkey bindings ──────────────────────────────────────────────────────

        // Mirror HotkeyService constants to avoid a hard dependency in the ViewModel.
        private const int HotkeyIdNext = HotkeyService.IdNext;
        private const int HotkeyIdPrev = HotkeyService.IdPrev;

        private int _recordingHotkeyId; // 0 = not recording

        /// <summary>Display text for the Next-device hotkey button.</summary>
        public string HotkeyNextText =>
            _recordingHotkeyId == HotkeyIdNext
                ? "Press keys…"
                : (_settingsService.Settings.HotkeyNext?.DisplayText ?? "Not set");

        /// <summary>Display text for the Prev-device hotkey button.</summary>
        public string HotkeyPrevText =>
            _recordingHotkeyId == HotkeyIdPrev
                ? "Press keys…"
                : (_settingsService.Settings.HotkeyPrev?.DisplayText ?? "Not set");

        internal void BeginHotkeyRecording(int id)
        {
            _recordingHotkeyId = id;
            OnPropertyChanged(nameof(HotkeyNextText));
            OnPropertyChanged(nameof(HotkeyPrevText));
        }

        internal void EndHotkeyRecording()
        {
            _recordingHotkeyId = 0;
            OnPropertyChanged(nameof(HotkeyNextText));
            OnPropertyChanged(nameof(HotkeyPrevText));
        }

        /// <summary>Saves a hotkey binding to settings (null = clear).</summary>
        public void SetHotkey(int id, HotkeyBinding? binding)
        {
            if (id == HotkeyIdNext) _settingsService.Settings.HotkeyNext = binding;
            else                    _settingsService.Settings.HotkeyPrev = binding;
            _settingsService.Save();
            OnPropertyChanged(nameof(HotkeyNextText));
            OnPropertyChanged(nameof(HotkeyPrevText));
        }

        /// <summary>Returns the saved binding for the given hotkey ID.</summary>
        internal HotkeyBinding? GetHotkeyBinding(int id) =>
            id == HotkeyIdNext
                ? _settingsService.Settings.HotkeyNext
                : _settingsService.Settings.HotkeyPrev;

        // ── Cycle device (hotkey) ────────────────────────────────────────────────

        public enum CycleOutcome { Switched, BtFailed, NoDevices }

        // Track the most recent BT connection failure so a quick second press skips past.
        private string   _lastBtFailId   = "";
        private DateTime _lastBtFailTime = DateTime.MinValue;

        /// <summary>
        /// Cycles one step in <paramref name="direction"/> (+1 = next, -1 = previous)
        /// through the enabled device list only.
        /// <para>
        /// If the target is a disconnected Bluetooth device a silent connection attempt
        /// is made; the switch only happens if it succeeds within 15 seconds.
        /// If the same device failed within the last 3 seconds (i.e. the hotkey was
        /// pressed twice in quick succession) it is skipped and the next device in the
        /// same direction is selected instead.
        /// </para>
        /// </summary>
        public async Task<(CycleOutcome Outcome, string DeviceName, int Index, int Total)> CycleAsync(int direction)
        {
            var list = Devices.ToList();
            if (list.Count < 2) return (CycleOutcome.NoDevices, "", 0, 0);

            int cur = list.FindIndex(d => d.IsDefault);
            if (cur < 0) return (CycleOutcome.NoDevices, "", 0, 0);

            int n      = list.Count;
            int target = ((cur + direction) % n + n) % n;
            var device = list[target];

            if (device.IsBluetooth && !device.IsBluetoothConnected)
            {
                bool recentlyFailed = device.Id == _lastBtFailId &&
                                      (DateTime.UtcNow - _lastBtFailTime).TotalSeconds < 3;

                if (recentlyFailed)
                {
                    // Second press within 3 s: skip past this device.
                    _lastBtFailId = "";
                    target        = ((target + direction) % n + n) % n;
                    device        = list[target];
                }
                else
                {
                    bool connected = await TryConnectSilentAsync(device);
                    if (!connected)
                    {
                        _lastBtFailId   = device.Id;
                        _lastBtFailTime = DateTime.UtcNow;
                        return (CycleOutcome.BtFailed, device.Name, 0, 0);
                    }
                    _lastBtFailId = "";
                }
            }
            else
            {
                _lastBtFailId = ""; // reset on any successful non-BT step
            }

            SwitchDefault(device.Id);
            _ = LoadDevicesAsync();
            return (CycleOutcome.Switched, device.Name, target + 1, n);
        }

        /// <summary>
        /// Attempts a Bluetooth connection without updating any UI state.
        /// Returns true if the device shows as connected within 15 seconds.
        /// </summary>
        private async Task<bool> TryConnectSilentAsync(AudioDevice device)
        {
            bool started = await Task.Run(() =>
                _audioService.ConnectBluetoothDevice(device.BluetoothAddress, device.Name));
            if (!started) return false;

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(1500);
                var endpoints = await Task.Run(() => _audioService.GetAllEndpoints());
                if (endpoints.FirstOrDefault(e => e.Id == device.Id)?.IsBluetoothConnected == true)
                    return true;
            }
            return false;
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
                await Task.Delay(1500);

                var fresh = await Task.Run(() => _audioService.GetAllEndpoints());
                if (fresh.FirstOrDefault(d => d.Id == device.Id)?.IsBluetoothConnected == true)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        _audioService.SetDefaultDevice(device.Id);
                        LoadDevices();
                        // WASAPI can lag reporting the new default after a BT connect;
                        // force the flag so the UI reflects the switch immediately.
                        foreach (var d in Devices) d.IsDefault = d.Id == device.Id;
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
            _ = LoadDevicesAsync();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
