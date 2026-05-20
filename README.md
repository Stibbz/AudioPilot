<div align="center">

# 🎧 AudioPilot

**A lightweight Windows tray app for switching audio devices — including Bluetooth — without touching the taskbar.**

![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-0078D4?style=flat-square&logo=windows)
![Runtime](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)
![License](https://img.shields.io/badge/license-MIT-22c55e?style=flat-square)

</div>

---

## ✨ Features

| | |
|---|---|
| 🖱️ **One-click switching** | Click any device in the popup to set it as default |
| 🔵 **Bluetooth connect & switch** | Connects disconnected BT devices, then switches — no manual pairing UI |
| ⌨️ **Global hotkeys** | Cycle forward/backward through your devices from any app |
| 🔔 **Live device list** | Reacts to WASAPI endpoint state changes in real time |
| 🚀 **Launch at startup** | Optional Windows startup shortcut, toggled in settings |
| 🙈 **Zero taskbar presence** | Popup window only — lives entirely in the system tray |

---

## 🚀 Getting Started

```
dotnet publish -c Release
```

The output lands in `bin/Release/net8.0-windows10.0.17763.0/`. Run `AudioPilot.exe` — it appears in the system tray immediately.

> **Note:** The `.0.17763.0` version suffix is required. It enables the WinRT Bluetooth APIs (`Windows.Devices.Bluetooth`). Stripping it breaks BT support.

---

## 🎮 Usage

**Open the picker** — left-click the tray icon

**Switch device** — click any device in the list; Bluetooth devices connect automatically

**Cycle with hotkeys** — configure *Next device* and *Previous device* shortcuts in ⚙️ Settings

**Hide devices** — use Settings → toggle off any device you never want to see

---

## 🏗️ Architecture

```
App.xaml.cs                       tray icon, app lifecycle, WASAPI debounce
MainWindow.xaml/.cs               popup window (hides on deactivation)
ViewModels/
  MainViewModel.cs                device list, hotkey cycling, BT connection flow
Services/
  AudioService.cs                 WASAPI enumeration, IPolicyConfig COM interop
  BluetoothService.cs             P/Invoke + WinRT BT connection
  HotkeyService.cs                global hotkey registration via message-only window
  SettingsService.cs              JSON settings in %APPDATA%\AudioPilot\
Models/
  AudioDevice.cs                  observable device model
  AppSettings.cs                  persisted settings shape
```

**Bluetooth connection flow** — AudioPilot runs the classic `BluetoothSetServiceState` and a WinRT RFCOMM path in parallel. For Apple devices (AirPods, etc.) the classic path always fails; WinRT carries the connection. An RFCOMM socket is held open during A2DP negotiation to keep the ACL link alive, then released once the endpoint is confirmed active.

---

## ⚙️ Settings & Persistence

| Path | Contents |
|---|---|
| `%APPDATA%\AudioPilot\settings.json` | Device visibility, hotkey bindings, startup preference |
| `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\AudioPilot.lnk` | Startup shortcut (when enabled) |
| `<install dir>\AudioPilot.log` | Diagnostic log |
