using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SwitchAudioDevices.Services
{
    /// <summary>
    /// Registers system-wide hotkeys and fires <see cref="HotkeyPressed"/> with the
    /// hotkey ID whenever one is detected.  Uses a message-only NativeWindow so no
    /// visible window is required.  Hotkeys are registered dynamically via <see cref="Register"/>.
    /// </summary>
    public sealed class HotkeyService : IDisposable
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;

        /// <summary>Hotkey ID for "cycle to next device".</summary>
        public const int IdNext = 9001;
        /// <summary>Hotkey ID for "cycle to previous device".</summary>
        public const int IdPrev = 9002;

        /// <summary>Fired on the thread that owns the message window (not the UI thread).</summary>
        public event Action<int>? HotkeyPressed;

        private readonly HotkeyWindow _window;
        private readonly HashSet<int> _registered = [];

        public HotkeyService() => _window = new HotkeyWindow(this);

        /// <summary>
        /// Registers (or re-registers) a hotkey.  Returns true on success.
        /// Silently unregisters any previous registration for the same <paramref name="id"/> first.
        /// </summary>
        public bool Register(int id, uint modifiers, uint vk)
        {
            // Always unregister first so we can cleanly re-bind.
            if (_registered.Remove(id))
                UnregisterHotKey(_window.Handle, id);

            if (vk == 0) return false;

            bool ok = RegisterHotKey(_window.Handle, id, modifiers, vk);
            if (ok) _registered.Add(id);
            return ok;
        }

        /// <summary>Unregisters a hotkey without disposing the service.</summary>
        public void Unregister(int id)
        {
            if (_registered.Remove(id))
                UnregisterHotKey(_window.Handle, id);
        }

        public void Dispose()
        {
            foreach (var id in _registered)
                UnregisterHotKey(_window.Handle, id);
            _registered.Clear();
            _window.DestroyHandle();
        }

        private sealed class HotkeyWindow : NativeWindow
        {
            private readonly HotkeyService _owner;

            public HotkeyWindow(HotkeyService owner)
            {
                _owner = owner;
                // Message-only window — no taskbar entry, no visible window.
                CreateHandle(new CreateParams { Parent = new IntPtr(-3) /* HWND_MESSAGE */ });
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                    _owner.HotkeyPressed?.Invoke(m.WParam.ToInt32());
                else
                    base.WndProc(ref m);
            }
        }
    }
}
