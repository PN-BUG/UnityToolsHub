using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityToolsHub.JoystickIcons;

[CustomEditor(typeof(JoystickIconSwitch))]
public sealed class JoystickIconSwitchEditor : Editor
{
    private const double ListenTimeoutSeconds = 10d;

    private SerializedProperty targetImageProperty;
    private SerializedProperty databaseProperty;
    private SerializedProperty buttonIdProperty;
    private SerializedProperty fallbackSpriteProperty;
    private SerializedProperty hideImageWhenMissingProperty;
    private SerializedProperty deviceNameOverrideProperty;

    private bool isListening;
    private double listenStartedAt;
    private string listenStatus = string.Empty;

    private void OnEnable()
    {
        targetImageProperty = serializedObject.FindProperty("targetImage");
        databaseProperty = serializedObject.FindProperty("database");
        buttonIdProperty = serializedObject.FindProperty("buttonId");
        fallbackSpriteProperty = serializedObject.FindProperty("fallbackSprite");
        hideImageWhenMissingProperty = serializedObject.FindProperty("hideImageWhenMissing");
        deviceNameOverrideProperty = serializedObject.FindProperty("deviceNameOverride");
    }

    private void OnDisable()
    {
        StopListening();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(targetImageProperty, new GUIContent("目标 Image"));
        EditorGUILayout.PropertyField(databaseProperty, new GUIContent("图标配置"));
        EditorGUILayout.Space(4f);

        DrawButtonBinding();

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("显示设置", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(fallbackSpriteProperty, new GUIContent("缺省图标"));
        EditorGUILayout.PropertyField(hideImageWhenMissingProperty, new GUIContent("无图标时隐藏 Image"));
        EditorGUILayout.PropertyField(deviceNameOverrideProperty, new GUIContent("设备名覆盖"));

        var changed = serializedObject.ApplyModifiedProperties();
        if (changed && Application.isPlaying)
        {
            ((JoystickIconSwitch)target).Refresh();
        }

        if (Application.isPlaying)
        {
            DrawRuntimeState();
        }
    }

    private void DrawButtonBinding()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("按钮绑定", EditorStyles.boldLabel);

        var database = databaseProperty.objectReferenceValue as JoystickIconDatabase;
        buttonIdProperty.stringValue = JoystickIconDatabase.NormalizeButtonId(buttonIdProperty.stringValue);
        var buttonIds = CollectButtonIds(database, buttonIdProperty.stringValue);
        var labels = buttonIds.Select(GetButtonDisplayName).ToArray();
        var selectedIndex = Mathf.Max(0, buttonIds.FindIndex(
            id => string.Equals(id, buttonIdProperty.stringValue, StringComparison.OrdinalIgnoreCase)));

        EditorGUI.BeginChangeCheck();
        selectedIndex = EditorGUILayout.Popup("绑定按钮", selectedIndex, labels);
        if (EditorGUI.EndChangeCheck() && selectedIndex >= 0 && selectedIndex < buttonIds.Count)
        {
            buttonIdProperty.stringValue = buttonIds[selectedIndex];
        }

        DrawSelectedIconPreview(database, buttonIdProperty.stringValue);

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(!Application.isPlaying && !isListening))
        {
            if (GUILayout.Button(isListening ? "取消监听" : "监听下一次手柄输入", GUILayout.Height(27f)))
            {
                if (isListening)
                {
                    StopListening("已取消输入监听。");
                }
                else
                {
                    StartListening();
                }
            }
        }

        if (GUILayout.Button("打开图标配置", GUILayout.Width(100f), GUILayout.Height(27f)))
        {
            if (database != null)
            {
                Selection.activeObject = database;
                EditorGUIUtility.PingObject(database);
            }
            JoystickIconBindingWindow.Open();
        }
        EditorGUILayout.EndHorizontal();

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("输入监听需要进入播放模式；开始后请聚焦 Game 视图并按下手柄按钮。", MessageType.Info);
        }
        else if (isListening)
        {
            var remaining = Mathf.Max(0, (float)(ListenTimeoutSeconds - (EditorApplication.timeSinceStartup - listenStartedAt)));
            EditorGUILayout.HelpBox($"正在监听手柄输入… {remaining:0.0}s（优先使用 Rewired）", MessageType.Warning);
            Repaint();
        }
        else if (!string.IsNullOrEmpty(listenStatus))
        {
            EditorGUILayout.HelpBox(listenStatus, MessageType.None);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawSelectedIconPreview(JoystickIconDatabase database, string buttonId)
    {
        EditorGUILayout.Space(5f);
        EditorGUILayout.LabelField($"各控制器图标  ·  {buttonId}", EditorStyles.miniBoldLabel);

        if (database == null)
        {
            EditorGUILayout.HelpBox("请先指定图标配置，随后会在这里显示每种控制器的按钮图片。", MessageType.Info);
            return;
        }

        if (string.IsNullOrWhiteSpace(buttonId))
        {
            EditorGUILayout.HelpBox("请选择要绑定的按钮。", MessageType.Info);
            return;
        }

        var hasActiveDevice = Application.isPlaying ||
                              !string.IsNullOrWhiteSpace(deviceNameOverrideProperty.stringValue);
        var activeDeviceType = Application.isPlaying
            ? ((JoystickIconSwitch)target).ActiveDeviceType
            : database.IdentifyDevice(deviceNameOverrideProperty.stringValue);
        var profiles = database.Profiles.Where(profile => profile != null).ToArray();
        if (profiles.Length == 0)
        {
            EditorGUILayout.HelpBox("图标配置中还没有控制器类型。", MessageType.Warning);
            return;
        }

        var columnCount = EditorGUIUtility.currentViewWidth >= 330f ? 2 : 1;
        const float gap = 4f;
        const float cardHeight = 62f;
        for (var startIndex = 0; startIndex < profiles.Length; startIndex += columnCount)
        {
            var rowRect = GUILayoutUtility.GetRect(0f, cardHeight, GUILayout.ExpandWidth(true));
            var cardWidth = (rowRect.width - gap * (columnCount - 1)) / columnCount;
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var profileIndex = startIndex + columnIndex;
                if (profileIndex >= profiles.Length)
                {
                    break;
                }

                var cardRect = new Rect(
                    rowRect.x + columnIndex * (cardWidth + gap),
                    rowRect.y,
                    cardWidth,
                    cardHeight);
                DrawControllerIconCard(
                    cardRect,
                    database,
                    profiles[profileIndex],
                    buttonId,
                    hasActiveDevice && profiles[profileIndex].DeviceType == activeDeviceType);
            }
            GUILayout.Space(2f);
        }
    }

    private void DrawControllerIconCard(
        Rect cardRect,
        JoystickIconDatabase database,
        JoystickIconProfile profile,
        string buttonId,
        bool isActive)
    {
        var hasDirectBinding = profile.TryGetIcon(buttonId, out var sprite);
        if (!hasDirectBinding)
        {
            database.TryGetIcon(profile.DeviceType, buttonId, out sprite);
        }

        var usesGenericFallback = sprite != null &&
                                  !hasDirectBinding &&
                                  profile.DeviceType != JoystickDeviceType.Generic;
        var background = EditorGUIUtility.isProSkin
            ? new Color(0.16f, 0.17f, 0.19f)
            : new Color(0.88f, 0.89f, 0.91f);
        EditorGUI.DrawRect(cardRect, background);
        GUI.Box(cardRect, GUIContent.none, EditorStyles.helpBox);

        if (isActive)
        {
            EditorGUI.DrawRect(new Rect(cardRect.x + 1f, cardRect.y + 2f, 3f, cardRect.height - 4f),
                new Color(0.22f, 0.62f, 1f));
        }

        var previewRect = new Rect(cardRect.x + 8f, cardRect.y + 9f, 44f, 44f);
        DrawSpritePreview(previewRect, sprite);

        var textX = previewRect.xMax + 7f;
        var textWidth = Mathf.Max(20f, cardRect.xMax - textX - 6f);
        var displayName = string.IsNullOrWhiteSpace(profile.DisplayName)
            ? profile.DeviceType.ToString()
            : profile.DisplayName;
        GUI.Label(new Rect(textX, cardRect.y + 7f, textWidth, 17f), displayName, EditorStyles.miniBoldLabel);
        GUI.Label(
            new Rect(textX, cardRect.y + 24f, textWidth, 16f),
            sprite != null ? sprite.name : "未配置图片",
            EditorStyles.miniLabel);

        var status = usesGenericFallback ? "通用回退" : isActive ? "当前控制器" : string.Empty;
        if (!string.IsNullOrEmpty(status))
        {
            var statusStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = usesGenericFallback
                    ? new Color(0.95f, 0.67f, 0.24f)
                    : new Color(0.28f, 0.7f, 1f) }
            };
            GUI.Label(new Rect(textX, cardRect.y + 41f, textWidth, 15f), status, statusStyle);
        }
    }

    private void DrawSpritePreview(Rect previewRect, Sprite sprite)
    {
        EditorGUI.DrawRect(previewRect, EditorGUIUtility.isProSkin
            ? new Color(0.08f, 0.085f, 0.095f)
            : new Color(0.76f, 0.77f, 0.79f));
        GUI.Box(previewRect, GUIContent.none, EditorStyles.helpBox);

        if (sprite == null)
        {
            GUI.Label(previewRect, "—", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        var preview = AssetPreview.GetAssetPreview(sprite) ?? AssetPreview.GetMiniThumbnail(sprite);
        if (preview != null)
        {
            var imageRect = new Rect(previewRect.x + 3f, previewRect.y + 3f, previewRect.width - 6f, previewRect.height - 6f);
            GUI.DrawTexture(imageRect, preview, ScaleMode.ScaleToFit, true);
        }

        if (AssetPreview.IsLoadingAssetPreview(sprite.GetInstanceID()))
        {
            Repaint();
        }
    }

    private void DrawRuntimeState()
    {
        var switcher = (JoystickIconSwitch)target;
        EditorGUILayout.Space(5f);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("运行时状态", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("当前设备", string.IsNullOrEmpty(switcher.ActiveDeviceName) ? "未检测到" : switcher.ActiveDeviceName);
        EditorGUILayout.LabelField("设备类型", switcher.ActiveDeviceType.ToString());
        if (GUILayout.Button("立即刷新"))
        {
            switcher.Refresh();
        }
        EditorGUILayout.EndVertical();
    }

    private void StartListening()
    {
        if (!Application.isPlaying)
        {
            listenStatus = "请先进入播放模式再开始监听。";
            return;
        }

        isListening = true;
        listenStartedAt = EditorApplication.timeSinceStartup;
        listenStatus = string.Empty;
        EditorApplication.update -= PollInput;
        EditorApplication.update += PollInput;
        Repaint();
    }

    private void StopListening(string status = null)
    {
        isListening = false;
        EditorApplication.update -= PollInput;
        if (status != null)
        {
            listenStatus = status;
        }
        Repaint();
    }

    private void PollInput()
    {
        if (!isListening || target == null || !Application.isPlaying)
        {
            StopListening("输入监听已停止。");
            return;
        }

        if (EditorApplication.timeSinceStartup - listenStartedAt >= ListenTimeoutSeconds)
        {
            StopListening("监听超时，未检测到手柄按钮输入。请聚焦 Game 视图后重试。");
            return;
        }

        var database = databaseProperty.objectReferenceValue as JoystickIconDatabase;
        if (!RewiredPollingBridge.TryPollButton(out var elementName, out var controllerName) &&
            !TryPollUnityJoystick(out elementName, out controllerName))
        {
            return;
        }

        var buttonId = MapInputToButtonId(elementName, controllerName, database);
        if (string.IsNullOrEmpty(buttonId))
        {
            listenStatus = $"检测到“{elementName}”，但无法映射到按钮 ID，继续监听…";
            Repaint();
            return;
        }

        serializedObject.Update();
        buttonIdProperty.stringValue = buttonId;
        serializedObject.ApplyModifiedProperties();
        ((JoystickIconSwitch)target).Refresh();
        StopListening($"已绑定：{buttonId}（{controllerName} / {elementName}）");
    }

    private static List<string> CollectButtonIds(JoystickIconDatabase database, string currentButtonId)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var standardId in StandardButtonIds)
        {
            AddButtonId(result, seen, standardId);
        }

        if (database != null)
        {
            for (var profileIndex = 0; profileIndex < database.Profiles.Count; profileIndex++)
            {
                var profile = database.Profiles[profileIndex];
                if (profile == null)
                {
                    continue;
                }

                for (var bindingIndex = 0; bindingIndex < profile.ButtonIcons.Count; bindingIndex++)
                {
                    var binding = profile.ButtonIcons[bindingIndex];
                    if (binding != null)
                    {
                        AddButtonId(result, seen, JoystickIconDatabase.NormalizeButtonId(binding.ButtonId));
                    }
                }
            }
        }

        AddButtonId(result, seen, currentButtonId);

        if (result.Count == 0)
        {
            result.Add("South");
        }

        return result;
    }

    private static void AddButtonId(List<string> result, HashSet<string> seen, string buttonId)
    {
        if (!string.IsNullOrWhiteSpace(buttonId) && seen.Add(buttonId))
        {
            result.Add(buttonId);
        }
    }

    private static string GetButtonDisplayName(string buttonId)
    {
        switch (buttonId)
        {
            case "South": return "South（下方主键）";
            case "East": return "East（右侧主键）";
            case "West": return "West（左侧主键）";
            case "North": return "North（上方主键）";
            case "LeftShoulder": return "Left Shoulder（左肩键）";
            case "RightShoulder": return "Right Shoulder（右肩键）";
            case "LeftTrigger": return "Left Trigger（左扳机）";
            case "RightTrigger": return "Right Trigger（右扳机）";
            case "LeftStickPress": return "Left Stick Press（按下左摇杆）";
            case "RightStickPress": return "Right Stick Press（按下右摇杆）";
            case "DpadUp": return "D-pad Up（十字键上）";
            case "DpadDown": return "D-pad Down（十字键下）";
            case "DpadLeft": return "D-pad Left（十字键左）";
            case "DpadRight": return "D-pad Right（十字键右）";
            default: return buttonId;
        }
    }

    private static bool TryPollUnityJoystick(out string elementName, out string controllerName)
    {
        controllerName = FirstConnectedJoystickName();
        for (var index = 0; index < 20; index++)
        {
            var keyCode = (KeyCode)((int)KeyCode.JoystickButton0 + index);
            if (Input.GetKeyDown(keyCode))
            {
                elementName = $"Joystick Button {index}";
                return true;
            }
        }

        elementName = string.Empty;
        return false;
    }

    private static string FirstConnectedJoystickName()
    {
        var names = Input.GetJoystickNames();
        if (names == null)
        {
            return "Unity Joystick";
        }

        for (var i = 0; i < names.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(names[i]))
            {
                return names[i];
            }
        }

        return "Unity Joystick";
    }

    private static string MapInputToButtonId(string elementName, string controllerName, JoystickIconDatabase database)
    {
        if (string.IsNullOrWhiteSpace(elementName))
        {
            return null;
        }

        var normalized = Normalize(elementName);
        if (normalized.Contains("dpadup")) return "DpadUp";
        if (normalized.Contains("dpaddown")) return "DpadDown";
        if (normalized.Contains("dpadleft")) return "DpadLeft";
        if (normalized.Contains("dpadright")) return "DpadRight";
        if (ContainsAny(normalized, "leftstickbutton", "leftstickpress", "leftthumb", "l3")) return "LeftStickPress";
        if (ContainsAny(normalized, "rightstickbutton", "rightstickpress", "rightthumb", "r3")) return "RightStickPress";
        if (normalized.Contains("leftstick")) return "LeftStick";
        if (normalized.Contains("rightstick")) return "RightStick";
        if (ContainsAny(normalized, "leftshoulder", "leftbumper", "l1")) return "LeftShoulder";
        if (ContainsAny(normalized, "rightshoulder", "rightbumper", "r1")) return "RightShoulder";
        if (ContainsAny(normalized, "lefttrigger", "l2")) return "LeftTrigger";
        if (ContainsAny(normalized, "righttrigger", "r2")) return "RightTrigger";
        if (ContainsAny(normalized, "start", "menu", "options", "plus")) return "Menu";
        if (ContainsAny(normalized, "back", "view", "share", "create", "minus")) return "View";

        if (TryGetNativeButtonIndex(normalized, out var nativeIndex))
        {
            return MapNativeButtonIndex(nativeIndex);
        }

        var deviceType = database != null
            ? database.IdentifyDevice(controllerName)
            : JoystickDeviceType.Generic;
        if (deviceType == JoystickDeviceType.NintendoSwitch)
        {
            if (IsExactButton(normalized, "a")) return "East";
            if (IsExactButton(normalized, "b")) return "South";
            if (IsExactButton(normalized, "x")) return "North";
            if (IsExactButton(normalized, "y")) return "West";
        }
        else
        {
            if (IsExactButton(normalized, "a") || normalized.Contains("cross")) return "South";
            if (IsExactButton(normalized, "b") || normalized.Contains("circle")) return "East";
            if (IsExactButton(normalized, "x") || normalized.Contains("square")) return "West";
            if (IsExactButton(normalized, "y") || normalized.Contains("triangle")) return "North";
        }

        if (normalized.Contains("south")) return "South";
        if (normalized.Contains("east")) return "East";
        if (normalized.Contains("west")) return "West";
        if (normalized.Contains("north")) return "North";
        return null;
    }

    private static bool TryGetNativeButtonIndex(string normalized, out int index)
    {
        const string joystickPrefix = "joystickbutton";
        const string buttonPrefix = "button";
        if (normalized.StartsWith(joystickPrefix, StringComparison.Ordinal) &&
            int.TryParse(normalized.Substring(joystickPrefix.Length), out index))
        {
            return true;
        }

        if (normalized.StartsWith(buttonPrefix, StringComparison.Ordinal) &&
            int.TryParse(normalized.Substring(buttonPrefix.Length), out index))
        {
            return true;
        }

        index = -1;
        return false;
    }

    private static string MapNativeButtonIndex(int index)
    {
        switch (index)
        {
            case 0: return "South";
            case 1: return "East";
            case 2: return "West";
            case 3: return "North";
            case 4: return "LeftShoulder";
            case 5: return "RightShoulder";
            case 6: return "View";
            case 7: return "Menu";
            case 8: return "LeftStickPress";
            case 9: return "RightStickPress";
            default: return null;
        }
    }

    private static bool IsExactButton(string normalized, string button)
    {
        return normalized == button || normalized == "button" + button;
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        for (var i = 0; i < candidates.Length; i++)
        {
            if (value.Contains(candidates[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static readonly string[] StandardButtonIds =
    {
        "South", "East", "West", "North",
        "LeftShoulder", "RightShoulder", "LeftTrigger", "RightTrigger",
        "LeftStickPress", "RightStickPress",
        "DpadUp", "DpadDown", "DpadLeft", "DpadRight", "View", "Menu"
    };
}

internal static class RewiredPollingBridge
{
    private static bool initialized;
    private static PropertyInfo isReadyProperty;
    private static PropertyInfo controllersProperty;
    private static PropertyInfo pollingProperty;
    private static MethodInfo pollButtonMethod;
    private static object joystickControllerType;

    public static bool TryPollButton(out string elementName, out string controllerName)
    {
        elementName = string.Empty;
        controllerName = string.Empty;

        try
        {
            EnsureInitialized();
            if (isReadyProperty == null || !(bool)isReadyProperty.GetValue(null))
            {
                return false;
            }

            var controllers = controllersProperty?.GetValue(null);
            var polling = controllers != null ? pollingProperty?.GetValue(controllers) : null;
            if (polling == null || pollButtonMethod == null || joystickControllerType == null)
            {
                return false;
            }

            var pollingInfo = pollButtonMethod.Invoke(polling, new[] { joystickControllerType });
            if (pollingInfo == null || !GetProperty<bool>(pollingInfo, "success"))
            {
                return false;
            }

            elementName = GetProperty<string>(pollingInfo, "elementIdentifierName") ?? string.Empty;
            controllerName = GetProperty<string>(pollingInfo, "controllerName") ?? "Rewired Joystick";
            return !string.IsNullOrWhiteSpace(elementName);
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        var reInputType = FindType("Rewired.ReInput");
        var controllerType = FindType("Rewired.ControllerType");
        if (reInputType == null || controllerType == null)
        {
            return;
        }

        const BindingFlags staticFlags = BindingFlags.Public | BindingFlags.Static;
        isReadyProperty = reInputType.GetProperty("isReady", staticFlags);
        controllersProperty = reInputType.GetProperty("controllers", staticFlags);
        var controllersType = controllersProperty?.PropertyType;
        pollingProperty = controllersType?.GetProperty("polling", BindingFlags.Public | BindingFlags.Instance);
        var pollingType = pollingProperty?.PropertyType;
        var pollingMethods = pollingType?.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        pollButtonMethod = pollingMethods?
            .FirstOrDefault(method => method.Name == "PollAllControllersOfTypeForFirstElementDown" &&
                                      method.GetParameters().Length == 1 &&
                                      method.GetParameters()[0].ParameterType == controllerType)
            ?? pollingMethods?.FirstOrDefault(method => method.Name == "PollAllControllersOfTypeForFirstButtonDown" &&
                                                       method.GetParameters().Length == 1 &&
                                                       method.GetParameters()[0].ParameterType == controllerType);
        joystickControllerType = Enum.Parse(controllerType, "Joystick");
    }

    private static T GetProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property != null ? (T)property.GetValue(instance) : default;
    }

    private static Type FindType(string fullName)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var i = 0; i < assemblies.Length; i++)
        {
            var type = assemblies[i].GetType(fullName, false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }
}
