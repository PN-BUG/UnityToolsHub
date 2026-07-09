#if UNITY_EDITOR
using UnityEditor;
using Nodin.Editor;

/// <summary>
/// FolderRuleConfig 自定义编辑器
/// 使用 NodinDrawer 反射绘制属性
/// </summary>
[CustomEditor(typeof(FolderRuleConfig))]
public class FolderRuleConfigEditor : NodinEditor
{
}
#endif
