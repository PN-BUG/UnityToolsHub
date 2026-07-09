#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ┏━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓
///  ToolEditorWindow — UnityToolsHub 工具编辑器面板基类
/// ┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛
///
///  统一深色主题、样式系统、绘图工具方法。
///  消除各工具面板中大量重复的调色板 / 纹理 / GUIStyle / 通用绘制代码。
///
///  ── 使用方式 ──────────────────────────────────────────────
///  继承 ToolEditorWindow 替代 EditorWindow：
///
///     [ToolInfo("我的工具", "编辑器工具", Description = "...", Icon = "🔧")]
///     public class MyTool : ToolEditorWindow
///     {
///         protected override string ToolTitle => "我的工具";
///         protected override string ToolIcon  => "🔧";
///
///         protected override void DrawToolContent()
///         {
///             // 工具主体 UI，自动拥有滚动视图、深色背景
///         }
///     }
///
///  ── 可选重写 ──────────────────────────────────────────────
///  DrawToolbarContent()   — 工具栏右侧自定义区域
///  DrawStatusBarContent() — 底部状态栏内容
///  OnToolEnable()         — 初始化逻辑（替代 OnEnable）
///  OnToolDisable()        — 清理逻辑（替代 OnDisable）
///
///  ── 内置绘制方法速查 ──────────────────────────────────────
///  按钮:  DrawPrimaryButton / DrawDangerButton / DrawSuccessButton
///         DrawWarnButton / DrawFlatButton / DrawBackButton / DrawIconButton
///  标签:  DrawTag / DrawTags / DrawKeyCap
///  布局:  DrawSection / DrawDivider / DrawGradientBar
///  容器:  BeginCard / EndCard / BeginGroupBox / EndGroupBox
///  信息:  DrawToolHelpBox / DrawStatCard / DrawProgressBar
///  列表:  DrawRemovableListItem / DrawSelectableItem / DrawToggle
///  输入:  DrawSearchField / DrawDropArea
///  文本:  DrawTitle / DrawSubtitle / DrawBody / DrawLabelDim
///  颜色:  DrawColorSwatch
/// </summary>
public abstract class ToolEditorWindow : EditorWindow
{
    #region ── 颜色调色板（统一深色主题，引用 HubPalette 单一来源）──

    // ── 背景色 ──────────────────────────────────────────────
    protected static readonly Color ClrBg           = HubPalette.Bg;
    protected static readonly Color ClrToolbarBg    = HubPalette.ToolbarBg;
    protected static readonly Color ClrSearchBg     = HubPalette.SearchBg;
    protected static readonly Color ClrItemBg       = HubPalette.ItemBg;
    protected static readonly Color ClrItemHover    = HubPalette.ItemHover;
    protected static readonly Color ClrItemSelected = HubPalette.ItemSelected;
    protected static readonly Color ClrCardBg       = HubPalette.CardBg;
    protected static readonly Color ClrGroupBoxBg   = HubPalette.GroupBoxBg;
    protected static readonly Color ClrTagBg        = HubPalette.TagBg;
    protected static readonly Color ClrStatusBar    = HubPalette.StatusBar;
    protected static readonly Color ClrIconBg       = HubPalette.IconBg;
    protected static readonly Color ClrHelpBoxBg    = HubPalette.HelpBoxBg;
    protected static readonly Color ClrProgressBg   = HubPalette.ProgressBg;
    protected static readonly Color ClrKeyCapBg     = HubPalette.KeyCapBg;

    // ── 文字色 ──────────────────────────────────────────────
    protected static readonly Color ClrText       = HubPalette.Text;
    protected static readonly Color ClrTextDim    = HubPalette.TextDim;
    protected static readonly Color ClrTextBright = HubPalette.TextBright;

    // ── 主题色 ──────────────────────────────────────────────
    protected static readonly Color ClrAccent    = HubPalette.Accent;
    protected static readonly Color ClrAccentDim = HubPalette.AccentDim;
    protected static readonly Color ClrDivider   = HubPalette.Divider;

    // ── 按钮色 ──────────────────────────────────────────────
    protected static readonly Color ClrBtnNormal    = HubPalette.BtnNormal;
    protected static readonly Color ClrBtnHover     = HubPalette.BtnHover;
    protected static readonly Color ClrBtnDanger    = HubPalette.BtnDanger;
    protected static readonly Color ClrBtnDangerHov = HubPalette.BtnDangerHov;
    protected static readonly Color ClrBtnSuccess   = HubPalette.BtnSuccess;
    protected static readonly Color ClrBtnSuccessHov= HubPalette.BtnSuccessHov;
    protected static readonly Color ClrBtnWarn      = HubPalette.BtnWarn;
    protected static readonly Color ClrBtnWarnHov   = HubPalette.BtnWarnHov;

    // ── 语义色 ──────────────────────────────────────────────
    protected static readonly Color ClrSuccess  = HubPalette.Success;
    protected static readonly Color ClrWarning  = HubPalette.Warning;
    protected static readonly Color ClrError    = HubPalette.Error;
    protected static readonly Color ClrInfo     = HubPalette.Info;

    // ── 拖拽叠加色 ──────────────────────────────────────────
    protected static readonly Color ClrDropOverlay = HubPalette.DropOverlay;
    protected static readonly Color ClrDropBorder  = HubPalette.DropBorder;

    // ── 分类配色（供工具卡片等场景复用）──────────────────
    protected static readonly Color ClrCatDefault = HubPalette.CatDefault;
    protected static readonly Color ClrCatGreen   = HubPalette.CatGreen;
    protected static readonly Color ClrCatOrange  = HubPalette.CatOrange;
    protected static readonly Color ClrCatPurple  = HubPalette.CatPurple;
    protected static readonly Color ClrCatRed     = HubPalette.CatRed;
    protected static readonly Color ClrCatTeal    = HubPalette.CatTeal;
    protected static readonly Color ClrCatPink    = HubPalette.CatPink;
    protected static readonly Color ClrCatYellow  = HubPalette.CatYellow;

    #endregion

    #region ── 配置属性（子类可重写）──

    protected virtual string ToolTitle => titleContent.text;
    protected virtual string ToolIcon  => "";
    protected virtual bool ShowSearchBar   => false;
    protected virtual bool ShowStatusBar   => false;
    protected virtual float SearchBarHeight  => 28f;
    protected virtual float ToolbarHeight    => 36f;
    protected virtual float StatusBarHeight  => 22f;
    protected virtual float ContentPadding   => 12f;

    #endregion

    #region ── 纹理缓存 ──

    private static Texture2D _texWhite;
    private static Texture2D _texHover;
    private static Texture2D _texSelected;
    private static Texture2D _texTransparent;
    private static Texture2D _texCardBg;

    protected static Texture2D TexWhite       => _texWhite ?? (_texWhite = CreateTex(1, 1, Color.white));
    protected static Texture2D TexHover       => _texHover ?? (_texHover = CreateTex(1, 1, ClrItemHover));
    protected static Texture2D TexSelected    => _texSelected ?? (_texSelected = CreateTex(1, 1, ClrItemSelected));
    protected static Texture2D TexTransparent => _texTransparent ?? (_texTransparent = CreateTex(1, 1, new Color(0, 0, 0, 0)));
    protected static Texture2D TexCardBg      => _texCardBg ?? (_texCardBg = CreateTex(1, 1, ClrCardBg));

    #endregion

    #region ── 样式缓存 ──

    private static GUIStyle _stToolbar;
    private static GUIStyle _stSearchField;
    private static GUIStyle _stSearchPlaceholder;
    private static GUIStyle _stTitle;
    private static GUIStyle _stSubtitle;
    private static GUIStyle _stBody;
    private static GUIStyle _stLabel;
    private static GUIStyle _stLabelDim;
    private static GUIStyle _stLabelSmall;
    private static GUIStyle _stSection;
    private static GUIStyle _stBtnPrimary;
    private static GUIStyle _stBtnFlat;
    private static GUIStyle _stBtnDanger;
    private static GUIStyle _stBtnSuccess;
    private static GUIStyle _stBtnWarn;
    private static GUIStyle _stCard;
    private static GUIStyle _stTag;
    private static GUIStyle _stHelpBox;
    private static GUIStyle _stStatusBar;
    private static GUIStyle _stIconLabel;
    private static GUIStyle _stBackButton;
    private static GUIStyle _stKeyCap;
    private static GUIStyle _stStatNum;
    private static GUIStyle _stStatLabel;
    private static GUIStyle _stSelectable;
    private static GUIStyle _stSelectableActive;
    private static bool _stylesReady;

    protected static GUIStyle StToolbar       => EnsureInit() ? _stToolbar : null;
    protected static GUIStyle StSearchField   => EnsureInit() ? _stSearchField : null;
    protected static GUIStyle StSearchPlaceholder => EnsureInit() ? _stSearchPlaceholder : null;
    protected static GUIStyle StTitle         => EnsureInit() ? _stTitle : null;
    protected static GUIStyle StSubtitle      => EnsureInit() ? _stSubtitle : null;
    protected static GUIStyle StBody          => EnsureInit() ? _stBody : null;
    protected static GUIStyle StLabel         => EnsureInit() ? _stLabel : null;
    protected static GUIStyle StLabelDim      => EnsureInit() ? _stLabelDim : null;
    protected static GUIStyle StLabelSmall    => EnsureInit() ? _stLabelSmall : null;
    protected static GUIStyle StSection       => EnsureInit() ? _stSection : null;
    protected static GUIStyle StBtnPrimary    => EnsureInit() ? _stBtnPrimary : null;
    protected static GUIStyle StBtnFlat       => EnsureInit() ? _stBtnFlat : null;
    protected static GUIStyle StBtnDanger     => EnsureInit() ? _stBtnDanger : null;
    protected static GUIStyle StBtnSuccess    => EnsureInit() ? _stBtnSuccess : null;
    protected static GUIStyle StBtnWarn       => EnsureInit() ? _stBtnWarn : null;
    protected static GUIStyle StCard          => EnsureInit() ? _stCard : null;
    protected static GUIStyle StTag           => EnsureInit() ? _stTag : null;
    protected static GUIStyle StHelpBox       => EnsureInit() ? _stHelpBox : null;
    protected static GUIStyle StStatusBar     => EnsureInit() ? _stStatusBar : null;
    protected static GUIStyle StIconLabel     => EnsureInit() ? _stIconLabel : null;
    protected static GUIStyle StBackButton    => EnsureInit() ? _stBackButton : null;
    protected static GUIStyle StKeyCap        => EnsureInit() ? _stKeyCap : null;
    protected static GUIStyle StStatNum       => EnsureInit() ? _stStatNum : null;
    protected static GUIStyle StStatLabel     => EnsureInit() ? _stStatLabel : null;
    protected static GUIStyle StSelectable    => EnsureInit() ? _stSelectable : null;
    protected static GUIStyle StSelectableActive => EnsureInit() ? _stSelectableActive : null;

    #endregion

    #region ── 搜索状态 ──

    protected string SearchText = "";
    protected Vector2 ContentScroll;

    #endregion

    #region ── 生命周期 ──

    private void OnEnable()
    {
        EnsureInit();
        OnToolEnable();
    }

    private void OnDisable()
    {
        OnToolDisable();
    }

    private void OnGUI()
    {
        EnsureInit();
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), ClrBg);

        float y = 0;

        // ── 工具栏 + 搜索栏 ──
        if (ShowSearchBar)
        {
            var toolbarRect = new Rect(0, y, position.width, ToolbarHeight);
            DrawToolbar(toolbarRect);
            y += ToolbarHeight;

            var searchRect = new Rect(0, y, position.width, SearchBarHeight);
            DrawSearchBarArea(searchRect);
            y += SearchBarHeight;
        }

        // ── 主内容区 ──
        float statusH = ShowStatusBar ? StatusBarHeight : 0;
        float contentH = position.height - y - statusH;
        EditorGUI.DrawRect(new Rect(0, y, position.width, contentH), ClrBg);

        var contentRect = new Rect(ContentPadding, y, position.width - ContentPadding * 2, contentH);
        GUILayout.BeginArea(contentRect);
        ContentScroll = EditorGUILayout.BeginScrollView(ContentScroll);
        DrawToolContent();
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();

        // ── 状态栏 ──
        if (ShowStatusBar)
        {
            var statusRect = new Rect(0, position.height - StatusBarHeight, position.width, StatusBarHeight);
            DrawStatusBarArea(statusRect);
        }
    }

    #endregion

    #region ── 虚拟方法 ──

    protected abstract void DrawToolContent();
    protected virtual void OnToolEnable() { }
    protected virtual void OnToolDisable() { }
    protected virtual void DrawToolbarContent() { }
    protected virtual void DrawStatusBarContent() { }

    #endregion

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  工具栏 & 搜索栏
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    #region ── 工具栏 ──

    private void DrawToolbar(Rect rect)
    {
        EditorGUI.DrawRect(rect, ClrToolbarBg);

        var iconRect = new Rect(rect.x + 12, rect.y, 28, rect.height);
        _stIconLabel.normal.textColor = ClrAccent;
        GUI.Label(iconRect, ToolIcon, _stIconLabel);

        var titleRect = new Rect(rect.x + 36, rect.y, 200, rect.height);
        _stToolbar.normal.textColor = ClrTextBright;
        GUI.Label(titleRect, ToolTitle, _stToolbar);

        var customRect = new Rect(rect.x + 220, rect.y, rect.width - 230, rect.height);
        GUILayout.BeginArea(customRect);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        DrawToolbarContent();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void DrawSearchBarArea(Rect rect)
    {
        EditorGUI.DrawRect(rect, ClrSearchBg);
        var fieldRect = new Rect(rect.x + 10, rect.y + 3, rect.width - 20, rect.height - 6);
        var iconRect = new Rect(fieldRect.x + 4, fieldRect.y, 20, fieldRect.height);
        GUI.Label(iconRect, "🔍", _stIconLabel);

        var inputRect = new Rect(fieldRect.x + 24, fieldRect.y, fieldRect.width - 28, fieldRect.height);
        if (string.IsNullOrEmpty(SearchText))
            GUI.Label(inputRect, "搜索...", _stSearchPlaceholder);
        SearchText = GUI.TextField(inputRect, SearchText, _stSearchField);
    }

    private void DrawStatusBarArea(Rect rect)
    {
        EditorGUI.DrawRect(rect, ClrStatusBar);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), ClrDivider);

        GUILayout.BeginArea(rect);
        GUILayout.BeginHorizontal();
        GUILayout.Space(10);
        DrawStatusBarContent();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    #endregion

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  按钮
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    #region ── 按钮 ──

    protected bool DrawPrimaryButton(string text, params GUILayoutOption[] options)
        => DrawColoredButton(text, ClrBtnNormal, ClrBtnHover, options);

    protected bool DrawDangerButton(string text, params GUILayoutOption[] options)
        => DrawColoredButton(text, ClrBtnDanger, ClrBtnDangerHov, options);

    protected bool DrawSuccessButton(string text, params GUILayoutOption[] options)
        => DrawColoredButton(text, ClrBtnSuccess, ClrBtnSuccessHov, options);

    protected bool DrawWarnButton(string text, params GUILayoutOption[] options)
        => DrawColoredButton(text, ClrBtnWarn, ClrBtnWarnHov, options);

    protected bool DrawFlatButton(string text, params GUILayoutOption[] options)
        => GUILayout.Button(text, _stBtnFlat, options);

    protected bool DrawBackButton(string label = "← 返回")
        => GUILayout.Button(label, _stBackButton, GUILayout.Width(60));

    protected bool DrawIconButton(string icon, string tooltip = "", params GUILayoutOption[] options)
    {
        var content = new GUIContent(icon, tooltip);
        var size = _stIconLabel.CalcSize(content);
        var rect = GUILayoutUtility.GetRect(content, _stIconLabel,
            GUILayout.Width(size.x + 8), GUILayout.Height(size.y + 4));

        bool hover = rect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(rect, hover ? ClrItemHover : new Color(0, 0, 0, 0));
        GUI.Label(rect, content, _stIconLabel);
        return Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition);
    }

    private bool DrawColoredButton(string text, Color normal, Color hover, params GUILayoutOption[] options)
    {
        var style = _stBtnPrimary;
        style.normal.background = TexWhite;
        style.normal.textColor = Color.white;
        style.hover.background = TexWhite;
        style.hover.textColor = Color.white;

        var rect = GUILayoutUtility.GetRect(new GUIContent(text), style, options);
        bool isHover = rect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(rect, isHover ? hover : normal);
        return GUI.Button(rect, text, style);
    }

    #endregion

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  标签 & 文本
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    #region ── 标签 & 文本 ──

    protected void DrawTag(string text, Color? color = null)
    {
        var bgColor = color ?? ClrTagBg;
        var content = new GUIContent(text);
        var size = _stTag.CalcSize(content);
        var rect = GUILayoutUtility.GetRect(content, _stTag,
            GUILayout.Width(size.x + 16), GUILayout.Height(size.y + 6));
        EditorGUI.DrawRect(rect, bgColor);
        _stTag.normal.textColor = ClrText;
        GUI.Label(rect, content, _stTag);
    }

    protected void DrawTags(string[] tags, Color? color = null)
    {
        if (tags == null || tags.Length == 0) return;
        EditorGUILayout.BeginHorizontal();
        foreach (var tag in tags)
        {
            DrawTag(tag, color);
            GUILayout.Space(4);
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>绘制键盘快捷键标签</summary>
    protected void DrawKeyCap(string text)
    {
        var content = new GUIContent(text);
        var size = _stKeyCap.CalcSize(content);
        var rect = GUILayoutUtility.GetRect(content, _stKeyCap,
            GUILayout.Width(size.x + 12), GUILayout.Height(size.y + 4));
        EditorGUI.DrawRect(rect, ClrKeyCapBg);
        GUI.Label(rect, content, _stKeyCap);
    }

    protected void DrawTitle(string text, Color? color = null)
    {
        if (color.HasValue) _stTitle.normal.textColor = color.Value;
        else _stTitle.normal.textColor = ClrTextBright;
        GUILayout.Label(text, _stTitle);
    }

    protected void DrawSubtitle(string text)
    {
        GUILayout.Label(text, _stSubtitle);
    }

    protected void DrawBody(string text)
    {
        GUILayout.Label(text, _stBody);
    }

    protected void DrawLabelDim(string text)
    {
        GUILayout.Label(text, _stLabelDim);
    }

    protected void DrawLabelSmall(string text)
    {
        GUILayout.Label(text, _stLabelSmall);
    }

    protected void DrawStatusText(string text, Color? color = null)
    {
        _stStatusBar.normal.textColor = color ?? ClrTextDim;
        GUILayout.Label(text, _stStatusBar);
    }

    #endregion

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  区域布局
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    #region ── 区域布局 ──

    protected void DrawSection(string title, Color? accentColor = null)
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        var colorRect = GUILayoutUtility.GetRect(3, 16, GUILayout.Width(3));
        EditorGUI.DrawRect(colorRect, accentColor ?? ClrAccent);
        GUILayout.Space(6);
        GUILayout.Label(title, _stSection);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);
    }

    protected void DrawToolHelpBox(string text, MessageType type = MessageType.Info)
    {
        var content = new GUIContent(text);
        float h = _stHelpBox.CalcHeight(content, position.width - ContentPadding * 2 - 24);
        var rect = GUILayoutUtility.GetRect(content, _stHelpBox, GUILayout.Height(h + 16));

        Color borderColor;
        switch (type)
        {
            case MessageType.Warning: borderColor = ClrWarning; break;
            case MessageType.Error:   borderColor = ClrError;   break;
            default:                  borderColor = ClrAccentDim; break;
        }

        EditorGUI.DrawRect(rect, ClrHelpBoxBg);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3, rect.height), borderColor);
        GUI.Label(new Rect(rect.x + 10, rect.y + 6, rect.width - 20, rect.height - 12), content, _stHelpBox);
    }

    protected void DrawGradientBar(Color left, Color right, float height = 4f)
    {
        HubDrawing.DrawGradientRect(new Rect(0, 0, position.width, height), left, right);
    }

    protected void DrawDivider(float height = 1f)
    {
        EditorGUILayout.Space(4);
        EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true)), ClrDivider);
        EditorGUILayout.Space(4);
    }

    protected void DrawStatCard(string label, string value, Color accent, params GUILayoutOption[] options)
    {
        var rect = GUILayoutUtility.GetRect(0, 0, options);
        EditorGUI.DrawRect(rect, ClrCardBg);
        EditorGUI.DrawRect(new Rect(rect.x + 8, rect.y + 2, rect.width - 16, 2), accent);

        _stStatNum.normal.textColor = accent;
        var numRect = new Rect(rect.x, rect.y + 10, rect.width, 28);
        GUI.Label(numRect, value, _stStatNum);
        GUI.Label(new Rect(rect.x, rect.y + 38, rect.width, 16), label, _stStatLabel);
    }

    /// <summary>绘制进度条</summary>
    protected void DrawProgressBar(float progress, string label = "", Color? color = null, float height = 16f)
    {
        var rect = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
        var fillColor = color ?? ClrAccent;

        EditorGUI.DrawRect(rect, ClrProgressBg);
        if (progress > 0)
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(progress), rect.height), fillColor);

        if (!string.IsNullOrEmpty(label))
        {
            _stLabelDim.alignment = TextAnchor.MiddleCenter;
            GUI.Label(rect, label, _stLabelDim);
            _stLabelDim.alignment = TextAnchor.MiddleLeft;
        }
    }

    protected Rect BeginCard(float height = 0, params GUILayoutOption[] options)
    {
        Rect rect;
        if (height > 0)
            rect = GUILayoutUtility.GetRect(0, height, options);
        else
            rect = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, ClrCardBg);
        EditorGUI.DrawRect(new Rect(rect.x + 8, rect.y + 2, rect.width - 16, 2), ClrAccent);
        GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, rect.height - 16));
        return rect;
    }

    protected void EndCard()
    {
        GUILayout.EndArea();
        EditorGUILayout.Space(4);
    }

    /// <summary>开始分组框（带标题的容器）</summary>
    protected void BeginGroupBox(string title)
    {
        EditorGUILayout.Space(4);
        var headerRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(headerRect, ClrGroupBoxBg);
        _stSection.normal.textColor = ClrTextBright;
        GUI.Label(new Rect(headerRect.x + 8, headerRect.y, headerRect.width - 16, headerRect.height), title, _stSection);
        EditorGUILayout.Space(2);
        EditorGUI.indentLevel++;
    }

    protected void EndGroupBox()
    {
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(6);
    }

    #endregion

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  列表 & 输入
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    #region ── 列表 & 输入 ──

    protected void DrawRemovableListItem(string text, Action onRemove)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(text, _stLabel);
        if (GUILayout.Button("✕", GUILayout.Width(24)))
            onRemove?.Invoke();
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>绘制可选中的列表项（返回是否点击）</summary>
    protected bool DrawSelectableItem(string text, bool selected, Color? accentColor = null)
    {
        var rect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
        bool hover = rect.Contains(Event.current.mousePosition);
        bool clicked = Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition);

        if (selected)
            EditorGUI.DrawRect(rect, ClrItemSelected);
        else if (hover)
            EditorGUI.DrawRect(rect, ClrItemHover);

        if (selected)
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3, rect.height), accentColor ?? ClrAccent);

        var style = selected ? _stSelectableActive : _stSelectable;
        GUI.Label(new Rect(rect.x + 12, rect.y, rect.width - 16, rect.height), text, style);

        if (clicked) Event.current.Use();
        return clicked;
    }

    protected bool DrawToggle(string label, bool value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, _stLabel, GUILayout.ExpandWidth(true));
        bool result = EditorGUILayout.Toggle(value, GUILayout.Width(20));
        EditorGUILayout.EndHorizontal();
        return result;
    }

    protected string DrawSearchField(string current, string placeholder = "搜索...", params GUILayoutOption[] options)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("🔍", _stIconLabel, GUILayout.Width(20));
        var result = EditorGUILayout.TextField(current, _stSearchField, options);
        EditorGUILayout.EndHorizontal();
        return result;
    }

    protected bool DrawDropArea(string label)
    {
        var rect = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        bool isHover = rect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(rect, isHover ? ClrDropOverlay : ClrGroupBoxBg);
        if (isHover)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), ClrDropBorder);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), ClrDropBorder);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), ClrDropBorder);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), ClrDropBorder);
        }
        _stLabelDim.alignment = TextAnchor.MiddleCenter;
        GUI.Label(rect, label, _stLabelDim);
        _stLabelDim.alignment = TextAnchor.MiddleLeft;
        return isHover;
    }

    /// <summary>绘制颜色方块预览</summary>
    protected void DrawColorSwatch(Color color, float size = 16f)
    {
        var rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size));
        EditorGUI.DrawRect(rect, color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), ClrDivider);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), ClrDivider);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), ClrDivider);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), ClrDivider);
    }

    #endregion

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  持久化辅助
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    #region ── 持久化辅助 ──

    protected static string MakePrefsKey(string toolName, string suffix = "")
    {
        string baseKey = $"{toolName}_{Application.dataPath.GetHashCode():X}";
        return string.IsNullOrEmpty(suffix) ? baseKey : $"{baseKey}_{suffix}";
    }

    protected static System.Collections.Generic.List<string> LoadStringList(string prefsKey)
    {
        var list = new System.Collections.Generic.List<string>();
        var raw = EditorPrefs.GetString(prefsKey, "");
        if (string.IsNullOrEmpty(raw)) return list;
        foreach (var item in raw.Split('|'))
        {
            string s = item.Trim();
            if (!string.IsNullOrEmpty(s)) list.Add(s);
        }
        return list;
    }

    protected static void SaveStringList(string prefsKey, System.Collections.Generic.List<string> list)
    {
        EditorPrefs.SetString(prefsKey, string.Join("|", list));
    }

    #endregion

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  纹理创建
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    #region ── 纹理创建 ──

    protected static Texture2D CreateTex(int w, int h, Color color)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = color;
        tex.SetPixels(px);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }

    #endregion

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  样式初始化
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    #region ── 样式初始化 ──

    private static bool EnsureInit()
    {
        if (_stylesReady) return true;

        _stToolbar = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft, normal = { textColor = ClrTextBright }, richText = true };

        _stSearchField = new GUIStyle("ToolbarSeachTextField")
            { fontSize = 12, normal = { textColor = ClrText }, fixedHeight = 20 };

        _stSearchPlaceholder = new GUIStyle { fontSize = 12,
            normal = { textColor = ClrTextDim }, alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(28, 8, 0, 0) };

        _stTitle = new GUIStyle { fontSize = 16, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft, normal = { textColor = ClrTextBright }, richText = true };

        _stSubtitle = new GUIStyle { fontSize = 12,
            normal = { textColor = ClrTextDim }, alignment = TextAnchor.MiddleLeft, richText = true };

        _stBody = new GUIStyle { fontSize = 12,
            normal = { textColor = ClrText }, wordWrap = true, richText = true,
            padding = new RectOffset(0, 0, 2, 2) };

        _stLabel = new GUIStyle(EditorStyles.label) { fontSize = 12, normal = { textColor = ClrText } };

        _stLabelDim = new GUIStyle { fontSize = 11,
            normal = { textColor = ClrTextDim }, alignment = TextAnchor.MiddleLeft };

        _stLabelSmall = new GUIStyle { fontSize = 10, normal = { textColor = ClrTextDim } };

        _stSection = new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold,
            normal = { textColor = ClrTextBright }, alignment = TextAnchor.MiddleLeft };

        _stBtnPrimary = new GUIStyle(EditorStyles.miniButton) { fontSize = 12, fontStyle = FontStyle.Bold,
            fixedHeight = 28, alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white, background = TexWhite },
            hover = { textColor = Color.white, background = TexWhite },
            active = { textColor = Color.white, background = TexWhite } };

        _stBtnFlat = new GUIStyle { fontSize = 11, alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(8, 8, 4, 4),
            normal = { textColor = ClrAccent }, hover = { textColor = ClrBtnHover } };

        _stBtnDanger = new GUIStyle(_stBtnPrimary);
        _stBtnSuccess = new GUIStyle(_stBtnPrimary);
        _stBtnWarn = new GUIStyle(_stBtnPrimary);

        _stCard = new GUIStyle { padding = new RectOffset(10, 10, 8, 8) };

        _stTag = new GUIStyle { fontSize = 10, alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrText }, padding = new RectOffset(6, 6, 2, 2) };

        _stHelpBox = new GUIStyle { fontSize = 11,
            normal = { textColor = ClrText }, wordWrap = true, richText = true,
            alignment = TextAnchor.UpperLeft };

        _stStatusBar = new GUIStyle { fontSize = 10, alignment = TextAnchor.MiddleLeft,
            normal = { textColor = ClrTextDim }, padding = new RectOffset(4, 4, 0, 0) };

        _stIconLabel = new GUIStyle { fontSize = 16, alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrAccent } };

        _stBackButton = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(6, 6, 4, 4),
            normal = { textColor = ClrAccent }, hover = { textColor = ClrBtnHover } };

        _stKeyCap = new GUIStyle { fontSize = 10, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrTextDim, background = CreateTex(1, 1, ClrKeyCapBg) },
            padding = new RectOffset(4, 4, 2, 2) };

        _stStatNum = new GUIStyle { fontSize = 24, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter, normal = { textColor = ClrAccent } };

        _stStatLabel = new GUIStyle { fontSize = 10,
            alignment = TextAnchor.MiddleCenter, normal = { textColor = ClrTextDim } };

        _stSelectable = new GUIStyle { fontSize = 12,
            normal = { textColor = ClrText }, alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(8, 8, 2, 2) };

        _stSelectableActive = new GUIStyle(_stSelectable)
            { fontStyle = FontStyle.Bold, normal = { textColor = ClrTextBright } };

        _stylesReady = true;
        return true;
    }

    #endregion

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  资源清理
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    #region ── 资源清理 ──

    [InitializeOnLoadMethod]
    private static void RegisterCleanup()
    {
        AssemblyReloadEvents.beforeAssemblyReload += CleanupTextures;
    }

    private static void CleanupTextures()
    {
        if (_texWhite != null)       { DestroyImmediate(_texWhite);       _texWhite = null; }
        if (_texHover != null)       { DestroyImmediate(_texHover);       _texHover = null; }
        if (_texSelected != null)    { DestroyImmediate(_texSelected);    _texSelected = null; }
        if (_texTransparent != null) { DestroyImmediate(_texTransparent); _texTransparent = null; }
        if (_texCardBg != null)      { DestroyImmediate(_texCardBg);      _texCardBg = null; }

        _stylesReady = false;
    }

    #endregion
}
#endif
