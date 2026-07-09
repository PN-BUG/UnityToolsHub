#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// UnityToolsHub — 绘图工具方法
/// 纹理创建、箭头绘制、渐变矩形、资源清理
/// </summary>
public partial class UnityToolsHub
{
    #region 绘图工具
    private static Texture2D MakeTex(int w, int h, Color color)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = color;
        tex.SetPixels(px);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }

    /// <summary>绘制折叠箭头（预烘焙纹理，FilterMode.Point 锐利渲染）</summary>
    private void DrawFoldoutArrow(Rect rect, bool expanded)
    {
        var tex = expanded ? _texArrowExpanded : _texArrowCollapsed;
        if (tex == null) return;

        const float size = 8f;
        float x = rect.x + (rect.width - size) * 0.5f;
        float y = rect.y + (rect.height - size) * 0.5f;
        GUI.DrawTexture(new Rect(x, y, size, size), tex, ScaleMode.StretchToFill);
    }

    /// <summary>预烘焙箭头纹理（16×16 高分辨率，FilterMode.Point 保持锐利）</summary>
    private static Texture2D MakeArrowTex(bool expanded)
    {
        const int SIZE = 16;
        var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        var px = new Color[SIZE * SIZE];
        for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

        Color c = ClrTextDim;
        if (expanded)
        {
            // ▼ 向下三角：每行宽度递增，水平居中
            for (int row = 0; row < SIZE; row++)
            {
                int lineW = System.Math.Min(SIZE, (row + 1) * SIZE / (SIZE - 1));
                int startX = (SIZE - lineW) / 2;
                for (int col = 0; col < lineW; col++)
                    px[row * SIZE + startX + col] = c;
            }
        }
        else
        {
            // ▶ 向右三角：左侧底边最宽，右侧尖端最窄
            for (int col = 0; col < SIZE; col++)
            {
                int lineH = System.Math.Min(SIZE, (SIZE - col) * SIZE / (SIZE - 1));
                int startY = (SIZE - lineH) / 2;
                for (int dy = 0; dy < lineH; dy++)
                    px[(startY + dy) * SIZE + col] = c;
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        tex.filterMode = FilterMode.Point;
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }

    private void DrawGradientRect(Rect rect, Color left, Color right)
    {
        DrawHorizontalGradient(rect, left, right);
    }

    // ── 渐变纹理缓存（按颜色对缓存，避免逐像素 DrawRect 循环）──
    private static readonly System.Collections.Generic.Dictionary<int, Texture2D> _gradientCache
        = new System.Collections.Generic.Dictionary<int, Texture2D>();
    private const int GradientTexWidth = 64;

    private static int ColorPairKey(Color a, Color b)
    {
        // 将 Color（32bit rgba）打包为 int，组合两色为 key
        unchecked
        {
            int ha = ((int)(a.r * 255) << 24) | ((int)(a.g * 255) << 16) | ((int)(a.b * 255) << 8) | (int)(a.a * 255);
            int hb = ((int)(b.r * 255) << 24) | ((int)(b.g * 255) << 16) | ((int)(b.b * 255) << 8) | (int)(b.a * 255);
            return (ha * 397) ^ hb;
        }
    }

    /// <summary>绘制水平渐变矩形（带纹理缓存，替代逐像素 DrawRect 循环）</summary>
    private static void DrawHorizontalGradient(Rect rect, Color left, Color right)
    {
        var tex = GetGradientTexture(left, right);
        if (tex == null)
        {
            // 回退：单色填充
            EditorGUI.DrawRect(rect, left);
            return;
        }
        // 保存并恢复 GUI.color，避免污染外部着色状态
        var prevColor = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill);
        GUI.color = prevColor;
    }

    /// <summary>获取或生成水平渐变纹理（按颜色对缓存）</summary>
    private static Texture2D GetGradientTexture(Color left, Color right)
    {
        int key = ColorPairKey(left, right);
        if (_gradientCache.TryGetValue(key, out var cached))
            return cached;

        var tex = new Texture2D(GradientTexWidth, 1, TextureFormat.RGBA32, false);
        var px = new Color32[GradientTexWidth];
        for (int i = 0; i < GradientTexWidth; i++)
        {
            float t = (float)i / (GradientTexWidth - 1);
            px[i] = Color.Lerp(left, right, t);
        }
        tex.SetPixels32(px);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        _gradientCache[key] = tex;
        return tex;
    }

    private void OnDestroy()
    {
        if (_texWhite != null) DestroyImmediate(_texWhite);
        if (_texHover != null) DestroyImmediate(_texHover);
        if (_texSelected != null) DestroyImmediate(_texSelected);
        if (_texTransparent != null) DestroyImmediate(_texTransparent);
        if (_texArrowExpanded != null) DestroyImmediate(_texArrowExpanded);
        if (_texArrowCollapsed != null) DestroyImmediate(_texArrowCollapsed);

        // 清理渐变纹理缓存
        foreach (var tex in _gradientCache.Values)
        {
            if (tex != null) DestroyImmediate(tex);
        }
        _gradientCache.Clear();
    }
    #endregion
}
#endif
