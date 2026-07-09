#if UNITY_EDITOR
using UnityEditor;
using UnityToolsHubCompat.Editor;

/// <summary>
/// FolderRuleConfig 自定义编辑器
/// 使用 OdinCompatDrawer 反射绘制属性
/// </summary>
[CustomEditor(typeof(FolderRuleConfig))]
public class FolderRuleConfigEditor : OdinCompatEditor
{
}
#endif
