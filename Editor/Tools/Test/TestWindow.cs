using System;
using System.Collections.Generic;
using System.Reflection;
using Nodin;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 测试窗口 —— 聚合展示场景中所有标记了 [Test] 的方法和字段。
/// 快捷键 Ctrl+T 打开/关闭。
/// </summary>
[ToolInfo("测试窗口", "调试工具", Description = "聚合展示场景中所有标记了 [Test] 的方法和字段。\n\n方法显示为可点击按钮，字段显示为可编辑控件。\n支持搜索过滤、按组件分组，快捷键 Ctrl+T 打开/关闭。", Icon = "⌘", Tags = new[] { "测试", "调试", "Test" }, Shortcut = "Ctrl+T")]
public class TestWindow : EditorWindow
{
#if UNITY_6000_0_OR_NEWER
    static int GetEntityKey(UnityEngine.Object obj) => obj.GetEntityId();
#else
    static int GetEntityKey(UnityEngine.Object obj) => obj.GetInstanceID();
#endif

    // ── 数据结构 ──────────────────────────────────────────────
    private class TestMethodEntry
    {
        public string DisplayName;
        public MonoBehaviour Target;
        public MethodInfo Method;
        public ParameterInfo[] Parameters;
        public object[] ParameterValues;
    }

    private class TestFieldEntry
    {
        public string DisplayName;
        public MonoBehaviour Target;
        public FieldInfo Field;
        public bool IsReadOnly;
    }

    private class TestPropertyEntry
    {
        public string DisplayName;
        public MonoBehaviour Target;
        public PropertyInfo Property;
        public bool IsReadOnly;
        public bool HasSetter;
    }

    private class TestTypeMetadata
    {
        public MethodInfo[] Methods;
        public FieldInfo[] Fields;
        public PropertyInfo[] Properties;
    }

    private class TestGroup
    {
        public string GroupName;           // 组件类型名
        public GameObject GameObject;      // 所属 GameObject
        public List<TestMethodEntry> Methods = new List<TestMethodEntry>();
        public List<TestFieldEntry> Fields = new List<TestFieldEntry>();
        public List<TestPropertyEntry> Properties = new List<TestPropertyEntry>();
        public HashSet<MemberInfo> Members = new HashSet<MemberInfo>();
    }

    // ── 状态 ─────────────────────────────────────────────────
    private Vector2 _scrollPos;
    private List<TestGroup> _groups = new List<TestGroup>();
    private Dictionary<TestGroup, bool> _foldouts = new Dictionary<TestGroup, bool>();
    private string _searchFilter = "";
    private Vector2 _scrollPosGroups;
    // 缓存参数值，避免刷新时丢失用户输入
    private Dictionary<string, object[]> _paramCache = new Dictionary<string, object[]>();
    private static readonly Dictionary<Type, TestTypeMetadata> TypeMetadataCache = new Dictionary<Type, TestTypeMetadata>();
    private static bool _typeMetadataInitialized;

    // ── 快捷键注册 ──────────────────────────────────────────
    [MenuItem("UnityToolsHub/测试窗口 %t")]   // Ctrl+T
    public static void ToggleWindow()
    {
        if (HasOpenInstances<TestWindow>())
        {
            GetWindow<TestWindow>().Close();
        }
        else
        {
            ShowWindow();
        }
    }

    [MenuItem("Window/测试窗口")]
    public static void ShowWindow()
    {
        var win = GetWindow<TestWindow>("测试窗口");
        win.minSize = new Vector2(320, 200);
        // OnEnable 中会自动调用 RefreshEntries，无需重复刷新
        win.Show();
    }

    private void OnEnable()
    {
        RefreshEntries();
        // 监听场景变化，标记为脏（防抖，避免频繁 FindObjectsOfType 全场景扫描）
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        EditorApplication.update -= OnEditorUpdate;
    }

    // ── 防抖：层级变化后延迟 1 秒再刷新，避免拖拽/编辑时频繁扫描 ──
    private bool _isDirty;
    private double _dirtyTime;

    private void OnHierarchyChanged()
    {
        // Play Mode 中对象和层级可能频繁变化。自动全场景扫描会阻塞 Editor 主线程，
        // 此时由工具栏“刷新”按钮显式更新即可。
        if (EditorApplication.isPlaying) return;

        _isDirty = true;
        _dirtyTime = EditorApplication.timeSinceStartup;
    }

    private void OnEditorUpdate()
    {
        // 延迟 1 秒后自动刷新（避免编辑过程中频繁 FindObjectsOfType）
        if (_isDirty && EditorApplication.timeSinceStartup - _dirtyTime > 1.0)
        {
            _isDirty = false;
            RefreshEntries();
            Repaint();
        }
    }

    /// <summary>
    /// 保存当前所有方法的参数值到缓存
    /// </summary>
    private void SaveParamCache()
    {
        foreach (var group in _groups)
        {
            foreach (var entry in group.Methods)
            {
                if (entry.Target == null || entry.Parameters.Length == 0) continue;
                string key = $"{GetEntityKey(entry.Target)}_{entry.Method.Name}";
                _paramCache[key] = entry.ParameterValues;
            }
        }
    }

    // ── 刷新数据 ────────────────────────────────────────────
    private void RefreshEntries()
    {
        // 保存当前参数值到缓存
        SaveParamCache();
        
        _groups.Clear();
        _foldouts.Clear();

        EnsureTypeMetadataCache();
        var groupsByTarget = new Dictionary<int, TestGroup>();

        // 只查询实际声明了 [Test] 成员的组件类型，不再扫描场景中的全部 MonoBehaviour。
        foreach (var pair in TypeMetadataCache)
        {
            var metadata = pair.Value;
            foreach (var found in FindObjectsOfType(pair.Key, true))
            {
                if (found is not MonoBehaviour mb || mb == null) continue;
                int targetKey = GetEntityKey(mb);
                if (!groupsByTarget.TryGetValue(targetKey, out var group))
                {
                    group = new TestGroup
                    {
                        GroupName = ObjectNames.NicifyVariableName(mb.GetType().Name),
                        GameObject = mb.gameObject
                    };
                    groupsByTarget.Add(targetKey, group);
                }

                foreach (var method in metadata.Methods)
                {
                    if (!group.Members.Add(method)) continue;
                    var attr = method.GetCustomAttribute<TestAttribute>();

                    var parameters = method.GetParameters();
                    var entry = new TestMethodEntry
                    {
                        DisplayName = attr.Name,
                        Target = mb,
                        Method = method,
                        Parameters = parameters,
                        ParameterValues = new object[parameters.Length]
                    };

                    string cacheKey = $"{targetKey}_{method.Name}";
                    if (_paramCache.TryGetValue(cacheKey, out var cached) && cached.Length == parameters.Length)
                        entry.ParameterValues = cached;
                    else
                    {
                        for (int i = 0; i < parameters.Length; i++)
                            entry.ParameterValues[i] = GetDefaultValue(parameters[i].ParameterType);
                    }

                    group.Methods.Add(entry);
                }

                foreach (var field in metadata.Fields)
                {
                    if (!group.Members.Add(field)) continue;
                    var attr = field.GetCustomAttribute<TestAttribute>();
                    group.Fields.Add(new TestFieldEntry
                    {
                        DisplayName = attr.Name,
                        Target = mb,
                        Field = field,
                        IsReadOnly = field.IsDefined(typeof(Nodin.ReadOnlyAttribute), true)
                    });
                }

                foreach (var prop in metadata.Properties)
                {
                    if (!group.Members.Add(prop)) continue;
                    var attr = prop.GetCustomAttribute<TestAttribute>();
                    group.Properties.Add(new TestPropertyEntry
                    {
                        DisplayName = attr.Name,
                        Target = mb,
                        Property = prop,
                        IsReadOnly = prop.IsDefined(typeof(Nodin.ReadOnlyAttribute), true),
                        HasSetter = prop.GetSetMethod(true) != null
                    });
                }
            }
        }

        foreach (var group in groupsByTarget.Values)
        {
            group.Methods.Sort((a, b) => GetInheritanceDepth(a.Method.DeclaringType).CompareTo(GetInheritanceDepth(b.Method.DeclaringType)));
            group.Fields.Sort((a, b) => GetInheritanceDepth(a.Field.DeclaringType).CompareTo(GetInheritanceDepth(b.Field.DeclaringType)));
            group.Properties.Sort((a, b) => GetInheritanceDepth(a.Property.DeclaringType).CompareTo(GetInheritanceDepth(b.Property.DeclaringType)));
            _groups.Add(group);
            _foldouts[group] = true;
        }
    }

    private static void EnsureTypeMetadataCache()
    {
        if (_typeMetadataInitialized) return;
        _typeMetadataInitialized = true;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (var method in TypeCache.GetMethodsWithAttribute<TestAttribute>())
            AddMetadata(method.DeclaringType, method: method);

        foreach (var field in TypeCache.GetFieldsWithAttribute<TestAttribute>())
            AddMetadata(field.DeclaringType, field: field);

        // TypeCache 没有属性查询接口；这里只扫描各类型自身声明的属性。
        // 继承属性会由声明它的基类收集，避免基类和派生类各收集一次而重复显示。
        foreach (var type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
        {
            foreach (var property in type.GetProperties(flags))
                if (property.GetIndexParameters().Length == 0 && property.IsDefined(typeof(TestAttribute), true))
                    AddMetadata(property.DeclaringType, property: property);
        }

        foreach (var metadata in TypeMetadataCache.Values)
        {
            metadata.Methods ??= Array.Empty<MethodInfo>();
            metadata.Fields ??= Array.Empty<FieldInfo>();
            metadata.Properties ??= Array.Empty<PropertyInfo>();
        }
    }

    private static void AddMetadata(Type type, MethodInfo method = null, FieldInfo field = null, PropertyInfo property = null)
    {
        if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type)) return;
        if (!TypeMetadataCache.TryGetValue(type, out var metadata))
        {
            metadata = new TestTypeMetadata();
            TypeMetadataCache.Add(type, metadata);
        }

        if (method != null)
            metadata.Methods = Append(metadata.Methods, method);
        if (field != null)
            metadata.Fields = Append(metadata.Fields, field);
        if (property != null)
            metadata.Properties = Append(metadata.Properties, property);
    }

    private static T[] Append<T>(T[] source, T value)
    {
        int count = source?.Length ?? 0;
        var result = new T[count + 1];
        if (count > 0) Array.Copy(source, result, count);
        result[count] = value;
        return result;
    }

    // ── GUI 绘制 ────────────────────────────────────────────
    private void OnGUI()
    {
        DrawToolbar();
        DrawSearchBar();

        _scrollPosGroups = EditorGUILayout.BeginScrollView(_scrollPosGroups);

        if (_groups.Count == 0)
        {
            EditorGUILayout.HelpBox("场景中未找到任何标记了 [Test] 的成员。\n\n" +
                "用法示例：\n" +
                "  [Test(\"创建存档\")] public void CreateSaveData() { ... }\n" +
                "  [Test(\"玩家速度\")] public float speed = 5f;\n" +
                "  [Test(\"生命值\")] public int Health { get; set; }",
                MessageType.Info);
        }
        else
        {
            bool anyShown = false;
            foreach (var group in _groups)
            {
                if (!MatchesFilter(group)) continue;
                anyShown = true;
                DrawGroup(group);
            }

            if (!anyShown)
            {
                EditorGUILayout.HelpBox("没有匹配的测试项。", MessageType.Info);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label($"共 {_groups.Count} 组", EditorStyles.miniLabel);

        if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(50)))
        {
            RefreshEntries();
        }

        if (GUILayout.Button("全部展开", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            foreach (var key in new List<TestGroup>(_foldouts.Keys))
                _foldouts[key] = true;
        }

        if (GUILayout.Button("全部折叠", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            foreach (var key in new List<TestGroup>(_foldouts.Keys))
                _foldouts[key] = false;
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSearchBar()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("搜索", GUILayout.Width(30));
        _searchFilter = EditorGUILayout.TextField(_searchFilter);
        if (GUILayout.Button("✕", GUILayout.Width(22)))
        {
            _searchFilter = "";
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();
    }

    private bool MatchesFilter(TestGroup group)
    {
        if (string.IsNullOrEmpty(_searchFilter)) return true;

        if (group.GroupName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (group.GameObject != null && group.GameObject.name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0) return true;

        foreach (var m in group.Methods)
            if (m.DisplayName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0) return true;

        foreach (var f in group.Fields)
            if (f.DisplayName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0) return true;

        foreach (var p in group.Properties)
            if (p.DisplayName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0) return true;

        return false;
    }

    private void DrawGroup(TestGroup group)
    {
        if (!_foldouts.ContainsKey(group))
            _foldouts[group] = true;

        // 分组标题：组件名 @ GameObject名
        string header = $"{group.GroupName}";
        if (group.GameObject != null)
            header += $"  ({group.GameObject.name})";

        EditorGUILayout.BeginVertical("box");

        _foldouts[group] = EditorGUILayout.Foldout(_foldouts[group], header, true, EditorStyles.foldoutHeader);

        if (_foldouts[group])
        {
            EditorGUI.indentLevel++;

            // 成员在元数据缓存创建时已经按继承顺序排序。
            foreach (var entry in group.Fields)
            {
                DrawFieldEntry(entry);
            }

            foreach (var entry in group.Properties)
            {
                DrawPropertyEntry(entry);
            }

            foreach (var entry in group.Methods)
            {
                DrawMethodEntry(entry);
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawFieldEntry(TestFieldEntry entry)
    {
        if (entry.Target == null) return;

        EditorGUILayout.BeginHorizontal();

        // 显示标签
        EditorGUILayout.PrefixLabel(entry.DisplayName);

        // 只读字段禁用编辑
        EditorGUI.BeginDisabledGroup(entry.IsReadOnly);

        // 获取当前值
        var value = entry.Field.GetValue(entry.Target);
        var fieldType = entry.Field.FieldType;

        EditorGUI.BeginChangeCheck();

        object newValue = null;
        bool changed = false;

        // 根据字段类型绘制对应的编辑器控件
        if (fieldType == typeof(bool))
        {
            newValue = EditorGUILayout.Toggle((bool)(value ?? false));
            changed = true;
        }
        else if (fieldType == typeof(int))
        {
            newValue = EditorGUILayout.IntField((int)(value ?? 0));
            changed = true;
        }
        else if (fieldType == typeof(float))
        {
            newValue = EditorGUILayout.FloatField((float)(value ?? 0f));
            changed = true;
        }
        else if (fieldType == typeof(string))
        {
            newValue = EditorGUILayout.TextField((string)(value ?? ""));
            changed = true;
        }
        else if (fieldType == typeof(Vector2))
        {
            newValue = EditorGUILayout.Vector2Field("", (Vector2)(value ?? Vector2.zero));
            changed = true;
        }
        else if (fieldType == typeof(Vector3))
        {
            newValue = EditorGUILayout.Vector3Field("", (Vector3)(value ?? Vector3.zero));
            changed = true;
        }
        else if (fieldType == typeof(Color))
        {
            newValue = EditorGUILayout.ColorField((Color)(value ?? Color.white));
            changed = true;
        }
        else if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
        {
            newValue = EditorGUILayout.ObjectField(value as UnityEngine.Object, fieldType, true);
            changed = true;
        }
        else if (fieldType.IsEnum)
        {
            newValue = EditorGUILayout.EnumPopup((Enum)(value ?? Enum.GetValues(fieldType).GetValue(0)));
            changed = true;
        }
        else
        {
            // 不支持的类型，只读显示
            EditorGUILayout.LabelField(value?.ToString() ?? "null");
        }

        if (changed && EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(entry.Target, $"[Test] 修改 {entry.DisplayName}");
            entry.Field.SetValue(entry.Target, newValue);
            EditorUtility.SetDirty(entry.Target);
        }

        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPropertyEntry(TestPropertyEntry entry)
    {
        if (entry.Target == null) return;

        var prop = entry.Property;

        EditorGUILayout.BeginHorizontal();

        // 显示标签
        EditorGUILayout.PrefixLabel(entry.DisplayName);

        // 只读字段或无 setter 禁用编辑
        EditorGUI.BeginDisabledGroup(entry.IsReadOnly || !entry.HasSetter);

        // 获取当前值
        object value = null;
        try
        {
            value = prop.GetValue(entry.Target);
        }
        catch { }

        var propType = prop.PropertyType;

        EditorGUI.BeginChangeCheck();

        object newValue = null;
        bool changed = false;

        // 根据属性类型绘制对应的编辑器控件
        if (propType == typeof(bool))
        {
            newValue = EditorGUILayout.Toggle((bool)(value ?? false));
            changed = true;
        }
        else if (propType == typeof(int))
        {
            newValue = EditorGUILayout.IntField((int)(value ?? 0));
            changed = true;
        }
        else if (propType == typeof(float))
        {
            newValue = EditorGUILayout.FloatField((float)(value ?? 0f));
            changed = true;
        }
        else if (propType == typeof(string))
        {
            newValue = EditorGUILayout.TextField((string)(value ?? ""));
            changed = true;
        }
        else if (propType == typeof(Vector2))
        {
            newValue = EditorGUILayout.Vector2Field("", (Vector2)(value ?? Vector2.zero));
            changed = true;
        }
        else if (propType == typeof(Vector3))
        {
            newValue = EditorGUILayout.Vector3Field("", (Vector3)(value ?? Vector3.zero));
            changed = true;
        }
        else if (propType == typeof(Color))
        {
            newValue = EditorGUILayout.ColorField((Color)(value ?? Color.white));
            changed = true;
        }
        else if (typeof(UnityEngine.Object).IsAssignableFrom(propType))
        {
            newValue = EditorGUILayout.ObjectField(value as UnityEngine.Object, propType, true);
            changed = true;
        }
        else if (propType.IsEnum)
        {
            newValue = EditorGUILayout.EnumPopup((Enum)(value ?? Enum.GetValues(propType).GetValue(0)));
            changed = true;
        }
        else
        {
            // 不支持的类型，只读显示
            EditorGUILayout.LabelField(value?.ToString() ?? "null");
        }

        if (changed && EditorGUI.EndChangeCheck() && entry.HasSetter)
        {
            Undo.RecordObject(entry.Target, $"[Test] 修改 {entry.DisplayName}");
            prop.SetValue(entry.Target, newValue);
            EditorUtility.SetDirty(entry.Target);
        }

        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawMethodEntry(TestMethodEntry entry)
    {
        if (entry.Target == null) return;

        // 检查目标是否为 prefab（不能执行方法）
        bool isPrefab = !entry.Target.gameObject.scene.IsValid();

        EditorGUI.BeginDisabledGroup(isPrefab);

        // 有参数的方法：先绘制参数输入框，再绘制按钮
        if (entry.Parameters != null && entry.Parameters.Length > 0)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(entry.DisplayName, EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            for (int i = 0; i < entry.Parameters.Length; i++)
            {
                var param = entry.Parameters[i];
                entry.ParameterValues[i] = DrawParameterField(param.Name, param.ParameterType, entry.ParameterValues[i]);
            }
            EditorGUI.indentLevel--;

            if (GUILayout.Button($"执行: {entry.DisplayName}"))
            {
                InvokeMethod(entry);
            }

            EditorGUILayout.EndVertical();
        }
        else
        {
            if (GUILayout.Button(entry.DisplayName))
            {
                InvokeMethod(entry);
            }
        }

        EditorGUI.EndDisabledGroup();
    }

    private void InvokeMethod(TestMethodEntry entry)
    {
        // 记录 Undo
        Undo.RecordObject(entry.Target, $"[Test] 调用 {entry.DisplayName}");

        try
        {
            entry.Method.Invoke(entry.Target, entry.ParameterValues);
            Debug.Log($"<color=cyan>[Test]</color> 已执行: {entry.DisplayName}  ({entry.Target.gameObject.name})");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Test] 执行失败: {entry.DisplayName}\n{ex}");
        }
    }

    private object DrawParameterField(string paramName, Type paramType, object currentValue)
    {
        object newValue = currentValue;

        if (paramType == typeof(bool))
        {
            newValue = EditorGUILayout.Toggle(paramName, (bool)(currentValue ?? false));
        }
        else if (paramType == typeof(int))
        {
            newValue = EditorGUILayout.IntField(paramName, (int)(currentValue ?? 0));
        }
        else if (paramType == typeof(float))
        {
            newValue = EditorGUILayout.FloatField(paramName, (float)(currentValue ?? 0f));
        }
        else if (paramType == typeof(string))
        {
            newValue = EditorGUILayout.TextField(paramName, (string)(currentValue ?? ""));
        }
        else if (paramType == typeof(Vector2))
        {
            newValue = EditorGUILayout.Vector2Field(paramName, (Vector2)(currentValue ?? Vector2.zero));
        }
        else if (paramType == typeof(Vector3))
        {
            newValue = EditorGUILayout.Vector3Field(paramName, (Vector3)(currentValue ?? Vector3.zero));
        }
        else if (paramType == typeof(Color))
        {
            newValue = EditorGUILayout.ColorField(paramName, (Color)(currentValue ?? Color.white));
        }
        else if (typeof(UnityEngine.Object).IsAssignableFrom(paramType))
        {
            newValue = EditorGUILayout.ObjectField(paramName, currentValue as UnityEngine.Object, paramType, true);
        }
        else if (paramType.IsEnum)
        {
            if (currentValue == null)
                currentValue = Enum.GetValues(paramType).GetValue(0);
            newValue = EditorGUILayout.EnumPopup(paramName, (Enum)currentValue);
        }
        else
        {
            EditorGUILayout.LabelField(paramName, currentValue?.ToString() ?? "null");
        }

        return newValue;
    }

    private static object GetDefaultValue(Type type)
    {
        if (type == typeof(string)) return "";
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.Length > 0 ? values.GetValue(0) : null;
        }
        if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return null;
        if (type.IsValueType) return Activator.CreateInstance(type);
        return null;
    }

    /// <summary>
    /// 获取类型的继承深度（用于排序：基类成员在前，当前类成员在后）
    /// </summary>
    private static int GetInheritanceDepth(Type type)
    {
        int depth = 0;
        var current = type;
        while (current != null && current != typeof(MonoBehaviour) && current != typeof(object))
        {
            depth++;
            current = current.BaseType;
        }
        return depth;
    }
}
