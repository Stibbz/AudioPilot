using System.Runtime.InteropServices;

namespace SwitchAudioDevices.Services
{
    public record BluetoothDeviceInfo(string Name, ulong Address, bool IsConnected);

    public class BluetoothService
    {
        // ── Native structs ──────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public short wYear, wMonth, wDayOfWeek, wDay;
            public short wHour, wMinute, wSecond, wMilliseconds;
        }

        // BLUETOOTH_ADDRESS is a union { ULONGLONG ullLong; BYTE rgBytes[6]; }
        // → size 8, align 8.  The preceding DWORD dwSize (4 bytes) needs 4 bytes
        // of explicit padding before Address so it lands at an 8-byte boundary.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BLUETOOTH_DEVICE_INFO
        {
            public uint dwSize;
            private uint _pad;              // alignment gap
            public ulong Address;           // ullLong member of BLUETOOTH_ADDRESS
            public uint ulClassofDevice;
            [MarshalAs(UnmanagedType.Bool)] public bool fConnected;
            [MarshalAs(UnmanagedType.Bool)] public bool fRemembered;
            [MarshalAs(UnmanagedType.Bool)] public bool fAuthenticated;
            public SYSTEMTIME stLastSeen;   // 16 bytes
            public SYSTEMTIME stLastUsed;   // 16 bytes
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
            public string szName;           // BLUETOOTH_MAX_NAME_SIZE = 248 WCHARs
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            public uint dwSize;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnAuthenticated;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnRemembered;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnUnknown;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnConnected;
            [MarshalAs(UnmanagedType.Bool)] public bool fIssueInquiry;
            public byte cTimeoutMultiplier;
            public IntPtr hRadio;           // HANDLE — auto-aligned to IntPtr size
        }

        // ── P/Invoke ────────────────────────────────────────────────────────────

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern IntPtr BluetoothFindFirstDevice(
            ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp,
            ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindNextDevice(
            IntPtr hFind, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl")]
        private static extern bool BluetoothFindDeviceClose(IntPtr hFind);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern uint BluetoothSetServiceState(
            IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi,
            ref Guid pGuidService, uint dwServiceFlags);

        // ── Service GUIDs ───────────────────────────────────────────────────────

        // Advanced Audio Distribution Profile – Sink (headphones receive audio)
        private static Guid A2dpSink   = new("0000110b-0000-1000-8000-00805f9b34fb");
        // Hands-Free Profile (headsets with microphone)
        private static Guid Handsfree  = new("0000111e-0000-1000-8000-00805f9b34fb");

        private const uint BLUETOOTH_SERVICE_ENABLE = 0x00000001;

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>Returns all paired/remembered Bluetooth devices.</summary>
        public IReadOnlyList<BluetoothDeviceInfo> GetPairedDevices()
        {
            var result = new List<BluetoothDeviceInfo>();
            try
            {
                var sp = MakeSearchParams();
                var di = MakeDeviceInfo();

                var hFind = BluetoothFindFirstDevice(ref sp, ref di);
                if (hFind == IntPtr.Zero) return result;
                try
                {
                    do
                    {
                        if (!string.IsNullOrWhiteSpace(di.szName))
                            result.Add(new BluetoothDeviceInfo(di.szName, di.Address, di.fConnected));
                    }
                    while (BluetoothFindNextDevice(hFind, ref di));
                }
                finally { BluetoothFindDeviceClose(hFind); }
            }
            catch { /* No BT adapter or BT disabled — silently return empty */ }
            return result;
        }

        /// <summary>
        /// Enables the A2DP and HFP service profiles for the paired device, which causes
        /// Windows to initiate a Bluetooth connection.  Matches by address when non-zero,
        /// otherwise falls back to substring name matching.  Returns true if at least one
        /// profile call succeeded (ERROR_SUCCESS = 0) or was already pending.
        /// Error codes are written to Debug output to aid diagnosis.
        /// </summary>
        public bool ConnectDevice(ulong bluetoothAddress, string? fallbackName = null)
        {
            try
            {
                var sp = MakeSearchParams();
                var di = MakeDeviceInfo();

                var hFind = BluetoothFindFirstDevice(ref sp, ref di);
                if (hFind == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("BT Connect: BluetoothFindFirstDevice returned NULL — no BT adapter or adapter disabled.");
                    return false;
                }
                try
                {
                    do
                    {
                        bool matched = bluetoothAddress != 0
                            ? di.Address == bluetoothAddress
                            : fallbackName != null && !string.IsNullOrWhiteSpace(di.szName) &&
                              (di.szName.Contains(fallbackName, StringComparison.OrdinalIgnoreCase) ||
                               fallbackName.Contains(di.szName, StringComparison.OrdinalIgnoreCase));

                        if (!matched) continue;

                        System.Diagnostics.Debug.WriteLine($"BT Connect: found '{di.szName}' addr={di.Address:X} connected={di.fConnected}");

                        // Close the find handle before SetServiceState (can't hold both)
                        BluetoothFindDeviceClose(hFind);
                        hFind = IntPtr.Zero;

                        uint r1 = BluetoothSetServiceState(IntPtr.Zero, ref di, ref A2dpSink,  BLUETOOTH_SERVICE_ENABLE);
                        uint r2 = BluetoothSetServiceState(IntPtr.Zero, ref di, ref Handsfree, BLUETOOTH_SERVICE_ENABLE);

                        System.Diagnostics.Debug.WriteLine($"BT Connect: SetServiceState A2DP={r1} HFP={r2} (0=success, check winerror.h for others)");

                        // ERROR_SUCCESS(0) on either profile means the request was accepted.
                        // Some drivers return non-zero but still initiate the connection;
                        // the caller should poll for actual connection state.
                        return r1 == 0 || r2 == 0;
                    }
                    while (BluetoothFindNextDevice(hFind, ref di));

                    System.Diagnostics.Debug.WriteLine($"BT Connect: no paired device matched addr={bluetoothAddress:X} name='{fallbackName}'");
                }
                finally { if (hFind != IntPtr.Zero) BluetoothFindDeviceClose(hFind); }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BT Connect: exception — {ex.Message}");
            }
            return false;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static BLUETOOTH_DEVICE_SEARCH_PARAMS MakeSearchParams() => new()
        {
            dwSize               = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
            fReturnAuthenticated = true,
            fReturnRemembered    = true,
            fReturnConnected     = true,
            fReturnUnknown       = false,
            fIssueInquiry        = false,
            cTimeoutMultiplier   = 0,
            hRadio               = IntPtr.Zero
        };

        private static BLUETOOTH_DEVICE_INFO MakeDeviceInfo() => new()
        {
            dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>()
        };
    }
}
