#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// UnityToolsHub — GUIStyle 缓存与初始化
/// 所有 GUI 样式集中管理，EnsureStyles() 懒加载
/// </summary>
public partial class UnityToolsHub
{
    #region 样式缓存
    private GUIStyle _styleCategoryHeader;
    private GUIStyle _styleToolItem;
    private GUIStyle _styleToolItemSelected;
    private GUIStyle _styleRightTitle;
    private GUIStyle _styleRightSubtitle;
    private GUIStyle _styleDescription;
    private GUIStyle _styleCard;
    private GUIStyle _styleTag;
    private GUIStyle _styleWelcomeTitle;
    private GUIStyle _styleWelcomeSub;
    private GUIStyle _styleStatNum;
    private GUIStyle _styleStatLabel;
    private GUIStyle _styleBtnPrimary;
    private GUIStyle _styleBtnFlat;
    private GUIStyle _styleSectionHeader;
    private GUIStyle _styleShortcut;
    private GUIStyle _styleInvisibleBtn;
    // ── 缓存热路径 GUIStyle（避免每帧 OnGUI 分配）─
    private GUIStyle _styleLogo;
    private GUIStyle _styleVersion;
    private GUIStyle _styleCatCardIcon;
    private GUIStyle _styleCatCardName;
    private GUIStyle _styleCatCardCount;
    private GUIStyle _styleBackButton;
    private GUIStyle _styleEmptyHint;
    private GUIStyle _styleKeyCap;
    private GUIStyle _styleHiddenItemName;
    private GUIStyle _styleHiddenItemDesc;
    private Texture2D _texWhite;
    private Texture2D _texHover;
    private Texture2D _texSelected;
    private Texture2D _texTransparent;
    private Texture2D _texArrowExpanded;
    private Texture2D _texArrowCollapsed;
    private bool _stylesReady;
    #endregion

    #region 样式初始化
    private void EnsureStyles()
    {
        if (_stylesReady) return;

        // ── 创建背景纹理 ──────────────────────────────────
        _texWhite = MakeTex(1, 1, Color.white);
        _texHover = MakeTex(1, 1, ClrHover);
        _texSelected = MakeTex(1, 1, ClrSelection);
        _texTransparent = MakeTex(1, 1, new Color(0, 0, 0, 0));

        // 预烘焙折叠箭头纹理
        _texArrowExpanded  = MakeArrowTex(true);
        _texArrowCollapsed = MakeArrowTex(false);

        // ── 左侧分类标题（纯 Label，箭头手动绘制）──────
        _styleCategoryHeader = new GUIStyle()
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(18, 8, 0, 0),
            margin = new RectOffset(0, 0, 0, 0),
            normal = { textColor = ClrTextDim, background = _texTransparent },
            richText = true
        };

        // ── 工具项（Label，带背景纹理）─────────────────────
        _styleToolItem = new GUIStyle(EditorStyles.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(26, 8, 0, 0),
            margin = new RectOffset(0, 0, 0, 0),
            normal = { textColor = ClrText, background = _texTransparent },
            hover = { textColor = ClrTextBright, background = _texHover },
            active = { textColor = ClrTextBright, background = _texSelected },
            richText = true
        };

        // ── 工具项选中态 ──────────────────────────────────
        _styleToolItemSelected = new GUIStyle(_styleToolItem)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = ClrTextBright, background = _texSelected },
            hover = { textColor = Color.white, background = _texSelected },
            active = { textColor = Color.white, background = _texSelected }
        };

        // ── 右侧标题 ──────────────────────────────────────
        _styleRightTitle = new GUIStyle()
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            normal = { textColor = ClrTextBright },
            richText = true,
            padding = new RectOffset(0, 0, 0, 0)
        };

        _styleRightSubtitle = new GUIStyle()
        {
            fontSize = 11,
            normal = { textColor = ClrTextDim },
            richText = true
        };

        // ── 描述文本 ──────────────────────────────────────
        _styleDescription = new GUIStyle()
        {
            fontSize = 13,
            wordWrap = true,
            normal = { textColor = ClrText },
            richText = true,
            padding = new RectOffset(2, 2, 2, 2)
        };

        // ── 卡片 ──────────────────────────────────────────
        _styleCard = new GUIStyle()
        {
            padding = new RectOffset(14, 14, 12, 12)
        };

        // ── 标签 ──────────────────────────────────────────
        _styleTag = new GUIStyle()
        {
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrText },
            padding = new RectOffset(8, 8, 3, 3)
        };

        // ── 欢迎页 ────────────────────────────────────────
        _styleWelcomeTitle = new GUIStyle()
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrTextBright },
            richText = true
        };

        _styleWelcomeSub = new GUIStyle()
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrTextDim },
            richText = true
        };

        _styleStatNum = new GUIStyle()
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrAccent }
        };

        _styleStatLabel = new GUIStyle()
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrTextDim }
        };

        // ── 按钮 ──────────────────────────────────────────
        _styleBtnPrimary = new GUIStyle()
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
            hover = { textColor = Color.white },
            active = { textColor = new Color(0.8f, 0.8f, 0.8f) },
            padding = new RectOffset(16, 16, 8, 8)
        };

        _styleBtnFlat = new GUIStyle()
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = ClrText },
            hover = { textColor = ClrTextBright },
            padding = new RectOffset(10, 10, 6, 6)
        };

        // ── 分节标题 ──────────────────────────────────────
        _styleSectionHeader = new GUIStyle()
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = ClrTextDim },
            padding = new RectOffset(0, 0, 4, 4)
        };

        // ── 快捷键标签 ────────────────────────────────────
        _styleShortcut = new GUIStyle()
        {
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrTextDim },
            padding = new RectOffset(5, 5, 2, 2)
        };

        // ── 透明按钮（点击可见、外观透明，替代 GUIStyle.none 修复点击失效）──
        _styleInvisibleBtn = new GUIStyle()
        {
            normal = { background = _texTransparent },
            hover = { background = _texTransparent },
            active = { background = _texTransparent },
            focused = { background = _texTransparent }
        };

        // ── 缓存热路径样式（避免每帧 OnGUI 堆分配）───
        _styleLogo = new GUIStyle()
        {
            fontSize = 15,
            richText = true,
            normal = { textColor = ClrTextBright },
            padding = new RectOffset(0, 0, 2, 2)
        };
        _styleVersion = new GUIStyle()
        {
            richText = true,
            normal = { textColor = ClrTextDim }
        };
        _styleCatCardIcon = new GUIStyle()
        {
            fontSize = 14,
            normal = { textColor = ClrAccent }
        };
        _styleCatCardName = new GUIStyle()
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            normal = { textColor = ClrText },
            clipping = TextClipping.Clip
        };
        _styleCatCardCount = new GUIStyle()
        {
            fontSize = 9,
            normal = { textColor = ClrTextDim }
        };
        _styleBackButton = new GUIStyle()
        {
            fontSize = 11,
            normal = { textColor = ClrTextDim },
            hover = { textColor = ClrText },
            padding = new RectOffset(0, 0, 2, 2)
        };
        _styleEmptyHint = new GUIStyle()
        {
            fontSize = 11,
            normal = { textColor = ClrTextDim },
            padding = new RectOffset(8, 0, 4, 4)
        };
        _styleKeyCap = new GUIStyle()
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = ClrTextBright }
        };
        _styleHiddenItemName = new GUIStyle()
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = ClrTextBright },
            clipping = TextClipping.Clip
        };
        _styleHiddenItemDesc = new GUIStyle()
        {
            fontSize = 9,
            normal = { textColor = ClrTextDim },
            clipping = TextClipping.Clip
        };

        _stylesReady = true;
    }
    #endregion
}
#endif
