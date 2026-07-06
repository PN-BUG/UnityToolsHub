using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 帧动画预览工具脚本
/// 挂载到 GameObject 上，可在 Scene 视图中实时预览帧动画效果
/// 支持多个动画切换（适合角色预制体直接使用）
/// </summary>
[ExecuteInEditMode]
[RequireComponent(typeof(SpriteRenderer))]
public class FrameAnimPreview : MonoBehaviour
{
    [System.Serializable]
    public class AnimClip
    {
        public string name;
        public List<Sprite> frames = new List<Sprite>();
        public bool loop = true;
    }

    [Header("动画列表")]
    [Tooltip("所有可用的动画剪辑")]
    public List<AnimClip> animations = new List<AnimClip>();

    [Header("播放设置")]
    [Tooltip("帧率 (FPS)")]
    [Range(1, 60)]
    public float frameRate = 12f;

    [Tooltip("默认播放的动画名称（留空则播放第一个）")]
    public string defaultAnimName = "";

    [Tooltip("是否在编辑模式下自动播放")]
    public bool playInEditMode = true;

    [Tooltip("是否在 Awake 时自动播放")]
    public bool playOnAwake = true;

    [Header("当前状态 (只读)")]
    [SerializeField] private string currentAnimName = "";
    [SerializeField] private int currentFrameIndex;
    [SerializeField] private bool isPlaying;
    [SerializeField] private float timer;

    // 兼容旧版单动画字段
    [HideInInspector] public List<Sprite> frames = new List<Sprite>();
    [HideInInspector] public bool loop = true;

    private SpriteRenderer spriteRenderer;
    private AnimClip currentClip;

    /// <summary>当前动画名</summary>
    public string CurrentAnimName => currentAnimName;
    /// <summary>当前帧索引</summary>
    public int CurrentFrame => currentFrameIndex;
    /// <summary>总帧数</summary>
    public int TotalFrames => currentClip != null ? currentClip.frames.Count : 0;
    /// <summary>是否正在播放</summary>
    public bool IsPlaying => isPlaying;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        MigrateLegacyData();

        if (playOnAwake)
        {
            if (!string.IsNullOrEmpty(defaultAnimName))
                Play(defaultAnimName);
            else if (animations.Count > 0)
                Play(animations[0].name);
        }
    }

    /// <summary>
    /// 兼容旧版：如果 animations 为空但 frames 不为空，自动迁移
    /// </summary>
    private void MigrateLegacyData()
    {
        if (animations.Count == 0 && frames.Count > 0)
        {
            animations.Add(new AnimClip
            {
                name = "default",
                frames = new List<Sprite>(frames),
                loop = loop
            });
        }
    }

    private void Update()
    {
        if (!isPlaying || currentClip == null || currentClip.frames.Count == 0) return;

#if UNITY_EDITOR
        if (!Application.isPlaying && !playInEditMode)
            return;
#endif

        timer += Time.deltaTime;
        float interval = 1f / frameRate;

        if (timer >= interval)
        {
            timer -= interval;
            AdvanceFrame();
        }
    }

    /// <summary>
    /// 播放指定名称的动画
    /// </summary>
    public void Play(string animName)
    {
        var clip = animations.Find(a => a.name == animName);
        if (clip == null || clip.frames.Count == 0)
        {
            Debug.LogWarning($"[FrameAnimPreview] 动画 '{animName}' 不存在或没有帧数据");
            return;
        }

        currentClip = clip;
        currentAnimName = animName;
        currentFrameIndex = 0;
        timer = 0f;
        isPlaying = true;
        ApplyFrame(0);
    }

    /// <summary>
    /// 播放第一个动画（兼容旧版）
    /// </summary>
    public void Play()
    {
        if (animations.Count > 0)
            Play(animations[0].name);
    }

    /// <summary>
    /// 暂停动画
    /// </summary>
    public void Pause()
    {
        isPlaying = false;
    }

    /// <summary>
    /// 继续播放
    /// </summary>
    public void Resume()
    {
        if (currentClip != null)
            isPlaying = true;
    }

    /// <summary>
    /// 停止动画并重置到第一帧
    /// </summary>
    public void Stop()
    {
        isPlaying = false;
        currentFrameIndex = 0;
        timer = 0f;
        if (currentClip != null && currentClip.frames.Count > 0)
            ApplyFrame(0);
    }

    /// <summary>
    /// 跳转到指定帧
    /// </summary>
    public void GoToFrame(int index)
    {
        if (currentClip == null || index < 0 || index >= currentClip.frames.Count) return;
        currentFrameIndex = index;
        timer = 0f;
        ApplyFrame(index);
    }

    /// <summary>
    /// 获取所有可用动画名称
    /// </summary>
    public List<string> GetAnimationNames()
    {
        var names = new List<string>();
        foreach (var anim in animations)
            names.Add(anim.name);
        return names;
    }

    /// <summary>
    /// 前进一帧
    /// </summary>
    private void AdvanceFrame()
    {
        currentFrameIndex++;

        if (currentFrameIndex >= currentClip.frames.Count)
        {
            if (currentClip.loop)
            {
                currentFrameIndex = 0;
            }
            else
            {
                currentFrameIndex = currentClip.frames.Count - 1;
                isPlaying = false;
                return;
            }
        }

        ApplyFrame(currentFrameIndex);
    }

    /// <summary>
    /// 应用当前帧的 Sprite
    /// </summary>
    private void ApplyFrame(int index)
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null && currentClip != null
            && index >= 0 && index < currentClip.frames.Count)
        {
            spriteRenderer.sprite = currentClip.frames[index];
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        MigrateLegacyData();

        if (currentClip != null && currentClip.frames.Count > 0
            && currentFrameIndex >= 0 && currentFrameIndex < currentClip.frames.Count)
        {
            ApplyFrame(currentFrameIndex);
        }
        else if (animations.Count > 0 && animations[0].frames.Count > 0)
        {
            currentClip = animations[0];
            ApplyFrame(0);
        }
    }
#endif
}
