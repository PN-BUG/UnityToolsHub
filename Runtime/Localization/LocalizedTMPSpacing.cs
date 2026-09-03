using System;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

/// <summary>
/// Applies locale-specific TMP spacing. Chinese can keep character tracking while English
/// moves that tracking to word spacing so individual Latin letters remain normally spaced.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Localization/Localized TMP Spacing")]
public sealed class LocalizedTMPSpacing : MonoBehaviour
{
    [SerializeField, InspectorName("TMP 文本")] private TMP_Text target;

    [Header("简体中文 (zh-Hans)")]
    [SerializeField, InspectorName("字符间距 (em)")] private float simplifiedCharacterSpacing;
    [SerializeField, InspectorName("单词间距 (em)")] private float simplifiedWordSpacing;

    [Header("繁体中文 (zh-TW)")]
    [SerializeField, InspectorName("字符间距 (em)")] private float traditionalCharacterSpacing;
    [SerializeField, InspectorName("单词间距 (em)")] private float traditionalWordSpacing;

    [Header("英语 (en)")]
    [SerializeField, InspectorName("字符间距 (em)")] private float englishCharacterSpacing;
    [SerializeField, InspectorName("单词间距 (em)")] private float englishWordSpacing;

    [SerializeField, HideInInspector] private float originalCharacterSpacing;
    [SerializeField, HideInInspector] private float originalWordSpacing;
    [SerializeField, HideInInspector] private bool configured;

    private int initializationVersion;

    public bool IsConfigured => configured;

    /// <summary>
    /// Uses the current TMP values for Chinese and moves its character spacing to English
    /// word spacing. This is called automatically when the component is first attached.
    /// </summary>
    public void ConfigureFromCurrent()
    {
        ResolveTarget();
        if (target == null) return;

        originalCharacterSpacing = target.characterSpacing;
        originalWordSpacing = target.wordSpacing;
        simplifiedCharacterSpacing = originalCharacterSpacing;
        simplifiedWordSpacing = originalWordSpacing;
        traditionalCharacterSpacing = originalCharacterSpacing;
        traditionalWordSpacing = originalWordSpacing;
        englishCharacterSpacing = 0f;
        englishWordSpacing = originalWordSpacing + originalCharacterSpacing;
        configured = true;
    }

    public void RestoreOriginalSpacing()
    {
        ResolveTarget();
        if (target == null || !configured) return;
        ApplySpacing(originalCharacterSpacing, originalWordSpacing);
    }

    public void RefreshNow()
    {
        if (!LocalizationSettings.HasSettings) return;
        ApplyLocale(LocalizationSettings.SelectedLocale);
    }

    private void Reset()
    {
        ResolveTarget();
        ConfigureFromCurrent();
    }

    private void OnValidate()
    {
        ResolveTarget();
    }

    private void OnEnable()
    {
        ResolveTarget();
        if (!configured) ConfigureFromCurrent();
        LocalizationSettings.SelectedLocaleChanged += HandleSelectedLocaleChanged;
        RefreshWhenInitializedAsync(++initializationVersion);
    }

    private void OnDisable()
    {
        initializationVersion++;
        if (LocalizationSettings.HasSettings)
            LocalizationSettings.SelectedLocaleChanged -= HandleSelectedLocaleChanged;
    }

    private void OnDestroy()
    {
        RestoreOriginalSpacing();
    }

    private async void RefreshWhenInitializedAsync(int version)
    {
        try
        {
            await LocalizationSettings.InitializationOperation.Task;
            if (this == null || !isActiveAndEnabled || version != initializationVersion) return;
            ApplyLocale(LocalizationSettings.SelectedLocale);
        }
        catch (Exception exception)
        {
            if (this != null) Debug.LogException(exception, this);
        }
    }

    private void HandleSelectedLocaleChanged(Locale locale)
    {
        ApplyLocale(locale);
    }

    private void ApplyLocale(Locale locale)
    {
        var code = NormalizeLocaleCode(locale?.Identifier.Code);
        if (code == "zh-hans" || code == "zh-cn" || code == "zh-sg")
            ApplySpacing(simplifiedCharacterSpacing, simplifiedWordSpacing);
        else if (code == "zh-tw" || code == "zh-hant" || code == "zh-hk" || code == "zh-mo")
            ApplySpacing(traditionalCharacterSpacing, traditionalWordSpacing);
        else if (code == "en" || code.StartsWith("en-", StringComparison.Ordinal))
            ApplySpacing(englishCharacterSpacing, englishWordSpacing);
        else
            ApplySpacing(originalCharacterSpacing, originalWordSpacing);
    }

    private void ApplySpacing(float characterSpacing, float wordSpacing)
    {
        ResolveTarget();
        if (target == null) return;
        var changed = !Mathf.Approximately(target.characterSpacing, characterSpacing) ||
                      !Mathf.Approximately(target.wordSpacing, wordSpacing);
        if (!changed) return;
        target.characterSpacing = characterSpacing;
        target.wordSpacing = wordSpacing;
        target.SetVerticesDirty();
        target.SetLayoutDirty();
    }

    private void ResolveTarget()
    {
        if (target == null) target = GetComponent<TMP_Text>();
    }

    private static string NormalizeLocaleCode(string value)
    {
        return (value ?? string.Empty).Trim().Replace('_', '-').ToLowerInvariant();
    }
}
