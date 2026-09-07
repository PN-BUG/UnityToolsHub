using System;
using UnityEngine;

namespace UnityToolsHub.JoystickIcons
{
    /// <summary>
    /// Input-system adapters publish active joystick changes here. Components only
    /// depend on this event, keeping the icon package independent of Rewired or any
    /// other input framework.
    /// </summary>
    public static class JoystickIconDeviceEvents
    {
        public static event Action<string> ActiveDeviceChanged;

        public static string ActiveDeviceName { get; private set; } = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            ActiveDeviceName = string.Empty;
            ActiveDeviceChanged = null;
        }

        public static void NotifyActiveDeviceChanged(string deviceName)
        {
            var normalizedName = deviceName?.Trim() ?? string.Empty;
            if (string.Equals(ActiveDeviceName, normalizedName, StringComparison.Ordinal))
            {
                return;
            }

            ActiveDeviceName = normalizedName;
            ActiveDeviceChanged?.Invoke(ActiveDeviceName);
        }

        public static void ClearActiveDevice()
        {
            NotifyActiveDeviceChanged(string.Empty);
        }
    }
}
