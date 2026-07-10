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

    private static Color ClrBg           => Theme.ClrBg;
    private static Color ClrLeftBg       => Theme.ClrLeftBg;
    private static Color ClrRightBg      => Theme.ClrRightBg;
    private static Color ClrSplitter     => Theme.ClrSplitter;
    private static Color ClrSelection    => Theme.ClrSelection;
    private static Color ClrHover        => Theme.ClrHover;
    private static Color ClrText         => Theme.ClrText;
    private static Color ClrTextDim      => Theme.ClrTextDim;
    private static Color ClrTextBright   => Theme.ClrTextBright;
    private static Color ClrAccent       => Theme.ClrAccent;
    private static Color ClrAccentDim    => Theme.ClrAccentDim;
    private static Color ClrCardBg       => Theme.ClrCardBg;
    private static Color ClrTagBg        => Theme.ClrTagBg;
    private static Color ClrDivider      => Theme.ClrDivider;
    private static Color ClrBtnNormal    => Theme.ClrBtnNormal;
    private static Color ClrBtnHover     => Theme.ClrBtnHover;

    // ── 样式别名（原 Styles.cs，统一引用 HubStyles）──

    private GUIStyle _styleCategoryHeader   => Styles.CategoryHeader;
    private GUIStyle _styleToolItem         => Styles.ToolItem;
    private GUIStyle _styleToolItemSelected => Styles.ToolItemSelected;
    private GUIStyle _styleRightTitle       => Styles.RightTitle;
    private GUIStyle _styleRightSubtitle    => Styles.RightSubtitle;
    private GUIStyle _styleDescription      => Styles.Description;
    private GUIStyle _styleCard             => Styles.Card;
    private GUIStyle _styleTag              => Styles.Tag;
    private GUIStyle _styleWelcomeTitle     => Styles.WelcomeTitle;
    private GUIStyle _styleWelcomeSub       => Styles.WelcomeSub;
    private GUIStyle _styleStatNum          => Styles.StatNum;
    private GUIStyle _styleStatLabel        => Styles.StatLabel;
    private GUIStyle _styleBtnPrimary      => Styles.BtnPrimary;
    private GUIStyle _styleBtnFlat          => Styles.BtnFlat;
    private GUIStyle _styleSectionHeader    => Styles.SectionHeader;
    private GUIStyle _styleShortcut         => Styles.Shortcut;
    private GUIStyle _styleInvisibleBtn    => Styles.InvisibleBtn;
    private GUIStyle _styleLogo             => Styles.Logo;
    private GUIStyle _styleVersion          => Styles.Version;
    private GUIStyle _styleCatCardIcon      => Styles.CatCardIcon;
    private GUIStyle _styleCatCardName      => Styles.CatCardName;
    private GUIStyle _styleCatCardCount     => Styles.CatCardCount;
    private GUIStyle _styleBackButton       => Styles.BackButton;
    private GUIStyle _styleEmptyHint        => Styles.EmptyHint;
    private GUIStyle _styleKeyCap           => Styles.KeyCap;
    private GUIStyle _styleHiddenItemName   => Styles.HiddenItemName;
    private GUIStyle _styleHiddenItemDesc   => Styles.HiddenItemDesc;

    // ── 纹理别名（原 Styles.cs / DrawingUtils.cs）──

    private Texture2D _texWhite       => Styles.TexWhite;
    private Texture2D _texHover       => Styles.TexHover;
    private Texture2D _texSelected    => Styles.TexSelected;
    private Texture2D _texTransparent => Styles.TexTransparent;

    // ── 绘图方法别名（原 DrawingUtils.cs）──

    private void DrawGradientRect(Rect rect, Color left, Color right)
        => Drawing.DrawGradientRect(rect, left, right);

    private void DrawFoldoutArrow(Rect rect, bool expanded)
        => Drawing.DrawFoldoutArrow(rect, expanded);

    // ── 主题数据别名（原 Theme.cs）──

    private static Dictionary<string, Color> _categoryColors => Theme.CategoryColors;
    private static Color[] _defaultPalette => Theme.DefaultPalette;
    private static string GetCategoryIcon(string categoryName)
        => Theme.GetCategoryIcon(categoryName);

    // ── 样式初始化（原 Styles.cs，现委托 HubStyles 懒加载）──

    private void EnsureStyles() => Styles.EnsureInit();
}
#endif
