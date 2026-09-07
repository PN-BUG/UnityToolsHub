using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityToolsHub.JoystickIcons;

[ToolInfo("手柄图标绑定", "UI工具",
    Description = "配置不同手柄的设备名匹配规则，以及按钮 ID 对应的 Sprite。配置保存在独立的 ScriptableObject 中。",
    Icon = "🎮",
    Tags = new[] { "手柄", "Joystick", "Gamepad", "按钮", "图标", "Sprite" },
    Priority = 20)]
public sealed class JoystickIconBindingWindow : EditorWindow
{
    private const float SidebarWidth = 172f;
    private const float ButtonIdWidth = 180f;

    private static readonly string[] CommonButtonIds =
    {
        "South", "East", "West", "North",
        "LeftShoulder", "RightShoulder",
        "LeftTrigger", "RightTrigger",
        "LeftStick", "LeftStickPress",
        "RightStick", "RightStickPress",
        "Dpad", "DpadUp", "DpadDown", "DpadLeft", "DpadRight",
        "View", "Menu"
    };

    private JoystickIconDatabase database;
    private SerializedObject serializedDatabase;
    private Vector2 profileScrollPosition;
    private Vector2 detailScrollPosition;
    private int selectedProfileIndex;
    private bool keywordsExpanded = true;
    private string bindingSearch = string.Empty;
    private string previewDeviceName = "Xbox Wireless Controller";
    private string previewButtonId = "South";

    private GUIStyle profileNameStyle;
    private GUIStyle selectedProfileNameStyle;
    private GUIStyle profileCountStyle;
    private GUIStyle selectedProfileCountStyle;
    private GUIStyle pageTitleStyle;
    private GUIStyle pageSubtitleStyle;
    private GUIStyle sectionTitleStyle;
    private GUIStyle statusBadgeStyle;

    private static Color AccentColor => EditorGUIUtility.isProSkin
        ? new Color(0.28f, 0.60f, 1f)
        : new Color(0.15f, 0.43f, 0.82f);

    private static Color PanelColor => EditorGUIUtility.isProSkin
        ? new Color(0.155f, 0.165f, 0.185f)
        : new Color(0.91f, 0.92f, 0.94f);

    private static Color RowColor => EditorGUIUtility.isProSkin
        ? new Color(0.20f, 0.21f, 0.23f)
        : new Color(0.95f, 0.955f, 0.965f);

    [MenuItem("UnityToolsHub/手柄图标绑定", priority = 220)]
    public static void Open()
    {
        var window = GetWindow<JoystickIconBindingWindow>("手柄图标绑定");
        window.minSize = new Vector2(760f, 500f);
        window.Show();
    }

    private void OnEnable()
    {
        if (database == null && Selection.activeObject is JoystickIconDatabase selected)
        {
            SetDatabase(selected);
        }
    }

    private void OnGUI()
    {
        EnsureStyles();
        DrawHeader();
        DrawPageHeader();
        if (database == null)
        {
            DrawEmptyState();
            return;
        }

        EnsureSerializedObject();
        serializedDatabase.Update();

        var profiles = serializedDatabase.FindProperty("profiles");
        EditorGUILayout.Space(5f);
        EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
        GUILayout.Space(6f);
        DrawProfileSidebar(profiles);
        GUILayout.Space(6f);
        DrawProfileDetails(profiles);
        GUILayout.Space(6f);
        EditorGUILayout.EndHorizontal();

        if (serializedDatabase.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(database);
        }

        EditorGUILayout.Space(3f);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(6f);
        DrawPreviewBar();
        GUILayout.Space(6f);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(5f);
    }

    private void EnsureStyles()
    {
        if (profileNameStyle != null)
        {
            return;
        }

        profileNameStyle = new GUIStyle(EditorStyles.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip
        };
        selectedProfileNameStyle = new GUIStyle(profileNameStyle);
        selectedProfileNameStyle.normal.textColor = Color.white;

        profileCountStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft
        };
        selectedProfileCountStyle = new GUIStyle(profileCountStyle);
        selectedProfileCountStyle.normal.textColor = new Color(0.88f, 0.94f, 1f);

        pageTitleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleLeft
        };
        pageSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft
        };
        sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft
        };
        statusBadgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = EditorGUIUtility.isProSkin ? Color.white : new Color(0.12f, 0.28f, 0.5f) }
        };
    }

    private void DrawPageHeader()
    {
        var rect = GUILayoutUtility.GetRect(0f, 54f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, PanelColor);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.55f));

        var titleRect = new Rect(rect.x + 14f, rect.y + 8f, rect.width - 28f, 22f);
        var subtitleRect = new Rect(rect.x + 14f, rect.y + 30f, rect.width - 28f, 17f);
        GUI.Label(titleRect, "手柄图标库", pageTitleStyle);
        GUI.Label(subtitleRect, "按设备类型维护按钮与 Sprite 映射，未配置的按钮会回退到通用手柄。", pageSubtitleStyle);
    }

    private void DrawEmptyState()
    {
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(380f));
        GUILayout.Space(12f);
        EditorGUILayout.LabelField("尚未选择配置", pageTitleStyle, GUILayout.Height(26f));
        EditorGUILayout.LabelField("选择一个 JoystickIconDatabase，或创建新的配置资产。", pageSubtitleStyle);
        GUILayout.Space(8f);
        if (GUILayout.Button("新建手柄图标配置", GUILayout.Height(30f)))
        {
            CreateDatabase();
        }
        GUILayout.Space(8f);
        EditorGUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("配置", GUILayout.Width(30f));
        var selectedDatabase = (JoystickIconDatabase)EditorGUILayout.ObjectField(
            database, typeof(JoystickIconDatabase), false, GUILayout.MinWidth(220f));
        if (selectedDatabase != database)
        {
            SetDatabase(selectedDatabase);
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("新建配置", EditorStyles.toolbarButton, GUILayout.Width(72f)))
        {
            CreateDatabase();
        }

        using (new EditorGUI.DisabledScope(database == null))
        {
            if (GUILayout.Button("定位", EditorStyles.toolbarButton, GUILayout.Width(44f)))
            {
                Selection.activeObject = database;
                EditorGUIUtility.PingObject(database);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawProfileSidebar(SerializedProperty profiles)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(SidebarWidth), GUILayout.ExpandHeight(true));
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("手柄类型", sectionTitleStyle);
        GUILayout.Label(profiles.arraySize.ToString(), statusBadgeStyle, GUILayout.Width(24f));
        EditorGUILayout.EndHorizontal();
        EditorGUI.DrawRect(GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true)), new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.35f));
        EditorGUILayout.Space(4f);

        profileScrollPosition = EditorGUILayout.BeginScrollView(profileScrollPosition, GUILayout.ExpandHeight(true));
        for (var i = 0; i < profiles.arraySize; i++)
        {
            var profile = profiles.GetArrayElementAtIndex(i);
            var displayName = profile.FindPropertyRelative("displayName").stringValue;
            var deviceType = (JoystickDeviceType)profile.FindPropertyRelative("deviceType").enumValueIndex;
            var bindingCount = profile.FindPropertyRelative("buttonIcons").arraySize;
            var label = string.IsNullOrWhiteSpace(displayName) ? deviceType.ToString() : displayName;

            var itemRect = GUILayoutUtility.GetRect(0f, 46f, GUILayout.ExpandWidth(true));
            var isSelected = i == selectedProfileIndex;
            var isHovered = itemRect.Contains(Event.current.mousePosition);
            var background = isSelected
                ? AccentColor
                : isHovered
                    ? new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.16f)
                    : (i % 2 == 0 ? RowColor : Color.clear);
            EditorGUI.DrawRect(itemRect, background);

            if (GUI.Button(itemRect, GUIContent.none, GUIStyle.none))
            {
                selectedProfileIndex = i;
                detailScrollPosition = Vector2.zero;
                bindingSearch = string.Empty;
                GUI.FocusControl(null);
            }

            if (isSelected)
            {
                EditorGUI.DrawRect(new Rect(itemRect.x, itemRect.y, 3f, itemRect.height), Color.white);
            }

            var nameRect = new Rect(itemRect.x + 10f, itemRect.y + 5f, itemRect.width - 18f, 20f);
            var countRect = new Rect(itemRect.x + 10f, itemRect.y + 25f, itemRect.width - 18f, 16f);
            GUI.Label(nameRect, label, isSelected ? selectedProfileNameStyle : profileNameStyle);
            GUI.Label(countRect, $"{bindingCount} 个绑定", isSelected ? selectedProfileCountStyle : profileCountStyle);
            GUILayout.Space(3f);
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(4f);
        if (GUILayout.Button("＋  补齐手柄类型", GUILayout.Height(28f)))
        {
            AddMissingProfiles(profiles);
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawProfileDetails(SerializedProperty profiles)
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (profiles.arraySize == 0)
        {
            EditorGUILayout.HelpBox("尚未配置手柄类型。点击左下角“补齐手柄类型”生成默认配置。", MessageType.Warning);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
            return;
        }

        selectedProfileIndex = Mathf.Clamp(selectedProfileIndex, 0, profiles.arraySize - 1);
        var profile = profiles.GetArrayElementAtIndex(selectedProfileIndex);
        var displayName = profile.FindPropertyRelative("displayName");
        var deviceType = profile.FindPropertyRelative("deviceType");

        detailScrollPosition = EditorGUILayout.BeginScrollView(detailScrollPosition, GUILayout.ExpandHeight(true));
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        DrawSectionTitle("基础信息", "用于编辑器展示和运行时设备分类");
        EditorGUILayout.Space(3f);
        EditorGUILayout.PropertyField(displayName, new GUIContent("显示名称"));
        EditorGUILayout.PropertyField(deviceType, new GUIContent("设备类型"));
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4f);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        keywordsExpanded = EditorGUILayout.Foldout(
            keywordsExpanded,
            $"设备名匹配  ({profile.FindPropertyRelative("deviceNameKeywords").arraySize})",
            true,
            EditorStyles.foldoutHeader);
        if (keywordsExpanded)
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.HelpBox("设备名包含任一关键词时匹配该类型，不区分大小写。越具体的关键词越可靠。", MessageType.None);
            DrawStringList(profile.FindPropertyRelative("deviceNameKeywords"));
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4f);
        var keywords = profile.FindPropertyRelative("deviceNameKeywords");
        if (keywords.arraySize == 0 && (JoystickDeviceType)deviceType.enumValueIndex != JoystickDeviceType.Generic)
        {
            EditorGUILayout.HelpBox("该手柄类型没有设备名关键词，因此不会被自动识别。", MessageType.Warning);
        }

        var bindings = profile.FindPropertyRelative("buttonIcons");
        DrawBindings(bindings);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawSectionTitle(string title, string subtitle)
    {
        var rect = GUILayoutUtility.GetRect(0f, 30f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + 3f, 3f, 24f), AccentColor);
        GUI.Label(new Rect(rect.x + 10f, rect.y, rect.width - 10f, 18f), title, sectionTitleStyle);
        GUI.Label(new Rect(rect.x + 10f, rect.y + 16f, rect.width - 10f, 14f), subtitle, pageSubtitleStyle);
    }

    private static void DrawStringList(SerializedProperty list)
    {
        var removeIndex = -1;
        if (list.arraySize == 0)
        {
            EditorGUILayout.LabelField("暂无关键词", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(22f));
        }

        for (var i = 0; i < list.arraySize; i++)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label((i + 1).ToString(), EditorStyles.miniLabel, GUILayout.Width(18f));
            EditorGUILayout.PropertyField(list.GetArrayElementAtIndex(i), GUIContent.none);
            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(26f), GUILayout.Height(20f)))
            {
                removeIndex = i;
            }
            EditorGUILayout.EndHorizontal();
        }

        if (removeIndex >= 0)
        {
            list.DeleteArrayElementAtIndex(removeIndex);
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("＋  添加关键词", GUILayout.Width(108f), GUILayout.Height(24f)))
        {
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).stringValue = string.Empty;
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawBindings(SerializedProperty bindings)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        DrawSectionTitle("按钮与图标", "按钮 ID 不区分大小写；相同设备内请保持唯一");
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label($"共 {bindings.arraySize} 项", statusBadgeStyle, GUILayout.Width(58f));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("补齐常用按钮", GUILayout.Width(100f), GUILayout.Height(24f)))
        {
            AddCommonBindings(bindings);
        }
        if (GUILayout.Button("＋  添加按钮", GUILayout.Width(88f), GUILayout.Height(24f)))
        {
            AddBinding(bindings, "NewButton");
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("筛选", GUILayout.Width(30f));
        var searchStyle = GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.textField;
        bindingSearch = GUILayout.TextField(bindingSearch, searchStyle, GUILayout.ExpandWidth(true));
        if (!string.IsNullOrEmpty(bindingSearch) && GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(24f)))
        {
            bindingSearch = string.Empty;
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(25f);
        EditorGUILayout.LabelField("按钮 ID", EditorStyles.miniBoldLabel, GUILayout.Width(ButtonIdWidth));
        EditorGUILayout.LabelField("预览", EditorStyles.miniBoldLabel, GUILayout.Width(54f));
        EditorGUILayout.LabelField("Sprite 资源", EditorStyles.miniBoldLabel);
        GUILayout.Space(30f);
        EditorGUILayout.EndHorizontal();

        var removeIndex = -1;
        var visibleCount = 0;
        var duplicateIds = FindDuplicateButtonIds(bindings);
        for (var i = 0; i < bindings.arraySize; i++)
        {
            var binding = bindings.GetArrayElementAtIndex(i);
            var buttonId = binding.FindPropertyRelative("buttonId");
            if (!string.IsNullOrWhiteSpace(bindingSearch) &&
                buttonId.stringValue.IndexOf(bindingSearch, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            visibleCount++;
            var rowRect = GUILayoutUtility.GetRect(0f, 56f, GUILayout.ExpandWidth(true));
            var isHovered = rowRect.Contains(Event.current.mousePosition);
            var rowBackground = isHovered
                ? new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.12f)
                : (visibleCount % 2 == 0 ? RowColor : new Color(RowColor.r, RowColor.g, RowColor.b, 0.45f));
            EditorGUI.DrawRect(rowRect, rowBackground);

            if (duplicateIds.Contains(buttonId.stringValue))
            {
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 3f, rowRect.height), new Color(1f, 0.55f, 0.2f));
            }

            var contentY = rowRect.y + 7f;
            var indexRect = new Rect(rowRect.x + 7f, contentY + 6f, 22f, 30f);
            var buttonRect = new Rect(indexRect.xMax, contentY + 6f, ButtonIdWidth, 30f);
            var previewRect = new Rect(buttonRect.xMax + 8f, contentY, 42f, 42f);
            var removeRect = new Rect(rowRect.xMax - 31f, contentY + 9f, 24f, 24f);
            var iconRect = new Rect(previewRect.xMax + 8f, contentY + 6f, removeRect.x - previewRect.xMax - 16f, 30f);

            GUI.Label(indexRect, (i + 1).ToString("00"), profileCountStyle);

            var previousColor = GUI.color;
            if (duplicateIds.Contains(buttonId.stringValue))
            {
                GUI.color = new Color(1f, 0.72f, 0.45f);
            }
            EditorGUI.PropertyField(buttonRect, buttonId, GUIContent.none);
            GUI.color = previousColor;

            var icon = binding.FindPropertyRelative("icon");
            DrawSpriteThumbnail(previewRect, icon.objectReferenceValue as Sprite);
            EditorGUI.PropertyField(iconRect, icon, GUIContent.none);

            if (GUI.Button(removeRect, "×", EditorStyles.miniButton))
            {
                removeIndex = i;
            }
        }

        if (removeIndex >= 0)
        {
            bindings.DeleteArrayElementAtIndex(removeIndex);
        }

        if (bindings.arraySize == 0)
        {
            EditorGUILayout.HelpBox("还没有按钮绑定。可以添加单个按钮，或补齐常用按钮。", MessageType.Info);
        }
        else if (visibleCount == 0)
        {
            EditorGUILayout.HelpBox("没有符合筛选条件的按钮。", MessageType.None);
        }

        if (duplicateIds.Count > 0)
        {
            EditorGUILayout.HelpBox("橙色按钮 ID 存在重复；运行时会使用列表中的第一条。", MessageType.Warning);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawSpriteThumbnail(Rect rect, Sprite sprite)
    {
        EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
            ? new Color(0.105f, 0.11f, 0.12f)
            : new Color(0.82f, 0.83f, 0.85f));
        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

        if (sprite == null)
        {
            GUI.Label(rect, "—", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        var preview = AssetPreview.GetAssetPreview(sprite) ?? AssetPreview.GetMiniThumbnail(sprite);
        if (preview != null)
        {
            var imageRect = new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, rect.height - 6f);
            GUI.DrawTexture(imageRect, preview, ScaleMode.ScaleToFit, true);
        }

        if (AssetPreview.IsLoadingAssetPreview(sprite.GetInstanceID()))
        {
            Repaint();
        }
    }

    private static HashSet<string> FindDuplicateButtonIds(SerializedProperty bindings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < bindings.arraySize; i++)
        {
            var id = JoystickIconDatabase.NormalizeButtonId(
                bindings.GetArrayElementAtIndex(i).FindPropertyRelative("buttonId").stringValue);
            if (!string.IsNullOrWhiteSpace(id) && !seen.Add(id))
            {
                duplicates.Add(id);
            }
        }

        return duplicates;
    }

    private void DrawPreviewBar()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
        EditorGUILayout.BeginHorizontal();
        EditorGUI.DrawRect(GUILayoutUtility.GetRect(3f, 42f, GUILayout.Width(3f)), AccentColor);
        GUILayout.Space(4f);
        EditorGUILayout.BeginVertical(GUILayout.Width(70f));
        GUILayout.Space(2f);
        EditorGUILayout.LabelField("匹配预览", sectionTitleStyle, GUILayout.Width(70f));
        EditorGUILayout.LabelField("实时结果", EditorStyles.miniLabel, GUILayout.Width(70f));
        EditorGUILayout.EndVertical();

        GUILayout.Space(6f);
        EditorGUILayout.BeginVertical();
        GUILayout.Label("设备名称", EditorStyles.miniLabel);
        previewDeviceName = EditorGUILayout.TextField(previewDeviceName, GUILayout.MinWidth(190f));
        EditorGUILayout.EndVertical();

        GUILayout.Space(5f);
        EditorGUILayout.BeginVertical(GUILayout.Width(150f));
        GUILayout.Label("按钮 ID", EditorStyles.miniLabel);
        previewButtonId = EditorGUILayout.TextField(previewButtonId, GUILayout.Width(150f));
        EditorGUILayout.EndVertical();

        var deviceType = database.IdentifyDevice(previewDeviceName);
        database.TryGetIcon(deviceType, previewButtonId, out var sprite);

        GUILayout.Space(6f);
        EditorGUILayout.BeginVertical(GUILayout.Width(108f));
        GUILayout.Label("识别类型", EditorStyles.miniLabel);
        var badgeRect = GUILayoutUtility.GetRect(108f, 20f, GUILayout.Width(108f));
        EditorGUI.DrawRect(badgeRect, new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.22f));
        GUI.Label(badgeRect, deviceType.ToString(), statusBadgeStyle);
        EditorGUILayout.EndVertical();

        GUILayout.Space(6f);
        var previewRect = GUILayoutUtility.GetRect(42f, 42f, GUILayout.Width(42f), GUILayout.Height(42f));
        DrawSpriteThumbnail(previewRect, sprite);

        GUILayout.Space(6f);
        EditorGUILayout.BeginVertical(GUILayout.MinWidth(100f));
        GUILayout.Label("匹配 Sprite", EditorStyles.miniLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(sprite, typeof(Sprite), false, GUILayout.MinWidth(100f));
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private static void AddMissingProfiles(SerializedProperty profiles)
    {
        var existing = new HashSet<JoystickDeviceType>();
        for (var i = 0; i < profiles.arraySize; i++)
        {
            existing.Add((JoystickDeviceType)profiles.GetArrayElementAtIndex(i)
                .FindPropertyRelative("deviceType").enumValueIndex);
        }

        foreach (JoystickDeviceType type in Enum.GetValues(typeof(JoystickDeviceType)))
        {
            if (existing.Contains(type))
            {
                continue;
            }

            profiles.InsertArrayElementAtIndex(profiles.arraySize);
            var profile = profiles.GetArrayElementAtIndex(profiles.arraySize - 1);
            profile.FindPropertyRelative("deviceType").enumValueIndex = (int)type;
            profile.FindPropertyRelative("displayName").stringValue = GetDefaultDisplayName(type);

            var keywords = profile.FindPropertyRelative("deviceNameKeywords");
            keywords.ClearArray();
            foreach (var keyword in GetDefaultKeywords(type))
            {
                keywords.InsertArrayElementAtIndex(keywords.arraySize);
                keywords.GetArrayElementAtIndex(keywords.arraySize - 1).stringValue = keyword;
            }

            profile.FindPropertyRelative("buttonIcons").ClearArray();
        }
    }

    private static void AddCommonBindings(SerializedProperty bindings)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < bindings.arraySize; i++)
        {
            existing.Add(JoystickIconDatabase.NormalizeButtonId(
                bindings.GetArrayElementAtIndex(i).FindPropertyRelative("buttonId").stringValue));
        }

        foreach (var buttonId in CommonButtonIds)
        {
            if (!existing.Contains(buttonId))
            {
                AddBinding(bindings, buttonId);
            }
        }
    }

    private static void AddBinding(SerializedProperty bindings, string buttonId)
    {
        bindings.InsertArrayElementAtIndex(bindings.arraySize);
        var binding = bindings.GetArrayElementAtIndex(bindings.arraySize - 1);
        binding.FindPropertyRelative("buttonId").stringValue = buttonId;
        binding.FindPropertyRelative("icon").objectReferenceValue = null;
    }

    private static string GetDefaultDisplayName(JoystickDeviceType type)
    {
        switch (type)
        {
            case JoystickDeviceType.Xbox: return "Xbox";
            case JoystickDeviceType.PlayStation: return "PlayStation";
            case JoystickDeviceType.NintendoSwitch: return "Nintendo Switch";
            case JoystickDeviceType.SteamDeck: return "Steam Deck";
            default: return "通用手柄";
        }
    }

    private static IEnumerable<string> GetDefaultKeywords(JoystickDeviceType type)
    {
        switch (type)
        {
            case JoystickDeviceType.Xbox:
                return new[] { "Xbox", "XInput", "Microsoft" };
            case JoystickDeviceType.PlayStation:
                return new[] { "DualSense", "DualShock", "PlayStation", "Sony", "Wireless Controller" };
            case JoystickDeviceType.NintendoSwitch:
                return new[] { "Nintendo", "Switch", "Joy-Con" };
            case JoystickDeviceType.SteamDeck:
                return new[] { "Steam Deck", "Steam Controller" };
            default:
                return Array.Empty<string>();
        }
    }

    private void CreateDatabase()
    {
        var path = EditorUtility.SaveFilePanelInProject(
            "新建手柄图标配置", "JoystickIconDatabase", "asset", "选择配置资产的保存位置");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var asset = CreateInstance<JoystickIconDatabase>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        SetDatabase(asset);

        EnsureSerializedObject();
        serializedDatabase.Update();
        AddMissingProfiles(serializedDatabase.FindProperty("profiles"));
        serializedDatabase.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        Selection.activeObject = asset;
    }

    private void SetDatabase(JoystickIconDatabase value)
    {
        database = value;
        serializedDatabase = database != null ? new SerializedObject(database) : null;
        selectedProfileIndex = 0;
    }

    private void EnsureSerializedObject()
    {
        if (serializedDatabase == null || serializedDatabase.targetObject != database)
        {
            serializedDatabase = new SerializedObject(database);
        }
    }
}
