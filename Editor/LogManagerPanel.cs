using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 日志管理面板 — 以脚本（通道）为单位管理 GameLogger 日志输出。
///
/// 功能：
///   1. 全局日志等级控制（Verbose / Info / Warning / Error / None）
///   2. 通道列表，每个通道可单独开关
///   3. 搜索过滤通道
///   4. 测试按钮，验证通道日志输出
///   5. 批量启用 / 禁用 / 刷新
///
/// 使用方式：
///   在 UnityToolsHub 面板中打开，或通过菜单 UnityToolsHub/日志管理 打开。
/// </summary>
[ToolInfo("日志管理", "编辑器工具",
    Description = "管理 GameLogger 日志通道，按脚本开关日志输出。\n\n" +
                  "• 全局日志等级控制\n" +
                  "• 以脚本为单位的通道开关\n" +
                  "• 搜索过滤与测试输出",
    Icon = "📋", Tags = new[] { "日志", "调试", "Log", "GameLogger" })]
public class LogManagerPanel : ToolEditorWindow
{
    // ═══════════════════════════════════════════════════════════════
    //  配置
    // ═══════════════════════════════════════════════════════════════

    protected override string ToolTitle => "日志管理";
    protected override string ToolIcon  => "📋";
    protected override bool ShowSearchBar => true;
    protected override bool ShowStatusBar => true;

    // ═══════════════════════════════════════════════════════════════
    //  生命周期
    // ═══════════════════════════════════════════════════════════════

    protected override void OnToolEnable()
    {
        // 刷新通道列表
        Repaint();
    }

    // ═══════════════════════════════════════════════════════════════
    //  主内容
    // ═══════════════════════════════════════════════════════════════

    protected override void DrawToolContent()
    {
        DrawGradientBar(ClrAccent, ClrCatTeal);

        EditorGUILayout.Space(4);

        if (!GameLoggerAdapter.IsAvailable)
        {
            EditorGUILayout.HelpBox(
                "当前项目未提供兼容的 GameLogger。日志管理功能已禁用，但不会影响 UnityToolsHub 的其他工具。",
                MessageType.Info);
            return;
        }

        DrawGlobalSettings();

        EditorGUILayout.Space(4);
        DrawDivider();

        DrawChannelManagement();

        EditorGUILayout.Space(4);
        DrawDivider();

        DrawBatchActions();

        EditorGUILayout.Space(8);
    }

    // ─── 全局设置 ───────────────────────────────────────────────

    private void DrawGlobalSettings()
    {
        DrawSection("全局设置", ClrAccent);

        BeginGroupBox("日志等级");
        {
            Enum currentLevel = GameLoggerAdapter.LogLevel;
            Enum newLevel = EditorGUILayout.EnumPopup("过滤等级", currentLevel);

            if (!Equals(newLevel, currentLevel))
            {
                GameLoggerAdapter.SetLogLevel(newLevel);
            }

            EditorGUILayout.Space(2);
            DrawLabelDim("低于此等级的日志不会输出。Assert 不受此限制。");
        }
        EndGroupBox();
    }

    // ─── 通道管理 ──────────────────────────────────────────────

    private void DrawChannelManagement()
    {
        var channels = GameLoggerAdapter.GetChannelNames();
        int totalCount = channels.Count;
        int enabledCount = 0;
        foreach (var ch in channels)
            if (GameLoggerAdapter.IsChannelEnabled(ch)) enabledCount++;

        DrawSection($"通道管理 ({enabledCount}/{totalCount} 启用)", ClrCatTeal);

        if (totalCount == 0)
        {
            DrawEmptyState();
            return;
        }

        // 过滤后的通道列表
        string search = SearchText?.Trim() ?? "";
        bool hasSearch = !string.IsNullOrEmpty(search);

        for (int i = 0; i < channels.Count; i++)
        {
            string channel = channels[i];

            // 搜索过滤
            if (hasSearch && !channel.ToLower().Contains(search.ToLower()))
                continue;

            DrawChannelRow(channel, i);
        }

        if (hasSearch)
        {
            int filteredCount = 0;
            foreach (var ch in channels)
                if (ch.ToLower().Contains(search.ToLower())) filteredCount++;

            EditorGUILayout.Space(4);
            DrawLabelDim($"搜索结果：{filteredCount} 个通道");
        }
    }

    private void DrawChannelRow(string channel, int index)
    {
        bool enabled = GameLoggerAdapter.IsChannelEnabled(channel);
        bool newEnabled = enabled;

        // 交替背景色
        var rect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, index % 2 == 0 ? ClrItemBg : ClrGroupBoxBg);

        // 左侧色条（状态指示）
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3, rect.height),
            enabled ? ClrSuccess : ClrTextDim);

        // 内容区域
        var contentRect = new Rect(rect.x + 10, rect.y, rect.width - 20, rect.height);

        // 通道名称
        var nameRect = new Rect(contentRect.x, contentRect.y + 2, contentRect.width - 120, 20);
        StLabel.normal.textColor = enabled ? ClrTextBright : ClrTextDim;
        GUI.Label(nameRect, channel, StLabel);
        StLabel.normal.textColor = ClrText;

        // 状态标签
        var tagRect = new Rect(contentRect.x + 200, contentRect.y + 4, 50, 18);
        if (enabled)
            DrawInlineTag(tagRect, "启用", ClrSuccess);
        else
            DrawInlineTag(tagRect, "禁用", ClrTextDim);

        // 测试按钮
        var testBtnRect = new Rect(contentRect.xMax - 60, contentRect.y + 4, 50, 22);
        bool testHover = testBtnRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(testBtnRect, testHover ? ClrBtnHover : ClrBtnNormal);
        StBtnPrimary.normal.textColor = Color.white;
        if (GUI.Button(testBtnRect, "测试", StBtnPrimary))
        {
            Debug.Log($"[{channel}] 🔍 测试日志输出");
        }

        // Toggle 开关
        var toggleRect = new Rect(contentRect.xMax - 22, contentRect.y + 6, 20, 20);
        newEnabled = GUI.Toggle(toggleRect, enabled, "");

        if (newEnabled != enabled)
            GameLoggerAdapter.SetChannelEnabled(channel, newEnabled);

        // 消费鼠标事件防止穿透
        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            Event.current.Use();

        EditorGUILayout.Space(1);
    }

    private void DrawInlineTag(Rect rect, string text, Color color)
    {
        EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.2f));
        StLabelSmall.normal.textColor = color;
        StLabelSmall.alignment = TextAnchor.MiddleCenter;
        GUI.Label(rect, text, StLabelSmall);
        StLabelSmall.alignment = TextAnchor.MiddleLeft;
        StLabelSmall.normal.textColor = ClrTextDim;
    }

    // ─── 空状态 ───────────────────────────────────────────────

    private void DrawEmptyState()
    {
        EditorGUILayout.Space(12);

        BeginCard(80);
        {
            EditorGUILayout.Space(4);
            GUILayout.Label("📭 暂无已注册的日志通道", StTitle);
            EditorGUILayout.Space(4);
            DrawBody("脚本中使用 <b>GameLogger.RegisterChannel(\"脚本名\")</b> 注册通道后，" +
                     "此处将显示该通道的开关控件。");
            EditorGUILayout.Space(2);
            DrawLabelDim("通道在脚本首次调用日志方法时自动注册。");
        }
        EndCard();
    }

    // ─── 批量操作 ─────────────────────────────────────────────

    private void DrawBatchActions()
    {
        DrawSection("批量操作", ClrCatOrange);

        EditorGUILayout.BeginHorizontal();
        {
            if (DrawSuccessButton("全部启用", GUILayout.Width(90)))
            {
                foreach (var ch in GameLoggerAdapter.GetChannelNames())
                    GameLoggerAdapter.SetChannelEnabled(ch, true);
                Repaint();
            }

            GUILayout.Space(6);

            if (DrawDangerButton("全部禁用", GUILayout.Width(90)))
            {
                foreach (var ch in GameLoggerAdapter.GetChannelNames())
                    GameLoggerAdapter.SetChannelEnabled(ch, false);
                Repaint();
            }

            GUILayout.Space(6);

            if (DrawPrimaryButton("🔄 刷新", GUILayout.Width(80)))
                Repaint();

            GUILayout.FlexibleSpace();
        }
        EditorGUILayout.EndHorizontal();
    }

    // ─── 状态栏 ───────────────────────────────────────────────

    protected override void DrawStatusBarContent()
    {
        if (!GameLoggerAdapter.IsAvailable)
        {
            DrawStatusText("📋 GameLogger 不可用", ClrTextDim);
            return;
        }

        var channels = GameLoggerAdapter.GetChannelNames();
        int enabledCount = 0;
        foreach (var ch in channels)
            if (GameLoggerAdapter.IsChannelEnabled(ch)) enabledCount++;

        DrawStatusText($"📊 {enabledCount}/{channels.Count} 通道启用", ClrAccent);
        DrawStatusText("  ·  ", ClrTextDim);
        DrawStatusText($"等级: {GameLoggerAdapter.LogLevel}", ClrTextDim);
    }
}

/// <summary>
/// 通过反射连接项目可选的 GameLogger，避免 UnityToolsHub 对特定 UnityFramework 版本产生编译期依赖。
/// </summary>
internal static class GameLoggerAdapter
{
    private const BindingFlags StaticPublic = BindingFlags.Public | BindingFlags.Static;

    private static readonly Type LoggerType = FindLoggerType();
    private static readonly PropertyInfo LogLevelProperty = LoggerType?.GetProperty("LogLevel", StaticPublic);
    private static readonly MethodInfo SaveLogLevelMethod = LoggerType?.GetMethod("SaveLogLevel", StaticPublic);
    private static readonly MethodInfo GetChannelNamesMethod = LoggerType?.GetMethod("GetChannelNames", StaticPublic);
    private static readonly MethodInfo IsChannelEnabledMethod = LoggerType?.GetMethod(
        "IsChannelEnabled", StaticPublic, null, new[] { typeof(string) }, null);
    private static readonly MethodInfo SetChannelEnabledMethod = LoggerType?.GetMethod(
        "SetChannelEnabled", StaticPublic, null, new[] { typeof(string), typeof(bool) }, null);

    internal static bool IsAvailable =>
        LogLevelProperty != null &&
        LogLevelProperty.PropertyType.IsEnum &&
        GetChannelNamesMethod != null &&
        IsChannelEnabledMethod != null &&
        SetChannelEnabledMethod != null;

    internal static Enum LogLevel => LogLevelProperty?.GetValue(null, null) as Enum;

    internal static void SetLogLevel(Enum level)
    {
        if (!IsAvailable || level == null || level.GetType() != LogLevelProperty.PropertyType) return;
        LogLevelProperty.SetValue(null, level, null);
        SaveLogLevelMethod?.Invoke(null, null);
    }

    internal static List<string> GetChannelNames()
    {
        var channels = new List<string>();
        if (!IsAvailable || !(GetChannelNamesMethod.Invoke(null, null) is IEnumerable values)) return channels;

        foreach (object value in values)
        {
            if (value is string channel) channels.Add(channel);
        }

        return channels;
    }

    internal static bool IsChannelEnabled(string channel)
    {
        return IsAvailable &&
               IsChannelEnabledMethod.Invoke(null, new object[] { channel }) is bool enabled &&
               enabled;
    }

    internal static void SetChannelEnabled(string channel, bool enabled)
    {
        if (!IsAvailable) return;
        SetChannelEnabledMethod.Invoke(null, new object[] { channel, enabled });
    }

    private static Type FindLoggerType()
    {
        Type type = Type.GetType("GameLogger, UnityFramework", false);
        if (type != null) return type;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType("GameLogger", false);
            if (type != null) return type;
        }

        return null;
    }
}
