#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UnityToolsHub 兼容层 — 将已迁移到 EditorCore 的成员别名回 partial class
/// 原 Hub/Styles.cs、Hub/Theme.cs、Hub/DrawingUtils.cs 已移至 EditorCore 包
/// 此文件提供透明代理，使剩余 partial class 文件无需修改
/// </summary>
public partial class UnityToolsHub
{
    // ── 颜色别名（原 Theme.cs，统一引用 HubTheme）──

    private static Color ClrBg           => HubTheme.ClrBg;
    private static Color ClrLeftBg       => HubTheme.ClrLeftBg;
    private static Color ClrRightBg      => HubTheme.ClrRightBg;
    private static Color ClrSplitter     => HubTheme.ClrSplitter;
    private static Color ClrSelection    => HubTheme.ClrSelection;
    private static Color ClrHover        => HubTheme.ClrHover;
    private static Color ClrText         => HubTheme.ClrText;
    private static Color ClrTextDim      => HubTheme.ClrTextDim;
    private static Color ClrTextBright   => HubTheme.ClrTextBright;
    private static Color ClrAccent       => HubTheme.ClrAccent;
    private static Color ClrAccentDim    => HubTheme.ClrAccentDim;
    private static Color ClrCardBg       => HubTheme.ClrCardBg;
    private static Color ClrTagBg        => HubTheme.ClrTagBg;
    private static Color ClrDivider      => HubTheme.ClrDivider;
    private static Color ClrBtnNormal    => HubTheme.ClrBtnNormal;
    private static Color ClrBtnHover     => HubTheme.ClrBtnHover;

    // ── 样式别名（原 Styles.cs，统一引用 HubStyles）──

    private GUIStyle _styleCategoryHeader   => HubStyles.CategoryHeader;
    private GUIStyle _styleToolItem         => HubStyles.ToolItem;
    private GUIStyle _styleToolItemSelected => HubStyles.ToolItemSelected;
    private GUIStyle _styleRightTitle       => HubStyles.RightTitle;
    private GUIStyle _styleRightSubtitle    => HubStyles.RightSubtitle;
    private GUIStyle _styleDescription      => HubStyles.Description;
    private GUIStyle _styleCard             => HubStyles.Card;
    private GUIStyle _styleTag              => HubStyles.Tag;
    private GUIStyle _styleWelcomeTitle     => HubStyles.WelcomeTitle;
    private GUIStyle _styleWelcomeSub       => HubStyles.WelcomeSub;
    private GUIStyle _styleStatNum          => HubStyles.StatNum;
    private GUIStyle _styleStatLabel        => HubStyles.StatLabel;
    private GUIStyle _styleBtnPrimary      => HubStyles.BtnPrimary;
    private GUIStyle _styleBtnFlat          => HubStyles.BtnFlat;
    private GUIStyle _styleSectionHeader    => HubStyles.SectionHeader;
    private GUIStyle _styleShortcut         => HubStyles.Shortcut;
    private GUIStyle _styleInvisibleBtn    => HubStyles.InvisibleBtn;
    private GUIStyle _styleLogo             => HubStyles.Logo;
    private GUIStyle _styleVersion          => HubStyles.Version;
    private GUIStyle _styleCatCardIcon      => HubStyles.CatCardIcon;
    private GUIStyle _styleCatCardName      => HubStyles.CatCardName;
    private GUIStyle _styleCatCardCount     => HubStyles.CatCardCount;
    private GUIStyle _styleBackButton       => HubStyles.BackButton;
    private GUIStyle _styleEmptyHint        => HubStyles.EmptyHint;
    private GUIStyle _styleKeyCap           => HubStyles.KeyCap;
    private GUIStyle _styleHiddenItemName   => HubStyles.HiddenItemName;
    private GUIStyle _styleHiddenItemDesc   => HubStyles.HiddenItemDesc;

    // ── 纹理别名（原 Styles.cs / DrawingUtils.cs）──

    private Texture2D _texWhite       => HubStyles.TexWhite;
    private Texture2D _texHover       => HubStyles.TexHover;
    private Texture2D _texSelected    => HubStyles.TexSelected;
    private Texture2D _texTransparent => HubStyles.TexTransparent;

    // ── 绘图方法别名（原 DrawingUtils.cs）──

    private void DrawGradientRect(Rect rect, Color left, Color right)
        => HubDrawing.DrawGradientRect(rect, left, right);

    private void DrawFoldoutArrow(Rect rect, bool expanded)
        => HubDrawing.DrawFoldoutArrow(rect, expanded);

    // ── 主题数据别名（原 Theme.cs）──

    private static Dictionary<string, Color> _categoryColors => HubTheme.CategoryColors;
    private static Color[] _defaultPalette => HubTheme.DefaultPalette;
    private static string GetCategoryIcon(string categoryName)
        => HubTheme.GetCategoryIcon(categoryName);

    // ── 样式初始化（原 Styles.cs，现委托 HubStyles 懒加载）──

    private void EnsureStyles() => HubStyles.EnsureInit();
}
#endif
