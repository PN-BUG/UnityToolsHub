using System;
using System.Collections.Generic;
using UnityEngine;
using UnityToolsHub.JoystickIcons;

public enum PlatformInputDeviceType
{
    KeyboardMouse,
    Mobile,
    Xbox,
    PlayStation,
    NintendoSwitch,
    SteamDeck,
    GenericJoystick
}

[Serializable]
public sealed class PlatformStringReplacement
{
    [SerializeField] private string token;
    [SerializeField] private string text;
    [Tooltip("TMP 文本优先显示这些按钮图标；为空时使用文字。多个图标使用分隔符连接。")]
    [SerializeField] private List<string> tmpIconButtonIds = new List<string>();
    [SerializeField] private string tmpIconSeparator = "/";
    [Tooltip("TMP 文本使用的直接 Sprite 图标，适用于键鼠或移动端按钮。")]
    [SerializeField] private Sprite tmpIconSprite;
    [Tooltip("TMP 内联图标相对于正文的缩放。")]
    [SerializeField] private float tmpIconScale = 1f;
    [SerializeField] private string tmpIconPrefix;
    [SerializeField] private string tmpIconSuffix;

    public string Token => token;
    public string Text => text;
    public IReadOnlyList<string> TmpIconButtonIds => tmpIconButtonIds;
    public string TmpIconSeparator => tmpIconSeparator;
    public Sprite TmpIconSprite => tmpIconSprite;
    public float TmpIconScale => tmpIconScale > 0f ? tmpIconScale : 1f;
    public string TmpIconPrefix => tmpIconPrefix;
    public string TmpIconSuffix => tmpIconSuffix;
}

[Serializable]
public sealed class PlatformStringReplacementProfile
{
    [SerializeField] private PlatformInputDeviceType inputDeviceType;
    [SerializeField] private string displayName;
    [SerializeField] private List<PlatformStringReplacement> replacements = new List<PlatformStringReplacement>();

    public PlatformInputDeviceType InputDeviceType => inputDeviceType;
    public string DisplayName => displayName;
    public IReadOnlyList<PlatformStringReplacement> Replacements => replacements;
}

[CreateAssetMenu(fileName = "PlatformStringReplaceDatabase", menuName = "Unity Tools Hub/Platform String Replace Database")]
public sealed class PlatformStringReplaceDatabase : ScriptableObject
{
    [Tooltip("用于识别手柄类型，并为 TMP 替换取得对应按钮 Sprite。")]
    [SerializeField] private JoystickIconDatabase joystickIconDatabase;
    [SerializeField] private List<PlatformStringReplacementProfile> profiles =
        new List<PlatformStringReplacementProfile>();

    public JoystickIconDatabase JoystickIconDatabase => joystickIconDatabase;
    public IReadOnlyList<PlatformStringReplacementProfile> Profiles => profiles;

    public bool TryGetProfile(PlatformInputDeviceType inputDeviceType,
        out PlatformStringReplacementProfile profile)
    {
        for (var i = 0; i < profiles.Count; i++)
        {
            var candidate = profiles[i];
            if (candidate != null && candidate.InputDeviceType == inputDeviceType)
            {
                profile = candidate;
                return true;
            }
        }

        profile = null;
        return false;
    }
}
