using SwitchAudioDevices.Services;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SwitchAudioDevices
{
    public partial class TrayFlyout : Window
    {
        private readonly System.Drawing.Point _clickPos;
        private bool _canDismiss = false;

        public TrayFlyout(AudioService audioService, System.Drawing.Point clickPos)
        {
            InitializeComponent();
            Opacity = 0;
            _clickPos = clickPos;
            DeviceNameText.Text = audioService.GetDefaultDeviceName() ?? "Unknown device";

            Loaded += OnLoaded;
            Deactivated += OnDeactivated;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PositionNearTrayIcon();

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
            var slideIn = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fadeIn);
            SlideTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);

            fadeIn.Completed += (s, _) => _canDismiss = true;
        }

        private void PositionNearTrayIcon()
        {
            double dpi;
            using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX;
            var scale = dpi / 96.0;

            double cx = _clickPos.X / scale;
            double cy = _clickPos.Y / scale;

            Left = cx - ActualWidth / 2;
            Top = cy - ActualHeight - 12;

            var screen = Screen.FromPoint(_clickPos);
            double wa_right = screen.WorkingArea.Right / scale;
            double wa_left = screen.WorkingArea.Left / scale;
            double wa_top = screen.WorkingArea.Top / scale;

            if (Left + ActualWidth > wa_right) Left = wa_right - ActualWidth - 8;
            if (Left < wa_left) Left = wa_left + 8;
            if (Top < wa_top) Top = cy + 12;
        }

        private void OnDeactivated(object? sender, EventArgs e)
        {
            if (!_canDismiss) return;
            ((App)System.Windows.Application.Current).CancelPendingFlyout();
            Close();
        }

        private void Border_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ((App)System.Windows.Application.Current).ShowMainWindow();
            Close();
        }
    }
}
