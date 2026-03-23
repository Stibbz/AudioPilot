using SwitchAudioDevices.Services;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace SwitchAudioDevices
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon _trayIcon = null!;
        private MainWindow? _mainWindow;
        private TrayFlyout? _flyout;
        private System.Threading.Timer? _singleClickTimer;
        private System.Drawing.Point _lastClickPos;

        private readonly AudioService _audioService = new();
        private readonly SettingsService _settingsService = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            InitializeTrayIcon();
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Text = "Audio Switcher",
                Visible = true,
                Icon = CreateSpeakerIcon()
            };

            _trayIcon.MouseClick += OnTrayMouseClick;
            _trayIcon.MouseDoubleClick += OnTrayMouseDoubleClick;
            _trayIcon.ContextMenuStrip = BuildContextMenu();
        }

        public ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                Renderer = new DarkMenuRenderer()
            };

            menu.Items.Add(new ToolStripMenuItem("Open", null, (s, e) => Dispatcher.Invoke(() => ShowMainWindow())));

            // "Switch" submenu
            var deviceMenu = new ToolStripMenuItem("Switch");
            var devices = _audioService.GetPlaybackEndpoints(_settingsService.Settings);
            if (devices.Count == 0)
            {
                deviceMenu.DropDownItems.Add(new ToolStripMenuItem("No devices configured") { Enabled = false });
            }
            else
            {
                foreach (var ep in devices)
                {
                    var item = new ToolStripMenuItem(ep.Name) { Checked = ep.IsDefault };
                    var capturedId = ep.Id;
                    item.Click += (s, e) =>
                    {
                        _audioService.SetDefaultDevice(capturedId);
                        _mainWindow?.ViewModel.LoadDevices();
                        RefreshContextMenu();
                    };
                    deviceMenu.DropDownItems.Add(item);
                }
            }
            menu.Items.Add(deviceMenu);
            menu.Items.Add(new ToolStripMenuItem("Preferences", null, (s, e) => Dispatcher.Invoke(() => ShowMainWindow(openSettings: true))));
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

        private void OnTrayMouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _lastClickPos = System.Windows.Forms.Cursor.Position;
            _singleClickTimer?.Dispose();
            _singleClickTimer = new System.Threading.Timer(_ =>
            {
                _singleClickTimer = null;
                Dispatcher.Invoke(ToggleFlyout);
            }, null, SystemInformation.DoubleClickTime + 50, System.Threading.Timeout.Infinite);
        }

        public void CancelPendingFlyout()
        {
            _singleClickTimer?.Dispose();
            _singleClickTimer = null;
        }

        private void OnTrayMouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _singleClickTimer?.Dispose();
            _singleClickTimer = null;
            Dispatcher.Invoke(() =>
            {
                _flyout?.Close();
                ShowMainWindow();
            });
        }

        private void ToggleFlyout()
        {
            if (_flyout != null)
            {
                _flyout.Close();
                return;
            }

            _flyout = new TrayFlyout(_audioService, _lastClickPos);
            _flyout.Closed += (s, e) => _flyout = null;
            _flyout.Show();
            _flyout.Activate();
        }

        private static Icon CreateSpeakerIcon()
        {
            try
            {
                using var bmp = new Bitmap(32, 32);
                using var g = Graphics.FromImage(bmp);
                g.Clear(Color.Transparent);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                using var font = new Font("Segoe MDL2 Assets", 20f, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Color.FromArgb(0x4A, 0x90, 0xD9));
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("\uE767", font, brush, new RectangleF(0, 0, 32, 32), sf);
                return Icon.FromHandle(bmp.GetHicon());
            }
            catch { return SystemIcons.Application; }
        }

        public void ShowMainWindow(bool openSettings = false)
        {
            _flyout?.Close();

            if (_mainWindow == null || !_mainWindow.IsLoaded)
            {
                _mainWindow = new MainWindow(_audioService, _settingsService);
                _mainWindow.Closed += (s, e) => _mainWindow = null;
            }

            _mainWindow.Show();
            _mainWindow.WindowState = System.Windows.WindowState.Normal;
            _mainWindow.Activate();

            if (openSettings)
                _mainWindow.NavigateToSettings();
            else
                _mainWindow.NavigateToDeviceList();
        }

        private void ExitApp()
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _audioService.Dispose();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
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
        public override Color ToolStripDropDownBackground => Color.FromArgb(28, 28, 28);
        public override Color MenuBorder => Color.FromArgb(55, 55, 55);
        public override Color MenuItemBorder => Color.FromArgb(55, 55, 55);
        public override Color MenuItemSelected => Color.FromArgb(45, 45, 45);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(45, 45, 45);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(45, 45, 45);
        public override Color ImageMarginGradientBegin => Color.FromArgb(28, 28, 28);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(28, 28, 28);
        public override Color ImageMarginGradientEnd => Color.FromArgb(28, 28, 28);
        public override Color SeparatorDark => Color.FromArgb(55, 55, 55);
        public override Color SeparatorLight => Color.FromArgb(55, 55, 55);
    }
}
