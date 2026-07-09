#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// UnityToolsHub — 共享调色板（单一来源）
///
/// 所有 Hub 面板与工具窗口统一引用此处的颜色常量，避免在多个文件中
/// 重复定义相同的 Color 值。Theme.cs / ToolEditorWindow.cs / 各工具类
/// 均通过别名属性或直接引用 HubPalette.Xxx 来复用。
/// </summary>
public static class HubPalette
{
    // ── 背景色 ─────────────────────────────────────────────
    public static readonly Color Bg           = new Color(0.16f, 0.16f, 0.17f, 1f);
    public static readonly Color LeftBg       = new Color(0.14f, 0.14f, 0.15f, 1f);
    public static readonly Color RightBg      = new Color(0.18f, 0.18f, 0.19f, 1f);
    public static readonly Color Splitter     = new Color(0.10f, 0.10f, 0.10f, 1f);
    public static readonly Color ToolbarBg    = new Color(0.14f, 0.14f, 0.15f, 1f);
    public static readonly Color SearchBg     = new Color(0.12f, 0.12f, 0.13f, 1f);
    public static readonly Color ItemBg       = new Color(0.19f, 0.19f, 0.20f, 1f);
    public static readonly Color ItemHover    = new Color(0.24f, 0.24f, 0.26f, 1f);
    public static readonly Color ItemSelected = new Color(0.22f, 0.45f, 0.85f, 0.30f);
    public static readonly Color Selection    = new Color(0.22f, 0.45f, 0.85f, 0.35f);
    public static readonly Color Hover        = new Color(1f, 1f, 1f, 0.04f);
    public static readonly Color CardBg       = new Color(0.21f, 0.21f, 0.22f, 1f);
    public static readonly Color GroupBoxBg   = new Color(0.17f, 0.17f, 0.18f, 1f);
    public static readonly Color TagBg        = new Color(0.25f, 0.25f, 0.27f, 1f);
    public static readonly Color StatusBar    = new Color(0.13f, 0.13f, 0.14f, 1f);
    public static readonly Color IconBg       = new Color(0.28f, 0.28f, 0.30f, 1f);
    public static readonly Color HelpBoxBg    = new Color(0.18f, 0.18f, 0.20f, 1f);
    public static readonly Color ProgressBg   = new Color(0.15f, 0.15f, 0.16f, 1f);
    public static readonly Color KeyCapBg     = new Color(0.20f, 0.20f, 0.22f, 1f);

    // ── 文字色 ─────────────────────────────────────────────
    public static readonly Color Text       = new Color(0.88f, 0.88f, 0.88f, 1f);
    public static readonly Color TextDim    = new Color(0.55f, 0.55f, 0.55f, 1f);
    public static readonly Color TextBright = new Color(0.95f, 0.95f, 0.95f, 1f);

    // ── 主题色 ─────────────────────────────────────────────
    public static readonly Color Accent    = new Color(0.30f, 0.55f, 0.95f, 1f);
    public static readonly Color AccentDim = new Color(0.22f, 0.45f, 0.85f, 0.5f);
    public static readonly Color Divider   = new Color(1f, 1f, 1f, 0.06f);

    // ── 按钮色 ─────────────────────────────────────────────
    public static readonly Color BtnNormal    = new Color(0.24f, 0.48f, 0.88f, 1f);
    public static readonly Color BtnHover     = new Color(0.30f, 0.55f, 0.95f, 1f);
    public static readonly Color BtnDanger    = new Color(0.75f, 0.28f, 0.28f, 1f);
    public static readonly Color BtnDangerHov = new Color(0.85f, 0.35f, 0.35f, 1f);
    public static readonly Color BtnSuccess   = new Color(0.25f, 0.65f, 0.35f, 1f);
    public static readonly Color BtnSuccessHov= new Color(0.30f, 0.75f, 0.40f, 1f);
    public static readonly Color BtnWarn      = new Color(0.80f, 0.58f, 0.18f, 1f);
    public static readonly Color BtnWarnHov   = new Color(0.90f, 0.65f, 0.25f, 1f);

    // ── 语义色 ─────────────────────────────────────────────
    public static readonly Color Success  = new Color(0.35f, 0.75f, 0.45f, 1f);
    public static readonly Color Warning  = new Color(0.90f, 0.70f, 0.20f, 1f);
    public static readonly Color Error    = new Color(0.85f, 0.30f, 0.30f, 1f);
    public static readonly Color Info     = new Color(0.30f, 0.65f, 0.80f, 1f);

    // ── 拖拽叠加色 ────────────────────────────────────────
    public static readonly Color DropOverlay = new Color(0.30f, 0.55f, 0.95f, 0.18f);
    public static readonly Color DropBorder  = new Color(0.30f, 0.55f, 0.95f, 0.60f);

    // ── 分类配色（供工具卡片等场景复用）──────────────────
    public static readonly Color CatDefault = new Color(0.30f, 0.65f, 0.80f, 1f);
    public static readonly Color CatGreen   = new Color(0.35f, 0.75f, 0.45f, 1f);
    public static readonly Color CatOrange  = new Color(0.90f, 0.65f, 0.25f, 1f);
    public static readonly Color CatPurple  = new Color(0.55f, 0.45f, 0.85f, 1f);
    public static readonly Color CatRed     = new Color(0.80f, 0.45f, 0.35f, 1f);
    public static readonly Color CatTeal    = new Color(0.35f, 0.70f, 0.75f, 1f);
    public static readonly Color CatPink    = new Color(0.85f, 0.40f, 0.55f, 1f);
    public static readonly Color CatYellow  = new Color(0.85f, 0.75f, 0.25f, 1f);

    // ── 纹理创建（共享，供未继承 ToolEditorWindow 的工具使用）──
    /// <summary>创建单色 1x1 纹理（HideAndDontSave），统一替代各工具重复的 MakeTex</summary>
    public static Texture2D MakeTex(int w, int h, Color color)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = color;
        tex.SetPixels(px);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }
}
#endif
