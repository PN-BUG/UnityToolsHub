#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#if ADDRESSABLES
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

/// <summary>
/// Addressable 分组下拉选择器 — PropertyDrawer
/// 自动读取项目中所有 Addressable 分组，绘制为下拉框。
/// </summary>
[CustomPropertyDrawer(typeof(AddressableGroupDropdownAttribute))]
public class AddressableGroupDropdownDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.LabelField(position, label.text, "仅支持 string 字段");
            return;
        }

        var attr = (AddressableGroupDropdownAttribute)attribute;
        var groupNames = GetGroupNames(attr.IncludeDefault);
        var displayNames = new List<string>();
        for (int i = 0; i < groupNames.Count; i++)
            displayNames.Add(string.IsNullOrEmpty(groupNames[i]) ? "（默认分组）" : groupNames[i]);

        string currentValue = property.stringValue;
        int currentIndex = groupNames.IndexOf(currentValue);

        // 如果当前值不在列表中，插入到首位
        if (currentIndex < 0 && !string.IsNullOrEmpty(currentValue))
        {
            groupNames.Insert(0, currentValue);
            displayNames.Insert(0, currentValue);
            currentIndex = 0;
        }

        EditorGUI.BeginProperty(position, label, property);

        int newIndex = EditorGUI.Popup(position, label.text, currentIndex, displayNames.ToArray());

        if (newIndex >= 0 && newIndex < groupNames.Count)
        {
            property.stringValue = groupNames[newIndex];
        }

        EditorGUI.EndProperty();
    }

    private static List<string> GetGroupNames(bool includeDefault)
    {
        var names = new List<string>();
        names.Add(""); // 空 = 使用默认分组

#if ADDRESSABLES
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        if (settings != null)
        {
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                if (!includeDefault && group == settings.DefaultGroup) continue;
                names.Add(group.Name);
            }
            Debug.Log($"[AddressableGroupDropdown] 已加载 {names.Count - 1} 个分组");
        }
        else
        {
            Debug.LogWarning("[AddressableGroupDropdown] AddressableAssetSettings 为 null，请先初始化 Addressable");
        }
#else
        Debug.LogWarning("[AddressableGroupDropdown] ADDRESSABLES 宏未定义，asmdef 可能未正确引用 Addressable 包");
#endif

        return names;
    }
}
#endif
