using System;
using UnityEngine;
using UnityEngine.UI;

namespace UnityToolsHub.JoystickIcons
{
    /// <summary>
    /// Switches a uGUI Image sprite using a button id and the connected joystick name.
    /// It deliberately uses only Unity APIs so the component does not depend on Rewired
    /// or any project-specific input framework.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class JoystickIconSwitch : MonoBehaviour
    {
        [SerializeField] private Image targetImage;
        [SerializeField] private JoystickIconDatabase database;
        [SerializeField] private string buttonId = "South";
        [SerializeField] private Sprite fallbackSprite;
        [SerializeField] private bool hideImageWhenMissing;
        [Tooltip("Optional device name used instead of Input.GetJoystickNames. Useful for previews and input-system adapters.")]
        [SerializeField] private string deviceNameOverride;

        private string activeDeviceName = string.Empty;
        private JoystickDeviceType activeDeviceType = JoystickDeviceType.Generic;

        /// <summary>Replaceable in tests or by an input-system adapter. Reset to null to use Unity Input.</summary>
        public static Func<string[]> JoystickNamesProvider { get; set; }

        public Image TargetImage => targetImage;
        public JoystickIconDatabase Database => database;
        public string ButtonId => buttonId;
        public string ActiveDeviceName => activeDeviceName;
        public JoystickDeviceType ActiveDeviceType => activeDeviceType;

        private void Reset()
        {
            targetImage = GetComponent<Image>();
        }

        private void Awake()
        {
            if (targetImage == null)
            {
                targetImage = GetComponent<Image>();
            }
        }

        private void OnEnable()
        {
            JoystickIconDeviceEvents.ActiveDeviceChanged += OnActiveDeviceChanged;
            Refresh();
        }

        private void OnDisable()
        {
            JoystickIconDeviceEvents.ActiveDeviceChanged -= OnActiveDeviceChanged;
        }

        [ContextMenu("Refresh Joystick Icon")]
        public void Refresh()
        {
            if (targetImage == null)
            {
                targetImage = GetComponent<Image>();
            }

            activeDeviceName = ResolveDeviceName();
            activeDeviceType = database != null
                ? database.IdentifyDevice(activeDeviceName)
                : JoystickDeviceType.Generic;

            Sprite sprite = null;
            if (database != null)
            {
                database.TryGetIcon(activeDeviceType, buttonId, out sprite);
            }

            ApplySprite(sprite != null ? sprite : fallbackSprite);
        }

        public void SetButton(string newButtonId)
        {
            buttonId = newButtonId ?? string.Empty;
            Refresh();
        }

        public void SetDeviceNameOverride(string deviceName)
        {
            deviceNameOverride = deviceName ?? string.Empty;
            Refresh();
        }

        public void ClearDeviceNameOverride()
        {
            deviceNameOverride = string.Empty;
            Refresh();
        }

        private void OnActiveDeviceChanged(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceNameOverride))
            {
                Refresh();
            }
        }

        private string ResolveDeviceName()
        {
            if (!string.IsNullOrWhiteSpace(deviceNameOverride))
            {
                return deviceNameOverride.Trim();
            }

            if (!string.IsNullOrWhiteSpace(JoystickIconDeviceEvents.ActiveDeviceName))
            {
                return JoystickIconDeviceEvents.ActiveDeviceName;
            }

            string[] names;
            try
            {
                names = JoystickNamesProvider != null ? JoystickNamesProvider() : Input.GetJoystickNames();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return string.Empty;
            }

            if (names == null)
            {
                return string.Empty;
            }

            for (var i = 0; i < names.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(names[i]))
                {
                    return names[i].Trim();
                }
            }

            return string.Empty;
        }

        private void ApplySprite(Sprite sprite)
        {
            if (targetImage == null)
            {
                return;
            }

            if (targetImage.sprite != sprite)
            {
                targetImage.sprite = sprite;
            }

            if (hideImageWhenMissing)
            {
                targetImage.enabled = sprite != null;
            }
        }
    }
}
