#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UnityToolsHub — 快捷键管理
/// 包含快捷键录制、导航、持久化存储、冲突检测
/// </summary>
public partial class UnityToolsHub
{
    #region 快捷键管理
    private const string ShortcutPrefsPrefix = "UnityToolsHub.Shortcut.";

    /// <summary>
    /// 构建（或刷新）快捷键→工具的索引，使 HandleShortcutNavigation 可 O(1) 查找。
    /// 在 DiscoverTools 后或快捷键变更后调用。
    /// </summary>
    private void RebuildShortcutIndex()
    {
        _shortcutIndex.Clear();
        foreach (var cat in _categories)
        {
            foreach (var tool in cat.tools)
            {
                if (string.IsNullOrEmpty(tool.typeName)) continue;
                var shortcut = GetEffectiveShortcut(tool.typeName);
                if (shortcut.IsValid && !_shortcutIndex.ContainsKey(shortcut))
                    _shortcutIndex[shortcut] = tool;
            }
        }
        _shortcutIndexVersion++;
    }

    /// <summary>获取工具的有效快捷键（优先使用 EditorPrefs 自定义，其次使用 ToolInfo 属性默认值）</summary>
    private ShortcutBinding GetEffectiveShortcut(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return default;

        // 1) 优先读取 EditorPrefs 自定义快捷键
        var stored = EditorPrefs.GetString(ShortcutPrefsPrefix + typeName, "");
        if (!string.IsNullOrEmpty(stored))
        {
            var parsed = ShortcutBinding.Parse(stored);
            if (parsed.IsValid) return parsed;
        }

        // 2) 回退到 ToolInfo 属性的默认快捷键
        _toolIndex.TryGetValue(typeName, out var tool);
        if (tool != null && !string.IsNullOrEmpty(tool.shortcut))
        {
            // 尝试用 MenuItem 格式解析（如 "%#e"），失败则用可读格式
            var parsed = ShortcutBinding.ParseMenuItem(tool.shortcut);
            if (!parsed.IsValid)
                parsed = ShortcutBinding.Parse(tool.shortcut);
            if (parsed.IsValid) return parsed;
        }

        return default;
    }

    /// <summary>保存自定义快捷键到 EditorPrefs</summary>
    private void SaveShortcut(string typeName, ShortcutBinding binding)
    {
        if (binding.IsValid)
            EditorPrefs.SetString(ShortcutPrefsPrefix + typeName, binding.ToString());
        else
            EditorPrefs.DeleteKey(ShortcutPrefsPrefix + typeName);
        // 快捷键变更后刷新索引
        RebuildShortcutIndex();
    }

    /// <summary>清除工具的自定义快捷键</summary>
    private void ClearShortcut(string typeName)
    {
        EditorPrefs.DeleteKey(ShortcutPrefsPrefix + typeName);
        // 快捷键变更后刷新索引
        RebuildShortcutIndex();
    }

    /// <summary>检查快捷键是否与已有工具的快捷键冲突（排除自身）</summary>
    private string FindShortcutConflict(string excludeTypeName, ShortcutBinding binding)
    {
        if (!binding.IsValid) return null;

        foreach (var cat in _categories)
        {
            foreach (var tool in cat.tools)
            {
                if (string.IsNullOrEmpty(tool.typeName)) continue;
                if (tool.typeName == excludeTypeName) continue;

                var existing = GetEffectiveShortcut(tool.typeName);
                if (existing.Equals(binding))
                    return tool.name;
            }
        }
        return null;
    }
    #endregion

    #region 快捷键录制与导航
    private void HandleShortcutRecording()
    {
        var evt = Event.current;
        if (evt == null) return;

        // 录制超时（10 秒无操作自动取消）
        if (EditorApplication.timeSinceStartup - _recordingStartTime > 10.0)
        {
            CancelRecording();
            Repaint();
            return;
        }

        if (evt.type == EventType.KeyDown)
        {
            // Esc 取消录制
            if (evt.keyCode == KeyCode.Escape)
            {
                CancelRecording();
                evt.Use();
                Repaint();
                return;
            }

            var binding = ShortcutBinding.FromEvent(evt);
            if (binding.IsValid)
            {
                // 检查冲突
                var conflict = FindShortcutConflict(_recordingForTypeName, binding);
                if (conflict != null)
                {
                    Debug.LogWarning($"[UnityToolsHub] 快捷键 {binding} 已被「{conflict}」使用，请选择其他组合键。");
                    evt.Use();
                    Repaint();
                    return;
                }

                SaveShortcut(_recordingForTypeName, binding);
                Debug.Log($"[UnityToolsHub] 已为「{_recordingForTypeName}」设置快捷键: {binding}");
                _isRecordingShortcut = false;
                _recordingForTypeName = null;
                evt.Use();
                Repaint();
            }
        }
        else if (evt.type == EventType.MouseDown)
        {
            // 点击其他地方取消录制
            CancelRecording();
            evt.Use();
            Repaint();
        }
    }

    private void CancelRecording()
    {
        _isRecordingShortcut = false;
        _recordingForTypeName = null;
    }

    /// <summary>Hub 自身的切换快捷键（与 [MenuItem] 保持一致，避免拦截）</summary>
    private static readonly ShortcutBinding _hubToggleShortcut = new ShortcutBinding
    {
        key = KeyCode.E,
        ctrl = true,
        shift = true
    };

    private void HandleShortcutNavigation()
    {
        var evt = Event.current;
        if (evt == null || evt.type != EventType.KeyDown) return;

        var pressed = ShortcutBinding.FromEvent(evt);
        if (!pressed.IsValid) return;

        // 不拦截 Hub 自身的切换快捷键（Ctrl+Shift+E）
        if (pressed.Equals(_hubToggleShortcut)) return;

        // 懒构建快捷键索引（首次或版本变化时重建）
        if (_shortcutIndexVersion != _categoriesVersion)
            RebuildShortcutIndex();

        // O(1) 字典查找，替代每帧遍历所有分类所有工具
        if (_shortcutIndex.TryGetValue(pressed, out var tool))
        {
            _selectedTool = tool;
            _selectedCategory = _categories.FirstOrDefault(c => c.tools.Contains(tool));
            _rightScroll = Vector2.zero;
            _showCreateForm = false;
            _showHiddenManager = false;
            RecordToolUsage(tool);
            evt.Use();
            Repaint();
        }
    }

    /// <summary>开始录制快捷键</summary>
    private void StartRecording(string typeName)
    {
        _isRecordingShortcut = true;
        _recordingForTypeName = typeName;
        _recordingStartTime = EditorApplication.timeSinceStartup;
        GUI.FocusControl(null);
        Repaint();
    }
    #endregion
}
#endif
