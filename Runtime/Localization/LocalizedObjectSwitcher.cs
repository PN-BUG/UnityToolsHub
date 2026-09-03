using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

/// <summary>
/// Shows different scene objects for the active locale. Attach this component to an object
/// that always stays active; the controller intentionally refuses to deactivate itself.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Localization/Localized Object Switcher")]
public sealed class LocalizedObjectSwitcher : MonoBehaviour
{
    [Serializable]
    public sealed class LocaleObjectGroup
    {
        [SerializeField, InspectorName("语言代码")] private string localeCode = "en";
        [SerializeField, InspectorName("显示物体")] private List<GameObject> targets = new List<GameObject>();

        public string LocaleCode => localeCode;
        public IReadOnlyList<GameObject> Targets => targets;

        public LocaleObjectGroup(string code)
        {
            localeCode = code;
        }
    }

    [Tooltip("语言代码与该语言下需要显示的物体。一个语言可配置多个物体。")]
    [SerializeField, InspectorName("语言显示组")] private List<LocaleObjectGroup> localeGroups = new List<LocaleObjectGroup>();

    [Tooltip("找不到匹配语言时是否显示默认物体。")]
    [SerializeField, InspectorName("未匹配时使用默认组")] private bool useFallbackWhenNoMatch = true;

    [Tooltip("没有匹配语言时显示的物体。")]
    [SerializeField, InspectorName("默认显示物体")] private List<GameObject> fallbackTargets = new List<GameObject>();

    [Tooltip("切换语言时关闭其它语言组中的物体。建议保持开启。")]
    [SerializeField, InspectorName("关闭其它语言物体")] private bool deactivateUnmatchedTargets = true;

    private int initializationVersion;

    public IReadOnlyList<LocaleObjectGroup> LocaleGroups => localeGroups;

    public void EnsureDefaultLocaleGroups()
    {
        AddLocaleGroupIfMissing("zh-Hans");
        AddLocaleGroupIfMissing("zh-TW");
        AddLocaleGroupIfMissing("en");
    }

    public void RefreshNow()
    {
        if (!LocalizationSettings.HasSettings) return;
        ApplyLocale(LocalizationSettings.SelectedLocale);
    }

    private void Reset()
    {
        EnsureDefaultLocaleGroups();
    }

    private void OnEnable()
    {
        EnsureDefaultLocaleGroups();
        LocalizationSettings.SelectedLocaleChanged += HandleSelectedLocaleChanged;
        RefreshWhenInitializedAsync(++initializationVersion);
    }

    private void OnDisable()
    {
        initializationVersion++;
        if (LocalizationSettings.HasSettings)
            LocalizationSettings.SelectedLocaleChanged -= HandleSelectedLocaleChanged;
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
        var localeCode = locale?.Identifier.Code ?? string.Empty;
        var matchedGroups = localeGroups
            .Where(group => group != null && LocaleCodesEqual(group.LocaleCode, localeCode))
            .ToList();
        var activeTargets = new HashSet<GameObject>();

        if (matchedGroups.Count > 0)
        {
            foreach (var group in matchedGroups)
            foreach (var target in group.Targets)
                if (target != null) activeTargets.Add(target);
        }
        else if (useFallbackWhenNoMatch)
        {
            foreach (var target in fallbackTargets)
                if (target != null) activeTargets.Add(target);
        }

        if (deactivateUnmatchedTargets)
        {
            foreach (var target in EnumerateManagedTargets())
                SetTargetActive(target, activeTargets.Contains(target));
        }
        else
        {
            foreach (var target in activeTargets)
                SetTargetActive(target, true);
        }
    }

    private IEnumerable<GameObject> EnumerateManagedTargets()
    {
        var visited = new HashSet<GameObject>();
        foreach (var group in localeGroups)
        {
            if (group == null) continue;
            foreach (var target in group.Targets)
                if (target != null && visited.Add(target)) yield return target;
        }

        foreach (var target in fallbackTargets)
            if (target != null && visited.Add(target)) yield return target;
    }

    private void SetTargetActive(GameObject target, bool active)
    {
        if (target == null) return;
        if (target == gameObject)
        {
            if (!active)
                Debug.LogWarning("LocalizedObjectSwitcher 不能关闭自身，请将控制器挂在常驻父物体上。", this);
            return;
        }
        if (target.activeSelf != active) target.SetActive(active);
    }

    private void AddLocaleGroupIfMissing(string localeCode)
    {
        if (localeGroups.Any(group => group != null && LocaleCodesEqual(group.LocaleCode, localeCode))) return;
        localeGroups.Add(new LocaleObjectGroup(localeCode));
    }

    private static bool LocaleCodesEqual(string left, string right)
    {
        return string.Equals(NormalizeLocaleCode(left), NormalizeLocaleCode(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLocaleCode(string value)
    {
        return (value ?? string.Empty).Trim().Replace('_', '-');
    }
}
