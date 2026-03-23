using SwitchAudioDevices.Services;
using SwitchAudioDevices.ViewModels;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
            Loaded           += OnLoaded;
            IsVisibleChanged += OnIsVisibleChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        // Reacts to ItemsToShow changes so the window resizes while on the device list.
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MainViewModel.DeviceScrollHeight)) return;
            if (_viewModel.IsSettingsOpen) return;

            // MaxHeight binding on the ScrollViewer raises InvalidateMeasure on the
            // ScrollViewer, but SizeToContent=Height on the Window doesn't always pick
            // that chain up after a Manual/Height toggle.  Calling InvalidateMeasure()
            // on the Window directly forces a fresh top-down layout pass.
            InvalidateMeasure();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Top = SystemParameters.WorkArea.Bottom - ActualHeight - 12;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => EnableDwmEffects();

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue) PlayShowAnimation();
        }

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

        protected override void OnClosing(CancelEventArgs e) { e.Cancel = true; Hide(); }

        // Auto-hide when the window loses focus (tray popup behaviour)
        protected override void OnDeactivated(EventArgs e) { base.OnDeactivated(e); Hide(); }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
            else
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

        // async void is correct for event handlers — exceptions surface via the dispatcher.
        private async void SettingsButton_Click(object sender, RoutedEventArgs e) => await NavigateToSettings();
        private async void BackButton_Click(object sender, RoutedEventArgs e)     => await NavigateToDeviceList();

        // ── Navigation ──────────────────────────────────────────────────────────

        public async Task NavigateToSettings()
        {
            if (_viewModel.IsSettingsOpen) return;
            _viewModel.IsSettingsOpen = true;

            // Switch to manual sizing so we can set an explicit height and grow upward.
            SizeToContent = SizeToContent.Manual;

            // Pin the bottom above the tray and grow upward — without this the OS
            // keeps Top fixed so the window expands downward behind the taskbar.
            double prevBottom = Top + ActualHeight;
            double availableH = prevBottom - SystemParameters.WorkArea.Top - 12;

            // Settings panel = 48 title + ~52 header + 420 scroll + status strip ≈ 520 px.
            Height = Math.Min(520, availableH);
            Top    = Math.Max(SystemParameters.WorkArea.Top, prevBottom - Height);

            SettingsPanel.Visibility = Visibility.Visible;
            var width = ContentContainer.ActualWidth;

            Animate(DeviceListTransform, TranslateTransform.XProperty, 0, -width);
            Animate(SettingsTransform,   TranslateTransform.XProperty, width, 0);

            // IO runs on a thread-pool thread while the animation plays.
            await _viewModel.LoadSettingsDevicesAsync();
        }

        public async Task NavigateToDeviceList()
        {
            if (!_viewModel.IsSettingsOpen) return;
            _viewModel.IsSettingsOpen = false;

            var width    = ContentContainer.ActualWidth;
            var hideAnim = Animate(SettingsTransform, TranslateTransform.XProperty, 0, width);
            hideAnim.Completed += (s, e) =>
            {
                SettingsPanel.Visibility = Visibility.Collapsed;
                SizeToContent            = SizeToContent.Height;

                // Setting SizeToContent=Height after a Manual cycle doesn't always trigger
                // a fresh layout pass on its own.  InvalidateMeasure() on the Window forces
                // a full top-down re-measure so the window shrinks to fit the device list.
                InvalidateMeasure();

                // Background priority (4) runs after the Render pass (7) where the HWND
                // has been resized, so ActualHeight is correct by the time this fires.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Top = SystemParameters.WorkArea.Bottom - ActualHeight - 12;
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
            Animate(DeviceListTransform, TranslateTransform.XProperty, -width, 0);

            await _viewModel.LoadDevicesAsync();
        }

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

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }
}
