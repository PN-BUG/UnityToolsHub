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
        if (_texWhite == null) return;
        int steps = Mathf.Max(1, (int)rect.width);
        float stepW = rect.width / steps;

        for (int i = 0; i < steps; i++)
        {
            float t = (float)i / steps;
            Color c = Color.Lerp(left, right, t);
            var stepRect = new Rect(rect.x + i * stepW, rect.y, stepW + 1, rect.height);
            EditorGUI.DrawRect(stepRect, c);
        }
    }

    private void OnDestroy()
    {
        if (_texWhite != null) DestroyImmediate(_texWhite);
        if (_texHover != null) DestroyImmediate(_texHover);
        if (_texSelected != null) DestroyImmediate(_texSelected);
        if (_texTransparent != null) DestroyImmediate(_texTransparent);
        if (_texArrowExpanded != null) DestroyImmediate(_texArrowExpanded);
        if (_texArrowCollapsed != null) DestroyImmediate(_texArrowCollapsed);
    }
    #endregion
}
#endif
