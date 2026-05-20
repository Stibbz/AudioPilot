using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;

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

        /// <summary>
        /// Async wrapper that first tries the classic bthprops path, then falls back to
        /// the WinRT <see cref="BluetoothDevice"/> API for devices (e.g. AirPods) that do
        /// not respond to <c>BluetoothSetServiceState</c>.  The WinRT path requests uncached
        /// RFCOMM services, which forces the BT stack to open an ACL link to the device;
        /// the Windows audio subsystem then negotiates A2DP over that link automatically.
        /// Returns true if a connection attempt was successfully initiated.
        /// </summary>
        public async Task<bool> ConnectDeviceAsync(ulong bluetoothAddress, string? fallbackName = null)
        {
            // Classic synchronous path (works for most standard BT devices).
            // Run on a thread-pool thread so blocking P/Invoke doesn't stall the UI.
            bool classicOk = await Task.Run(() => ConnectDevice(bluetoothAddress, fallbackName));
            if (classicOk) return true;

            // WinRT fallback — requires a known address (no name-only path here).
            if (bluetoothAddress == 0) return false;
            try
            {
                var bt = await BluetoothDevice.FromBluetoothAddressAsync(bluetoothAddress);
                if (bt == null)
                {
                    System.Diagnostics.Debug.WriteLine("BT Connect WinRT: device not found for address");
                    return false;
                }

                // Uncached forces an over-the-air ACL connection; the audio subsystem
                // negotiates A2DP over it automatically once the link is up.
                var result = await bt.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);

                System.Diagnostics.Debug.WriteLine($"BT Connect WinRT: GetRfcommServices error={result.Error}");

                // RadioNotAvailable / OtherError mean we genuinely can't proceed.
                // Everything else (Success, NotSupported, DeviceNotConnected, …) means the
                // stack contacted or attempted to contact the device — worth polling.
                return result.Error is not BluetoothError.RadioNotAvailable
                                    and not BluetoothError.OtherError;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BT Connect WinRT fallback exception: {ex.Message}");
                return false;
            }
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
