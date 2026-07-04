using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// 编辑器工具：将脚本A的Inspector参数复制给脚本B中相同名称的参数
/// 使用反射直接读写字段值
/// </summary>
[ToolInfo("组件参数复制", "编辑器工具",
    Description = "将脚本 A 的 Inspector 参数复制给脚本 B 中相同名称的参数。\n\n使用反射直接读写字段值，支持 MonoBehaviour 间的快速参数迁移。",
    Icon = "🛠", Tags = new[] { "反射", "参数复制" })]
public class ComponentParameterCopier : EditorWindow
{
    private MonoBehaviour sourceComponent;
    private MonoBehaviour targetComponent;
    private Vector2 scrollPos;
    private List<string> copiedFields = new List<string>();
    private List<string> skippedFields = new List<string>();

    [MenuItem("UnityToolsHub/Component Parameter Copier")]
    public static void ShowWindow()
    {
        GetWindow<ComponentParameterCopier>("Component Parameter Copier");
    }

    private void OnGUI()
    {
        GUILayout.Label("组件参数复制工具", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("源组件 (A)");
        sourceComponent = (MonoBehaviour)EditorGUILayout.ObjectField(sourceComponent, typeof(MonoBehaviour), true);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("目标组件 (B)");
        targetComponent = (MonoBehaviour)EditorGUILayout.ObjectField(targetComponent, typeof(MonoBehaviour), true);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        GUI.enabled = sourceComponent != null && targetComponent != null;
        if (GUILayout.Button("复制参数", GUILayout.Height(30)))
        {
            CopyParameters();
        }
        GUI.enabled = true;

        EditorGUILayout.Space();

        if (copiedFields.Count > 0 || skippedFields.Count > 0)
        {
            EditorGUILayout.LabelField("操作结果:", EditorStyles.boldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));

            if (copiedFields.Count > 0)
            {
                EditorGUILayout.LabelField($"成功复制 {copiedFields.Count} 个字段:", EditorStyles.miniLabel);
                foreach (string field in copiedFields)
                    EditorGUILayout.LabelField($"  ✓ {field}", EditorStyles.miniLabel);
            }

            if (skippedFields.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"跳过 {skippedFields.Count} 个字段:", EditorStyles.miniLabel);
                foreach (string field in skippedFields)
                    EditorGUILayout.LabelField($"  ✗ {field}", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "使用说明:\n" +
            "1. 将源组件(A)拖拽到第一个字段\n" +
            "2. 将目标组件(B)拖拽到第二个字段\n" +
            "3. 点击'复制参数'按钮\n" +
            "4. 相同名称和类型的字段值将被复制\n\n" +
            "支持: public字段 + [SerializeField] private字段",
            MessageType.Info);
    }

    private void CopyParameters()
    {
        copiedFields.Clear();
        skippedFields.Clear();

        if (sourceComponent == null || targetComponent == null)
        {
            EditorUtility.DisplayDialog("错误", "请先选择源组件和目标组件！", "确定");
            return;
        }

        Undo.RecordObject(targetComponent, "Copy Component Parameters");

        // 获取源和目标的字段
        var sourceFields = GetAllSerializedFields(sourceComponent.GetType());
        var targetFields = GetAllSerializedFields(targetComponent.GetType());

        // 构建目标字段名 -> FieldInfo 的映射
        var targetFieldMap = new Dictionary<string, FieldInfo>();
        foreach (var f in targetFields)
            targetFieldMap[f.Name] = f;

        int copied = 0, skipped = 0;

        foreach (var sourceField in sourceFields)
        {
            if (!targetFieldMap.TryGetValue(sourceField.Name, out FieldInfo targetField))
            {
                skippedFields.Add($"{sourceField.Name} (目标中不存在)");
                skipped++;
                continue;
            }

            // 类型兼容性检查
            if (!targetField.FieldType.IsAssignableFrom(sourceField.FieldType))
            {
                skippedFields.Add($"{sourceField.Name} (类型不匹配: {sourceField.FieldType.Name} -> {targetField.FieldType.Name})");
                skipped++;
                continue;
            }

            try
            {
                object value = sourceField.GetValue(sourceComponent);
                targetField.SetValue(targetComponent, value);
                copiedFields.Add($"{sourceField.Name} ({sourceField.FieldType.Name}) = {FormatValue(value)}");
                copied++;
            }
            catch (System.Exception e)
            {
                skippedFields.Add($"{sourceField.Name} (失败: {e.Message})");
                skipped++;
            }
        }

        EditorUtility.SetDirty(targetComponent);
        EditorUtility.SetDirty(targetComponent.gameObject);

        Debug.Log($"[参数复制工具] 完成: 成功 {copied} 个, 跳过 {skipped} 个");
        Repaint();
    }

    // ═══════════════════════════════════════
    //  反射辅助
    // ═══════════════════════════════════════

    private static FieldInfo[] GetAllSerializedFields(System.Type type)
    {
        var fields = new List<FieldInfo>();
        var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        System.Type current = type;
        while (current != null && current != typeof(MonoBehaviour) && current != typeof(Behaviour)
               && current != typeof(Component) && current != typeof(Object))
        {
            foreach (var field in current.GetFields(bindingFlags))
            {
                if (fields.Exists(f => f.Name == field.Name))
                    continue;

                if (field.IsPublic || field.GetCustomAttribute<SerializeField>() != null)
                    fields.Add(field);
            }
            current = current.BaseType;
        }

        return fields.ToArray();
    }

    private static string FormatValue(object value)
    {
        if (value == null) return "null";
        if (value is string s) return $"\"{s}\"";
        if (value is Object unityObj) return unityObj.name;
        if (value is System.Collections.IList list) return $"Array[{list.Count}]";
        return value.ToString();
    }
}