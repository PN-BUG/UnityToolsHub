#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LocalizedTextReceiver))]
[CanEditMultipleObjects]
public sealed class LocalizedTextReceiverEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(6f);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (!GUILayout.Button(targets.Length > 1 ? "按当前文本批量刷新本地化" : "按当前文本刷新本地化",
                    GUILayout.Height(26f))) return;

            foreach (var item in targets)
                LocalizationPipelineWindow.RefreshReceiverFromCurrentText(item as LocalizedTextReceiver);
        }

        EditorGUILayout.HelpBox("以当前 Text/TMP 的实际中文为准，重新生成 Key、补充翻译，并保存嵌套 Prefab 实例 Override。",
            MessageType.Info);
    }
}
#endif
