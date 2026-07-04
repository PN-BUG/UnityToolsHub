using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// 组件右键菜单：快速复制/粘贴组件参数
/// 使用反射直接读写字段值，确保修改能正确生效
/// </summary>
public static class ComponentContextMenu
{
    // 静态缓存：保存字段名 -> 值的映射
    private static Dictionary<string, object> s_Clipboard = null;
    private static string s_SourceTypeName = null;

    [MenuItem("CONTEXT/MonoBehaviour/复制组件参数 (Copy Parameters)")]
    private static void CopyComponentParameters(MenuCommand command)
    {
        MonoBehaviour component = command.context as MonoBehaviour;
        if (component == null) return;

        s_Clipboard = new Dictionary<string, object>();
        s_SourceTypeName = component.GetType().AssemblyQualifiedName;
        System.Type type = component.GetType();

        // 获取所有序列化字段（public + [SerializeField] private）
        FieldInfo[] fields = GetAllSerializedFields(type);

        foreach (var field in fields)
        {
            object value = field.GetValue(component);
            s_Clipboard[field.Name] = value;
            Debug.Log($"  [复制] {field.Name} = {FormatValue(value)} ({field.FieldType.Name})");
        }

        Debug.Log($"[参数复制工具] 已复制 {s_Clipboard.Count} 个字段 from {type.Name} on {component.gameObject.name}");
    }

    [MenuItem("CONTEXT/MonoBehaviour/粘贴组件参数 (Paste Parameters)")]
    private static void PasteComponentParameters(MenuCommand command)
    {
        MonoBehaviour targetComponent = command.context as MonoBehaviour;
        if (targetComponent == null) return;

        if (s_Clipboard == null || s_Clipboard.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "剪贴板为空，请先复制一个组件的参数。", "确定");
            return;
        }

        System.Type sourceType = System.Type.GetType(s_SourceTypeName);
        System.Type targetType = targetComponent.GetType();

        // 类型兼容性检查
        if (sourceType != null && sourceType != targetType)
        {
            if (!EditorUtility.DisplayDialog("类型不匹配",
                $"源组件类型: {sourceType.Name}\n目标组件类型: {targetType.Name}\n\n仅会复制名称和类型都匹配的字段，是否继续？",
                "继续", "取消"))
            {
                return;
            }
        }

        // 记录 Undo，支持 Ctrl+Z 撤销
        Undo.RecordObject(targetComponent, "Paste Component Parameters");

        FieldInfo[] targetFields = GetAllSerializedFields(targetType);
        Dictionary<string, FieldInfo> targetFieldMap = new Dictionary<string, FieldInfo>();
        foreach (var f in targetFields)
            targetFieldMap[f.Name] = f;

        int copiedCount = 0;
        int skippedCount = 0;

        foreach (var kvp in s_Clipboard)
        {
            string name = kvp.Key;
            object value = kvp.Value;

            if (!targetFieldMap.TryGetValue(name, out FieldInfo targetField))
            {
                skippedCount++;
                Debug.Log($"  ✗ 跳过: {name} (目标中不存在)");
                continue;
            }

            // 检查类型兼容性（相同类型或可赋值）
            System.Type sourceValueType = value?.GetType();
            if (sourceValueType != null && !targetField.FieldType.IsAssignableFrom(sourceValueType))
            {
                skippedCount++;
                Debug.Log($"  ✗ 跳过: {name} (类型不兼容: {sourceValueType.Name} -> {targetField.FieldType.Name})");
                continue;
            }

            try
            {
                object oldValue = targetField.GetValue(targetComponent);
                targetField.SetValue(targetComponent, value);
                object newValue = targetField.GetValue(targetComponent);
                copiedCount++;
                Debug.Log($"  ✓ 复制: {name} = {FormatValue(newValue)} (原值: {FormatValue(oldValue)})");
            }
            catch (System.Exception ex)
            {
                skippedCount++;
                Debug.LogWarning($"  ✗ 复制失败: {name} - {ex.Message}");
            }
        }

        // 强制标记脏并刷新
        EditorUtility.SetDirty(targetComponent);
        EditorUtility.SetDirty(targetComponent.gameObject);

        Debug.Log($"[参数复制工具] 完成: 成功 {copiedCount} 个, 跳过 {skippedCount} 个 to {targetType.Name} on {targetComponent.gameObject.name}");
        EditorUtility.DisplayDialog("完成", $"成功复制 {copiedCount} 个参数\n跳过 {skippedCount} 个参数", "确定");
    }

    // ═══════════════════════════════════════
    //  反射辅助
    // ═══════════════════════════════════════

    /// <summary>
    /// 获取类型的所有可序列化字段（public 字段 + 标记了 [SerializeField] 的 private 字段）
    /// 递归获取父类字段
    /// </summary>
    private static FieldInfo[] GetAllSerializedFields(System.Type type)
    {
        var fields = new List<FieldInfo>();
        var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // 从当前类到 MonoBehaviour 基类，逐级向上收集
        System.Type current = type;
        while (current != null && current != typeof(MonoBehaviour) && current != typeof(Behaviour) && current != typeof(Component) && current != typeof(Object))
        {
            foreach (var field in current.GetFields(bindingFlags))
            {
                // 跳过已存在的同名字段（子类覆盖）
                if (fields.Exists(f => f.Name == field.Name))
                    continue;

                // public 字段自动序列化
                if (field.IsPublic)
                {
                    fields.Add(field);
                    continue;
                }

                // private 字段需要 [SerializeField] 特性
                if (field.GetCustomAttribute<SerializeField>() != null)
                {
                    fields.Add(field);
                    continue;
                }

                // [HideInInspector] 的 public 字段也跳过（可选）
                // if (field.GetCustomAttribute<HideInInspector>() != null) continue;
            }
            current = current.BaseType;
        }

        return fields.ToArray();
    }

    /// <summary>
    /// 格式化值用于日志输出
    /// </summary>
    private static string FormatValue(object value)
    {
        if (value == null) return "null";
        if (value is string s) return $"\"{s}\"";
        if (value is Object unityObj) return unityObj.name;
        if (value is System.Collections.IList list) return $"Array[{list.Count}]";
        return value.ToString();
    }
}