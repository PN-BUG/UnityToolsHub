using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 通用序列化脚本默认值同步工具
/// 支持任意 MonoBehaviour / ScriptableObject 子类
/// 自动扫描预制体 + 场景中所有实例，将等于旧默认值的字段更新为新默认值
/// </summary>
[ToolInfo("脚本默认值同步工具", "序列化工具",
    Description = "选择目标脚本 → 设置新默认值 → 一键同步所有预制体和场景中等于旧默认值的字段。\n\n支持 MonoBehaviour 和 ScriptableObject，精确匹配旧值后批量替换。",
    Icon = "🔄", Tags = new[] { "序列化", "默认值", "批量同步", "预制体", "场景" })]
public class DefaultValueSyncTool : EditorWindow
{
    // ───────── 类型选择 ─────────
    private MonoScript selectedScript;
    private Type selectedType;

    // ───────── 字段映射 ─────────
    private List<FieldEntry> fieldEntries = new();
    private Vector2 scrollPos;
    private bool fieldsFoldout = true;

    // ───────── 进度 ─────────
    private bool isProcessing;

    [MenuItem("UnityToolsHub/脚本默认值同步工具")]
    static void OpenWindow()
    {
        var window = GetWindow<DefaultValueSyncTool>("脚本默认值同步工具");
        window.minSize = new Vector2(520, 400);
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawTypeSelector();

        if (selectedType != null)
        {
            DrawFieldList();
            EditorGUILayout.Space(10);
            DrawActions();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  头部
    // ═══════════════════════════════════════════════════════════════
    private void DrawHeader()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("序列化脚本默认值同步工具", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "选择目标脚本 → 设置新默认值 → 一键同步所有预制体和场景中等于旧默认值的字段。",
            MessageType.Info);
        EditorGUILayout.Space(4);
    }

    // ═══════════════════════════════════════════════════════════════
    //  类型选择
    // ═══════════════════════════════════════════════════════════════
    private void DrawTypeSelector()
    {
        EditorGUILayout.LabelField("目标脚本", EditorStyles.boldLabel);

        var newScript = (MonoScript)EditorGUILayout.ObjectField(
            "脚本文件", selectedScript, typeof(MonoScript), false);

        if (newScript != selectedScript)
        {
            selectedScript = newScript;
            selectedType = selectedScript?.GetClass();
            RebuildFieldEntries();
        }

        if (selectedType != null)
        {
            string baseType = typeof(MonoBehaviour).IsAssignableFrom(selectedType)
                ? "MonoBehaviour"
                : typeof(ScriptableObject).IsAssignableFrom(selectedType)
                    ? "ScriptableObject"
                    : selectedType.BaseType?.Name ?? "?";

            EditorGUILayout.HelpBox(
                $"类型: {selectedType.FullName}  (继承自 {baseType})",
                MessageType.None);
        }
        else if (selectedScript != null)
        {
            EditorGUILayout.HelpBox("所选脚本不是 MonoBehaviour 或 ScriptableObject 子类。", MessageType.Warning);
        }

        EditorGUILayout.Space(6);
    }

    // ═══════════════════════════════════════════════════════════════
    //  字段列表
    // ═══════════════════════════════════════════════════════════════
    private void DrawFieldList()
    {
        fieldsFoldout = EditorGUILayout.Foldout(fieldsFoldout,
            $"可同步字段 ({fieldEntries.Count})", true);

        if (!fieldsFoldout) return;

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.MaxHeight(280));

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("字段名", EditorStyles.boldLabel, GUILayout.Width(160));
        EditorGUILayout.LabelField("旧默认值", EditorStyles.boldLabel, GUILayout.Width(100));
        EditorGUILayout.LabelField("新默认值", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField("同步", EditorStyles.boldLabel, GUILayout.Width(40));
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < fieldEntries.Count; i++)
        {
            DrawFieldEntry(fieldEntries[i]);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawFieldEntry(FieldEntry entry)
    {
        EditorGUILayout.BeginHorizontal();

        // 字段名
        EditorGUILayout.LabelField(entry.displayName, GUILayout.Width(160));

        // 旧默认值（只读）
        EditorGUI.BeginDisabledGroup(true);
        DrawValueField(entry.oldValue, entry.fieldType, GUILayout.Width(100));
        EditorGUI.EndDisabledGroup();

        // 新默认值（可编辑）
        entry.newValue = DrawValueField(entry.newValue, entry.fieldType, GUILayout.ExpandWidth(true));

        // 是否同步
        entry.enabled = EditorGUILayout.Toggle(entry.enabled, GUILayout.Width(40));

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 根据类型绘制对应的输入控件
    /// </summary>
    private object DrawValueField(object value, Type type, params GUILayoutOption[] options)
    {
        if (type == typeof(int))
            return EditorGUILayout.IntField((int)value, options);
        if (type == typeof(float))
            return EditorGUILayout.FloatField((float)value, options);
        if (type == typeof(bool))
            return EditorGUILayout.Toggle((bool)value, options);
        if (type == typeof(string))
            return EditorGUILayout.TextField((string)value, options);
        if (type == typeof(long))
            return EditorGUILayout.LongField((long)value, options);
        if (type == typeof(double))
            return EditorGUILayout.DoubleField((double)value, options);
        if (type == typeof(Vector2))
            return EditorGUILayout.Vector2Field("", (Vector2)value, options);
        if (type == typeof(Vector3))
            return EditorGUILayout.Vector3Field("", (Vector3)value, options);
        if (type == typeof(Vector4))
            return EditorGUILayout.Vector4Field("", (Vector4)value, options);
        if (type == typeof(Color))
            return EditorGUILayout.ColorField((Color)value, options);
        if (type == typeof(AnimationCurve))
            return EditorGUILayout.CurveField((AnimationCurve)value, options);
        if (type.IsEnum)
        {
            if (type.GetCustomAttribute<FlagsAttribute>() != null)
                return EditorGUILayout.EnumFlagsField((Enum)value, options);
            return EditorGUILayout.EnumPopup((Enum)value, options);
        }
        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            return EditorGUILayout.ObjectField((UnityEngine.Object)value, type, false, options);

        // 兜底：不可编辑
        EditorGUILayout.LabelField(value?.ToString() ?? "null", options);
        return value;
    }

    // ═══════════════════════════════════════════════════════════════
    //  操作按钮
    // ═══════════════════════════════════════════════════════════════
    private void DrawActions()
    {
        EditorGUI.BeginDisabledGroup(isProcessing);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("全选", GUILayout.Height(28)))
            fieldEntries.ForEach(e => e.enabled = true);

        if (GUILayout.Button("全不选", GUILayout.Height(28)))
            fieldEntries.ForEach(e => e.enabled = false);

        if (GUILayout.Button("仅更新预制体", GUILayout.Height(28)))
            Execute(prefabs: true, scenes: false);

        if (GUILayout.Button("仅更新场景", GUILayout.Height(28)))
            Execute(prefabs: false, scenes: true);

        var prevColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
        if (GUILayout.Button("全部更新（预制体 + 场景）", GUILayout.Height(28)))
            Execute(prefabs: true, scenes: true);
        GUI.backgroundColor = prevColor;

        EditorGUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();
    }

    // ═══════════════════════════════════════════════════════════════
    //  字段发现
    // ═══════════════════════════════════════════════════════════════
    private void RebuildFieldEntries()
    {
        fieldEntries.Clear();

        if (selectedType == null) return;

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var fields = selectedType.GetFields(flags);

        // 创建临时实例获取代码默认值
        object defaultInstance = null;
        try
        {
            if (typeof(ScriptableObject).IsAssignableFrom(selectedType))
                defaultInstance = ScriptableObject.CreateInstance(selectedType);
            else if (typeof(MonoBehaviour).IsAssignableFrom(selectedType))
                defaultInstance = selectedType.GetConstructor(Type.EmptyTypes)?.Invoke(null);
        }
        catch { /* 部分类无法无参构造 */ }

        foreach (var field in fields)
        {
            // 只处理可序列化字段
            if (!IsSerializedField(field)) continue;

            var entry = new FieldEntry
            {
                fieldInfo = field,
                fieldType = field.FieldType,
                displayName = GetDisplayName(field),
                enabled = true
            };

            // 读取代码默认值
            if (defaultInstance != null)
            {
                try
                {
                    entry.oldValue = field.GetValue(defaultInstance);
                    entry.newValue = field.GetValue(defaultInstance);
                }
                catch
                {
                    entry.oldValue = GetDefaultValue(field.FieldType);
                    entry.newValue = GetDefaultValue(field.FieldType);
                }
            }
            else
            {
                entry.oldValue = GetDefaultValue(field.FieldType);
                entry.newValue = GetDefaultValue(field.FieldType);
            }

            fieldEntries.Add(entry);
        }

        if (defaultInstance != null && defaultInstance is ScriptableObject so)
            DestroyImmediate(so);
    }

    private static bool IsSerializedField(FieldInfo field)
    {
        // 非 Public 字段需要 [SerializeField]
        if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null)
            return false;

        // 排除 [HideInInspector]
        if (field.GetCustomAttribute<HideInInspector>() != null)
            return false;

        // 排除 [NonSerialized]
        if (field.GetCustomAttribute<NonSerializedAttribute>() != null)
            return false;

        // 排除 static / readonly
        if (field.IsStatic || field.IsInitOnly)
            return false;

        // 检查类型是否可序列化
        return IsSerializableType(field.FieldType);
    }

    private static bool IsSerializableType(Type type)
    {
        if (type.IsPrimitive || type == typeof(string)) return true;
        if (type.IsEnum) return true;
        if (type.GetCustomAttribute<SerializableAttribute>() != null) return true;
        if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return true;
        return false;
    }

    private static string GetDisplayName(FieldInfo field)
    {
        // 优先使用 Odin [LabelText]
        var labelText = field.GetCustomAttribute(
            typeof(UnityToolsHubCompat.LabelTextAttribute), false);
        if (labelText != null)
        {
            var prop = labelText.GetType().GetProperty("Text");
            if (prop != null) return (string)prop.GetValue(labelText);
        }

        // 尝试 [Header] 作为分组标记
        var header = field.GetCustomAttribute<HeaderAttribute>();
        string prefix = header != null ? $"[{header.header}] " : "";

        // 驼峰转中文友好名
        string name = ObjectNames.NicifyVariableName(field.Name);
        return prefix + name;
    }

    private static object GetDefaultValue(Type type)
    {
        if (type.IsValueType) return Activator.CreateInstance(type);
        if (type == typeof(string)) return "";
        if (type == typeof(AnimationCurve)) return new AnimationCurve();
        if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return null;
        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    //  执行更新
    // ═══════════════════════════════════════════════════════════════
    private void Execute(bool prefabs, bool scenes)
    {
        var activeEntries = fieldEntries.Where(e => e.enabled).ToList();
        if (activeEntries.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请至少勾选一个需要同步的字段。", "确定");
            return;
        }

        isProcessing = true;

        // 构建字段名 → entry 映射
        var entryMap = new Dictionary<string, FieldEntry>();
        foreach (var e in activeEntries)
            entryMap[e.fieldInfo.Name] = e;

        int prefabCount = 0, sceneCount = 0;

        if (prefabs)
            prefabCount = UpdatePrefabs(entryMap);

        if (scenes)
            sceneCount = UpdateScenes(entryMap);

        isProcessing = false;

        string msg = $"同步完成！\n预制体更新: {prefabCount} 个组件\n场景更新: {sceneCount} 个组件";
        Debug.Log($"[DefaultValueSync] {msg}");
        EditorUtility.DisplayDialog("完成", msg, "确定");
    }

    // ───────── 预制体 ─────────
    private int UpdatePrefabs(Dictionary<string, FieldEntry> entryMap)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        int updated = 0;

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);

                if (EditorUtility.DisplayCancelableProgressBar(
                        "扫描预制体", path, (float)i / guids.Length))
                    break;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                // 支持 MonoBehaviour 和 ScriptableObject
                if (typeof(MonoBehaviour).IsAssignableFrom(selectedType))
                {
                    foreach (var comp in prefab.GetComponentsInChildren(selectedType, true))
                    {
                        if (TryUpdateObject(comp, entryMap))
                        {
                            EditorUtility.SetDirty(comp);
                            updated++;
                        }
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (updated > 0)
            AssetDatabase.SaveAssets();

        return updated;
    }

    // ───────── 场景 ─────────
    private int UpdateScenes(Dictionary<string, FieldEntry> entryMap)
    {
        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        string[] guids = AssetDatabase.FindAssets("t:Scene");
        int updated = 0;

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);

                if (EditorUtility.DisplayCancelableProgressBar(
                        "扫描场景", path, (float)i / guids.Length))
                    break;

                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                bool sceneDirty = false;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var comp in root.GetComponentsInChildren(selectedType, true))
                    {
                        if (TryUpdateObject(comp, entryMap))
                        {
                            EditorUtility.SetDirty(comp);
                            sceneDirty = true;
                            updated++;
                        }
                    }
                }

                if (sceneDirty)
                    EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        return updated;
    }

    /// <summary>
    /// 对单个对象执行字段同步，返回是否发生修改
    /// </summary>
    private bool TryUpdateObject(UnityEngine.Object obj, Dictionary<string, FieldEntry> entryMap)
    {
        bool changed = false;
        var so = new SerializedObject(obj);

        foreach (var kvp in entryMap)
        {
            var sp = so.FindProperty(kvp.Key);
            if (sp == null) continue;

            var entry = kvp.Value;

            if (PropertyValueEquals(sp, entry.oldValue, entry.fieldType))
            {
                SetPropertyValue(sp, entry.newValue, entry.fieldType);
                changed = true;
            }
        }

        if (changed)
            so.ApplyModifiedPropertiesWithoutUndo();

        return changed;
    }

    // ═══════════════════════════════════════════════════════════════
    //  SerializedProperty 读写（精确匹配）
    // ═══════════════════════════════════════════════════════════════
    private static bool PropertyValueEquals(SerializedProperty sp, object target, Type type)
    {
        try
        {
            if (type == typeof(int)) return sp.intValue == (int)target;
            if (type == typeof(float)) return Mathf.Approximately(sp.floatValue, (float)target);
            if (type == typeof(bool)) return sp.boolValue == (bool)target;
            if (type == typeof(string)) return sp.stringValue == (string)(target ?? "");
            if (type == typeof(long)) return sp.longValue == (long)target;
            if (type == typeof(double)) return Math.Abs(sp.doubleValue - (double)target) < 1e-10;
            if (type.IsEnum) return sp.enumValueIndex == (int)target;
            if (type == typeof(Vector2))
            {
                var v = (Vector2)target;
                return sp.vector2Value == v;
            }
            if (type == typeof(Vector3))
            {
                var v = (Vector3)target;
                return sp.vector3Value == v;
            }
            if (type == typeof(Color))
            {
                var c = (Color)target;
                return sp.colorValue == c;
            }
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                return sp.objectReferenceValue == (UnityEngine.Object)target;
        }
        catch { }

        return false;
    }

    private static void SetPropertyValue(SerializedProperty sp, object value, Type type)
    {
        try
        {
            if (type == typeof(int)) { sp.intValue = (int)value; return; }
            if (type == typeof(float)) { sp.floatValue = (float)value; return; }
            if (type == typeof(bool)) { sp.boolValue = (bool)value; return; }
            if (type == typeof(string)) { sp.stringValue = (string)(value ?? ""); return; }
            if (type == typeof(long)) { sp.longValue = (long)value; return; }
            if (type == typeof(double)) { sp.doubleValue = (double)value; return; }
            if (type.IsEnum) { sp.enumValueIndex = (int)value; return; }
            if (type == typeof(Vector2)) { sp.vector2Value = (Vector2)value; return; }
            if (type == typeof(Vector3)) { sp.vector3Value = (Vector3)value; return; }
            if (type == typeof(Color)) { sp.colorValue = (Color)value; return; }
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            { sp.objectReferenceValue = (UnityEngine.Object)value; return; }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DefaultValueSync] 写入字段失败: {e.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  数据模型
    // ═══════════════════════════════════════════════════════════════
    private class FieldEntry
    {
        public FieldInfo fieldInfo;
        public Type fieldType;
        public string displayName;
        public object oldValue;   // 代码中的旧默认值（用于匹配）
        public object newValue;   // 要同步的新默认值
        public bool enabled;      // 是否参与同步
    }
}

