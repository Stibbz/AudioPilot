using NAudio.CoreAudioApi;
using SwitchAudioDevices.Models;
using System.Runtime.InteropServices;

namespace SwitchAudioDevices.Services
{
    /// <summary>Rich description of a single audio render endpoint.</summary>
    public record AudioEndpointInfo(
        string Id,
        string Name,
        bool   IsDefault,
        bool   IsBluetooth,
        bool   IsBluetoothConnected,
        ulong  BluetoothAddress);

    public class AudioService : IDisposable
    {
        private readonly MMDeviceEnumerator _enumerator = new();
        private readonly BluetoothService   _bt         = new();

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all audio render endpoints the app knows about:
        /// active (wired / BT connected) + paired-but-disconnected BT devices.
        /// Non-BT unplugged devices are excluded.
        /// </summary>
        public IReadOnlyList<AudioEndpointInfo> GetAllEndpoints()
        {
            var defaultId = GetDefaultId();
            var btDevices = _bt.GetPairedDevices();
            var result    = new List<AudioEndpointInfo>();

            // DeviceState.All = Active | Disabled | NotPresent | Unplugged (0xF)
            // On Windows 11 paired-but-disconnected BT devices can appear as
            // either Unplugged (0x8) or NotPresent (0x4) depending on driver/version.
            // Enumerating All and checking .State covers both.
            foreach (var ep in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All))
            {
                var isActive       = ep.State == DeviceState.Active;
                var isDisconnected = ep.State is DeviceState.Unplugged or DeviceState.NotPresent;

                if (!isActive && !isDisconnected) continue;   // skip Disabled

                var btMatch  = MatchBluetooth(ep.FriendlyName, btDevices);
                var isBtByProp = btMatch == null && IsBluetoothByProperty(ep);

                if (isActive)
                {
                    // All active render endpoints — wired, USB, and connected BT
                    bool isBt = btMatch != null || isBtByProp;
                    result.Add(new AudioEndpointInfo(
                        ep.ID, ep.FriendlyName, ep.ID == defaultId,
                        IsBluetooth:          isBt,
                        IsBluetoothConnected: isBt,
                        BluetoothAddress:     btMatch?.Address ?? 0));
                }
                else if (btMatch != null || isBtByProp)
                {
                    // Disconnected BT device — show with "Connect" badge
                    result.Add(new AudioEndpointInfo(
                        ep.ID, ep.FriendlyName, ep.ID == defaultId,
                        IsBluetooth:          true,
                        IsBluetoothConnected: false,
                        BluetoothAddress:     btMatch?.Address ?? 0));
                }
                // Non-BT Unplugged/NotPresent endpoints are excluded
            }

            return result;
        }

        /// <summary>Filtered view used by the main switcher window.</summary>
        public IReadOnlyList<AudioEndpointInfo> GetPlaybackEndpoints(AppSettings settings)
            => GetAllEndpoints()
                .Where(d => !settings.DisabledDeviceIds.Contains(d.Id))
                .ToList();

        public string? GetDefaultDeviceName()
        {
            try { return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)?.FriendlyName; }
            catch { return null; }
        }

        public void SetDefaultDevice(string deviceId)
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            policy.SetDefaultEndpoint(deviceId, Role.Console);
            policy.SetDefaultEndpoint(deviceId, Role.Multimedia);
            policy.SetDefaultEndpoint(deviceId, Role.Communications);
        }

        /// <summary>Triggers a Bluetooth connection for the paired device at the given address.</summary>
        public bool ConnectBluetoothDevice(ulong bluetoothAddress)
            => _bt.ConnectDevice(bluetoothAddress);

        public void Dispose() => _enumerator.Dispose();

        // ── Helpers ─────────────────────────────────────────────────────────────

        private string? GetDefaultId()
        {
            try { return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)?.ID; }
            catch { return null; }
        }

        /// <summary>
        /// Finds the best matching paired BT device for an audio endpoint name.
        /// Matching is bidirectional substring (case-insensitive) so both
        /// "AirPods Pro" ↔ "AirPods Pro Stereo" and exact names work.
        /// </summary>
        private static BluetoothDeviceInfo? MatchBluetooth(
            string endpointName, IReadOnlyList<BluetoothDeviceInfo> btDevices)
            => btDevices
                .Where(bt =>
                    endpointName.Contains(bt.Name, StringComparison.OrdinalIgnoreCase) ||
                    bt.Name.Contains(endpointName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(bt => bt.Name.Length)    // prefer more specific match
                .FirstOrDefault();

        // DEVPKEY_Device_InstanceId  {b3f8fa53-0004-438e-9003-51a46e139bfc}, 2
        // The PnP instance ID for a BT audio device contains "BTHENUM" or "BTHLE".
        private static readonly PropertyKey InstanceIdKey =
            new(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 2);

        /// <summary>
        /// Fallback BT detection: reads the underlying PnP device instance ID from
        /// the audio endpoint's property store and checks for Bluetooth bus prefixes.
        /// Works even when bthprops.cpl enumeration returns nothing.
        /// </summary>
        private static bool IsBluetoothByProperty(MMDevice ep)
        {
            try
            {
                var val = ep.Properties[InstanceIdKey]?.Value?.ToString() ?? string.Empty;
                return val.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
                    || val.Contains("BTHLE",   StringComparison.OrdinalIgnoreCase)
                    || val.Contains("BTH\\",   StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    // ── COM interop for setting default audio endpoint ──────────────────────────

    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string pszDeviceName, IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat(string pszDeviceName, bool bDefault, IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat(string pszDeviceName);
        [PreserveSig] int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr pMixFormat);
        [PreserveSig] int GetProcessingPeriod(string pszDeviceName, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode(string pszDeviceName, IntPtr pMode);
        [PreserveSig] int SetShareMode(string pszDeviceName, IntPtr pMode);
        [PreserveSig] int GetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr pKey, IntPtr pv);
        [PreserveSig] int SetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr pKey, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint(string pszDeviceName, Role role);
        [PreserveSig] int SetEndpointVisibility(string pszDeviceName, bool bVisible);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    class PolicyConfigClient { }
}
