using AudioPilot.Services;
using AudioPilot.ViewModels;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace AudioPilot
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon _trayIcon = null!;
        private MainWindow? _mainWindow;

        private readonly AudioService    _audioService    = new();
        private readonly SettingsService _settingsService = new();
        private readonly HotkeyService _hotkeyService = new();
        public  HotkeyService HotkeyService => _hotkeyService;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Logger.LogSeparator();
            InitializeTrayIcon();
            RegisterSavedHotkeys();
            _hotkeyService.HotkeyPressed   += OnHotkeyPressed;
            _audioService.DeviceStateChanged += OnDeviceStateChanged;
        }

        private void RegisterSavedHotkeys()
        {
            var s = _settingsService.Settings;
            if (s.HotkeyNext?.IsSet == true)
                _hotkeyService.Register(HotkeyService.IdNext, s.HotkeyNext.Modifiers, s.HotkeyNext.VirtualKey);
            if (s.HotkeyPrev?.IsSet == true)
                _hotkeyService.Register(HotkeyService.IdPrev, s.HotkeyPrev.Modifiers, s.HotkeyPrev.VirtualKey);
        }

        // ── Tray icon ───────────────────────────────────────────────────────────

        private void InitializeTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Text    = "AudioPilot",
                Visible = true,
                Icon    = CreateSpeakerIcon()
            };

            UpdateTrayTooltip();
            _trayIcon.MouseClick      += OnTrayMouseClick;
            _trayIcon.ContextMenuStrip = BuildContextMenu();
        }

        public ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                Renderer        = new DarkMenuRenderer()
            };

            menu.Items.Add(new ToolStripMenuItem("Open", null, (s, e) => Dispatcher.Invoke(() => ShowMainWindow())));

            // "Switch" submenu
            var deviceMenu = new ToolStripMenuItem("Switch");
            var devices    = _audioService.GetPlaybackEndpoints(_settingsService.Settings);
            if (devices.Count == 0)
            {
                deviceMenu.DropDownItems.Add(new ToolStripMenuItem("No devices configured") { Enabled = false });
            }
            else
            {
                foreach (var ep in devices)
                {
                    var item       = new ToolStripMenuItem(ep.Name) { Checked = ep.IsDefault };
                    var capturedId = ep.Id;
                    item.Click += (s, e) =>
                    {
                        _audioService.SetDefaultDevice(capturedId);
                        _mainWindow?.ViewModel.LoadDevices();
                        UpdateTrayTooltip();
                        RefreshContextMenu();
                    };
                    deviceMenu.DropDownItems.Add(item);
                }
            }
            menu.Items.Add(deviceMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Close App", null, (s, e) => ExitApp()));

            return menu;
        }

        public void RefreshContextMenu()
        {
            var old = _trayIcon.ContextMenuStrip;
            _trayIcon.ContextMenuStrip = BuildContextMenu();
            old?.Dispose();
        }

        private void UpdateTrayTooltip()
        {
            var name    = _audioService.GetDefaultDeviceName() ?? "No device";
            var tooltip = $"Audio: {name}";
            // NotifyIcon.Text is capped at 63 characters
            _trayIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
        }

        // ── Hotkey ──────────────────────────────────────────────────────────────

        private CancellationTokenSource? _deviceChangeCts;

        private void OnDeviceStateChanged()
        {
            // Debounce: Windows fires several notifications in quick succession when a
            // BT device connects or disconnects — collapse them into one reload.
            _deviceChangeCts?.Cancel();
            _deviceChangeCts = new CancellationTokenSource();
            var token = _deviceChangeCts.Token;
            _ = Task.Delay(500, token).ContinueWith(_ =>
            {
                if (token.IsCancellationRequested) return;
                Dispatcher.Invoke(() =>
                {
                    _mainWindow?.ViewModel.LoadDevices();
                    UpdateTrayTooltip();
                    RefreshContextMenu();
                });
            }, TaskScheduler.Default);
        }

        private void OnHotkeyPressed(int hotkeyId)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                if (_mainWindow?.ViewModel is { } vm)
                {
                    var (outcome, name, index, total) = await vm.CycleAsync(hotkeyId == HotkeyService.IdNext ? 1 : -1);

                    if (outcome == MainViewModel.CycleOutcome.Switched)
                        Notify($"Switched to {name} [{index}/{total}]");
                    else if (outcome == MainViewModel.CycleOutcome.BtFailed)
                        Notify($"Could not connect to {name}", isError: true);
                }
                UpdateTrayTooltip();
                RefreshContextMenu();
            });
        }

        private void Notify(string message, bool isError = false)
        {
            _trayIcon.ShowBalloonTip(
                timeout: 4000,
                tipTitle: "AudioPilot",
                tipText:  message,
                tipIcon:  isError ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        // ── Tray click ──────────────────────────────────────────────────────────

        private void OnTrayMouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            // Cursor.Position is the only reliable source for tray icon screen coordinates.
            // MouseEventArgs.X/Y from NotifyIcon can be 0,0 on some Windows configurations.
            var pt = Cursor.Position;
            Dispatcher.Invoke(() => ShowMainWindow(pt));
        }

        // ── Window management ───────────────────────────────────────────────────

        public void ShowMainWindow(System.Drawing.Point? trayClickPoint = null, bool openSettings = false)
        {
            if (_mainWindow == null || !_mainWindow.IsLoaded)
            {
                _mainWindow = new MainWindow(_audioService, _settingsService);
                _mainWindow.Closed += (s, e) => _mainWindow = null;
            }

            _mainWindow.Show();
            _mainWindow.WindowState = System.Windows.WindowState.Normal;

            if (trayClickPoint.HasValue)
            {
                // UpdateLayout() forces WPF to measure the window so ActualHeight is
                // correct before we compute the tray-relative position. Without this,
                // ActualHeight is 0 on first show and the window spawns too high.
                _mainWindow.UpdateLayout();
                PositionNearTray(_mainWindow, trayClickPoint.Value);
            }

            _mainWindow.Activate();

            if (openSettings)
                _ = _mainWindow.NavigateToSettings();
        }

        private static void PositionNearTray(MainWindow window, System.Drawing.Point clickPoint)
        {
            float dpiScale;
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
                dpiScale = g.DpiX / 96f;

            var screen = Screen.FromPoint(clickPoint);
            var area   = screen.WorkingArea;

            double areaLeft   = area.Left   / dpiScale;
            double areaTop    = area.Top    / dpiScale;
            double areaRight  = area.Right  / dpiScale;
            double areaBottom = area.Bottom / dpiScale;
            double clickDipX  = clickPoint.X / dpiScale;

            double winW = window.Width;
            double winH = window.ActualHeight > 0 ? window.ActualHeight : 460;

            double left = clickDipX - winW / 2.0;
            double top  = areaBottom - winH - 12;

            left = Math.Max(areaLeft, Math.Min(left, areaRight - winW));
            top  = Math.Max(areaTop, top);

            window.Left = left;
            window.Top  = top;
        }

        // ── Icon ────────────────────────────────────────────────────────────────

        private static Icon CreateSpeakerIcon()
        {
            try
            {
                using var bmp = new Bitmap(32, 32);
                using var g   = Graphics.FromImage(bmp);
                g.Clear(Color.Transparent);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                using var font  = new Font("Segoe MDL2 Assets", 28f, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Color.FromArgb(255, 144, 104));
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

                // Simulate stroke by drawing at 1px offsets in all 8 directions first
                foreach (var (dx, dy) in new[]{(-0.5f,-0.5f),(0f,-0.5f),(0.5f,-0.5f),(-0.5f,0f),(0.5f,0f),(-0.5f,0.5f),(0f,0.5f),(0.5f,0.5f)})
                    g.DrawString("\uE772", font, brush, new RectangleF(dx, dy, 32, 32), sf);

                // Draw the main glyph on top to keep edges crisp
                g.DrawString("\uE772", font, brush, new RectangleF(0, 0, 32, 32), sf);

                return Icon.FromHandle(bmp.GetHicon());
            }
            catch { return SystemIcons.Application; }
        }

        // ── Shutdown ────────────────────────────────────────────────────────────

        private void ExitApp()
        {
            _hotkeyService.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _audioService.Dispose();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _hotkeyService?.Dispose();
            _trayIcon?.Dispose();
            _audioService?.Dispose();
            base.OnExit(e);
        }
    }

    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? Color.FromArgb(220, 220, 220)
                : Color.FromArgb(100, 100, 100);
            base.OnRenderItemText(e);
        }
    }

    internal class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground    => Color.FromArgb(28, 28, 28);
        public override Color MenuBorder                     => Color.FromArgb(55, 55, 55);
        public override Color MenuItemBorder                 => Color.FromArgb(55, 55, 55);
        public override Color MenuItemSelected               => Color.FromArgb(45, 45, 45);
        public override Color MenuItemSelectedGradientBegin  => Color.FromArgb(45, 45, 45);
        public override Color MenuItemSelectedGradientEnd    => Color.FromArgb(45, 45, 45);
        public override Color ImageMarginGradientBegin       => Color.FromArgb(28, 28, 28);
        public override Color ImageMarginGradientMiddle      => Color.FromArgb(28, 28, 28);
        public override Color ImageMarginGradientEnd         => Color.FromArgb(28, 28, 28);
        public override Color SeparatorDark                  => Color.FromArgb(55, 55, 55);
        public override Color SeparatorLight                 => Color.FromArgb(55, 55, 55);
    }
}
