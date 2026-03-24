using SwitchAudioDevices.Services;
using SwitchAudioDevices.ViewModels;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SwitchAudioDevices
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;
        public MainViewModel ViewModel => _viewModel;

        public MainWindow(AudioService audioService, SettingsService settingsService)
        {
            InitializeComponent();
            _viewModel = new MainViewModel(audioService, settingsService);
            DataContext = _viewModel;
            Loaded += OnLoaded;
            IsVisibleChanged += OnIsVisibleChanged;
            SizeChanged += OnSizeChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => EnableDwmEffects();

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue) PlayShowAnimation();
        }

        // ── Window sizing ──────────────────────────────────────────────────────

        /// <summary>
        /// Keeps the window from extending below the taskbar.
        /// When content grows (settings panel opens, status bar appears) the
        /// window pushes upward instead of growing behind the taskbar.
        /// </summary>
        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            double maxBottom = SystemParameters.WorkArea.Bottom - 12;
            if (Top + ActualHeight > maxBottom)
                Top = Math.Max(SystemParameters.WorkArea.Top, maxBottom - ActualHeight);
        }

        /// <summary>Repositions the window so its bottom sits just above the taskbar.</summary>
        private void RepinToTray()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Top = SystemParameters.WorkArea.Bottom - ActualHeight - 12;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // ── Navigation ─────────────────────────────────────────────────────────

        public async Task NavigateToSettings()
        {
            if (_viewModel.IsSettingsOpen) return;
            _viewModel.IsSettingsOpen = true;

            SettingsPanel.Visibility = Visibility.Visible;

            var width = ContentContainer.ActualWidth;
            Animate(DeviceListTransform, TranslateTransform.XProperty, 0, -width);
            Animate(SettingsTransform, TranslateTransform.XProperty, width, 0);

            // SizeToContent="Height" auto-grows the window.
            // OnSizeChanged keeps it from going behind the taskbar.

            await _viewModel.LoadSettingsDevicesAsync();
        }

        public async Task NavigateToDeviceList()
        {
            if (!_viewModel.IsSettingsOpen) return;
            _viewModel.IsSettingsOpen = false;

            var width = ContentContainer.ActualWidth;
            var hideAnim = Animate(SettingsTransform, TranslateTransform.XProperty, 0, width);
            hideAnim.Completed += (s, e) =>
            {
                SettingsPanel.Visibility = Visibility.Collapsed;
                // WPF's SizeToContent grows but won't auto-shrink.
                // Toggling it forces a fresh measurement pass.
                SizeToContent = SizeToContent.Manual;
                SizeToContent = SizeToContent.Height;
                RepinToTray();
            };
            Animate(DeviceListTransform, TranslateTransform.XProperty, -width, 0);

            await _viewModel.LoadDevicesAsync();
        }

        // ── Animation helper ───────────────────────────────────────────────────

        private static DoubleAnimation Animate(
            TranslateTransform target, DependencyProperty property, double from, double to)
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            target.BeginAnimation(property, anim);
            return anim;
        }

        // ── Show animation ─────────────────────────────────────────────────────

        private void PlayShowAnimation()
        {
            Opacity = 0;
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            if (RootBorder.RenderTransform is not TranslateTransform tt)
            {
                tt = new TranslateTransform(0, 10);
                RootBorder.RenderTransform = tt;
            }
            else tt.Y = 10;

            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(200))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // ── Window chrome ──────────────────────────────────────────────────────

        protected override void OnClosing(CancelEventArgs e) { e.Cancel = true; Hide(); }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            ResetToDeviceList();
            Hide();
        }

        /// <summary>
        /// Snaps the window back to the device-list view at its default (minimum) height.
        /// Called every time the window is dismissed so the next tray click always shows a clean state.
        /// </summary>
        private void ResetToDeviceList()
        {
            if (!_viewModel.IsSettingsOpen) return;

            _viewModel.IsSettingsOpen = false;
            SettingsPanel.Visibility  = Visibility.Collapsed;

            // WPF animations hold their final value and override local assignments.
            // BeginAnimation(null) releases that hold so the direct assignment below takes effect.
            DeviceListTransform.BeginAnimation(TranslateTransform.XProperty, null);
            SettingsTransform.BeginAnimation(TranslateTransform.XProperty, null);
            DeviceListTransform.X = 0;
            SettingsTransform.X   = ActualWidth;

            // Force SizeToContent to re-measure so the window shrinks back down.
            SizeToContent = SizeToContent.Manual;
            SizeToContent = SizeToContent.Height;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
            else
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();
        private async void SettingsButton_Click(object sender, RoutedEventArgs e) => await NavigateToSettings();
        private async void BackButton_Click(object sender, RoutedEventArgs e) => await NavigateToDeviceList();

        // ── DWM effects ────────────────────────────────────────────────────────

        private void EnableDwmEffects()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = 1;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            if (Environment.OSVersion.Version.Build >= 22000)
            {
                int mica = 2;
                DwmSetWindowAttribute(hwnd, 38, ref mica, sizeof(int));
            }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }
}
