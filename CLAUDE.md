# SwitchAudioDevices — Claude context

## Build
`dotnet build` — builds from repo root; outputs to `bin/Debug/net8.0-windows10.0.17763.0/`
`dotnet run` — launches the tray app (appears in system tray, no main window on start)

## Structure
```
App.xaml.cs          — app lifecycle; owns AudioService, SettingsService, HotkeyService, tray icon
MainWindow.xaml/.cs  — create-on-demand popup window; hides on deactivation (not closed)
ViewModels/MainViewModel.cs     — device list, commands, BT connection flow
Services/AudioService.cs        — WASAPI endpoint enumeration + BT device matching
Services/BluetoothService.cs    — P/Invoke bthprops.cpl + WinRT BT connection
Services/SettingsService.cs     — persists AppSettings (JSON, user AppData folder)
Services/HotkeyService.cs       — global hotkeys; IdNext/IdPrev constants used codebase-wide
Models/AudioDevice.cs           — observable device model; IsBluetooth/IsBluetoothConnected are init-only
```

## Architecture notes
- **Device objects are ephemeral**: `LoadDevices()` / `LoadDevicesAsync()` clears and replaces every `AudioDevice` in `Devices`. Never hold an `AudioDevice` reference across those calls — capture the `Id` and use `FindDevice(id)` afterwards.
- **BT connection**: two-layer approach in `BluetoothService.ConnectDeviceAsync` — classic `BluetoothSetServiceState` first, then WinRT `BluetoothDevice.GetRfcommServicesAsync(Uncached)` fallback for Apple/BLE-hybrid devices.
- **BT cache**: `AudioService` caches `GetPairedDevices()` for 5 s. Call `InvalidateBtCache()` before each poll when freshness matters.
- **Window auto-hides on `OnDeactivated`** — intentional tray-app behaviour; guard async flows that must keep the window visible.
- **WASAPI state model**: `GetAllEndpoints()` includes Active endpoints + Unplugged/NotPresent ones that match a paired BT device. Non-BT unplugged devices are excluded.
- **COM interop**: `IPolicyConfig` / `PolicyConfigClient` (AudioService.cs) is an undocumented COM interface for setting the default audio endpoint — it has no public SDK; do not replace with a "standard" API.
- **`IsBluetooth` / `IsBluetoothConnected` are `init`-only** on `AudioDevice` — they can't be mutated after construction; re-create via `LoadDevices()` to reflect new state.

## Framework
Target is `net8.0-windows10.0.17763.0` — the version suffix is required for `Windows.Devices.Bluetooth` WinRT APIs. Changing it to bare `net8.0-windows` breaks WinRT.

## No automated tests
Verify changes by running the app. There is no test suite.
