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
            Loaded           += OnLoaded;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => EnableDwmEffects();

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue) PlayShowAnimation();
        }

        private void PlayShowAnimation()
        {
            // Fade in
            Opacity = 0;
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            // Slide up 10px from below
            if (RootBorder.RenderTransform is not TranslateTransform tt)
            {
                tt = new TranslateTransform(0, 10);
                RootBorder.RenderTransform = tt;
            }
            else tt.Y = 10;

            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
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

        // Hide to tray instead of closing
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        // Auto-hide when the window loses focus (tray popup behaviour)
        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            Hide();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
            else
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

        private void SettingsButton_Click(object sender, RoutedEventArgs e) => NavigateToSettings();
        private void BackButton_Click(object sender, RoutedEventArgs e)    => NavigateToDeviceList();

        public void NavigateToSettings()
        {
            if (_viewModel.IsSettingsOpen) return;
            _viewModel.IsSettingsOpen = true;
            _viewModel.LoadSettingsDevices();

            SizeToContent = SizeToContent.Manual;
            Height = 560;

            SettingsPanel.Visibility = Visibility.Visible;
            var width = ContentContainer.ActualWidth;

            Animate(DeviceListTransform, TranslateTransform.XProperty, 0, -width);
            Animate(SettingsTransform,   TranslateTransform.XProperty, width, 0);
        }

        public void NavigateToDeviceList()
        {
            if (!_viewModel.IsSettingsOpen) return;
            _viewModel.IsSettingsOpen = false;
            _viewModel.LoadDevices();

            var width    = ContentContainer.ActualWidth;
            var hideAnim = Animate(SettingsTransform,   TranslateTransform.XProperty, 0, width);
            hideAnim.Completed += (s, e) =>
            {
                SettingsPanel.Visibility = Visibility.Collapsed;
                SizeToContent = SizeToContent.Height;
            };
            Animate(DeviceListTransform, TranslateTransform.XProperty, -width, 0);
        }

        private static DoubleAnimation Animate(
            TranslateTransform target,
            DependencyProperty property,
            double from, double to)
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
