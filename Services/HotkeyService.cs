using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SwitchAudioDevices.Services
{
    /// <summary>
    /// Registers a system-wide hotkey (Ctrl+Alt+S) and fires <see cref="HotkeyPressed"/>
    /// whenever it is detected.  Uses a message-only NativeWindow so no visible window
    /// is required.
    /// </summary>
    public sealed class HotkeyService : IDisposable
    {
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const int HotkeyId  = 9001;

        // Modifier flags
        private const uint MOD_ALT     = 0x0001;
        private const uint MOD_CONTROL = 0x0002;

        // Virtual key for S
        private const uint VK_S = 0x53;

        public event Action? HotkeyPressed;

        private readonly HotkeyWindow _window;
        public readonly bool IsRegistered;

        public HotkeyService()
        {
            _window = new HotkeyWindow(this);
            IsRegistered = RegisterHotKey(_window.Handle, HotkeyId, MOD_CONTROL | MOD_ALT, VK_S);
            if (!IsRegistered)
                System.Diagnostics.Debug.WriteLine("HotkeyService: RegisterHotKey failed — Ctrl+Alt+S may be in use by another app.");
        }

        public void Dispose()
        {
            UnregisterHotKey(_window.Handle, HotkeyId);
            _window.DestroyHandle();
        }

        private sealed class HotkeyWindow : NativeWindow
        {
            private readonly HotkeyService _owner;

            public HotkeyWindow(HotkeyService owner)
            {
                _owner = owner;
                // Message-only window — no taskbar entry, no visible window
                var cp = new CreateParams { Parent = new IntPtr(-3) /* HWND_MESSAGE */ };
                CreateHandle(cp);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
                    _owner.HotkeyPressed?.Invoke();
                else
                    base.WndProc(ref m);
            }
        }
    }
}
