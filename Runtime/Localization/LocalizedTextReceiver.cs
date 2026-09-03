using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

/// <summary>
/// Receives a localized template, resolves platform input placeholders, and writes it to a UI text component.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Localization/Dynamic Localization Bridge")]
public sealed class LocalizedTextReceiver : MonoBehaviour
{
    [Serializable]
    private sealed class DynamicBinding
    {
        public string source;
        public LocalizedString localized = new LocalizedString();
    }

    [SerializeField] private Text legacyText;
    [SerializeField] private TMP_Text tmpText;
    [SerializeField] private List<DynamicBinding> dynamicBindings = new List<DynamicBinding>();
    [SerializeField, HideInInspector] private string originalSourceText;

    private DynamicBinding activeDynamicBinding;
    private Dictionary<string, string> activeTemplateValues;
    private string expectedDisplay;
    private string lastObservedSourceText;

    private static readonly Regex TemplateTokenRegex = new Regex(@"\{[^{}]+\}", RegexOptions.Compiled);

    public void ApplyLocalizedText(string localizedTemplate)
    {
        ResolveTargets();
        var resolvedTemplate = ApplyTemplateValues(localizedTemplate ?? string.Empty);
        var value = PlatformStringReplace.Replace(resolvedTemplate, HasJoystick());
        expectedDisplay = value;
        if (legacyText != null) legacyText.text = value;
        if (tmpText != null) tmpText.text = value;
    }

    public void Configure(Text text)
    {
        Configure(text, false);
    }

    public void Configure(Text text, bool replaceOriginalSource)
    {
        legacyText = text;
        tmpText = null;
        CaptureOriginalSource(text != null ? text.text : string.Empty, replaceOriginalSource);
    }

    public void Configure(TMP_Text text)
    {
        Configure(text, false);
    }

    public void Configure(TMP_Text text, bool replaceOriginalSource)
    {
        tmpText = text;
        legacyText = null;
        CaptureOriginalSource(text != null ? text.text : string.Empty, replaceOriginalSource);
    }

    /// <summary>
    /// Replaces the source captured by the receiver. Editor tooling uses this for a nested Prefab
    /// whose displayed Text is overridden without applying that value to the source Prefab.
    /// </summary>
    public void SetOriginalSource(string source)
    {
        if (!string.IsNullOrEmpty(source)) originalSourceText = source;
    }

    public void AddOrUpdateDynamicBinding(string source, string table, string entry)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(table) || string.IsNullOrEmpty(entry)) return;
        var binding = dynamicBindings.Find(item => item.source == source);
        if (binding == null)
        {
            binding = new DynamicBinding { source = source };
            dynamicBindings.Add(binding);
        }
        binding.localized.TableReference = table;
        binding.localized.TableEntryReference = entry;
    }

    private void Reset() => ResolveTargets();
    private void OnValidate() => ResolveTargets();

    private void OnEnable()
    {
        ResolveTargets();
        var source = ReadText();
        CaptureOriginalSource(source);
        lastObservedSourceText = source;
        ObserveDynamicSource(source);
    }

    private void OnDisable() => ReleaseDynamicBinding();

    private void OnDestroy()
    {
        ReleaseDynamicBinding();
        RestoreOriginalText();
    }

    private void LateUpdate()
    {
        if (dynamicBindings.Count == 0) return;
        var value = ReadText();
        if (value != expectedDisplay)
        {
            lastObservedSourceText = value;
            ObserveDynamicSource(value);
        }
    }

    /// <summary>
    /// Restores the value produced by the original business code. Removing this component therefore
    /// returns the object to its pre-localization behaviour without editing that business code.
    /// </summary>
    public void RestoreOriginalText()
    {
        ResolveTargets();
        var value = !string.IsNullOrEmpty(lastObservedSourceText) ? lastObservedSourceText : originalSourceText;
        if (legacyText != null) legacyText.text = value ?? string.Empty;
        if (tmpText != null) tmpText.text = value ?? string.Empty;
        expectedDisplay = value;
    }

    private void ObserveDynamicSource(string source)
    {
        ReleaseDynamicBinding();
        expectedDisplay = source;
        if (string.IsNullOrEmpty(source)) return;
        activeDynamicBinding = dynamicBindings.Find(item => item.source == source);
        if (activeDynamicBinding == null)
        {
            foreach (var binding in dynamicBindings)
            {
                if (!TryMatchTemplate(binding.source, source, out var values)) continue;
                activeDynamicBinding = binding;
                activeTemplateValues = values;
                break;
            }
        }
        if (activeDynamicBinding == null || activeDynamicBinding.localized == null || activeDynamicBinding.localized.IsEmpty)
        {
            activeDynamicBinding = null;
            return;
        }
        activeDynamicBinding.localized.StringChanged += ApplyLocalizedText;
    }

    private void ReleaseDynamicBinding()
    {
        if (activeDynamicBinding != null && activeDynamicBinding.localized != null)
            activeDynamicBinding.localized.StringChanged -= ApplyLocalizedText;
        activeDynamicBinding = null;
        activeTemplateValues = null;
    }

    private string ApplyTemplateValues(string template)
    {
        if (activeTemplateValues == null || activeTemplateValues.Count == 0) return template;
        foreach (var pair in activeTemplateValues) template = template.Replace(pair.Key, pair.Value);
        return template;
    }

    private static bool TryMatchTemplate(string template, string displayedValue,
        out Dictionary<string, string> values)
    {
        values = null;
        if (string.IsNullOrEmpty(template) || string.IsNullOrEmpty(displayedValue)) return false;
        var tokens = TemplateTokenRegex.Matches(template).Cast<Match>().ToArray();
        if (tokens.Length == 0) return false;

        var pattern = new StringBuilder("^");
        var position = 0;
        foreach (var token in tokens)
        {
            pattern.Append(Regex.Escape(template.Substring(position, token.Index - position)));
            pattern.Append("(.*?)");
            position = token.Index + token.Length;
        }
        pattern.Append(Regex.Escape(template.Substring(position))).Append('$');

        var match = Regex.Match(displayedValue, pattern.ToString(), RegexOptions.Singleline);
        if (!match.Success) return false;
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < tokens.Length; index++)
            values[tokens[index].Value] = match.Groups[index + 1].Value;
        return true;
    }

    private string ReadText()
    {
        if (tmpText != null) return tmpText.text;
        return legacyText != null ? legacyText.text : string.Empty;
    }

    private void ResolveTargets()
    {
        if (legacyText == null && tmpText == null)
        {
            tmpText = GetComponent<TMP_Text>();
            if (tmpText == null) legacyText = GetComponent<Text>();
        }
    }

    private void CaptureOriginalSource(string value, bool replace = false)
    {
        if ((replace || string.IsNullOrEmpty(originalSourceText)) && !string.IsNullOrEmpty(value))
            originalSourceText = value;
    }

    private static bool HasJoystick()
    {
        var names = Input.GetJoystickNames();
        return names != null && names.Any(name => !string.IsNullOrWhiteSpace(name));
    }
}
