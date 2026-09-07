using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityToolsHub.JoystickIcons
{
    public enum JoystickDeviceType
    {
        Xbox,
        PlayStation,
        NintendoSwitch,
        SteamDeck,
        Generic
    }

    [Serializable]
    public sealed class JoystickButtonIconBinding
    {
        [SerializeField] private string buttonId = "South";
        [SerializeField] private Sprite icon;

        public string ButtonId => buttonId;
        public Sprite Icon => icon;
    }

    [Serializable]
    public sealed class JoystickIconProfile
    {
        [SerializeField] private JoystickDeviceType deviceType = JoystickDeviceType.Generic;
        [SerializeField] private string displayName = "通用手柄";
        [SerializeField] private List<string> deviceNameKeywords = new List<string>();
        [SerializeField] private List<JoystickButtonIconBinding> buttonIcons = new List<JoystickButtonIconBinding>();

        public JoystickDeviceType DeviceType => deviceType;
        public string DisplayName => displayName;
        public IReadOnlyList<string> DeviceNameKeywords => deviceNameKeywords;
        public IReadOnlyList<JoystickButtonIconBinding> ButtonIcons => buttonIcons;

        public bool Matches(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                return false;
            }

            for (var i = 0; i < deviceNameKeywords.Count; i++)
            {
                var keyword = deviceNameKeywords[i];
                if (!string.IsNullOrWhiteSpace(keyword) &&
                    deviceName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetIcon(string buttonId, out Sprite icon)
        {
            buttonId = JoystickIconDatabase.NormalizeButtonId(buttonId);
            for (var i = 0; i < buttonIcons.Count; i++)
            {
                var binding = buttonIcons[i];
                if (binding != null && string.Equals(
                        JoystickIconDatabase.NormalizeButtonId(binding.ButtonId),
                        buttonId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    icon = binding.Icon;
                    return icon != null;
                }
            }

            icon = null;
            return false;
        }
    }

    [CreateAssetMenu(fileName = "JoystickIconDatabase", menuName = "Unity Tools Hub/Joystick Icon Database")]
    public sealed class JoystickIconDatabase : ScriptableObject
    {
        [SerializeField] private List<JoystickIconProfile> profiles = new List<JoystickIconProfile>();

        public IReadOnlyList<JoystickIconProfile> Profiles => profiles;

        /// <summary>
        /// Converts legacy/action-style button names to the position-based IDs used by the icon database.
        /// This keeps old scenes compatible without storing duplicate icon bindings.
        /// </summary>
        public static string NormalizeButtonId(string buttonId)
        {
            if (string.IsNullOrWhiteSpace(buttonId))
            {
                return buttonId;
            }

            switch (buttonId.Trim().ToLowerInvariant())
            {
                case "abutton": return "South";
                case "bbutton": return "East";
                case "xbutton": return "West";
                case "ybutton": return "North";
                case "lbbutton": return "LeftShoulder";
                case "rbbutton": return "RightShoulder";
                case "ltrigger": return "LeftTrigger";
                case "rtrigger": return "RightTrigger";
                case "mainjoystick": return "LeftStick";
                case "secondjoystick": return "RightStick";
                case "crossjoystick": return "Dpad";
                case "start":
                case "esc": return "Menu";
                default: return buttonId.Trim();
            }
        }

        public JoystickDeviceType IdentifyDevice(string deviceName)
        {
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                // Profile order is intentional: users can put a more specific matcher first.
                for (var i = 0; i < profiles.Count; i++)
                {
                    var profile = profiles[i];
                    if (profile != null && profile.DeviceType != JoystickDeviceType.Generic && profile.Matches(deviceName))
                    {
                        return profile.DeviceType;
                    }
                }
            }

            return JoystickDeviceType.Generic;
        }

        public bool TryGetIcon(string deviceName, string buttonId, out Sprite icon)
        {
            return TryGetIcon(IdentifyDevice(deviceName), buttonId, out icon);
        }

        public bool TryGetIcon(JoystickDeviceType deviceType, string buttonId, out Sprite icon)
        {
            if (string.IsNullOrWhiteSpace(buttonId))
            {
                icon = null;
                return false;
            }

            var profile = FindProfile(deviceType);
            if (profile != null && profile.TryGetIcon(buttonId, out icon))
            {
                return true;
            }

            // Every device can inherit missing bindings from Generic.
            if (deviceType != JoystickDeviceType.Generic)
            {
                profile = FindProfile(JoystickDeviceType.Generic);
                if (profile != null && profile.TryGetIcon(buttonId, out icon))
                {
                    return true;
                }
            }

            icon = null;
            return false;
        }

        private JoystickIconProfile FindProfile(JoystickDeviceType deviceType)
        {
            for (var i = 0; i < profiles.Count; i++)
            {
                var profile = profiles[i];
                if (profile != null && profile.DeviceType == deviceType)
                {
                    return profile;
                }
            }

            return null;
        }
    }
}
