using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

/// <summary>
/// Switches one LocalizeSpriteEvent between multiple semantic states while preserving locale changes.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LocalizeSpriteEvent))]
[AddComponentMenu("Localization/Localized Sprite State Switcher")]
public sealed class LocalizedSpriteStateSwitcher : MonoBehaviour
{
    [Serializable]
    public sealed class SpriteState
    {
        [SerializeField, InspectorName("状态名称")] private string stateName;
        [SerializeField, InspectorName("本地化 Key")] private string entryKey;
        [SerializeField, InspectorName("加载前备用图片")] private Sprite fallbackSprite;

        public string StateName => stateName;
        public string EntryKey => entryKey;
        public Sprite FallbackSprite => fallbackSprite;

        public SpriteState(string name, string key, Sprite fallback)
        {
            stateName = name;
            entryKey = key;
            fallbackSprite = fallback;
        }
    }

    [SerializeField, InspectorName("Sprite 本地化组件")] private LocalizeSpriteEvent localizer;
    [SerializeField, InspectorName("目标 Image")] private Image targetImage;
    [SerializeField, InspectorName("目标 SpriteRenderer")] private SpriteRenderer targetSpriteRenderer;
    [SerializeField, InspectorName("状态列表")] private List<SpriteState> states = new List<SpriteState>();
    [SerializeField, InspectorName("初始状态")] private int initialState;

    private int currentState = -1;

    public int CurrentState => currentState;
    public IReadOnlyList<SpriteState> States => states;

    private void Reset() => ResolveReferences();

    private void Awake() => ResolveReferences();

    private void OnEnable()
    {
        ResolveReferences();
        if (localizer != null)
            localizer.OnUpdateAsset.AddListener(ApplySprite);
        SetState(initialState);
    }

    private void OnDisable()
    {
        if (localizer != null)
            localizer.OnUpdateAsset.RemoveListener(ApplySprite);
    }

    public bool SetState(int index)
    {
        if (states == null || index < 0 || index >= states.Count)
            return false;

        SpriteState state = states[index];
        if (state == null || string.IsNullOrWhiteSpace(state.EntryKey))
            return false;

        currentState = index;
        if (state.FallbackSprite != null)
            ApplySprite(state.FallbackSprite);

        ResolveReferences();
        if (localizer == null)
            return false;

        // The reference refreshes now and whenever LocalizationSettings.SelectedLocale changes.
        localizer.AssetReference.TableEntryReference = state.EntryKey;
        return true;
    }

    public bool SetState(string stateName)
    {
        if (states == null || string.IsNullOrWhiteSpace(stateName))
            return false;

        for (int i = 0; i < states.Count; i++)
        {
            SpriteState state = states[i];
            if (state != null && string.Equals(state.StateName, stateName, StringComparison.OrdinalIgnoreCase))
                return SetState(i);
        }

        return false;
    }

    /// <summary>Populates a new component without replacing Inspector-authored states.</summary>
    public void ConfigureIfEmpty(IReadOnlyList<string> entryKeys, IReadOnlyList<Sprite> fallbackSprites = null)
    {
        if (states == null)
            states = new List<SpriteState>();
        if (states.Count > 0 || entryKeys == null)
            return;

        for (int i = 0; i < entryKeys.Count; i++)
        {
            Sprite fallback = fallbackSprites != null && i < fallbackSprites.Count ? fallbackSprites[i] : null;
            states.Add(new SpriteState(i.ToString(), entryKeys[i], fallback));
        }
    }

    private void ResolveReferences()
    {
        if (localizer == null)
            localizer = GetComponent<LocalizeSpriteEvent>();
        if (targetImage == null)
            targetImage = GetComponent<Image>();
        if (targetSpriteRenderer == null)
            targetSpriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void ApplySprite(Sprite sprite)
    {
        if (sprite == null)
        {
            // LocalizeSpriteEvent persistent callbacks may already have assigned null to
            // Image.sprite. Restore the active state's fallback instead of leaving it blank.
            if (states == null || currentState < 0 || currentState >= states.Count)
                return;

            SpriteState state = states[currentState];
            sprite = state != null ? state.FallbackSprite : null;
            if (sprite == null)
                return;
        }

        if (targetImage != null)
            targetImage.sprite = sprite;
        if (targetSpriteRenderer != null)
            targetSpriteRenderer.sprite = sprite;
    }
}
