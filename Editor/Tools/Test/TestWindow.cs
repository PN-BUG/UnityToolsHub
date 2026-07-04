using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 测试窗口 —— 聚合展示场景中所有标记了 [Test] 的方法和字段。
/// 快捷键 Ctrl+T 打开/关闭。
/// </summary>
[ToolInfo("测试窗口", "调试工具", Description = "聚合展示场景中所有标记了 [Test] 的方法和字段。\n\n方法显示为可点击按钮，字段显示为可编辑控件。\n支持搜索过滤、按组件分组，快捷键 Ctrl+T 打开/关闭。", Icon = "⌘", Tags = new[] { "测试", "调试", "Test" }, Shortcut = "Ctrl+T")]
public class TestWindow : EditorWindow
{
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
    }

    private class TestGroup
    {
        public string GroupName;           // 组件类型名
        public GameObject GameObject;      // 所属 GameObject
        public List<TestMethodEntry> Methods = new List<TestMethodEntry>();
        public List<TestFieldEntry> Fields = new List<TestFieldEntry>();
    }

    // ── 状态 ─────────────────────────────────────────────────
    private Vector2 _scrollPos;
    private List<TestGroup> _groups = new List<TestGroup>();
    private Dictionary<TestGroup, bool> _foldouts = new Dictionary<TestGroup, bool>();
    private string _searchFilter = "";
    private Vector2 _scrollPosGroups;

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
        win.RefreshEntries();
        win.Show();
    }

    private void OnEnable()
    {
        RefreshEntries();
        // 监听场景变化，自动刷新
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }

    private void OnDisable()
    {
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
    }

    private void OnHierarchyChanged()
    {
        RefreshEntries();
        Repaint();
    }

    // ── 刷新数据 ────────────────────────────────────────────
    private void RefreshEntries()
    {
        _groups.Clear();
        _foldouts.Clear();

        // 找到场景中所有 MonoBehaviour
        var allMonoBehaviours = FindObjectsOfType<MonoBehaviour>(true);

        foreach (var mb in allMonoBehaviours)
        {
            if (mb == null) continue;

            var type = mb.GetType();
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var group = new TestGroup
            {
                GroupName = ObjectNames.NicifyVariableName(type.Name),
                GameObject = mb.gameObject
            };

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<TestAttribute>();
                if (attr == null) continue;

                var parameters = method.GetParameters();
                var entry = new TestMethodEntry
                {
                    DisplayName = attr.Name,
                    Target = mb,
                    Method = method,
                    Parameters = parameters,
                    ParameterValues = new object[parameters.Length]
                };

                // 初始化参数默认值
                for (int i = 0; i < parameters.Length; i++)
                {
                    entry.ParameterValues[i] = GetDefaultValue(parameters[i].ParameterType);
                }

                group.Methods.Add(entry);
            }

            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<TestAttribute>();
                if (attr == null) continue;

                group.Fields.Add(new TestFieldEntry
                {
                    DisplayName = attr.Name,
                    Target = mb,
                    Field = field
                });
            }

            // 只添加有内容的组
            if (group.Methods.Count > 0 || group.Fields.Count > 0)
            {
                _groups.Add(group);
                _foldouts[group] = true;
            }
        }
    }

    // ── GUI 绘制 ────────────────────────────────────────────
    private void OnGUI()
    {
        DrawToolbar();
        DrawSearchBar();

        _scrollPosGroups = EditorGUILayout.BeginScrollView(_scrollPosGroups);

        if (_groups.Count == 0)
        {
            EditorGUILayout.HelpBox("场景中未找到任何标记了 [Test] 的方法或字段。\n\n" +
                "用法示例：\n" +
                "  [Test(\"创建存档\")] public void CreateSaveData() { ... }\n" +
                "  [Test(\"玩家速度\")] public float speed = 5f;",
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

        var filter = _searchFilter.ToLower();
        if (group.GroupName.ToLower().Contains(filter)) return true;
        if (group.GameObject != null && group.GameObject.name.ToLower().Contains(filter)) return true;

        foreach (var m in group.Methods)
            if (m.DisplayName.ToLower().Contains(filter)) return true;

        foreach (var f in group.Fields)
            if (f.DisplayName.ToLower().Contains(filter)) return true;

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

            // 绘制字段
            foreach (var entry in group.Fields)
            {
                DrawFieldEntry(entry);
            }

            // 绘制方法按钮
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
}
