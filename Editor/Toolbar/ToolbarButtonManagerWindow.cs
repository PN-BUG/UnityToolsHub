#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
namespace Zko.UnityToolsHub.Toolbar
{
    [ToolInfo("工具栏按钮", "编辑器工具", Description = "管理通过 ToolbarButton 标签注册的 Unity 主工具栏按钮", Icon = "TB", Tags = new[] { "Toolbar", "工具栏", "按钮" }, Priority = -20)]
    public sealed class ToolbarButtonManagerWindow : EditorWindow
    {
        private Vector2 _scroll;
        [MenuItem("Window/Unity Tools Hub/Toolbar Buttons")]
        [ToolbarButton("管理", Tooltip = "打开工具栏按钮管理窗口", Side = ToolbarSide.Right, Order = -100)]
        public static void ShowWindow() => GetWindow<ToolbarButtonManagerWindow>("Toolbar Buttons");
        private void OnEnable() { ToolbarButtonRegistry.Changed += Repaint; ToolbarButtonRegistry.Refresh(); }
        private void OnDisable() { ToolbarButtonRegistry.Changed -= Repaint; }
        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Unity 主工具栏按钮", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("给无参数静态方法添加 [ToolbarButton(\"文字\")]，即可自动显示。按钮默认按文字自适应宽度，也可在标签中填写 Width。配置保存在本机 EditorPrefs。", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重新扫描", GUILayout.Width(90))) ToolbarButtonRegistry.Refresh();
            if (GUILayout.Button("恢复默认", GUILayout.Width(90)) && EditorUtility.DisplayDialog("恢复默认", "清除所有工具栏按钮配置？", "恢复", "取消")) ToolbarButtonRegistry.Reset();
            GUILayout.FlexibleSpace(); EditorGUILayout.EndHorizontal();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var b in ToolbarButtonRegistry.Buttons) DrawButton(b);
            EditorGUILayout.EndScrollView();
        }
        private static void DrawButton(ToolbarButtonDescriptor b)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox); EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck(); bool enabled = EditorGUILayout.Toggle(ToolbarButtonRegistry.IsEnabled(b), GUILayout.Width(18));
            if (EditorGUI.EndChangeCheck()) ToolbarButtonRegistry.SetEnabled(b, enabled);
            EditorGUILayout.LabelField(b.Attribute.Text, EditorStyles.boldLabel, GUILayout.MinWidth(80));
            EditorGUI.BeginChangeCheck(); var side = (ToolbarSide)EditorGUILayout.EnumPopup(ToolbarButtonRegistry.GetSide(b), GUILayout.Width(70));
            if (EditorGUI.EndChangeCheck()) ToolbarButtonRegistry.SetSide(b, side);
            GUI.enabled = b.IsValid;
            if (GUILayout.Button("↑", GUILayout.Width(26))) ToolbarButtonRegistry.Move(b, -1);
            if (GUILayout.Button("↓", GUILayout.Width(26))) ToolbarButtonRegistry.Move(b, 1);
            GUI.enabled = true; EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(b.Method.DeclaringType.FullName + "." + b.Method.Name + "()", EditorStyles.miniLabel);
            if (!b.IsValid) EditorGUILayout.HelpBox(b.ValidationMessage, MessageType.Error);
            EditorGUILayout.EndVertical();
        }
    }
}
#endif
