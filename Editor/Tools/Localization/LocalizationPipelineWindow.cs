#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[ToolInfo("本地化流水线", "文本工具",
    Description = "扫描 Text/TMP 文本，翻译并生成 Unity Localization String Tables，自动挂载本地化组件。支持 Prefab 优先、场景 Override、组合文本和占位符保护。",
    Icon = "文",
    Tags = new[] { "Localization", "本地化", "翻译", "Text", "TMP", "Prefab" },
    Priority = -20)]
public sealed class LocalizationPipelineWindow : EditorWindow
{
    [Serializable]
    private sealed class ScanRecord
    {
        public bool selected = true;
        public bool expanded;
        public bool codeControlled;
        public string codeControlReason;
        public string assetPath;
        public string hierarchyPath;
        public string componentType;
        public string sourceText;
        public string entryKey;
        public string traditional;
        public string english;
    }

    private enum TranslationProvider
    {
        Google,
        MyMemory,
        LibreTranslate,
        DeepL,
        Azure,
        OpenAI
    }

    private sealed class TranslationJobResult
    {
        public ScanRecord record;
        public Exception error;
    }

    private sealed class DynamicSourceCandidate
    {
        public string source;
        public string hans;
        public string traditional;
        public string english;
        public string origin;
        public readonly HashSet<string> targetFieldNames = new HashSet<string>(StringComparer.Ordinal);
    }

    private sealed class DynamicPreviewItem
    {
        public bool selected = true;
        public Component target;
        public bool sourceResolverOnly;
        public DynamicSourceCandidate candidate;
        public string targetPath;
        public string scriptName;
        public string entryKey;
        public bool receiverExists;
        public bool translationExists;
        public int occurrenceCount = 1;
    }

    [Serializable]
    private sealed class BindingMismatch
    {
        public bool selected = true;
        public string assetPath;
        public string hierarchyPath;
        public string componentType;
        public string actualText;
        public string tableSourceText;
        public string currentKey;
        public string correctedKey;
        public string collectionName;
        public string reason;
    }

    private const string GeneratedPrefix = "auto.";
    private const string DefaultCollection = "ProjectText";
    private const string DefaultDirectory = "Assets/Localization/ProjectText";
    private const string DefaultAssetCollection = "ProjectAssets";
    private const string DefaultAssetDirectory = "Assets/Localization/ProjectAssets";
    private static readonly Regex ChineseRegex = new Regex("[\\u3400-\\u9FFF]", RegexOptions.Compiled);
    private static readonly Regex ProtectedTokenRegex = new Regex("(<[^>]+>|\\{[^{}]+\\})", RegexOptions.Compiled);
    // Unity Localization 1.4.5 always creates a SmartFormat cache when a LocalizedString is
    // refreshed. A CJK selector such as {确认} overflows SmartFormat's byte-based parser even
    // when the table entry is not marked Smart. Store business placeholders as escaped literal
    // braces ({{确认}}) and mark those entries Smart; SmartFormat then safely returns {确认}
    // for our runtime receiver to process.
    private static readonly Regex LiteralBraceTokenRegex = new Regex(
        "(?<!\\{)\\{([^{}]+)\\}(?!\\})",
        RegexOptions.Compiled);
    private static readonly Regex EscapedBraceTokenRegex = new Regex(
        "\\{\\{([^{}]+)\\}\\}",
        RegexOptions.Compiled);
    private static readonly Regex HanLayoutWhitespaceRegex = new Regex(
        "(?<=[\\u3400-\\u9FFF])[\\p{Zs}\\t]+(?=[\\u3400-\\u9FFF])",
        RegexOptions.Compiled);
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly SemaphoreSlim GoogleRequestGate = new SemaphoreSlim(1, 1);
    private static readonly SemaphoreSlim MyMemoryRequestGate = new SemaphoreSlim(1, 1);
    private static readonly SemaphoreSlim LibreRequestGate = new SemaphoreSlim(1, 1);
    private static readonly SemaphoreSlim DeepLRequestGate = new SemaphoreSlim(1, 1);
    private static readonly SemaphoreSlim AzureRequestGate = new SemaphoreSlim(1, 1);
    private static readonly SemaphoreSlim OpenAIRequestGate = new SemaphoreSlim(1, 1);
    private static readonly Dictionary<string, string> TranslationCache = new Dictionary<string, string>();
    private static readonly Dictionary<string, Task<string>> TranslationInFlight = new Dictionary<string, Task<string>>();
    private static DateTime nextGoogleRequestUtc = DateTime.MinValue;
    private static DateTime nextMyMemoryRequestUtc = DateTime.MinValue;
    private static DateTime nextLibreRequestUtc = DateTime.MinValue;
    private static DateTime nextDeepLRequestUtc = DateTime.MinValue;
    private static DateTime nextAzureRequestUtc = DateTime.MinValue;
    private static DateTime nextOpenAIRequestUtc = DateTime.MinValue;
    private static int providerRoundRobin;
    private const int MaxProviderRounds = 3;

    [SerializeField] private string collectionName = DefaultCollection;
    [SerializeField] private string outputDirectory = DefaultDirectory;
    [SerializeField] private bool scanScenes = true;
    [SerializeField] private bool scanPrefabs = true;
    [SerializeField] private bool removeMissingGeneratedEntries = true;
    [SerializeField] private bool useGoogleTranslate = true;
    [SerializeField] private bool useMyMemory = true;
    [SerializeField] private bool useLibreTranslate;
    [SerializeField] private string libreTranslateEndpoint = "http://localhost:5000/translate";
    [SerializeField] private string libreTraditionalLocale = "zt";
    [SerializeField] private bool useDeepL;
    [SerializeField] private bool deepLFreeApi = true;
    [SerializeField] private bool useAzureTranslator;
    [SerializeField] private string azureTranslatorRegion = string.Empty;
    [SerializeField] private bool useOpenAITranslation;
    [SerializeField] private string openAIEndpoint = "https://api.openai.com/v1/responses";
    [SerializeField] private string openAIModel = "gpt-5-mini";
    [SerializeField, TextArea(2, 5)] private string aiTranslationContext = "游戏界面本地化。译文应自然、简洁，并符合游戏 UI 用语。";
    [SerializeField, Range(1, 8)] private int translationConcurrency = 3;
    [SerializeField] private bool showTranslationSettings;
    [NonSerialized] private string libreTranslateApiKey = string.Empty;
    [NonSerialized] private string deepLApiKey = string.Empty;
    [NonSerialized] private string azureTranslatorKey = string.Empty;
    [NonSerialized] private string openAIApiKey = string.Empty;
    [NonSerialized] private int dynamicTextDetected;
    [SerializeField] private List<DefaultAsset> scanFolders = new List<DefaultAsset>();
    [SerializeField] private bool showScanFolders = true;
    [SerializeField] private List<ScanRecord> records = new List<ScanRecord>();
    [SerializeField] private string filter = string.Empty;
    [SerializeField] private string status = "尚未扫描";
    [SerializeField] private Vector2 scroll;
    [SerializeField] private bool lastScanWasFullProject;
    [SerializeField] private bool showSettings;
    [NonSerialized] private List<DynamicPreviewItem> dynamicPreviewItems = new List<DynamicPreviewItem>();
    [NonSerialized] private List<BindingMismatch> bindingMismatches = new List<BindingMismatch>();
    [NonSerialized] private bool showDynamicPreview = true;
    private bool busy;
    private GUIStyle cardStyle;
    private GUIStyle titleStyle;
    private GUIStyle subtitleStyle;
    private GUIStyle sectionStyle;
    private GUIStyle dimStyle;
    private GUIStyle primaryButtonStyle;
    private GUIStyle successButtonStyle;
    private GUIStyle flatButtonStyle;
    private GUIStyle statusStyle;
    private Texture2D cardTexture;
    private Texture2D primaryTexture;
    private Texture2D successTexture;
    private Texture2D flatTexture;
    private static readonly Color HubBackground = new Color32(30, 32, 36, 255);
    private static readonly Color HubToolbar = new Color32(38, 41, 46, 255);
    private static readonly Color HubCard = new Color32(45, 48, 54, 255);
    private static readonly Color HubAccent = new Color32(70, 139, 230, 255);
    private static readonly Color HubSuccess = new Color32(55, 165, 112, 255);
    private static readonly Color HubWarning = new Color32(218, 157, 65, 255);
    private static readonly Color HubDivider = new Color32(62, 66, 74, 255);
    private static string FolderPreferenceKey => "LocalizationPipeline.ScanFolders." + Hash128.Compute(Application.dataPath);

    [MenuItem("GameTools/Localization/完整本地化工具", false, 900)]
    private static void Open() => GetWindow<LocalizationPipelineWindow>("本地化工具");

    [InitializeOnLoadMethod]
    private static void ScheduleEnsureSettingSemanticEntries()
    {
        EditorApplication.delayCall += EnsureSettingSemanticEntries;
    }

    private static void EnsureSettingSemanticEntries()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += EnsureSettingSemanticEntries;
            return;
        }
        var collection = GetOrCreateDefaultStringCollection();
        var hans = GetOrCreateTable(collection, "zh-Hans");
        var traditional = GetOrCreateTable(collection, "zh-TW");
        var english = GetOrCreateTable(collection, "en");
        var values = new[]
        {
            new[] { "settings.language.zh_hans", "简体中文", "簡體中文", "Simplified Chinese" },
            new[] { "settings.language.zh_tw", "繁体中文", "繁體中文", "Traditional Chinese" },
            new[] { "settings.language.en", "英语", "英語", "English" },
            new[] { "settings.quality.low", "低", "低", "Low" },
            new[] { "settings.quality.medium", "中", "中", "Medium" },
            new[] { "settings.quality.high", "高", "高", "High" },
            new[] { "settings.breath.off", "关闭", "關閉", "Off" },
            new[] { "settings.breath.always", "始终显示", "始終顯示", "Always Visible" },
            new[] { "settings.breath.pulse", "呼吸循环", "呼吸循環", "Pulse" },
            new[] { "settings.visibility.visible", "显示", "顯示", "Visible" },
            new[] { "settings.visibility.off", "关闭", "關閉", "Off" },
            new[] { "settings.state.on", "开启", "開啟", "On" },
            new[] { "settings.state.off", "关闭", "關閉", "Off" },
            new[] { "settings.display.fullscreen", "全屏", "全螢幕", "Fullscreen" },
            new[] { "settings.display.window", "窗口", "視窗", "Window" }
        };
        foreach (var value in values)
        {
            var shared = collection.SharedData.GetEntry(value[0]) ?? collection.SharedData.AddKey(value[0]);
            SetEntry(hans, shared.Id, value[1]);
            SetEntry(traditional, shared.Id, value[2]);
            SetEntry(english, shared.Id, value[3]);
        }
        var repairedPlaceholderFields = RepairLiteralBraceTokens(collection);
        var repairedSemanticTranslations = RepairKnownUiTranslations(collection);
        EditorUtility.SetDirty(collection.SharedData);
        EditorUtility.SetDirty(hans);
        EditorUtility.SetDirty(traditional);
        EditorUtility.SetDirty(english);
        AssetDatabase.SaveAssets();
        if (repairedPlaceholderFields > 0)
            Debug.Log($"Localization 占位符迁移完成：已修复 {repairedPlaceholderFields} 个字段。");
        if (repairedSemanticTranslations > 0)
            Debug.Log($"Localization 固定 UI 语义修正完成：已更新 {repairedSemanticTranslations} 条译文。");
    }

    [MenuItem("GameTools/Localization/修复 String Table 占位符", false, 921)]
    private static void RepairDefaultCollectionPlaceholdersFromMenu()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(DefaultCollection);
        if (collection == null)
        {
            EditorUtility.DisplayDialog("Localization", $"找不到 String Table Collection：{DefaultCollection}", "确定");
            return;
        }
        var changed = RepairLiteralBraceTokens(collection);
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Localization", $"占位符检查完成，修复 {changed} 个字段。", "确定");
    }

    [MenuItem("GameObject/本地化/自动挂载并绑定", false, 25)]
    private static async void BindSelectedObjectsFromContextMenu()
    {
        var targets = Selection.gameObjects.Distinct().ToArray();
        var textCount = 0;
        var imageCount = 0;
        var skippedImages = 0;
        var failedTranslations = 0;
        StringTableCollection stringCollection = null;
        AssetTableCollection assetCollection = null;
        var pipeline = Resources.FindObjectsOfTypeAll<LocalizationPipelineWindow>().FirstOrDefault() ??
                       GetWindow<LocalizationPipelineWindow>("本地化工具");

        foreach (var target in targets)
        {
            foreach (var textComponent in GetLocalizableTextComponents(target))
            {
                var source = textComponent is Text legacy ? legacy.text : ((TMP_Text)textComponent).text;
                if (string.IsNullOrWhiteSpace(source)) continue;
                stringCollection ??= GetOrCreateDefaultStringCollection();
                var key = EnsureDefaultStringEntry(stringCollection, source);
                try
                {
                    await pipeline.EnsureDefaultStringTranslationsAsync(stringCollection, key, source);
                }
                catch (Exception exception)
                {
                    failedTranslations++;
                    Debug.LogWarning($"右键绑定时自动翻译失败：{key}\n{exception.Message}");
                }
                ConfigureLocalization(textComponent, key, stringCollection, true, source);
                textCount++;
            }

            foreach (var image in target.GetComponents<Image>())
            {
                if (image.sprite == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(image.sprite)))
                {
                    skippedImages++;
                    continue;
                }
                assetCollection ??= GetOrCreateDefaultAssetCollection();
                var key = EnsureDefaultSpriteEntry(assetCollection, image.sprite);
                ConfigureImageLocalization(image, key, assetCollection);
                imageCount++;
            }
            EditorUtility.SetDirty(target);
        }

        AssetDatabase.SaveAssets();
        var message = $"本地化绑定完成：文本 {textCount} 个，图片 {imageCount} 个";
        if (skippedImages > 0) message += $"，跳过无 Sprite 的图片 {skippedImages} 个";
        if (failedTranslations > 0) message += $"，翻译失败 {failedTranslations} 条（绑定已保留，可稍后重试）";
        Debug.Log(message);
    }

    [MenuItem("GameObject/本地化/自动挂载并绑定", true)]
    private static bool ValidateBindSelectedObjectsFromContextMenu() =>
        Selection.gameObjects.Any(target => GetLocalizableTextComponents(target).Any() || target.GetComponent<Image>() != null);

    [MenuItem("GameObject/本地化/移除本地化组件", false, 26)]
    private static void RemoveLocalizationFromSelectedObjects()
    {
        var removed = 0;
        foreach (var target in Selection.gameObjects.Distinct())
        {
            var components = target.GetComponents<LocalizeStringEvent>().Cast<Component>()
                .Concat(target.GetComponents<LocalizeSpriteEvent>())
                .Concat(target.GetComponents<LocalizedTextReceiver>())
                .Concat(target.GetComponents<LocalizedObjectSwitcher>())
                .Concat(target.GetComponents<LocalizedTMPSpacing>())
                .ToArray();
            foreach (var component in components)
            {
                if (component is LocalizedTextReceiver receiver)
                    receiver.RestoreOriginalText();
                if (component is LocalizedTMPSpacing spacing)
                    spacing.RestoreOriginalSpacing();
                Undo.DestroyObjectImmediate(component);
                removed++;
            }
            EditorUtility.SetDirty(target);
        }
        Debug.Log($"已移除 {removed} 个本地化组件");
    }

    [MenuItem("GameObject/本地化/移除本地化组件", true)]
    private static bool ValidateRemoveLocalizationFromSelectedObjects() => Selection.gameObjects.Any(target =>
        target.GetComponent<LocalizeStringEvent>() != null || target.GetComponent<LocalizeSpriteEvent>() != null ||
        target.GetComponent<LocalizedTextReceiver>() != null || target.GetComponent<LocalizedObjectSwitcher>() != null ||
        target.GetComponent<LocalizedTMPSpacing>() != null);

    [MenuItem("GameObject/本地化/添加语言物体显示切换", false, 29)]
    private static void AddLocalizedObjectSwitcherToSelectedObjects()
    {
        var added = 0;
        foreach (var target in Selection.gameObjects.Distinct())
        {
            var switcher = target.GetComponent<LocalizedObjectSwitcher>();
            if (switcher == null)
            {
                switcher = Undo.AddComponent<LocalizedObjectSwitcher>(target);
                added++;
            }
            Undo.RecordObject(switcher, "配置语言物体显示切换");
            switcher.EnsureDefaultLocaleGroups();
            EditorUtility.SetDirty(switcher);
            if (PrefabUtility.IsPartOfPrefabInstance(switcher))
                PrefabUtility.RecordPrefabInstancePropertyModifications(switcher);
        }
        Debug.Log($"语言物体显示切换：新增 {added} 个组件。请在常驻父物体的语言组中配置目标物体。");
    }

    [MenuItem("GameObject/本地化/添加语言物体显示切换", true)]
    private static bool ValidateAddLocalizedObjectSwitcherToSelectedObjects() => Selection.gameObjects.Length > 0;

    [MenuItem("GameObject/本地化/添加 TMP 多语言间距", false, 30)]
    private static void AddLocalizedTmpSpacingToSelectedObjects()
    {
        var added = 0;
        foreach (var tmp in Selection.gameObjects.Distinct().SelectMany(target => target.GetComponents<TMP_Text>()))
        {
            var spacing = tmp.GetComponent<LocalizedTMPSpacing>();
            if (spacing != null) continue;
            spacing = Undo.AddComponent<LocalizedTMPSpacing>(tmp.gameObject);
            spacing.ConfigureFromCurrent();
            EditorUtility.SetDirty(spacing);
            if (PrefabUtility.IsPartOfPrefabInstance(spacing))
                PrefabUtility.RecordPrefabInstancePropertyModifications(spacing);
            added++;
        }
        Debug.Log($"TMP 多语言间距：新增 {added} 个组件。中文保留字符间距，英文自动改用单词间距。");
    }

    [MenuItem("GameObject/本地化/添加 TMP 多语言间距", true)]
    private static bool ValidateAddLocalizedTmpSpacingToSelectedObjects() =>
        Selection.gameObjects.Any(target => target.GetComponent<TMP_Text>() != null);

    [MenuItem("GameObject/本地化/分析脚本并接入动态文本", false, 27)]
    private static async void AnalyzeSelectedObjectsForDynamicLocalization()
    {
        var behaviours = Selection.gameObjects
            .SelectMany(target => target.GetComponentsInChildren<MonoBehaviour>(true)
                .Concat(target.GetComponentsInParent<MonoBehaviour>(true)))
            .Where(item => item != null);
        await AnalyzeAndAttachDynamicLocalizationAsync(behaviours);
    }

    [MenuItem("GameObject/本地化/分析脚本并接入动态文本", true)]
    private static bool ValidateAnalyzeSelectedObjectsForDynamicLocalization() =>
        Selection.gameObjects.Any(target => target.GetComponentsInChildren<MonoBehaviour>(true).Any(item => item != null));

    [MenuItem("GameObject/本地化/补充缺失翻译", false, 28)]
    private static async void SupplementSelectedTextTranslations()
    {
        var pipeline = Resources.FindObjectsOfTypeAll<LocalizationPipelineWindow>().FirstOrDefault() ??
                       GetWindow<LocalizationPipelineWindow>("本地化工具");
        var handled = new HashSet<string>(StringComparer.Ordinal);
        var completed = 0;
        var failed = 0;
        foreach (var textComponent in Selection.gameObjects.Distinct().SelectMany(GetLocalizableTextComponents))
        {
            var source = textComponent is Text legacy ? legacy.text : ((TMP_Text)textComponent).text;
            if (string.IsNullOrWhiteSpace(source)) continue;
            var localizer = textComponent.GetComponents<LocalizeStringEvent>().FirstOrDefault();
            var collection = localizer == null
                ? GetOrCreateDefaultStringCollection()
                : LocalizationEditorSettings.GetStringTableCollection(localizer.StringReference.TableReference);
            if (collection == null)
            {
                failed++;
                Debug.LogWarning($"补充翻译失败：找不到 {textComponent.name} 使用的 String Table Collection");
                continue;
            }

            string key;
            if (localizer != null)
            {
                var shared = collection.SharedData.GetEntryFromReference(localizer.StringReference.TableEntryReference);
                key = shared?.Key;
                if (shared != null)
                {
                    var hans = GetOrCreateTable(collection, "zh-Hans");
                    var storedSource = hans.GetEntry(shared.Id)?.Value;
                    if (!string.IsNullOrEmpty(storedSource)) source = DecodeTableValue(storedSource);
                }
            }
            else key = null;
            if (string.IsNullOrEmpty(key)) key = EnsureDefaultStringEntry(collection, source);
            var identity = collection.TableCollectionName + "|" + key;
            if (!handled.Add(identity)) continue;

            try
            {
                await pipeline.EnsureDefaultStringTranslationsAsync(collection, key, source);
                completed++;
            }
            catch (Exception exception)
            {
                failed++;
                Debug.LogWarning($"补充翻译失败：{key}\n{exception.Message}");
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"补充翻译完成：处理 {completed} 条，失败 {failed} 条；有效译文未覆盖，与简中相同的未翻译内容已更新");
    }

    [MenuItem("GameObject/本地化/补充缺失翻译", true)]
    private static bool ValidateSupplementSelectedTextTranslations() =>
        Selection.gameObjects.Any(target => GetLocalizableTextComponents(target).Any());

    [MenuItem("Assets/本地化/扫描全项目中的选中脚本文本", false, 2100)]
    private static void AnalyzeSelectedScriptsForDynamicLocalization()
    {
        var scripts = Selection.objects.OfType<MonoScript>().ToList();
        var pipeline = Resources.FindObjectsOfTypeAll<LocalizationPipelineWindow>().FirstOrDefault() ??
                       GetWindow<LocalizationPipelineWindow>("本地化工具");
        pipeline.ScanSelectedScriptInstancesAcrossProject(scripts);
    }

    [MenuItem("Assets/本地化/扫描全项目中的选中脚本文本", true)]
    private static bool ValidateAnalyzeSelectedScriptsForDynamicLocalization() =>
        Selection.objects.OfType<MonoScript>().Any(script => script.GetClass() != null);

    private void OnEnable()
    {
        LoadScanFolders();
        RefreshRecordKeys();
    }
    private void OnDisable()
    {
        SaveScanFolders();
        DestroyImmediate(cardTexture);
        DestroyImmediate(primaryTexture);
        DestroyImmediate(successTexture);
        DestroyImmediate(flatTexture);
    }

    private void OnGUI()
    {
        EnsureStyles();
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), HubBackground);
        DrawHeader();

        var contentRect = new Rect(12, 58, Mathf.Max(100, position.width - 24), Mathf.Max(40, position.height - 86));
        GUILayout.BeginArea(contentRect);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        using (new EditorGUI.DisabledScope(busy))
        {
            DrawOverview();
            GUILayout.Space(8);
            DrawWorkflow();
            GUILayout.Space(8);
            DrawConfiguration();
            GUILayout.Space(10);
            DrawBindingMismatchPreview();
            if (bindingMismatches.Count > 0) GUILayout.Space(10);
            DrawDynamicPreview();
            if (dynamicPreviewItems.Count > 0) GUILayout.Space(10);
            DrawRecords();
        }
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();

        DrawStatusBar();
    }

    private void DrawHeader()
    {
        var rect = new Rect(0, 0, position.width, 50);
        EditorGUI.DrawRect(rect, HubToolbar);
        EditorGUI.DrawRect(new Rect(0, 49, position.width, 1), HubAccent);
        GUI.Label(new Rect(14, 7, position.width - 250, 23), "本地化流水线", titleStyle);
        GUI.Label(new Rect(15, 29, position.width - 250, 17), "扫描  ·  翻译  ·  生成 String Tables  ·  自动挂载", subtitleStyle);
        var stateRect = new Rect(Mathf.Max(16, position.width - 205), 13, 190, 24);
        EditorGUI.DrawRect(stateRect, busy ? HubWarning : HubSuccess);
        GUI.Label(stateRect, busy ? "处理中…" : "就绪", statusStyle);
    }

    private void DrawOverview()
    {
        var unique = records.Select(r => r.entryKey).Where(key => !string.IsNullOrEmpty(key)).Distinct().Count();
        var selected = records.Count(r => r.selected);
        var translated = records.GroupBy(r => r.entryKey).Select(group => group.First())
            .Count(r => !string.IsNullOrEmpty(r.traditional) && !string.IsNullOrEmpty(r.english));

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawStatCard("扫描组件", records.Count.ToString(), HubAccent);
            GUILayout.Space(6);
            DrawStatCard("唯一文本", unique.ToString(), new Color32(137, 102, 215, 255));
            GUILayout.Space(6);
            DrawStatCard("已翻译", $"{translated}/{unique}", HubSuccess);
            GUILayout.Space(6);
            DrawStatCard("已勾选", selected.ToString(), HubWarning);
        }
    }

    private void DrawWorkflow()
    {
        DrawSectionTitle("工作流程", HubAccent);
        using (new EditorGUILayout.VerticalScope(cardStyle))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("固定顺序：最底层 Prefab → 外层嵌套/Variant → 场景独立文本与真实 Override。扫描目录会自动补齐嵌套依赖；每一步也可以单独执行。", dimStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("执行完整 Prefab → 场景流水线", primaryButtonStyle, GUILayout.Width(230), GUILayout.Height(30))) _ = RunAllAsync();
            }
            GUILayout.Space(7);
            if (position.width >= 880)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawScanStep();
                    GUILayout.Space(6);
                    DrawActionStep("2", "翻译", "保护标签、组合文本与占位符", "翻译已勾选", () => _ = TranslateCheckedItemsAsync(), HubWarning);
                    GUILayout.Space(6);
                    DrawActionStep("3", "生成", "写入简中 / 繁中 / 英文表", "生成已勾选", () => GenerateTables(SelectedRecords(), false), new Color32(137, 102, 215, 255));
                    GUILayout.Space(6);
                    DrawActionStep("4", "挂载", "添加组件并保留实例 Override", "挂载已勾选", () => ApplyComponents(SelectedRecords()), HubSuccess);
                }
            }
            else
            {
                DrawScanStep();
                GUILayout.Space(6);
                DrawActionStep("2", "翻译", "保护标签、组合文本与占位符", "翻译已勾选", () => _ = TranslateCheckedItemsAsync(), HubWarning);
                GUILayout.Space(6);
                DrawActionStep("3", "生成", "写入简中 / 繁中 / 英文表", "生成已勾选", () => GenerateTables(SelectedRecords(), false), new Color32(137, 102, 215, 255));
                GUILayout.Space(6);
                DrawActionStep("4", "挂载", "添加组件并保留实例 Override", "挂载已勾选", () => ApplyComponents(SelectedRecords()), HubSuccess);
            }
        }
    }

    private void DrawScanStep()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(190)))
        {
            DrawStepHeading("1", "扫描", HubAccent);
            GUILayout.Label("选择一种范围获取待本地化文本", dimStyle);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全部 Prefab", flatButtonStyle, GUILayout.Height(25))) ScanAllPrefabs();
                if (GUILayout.Button("全部场景", flatButtonStyle, GUILayout.Height(25))) ScanAllScenes();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("已存目录", flatButtonStyle, GUILayout.Height(25))) ScanConfiguredFolders();
                if (GUILayout.Button("Project 选中项", flatButtonStyle, GUILayout.Height(25))) ScanProjectSelection();
            }
            if (GUILayout.Button("Project 选中脚本（全项目）", flatButtonStyle, GUILayout.Height(25)))
                ScanSelectedScriptInstancesAcrossProject(Selection.objects.OfType<MonoScript>());
            if (GUILayout.Button("检查全项目 Key / Text 错配", flatButtonStyle, GUILayout.Height(25)))
                ScanBindingMismatches();
        }
    }

    private void DrawActionStep(string number, string heading, string description, string buttonText, Action action, Color accent)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(165), GUILayout.MinHeight(91)))
        {
            DrawStepHeading(number, heading, accent);
            GUILayout.Label(description, dimStyle, GUILayout.MinHeight(30));
            if (GUILayout.Button(buttonText, flatButtonStyle, GUILayout.Height(25))) action();
        }
    }

    private void DrawStepHeading(string number, string heading, Color accent)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            var badge = GUILayoutUtility.GetRect(22, 20, GUILayout.Width(22));
            EditorGUI.DrawRect(badge, accent);
            GUI.Label(badge, number, statusStyle);
            GUILayout.Label(heading, sectionStyle);
            GUILayout.FlexibleSpace();
        }
    }

    private void DrawConfiguration()
    {
        showSettings = EditorGUILayout.Foldout(showSettings, "配置与扫描目录", true, sectionStyle);
        if (!showSettings) return;
        using (new EditorGUILayout.VerticalScope(cardStyle))
        {
            collectionName = EditorGUILayout.TextField("String Table", collectionName);
            outputDirectory = EditorGUILayout.TextField("资源目录", outputDirectory);
            using (new EditorGUILayout.HorizontalScope())
            {
                scanPrefabs = EditorGUILayout.ToggleLeft("扫描 Prefab", scanPrefabs, GUILayout.Width(115));
                scanScenes = EditorGUILayout.ToggleLeft("扫描场景", scanScenes, GUILayout.Width(105));
                removeMissingGeneratedEntries = EditorGUILayout.ToggleLeft("完整生成时删除失效的 auto.* 键", removeMissingGeneratedEntries);
            }
            GUILayout.Space(4);
            showTranslationSettings = EditorGUILayout.Foldout(showTranslationSettings, "翻译服务与并发", true);
            if (showTranslationSettings) DrawTranslationSettings();
            GUILayout.Space(4);
            showScanFolders = EditorGUILayout.Foldout(showScanFolders, $"已保存的扫描目录（{scanFolders.Count}）", true);
            if (showScanFolders) DrawScanFolderList();
        }
    }

    private void DrawTranslationSettings()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            translationConcurrency = EditorGUILayout.IntSlider("并发文本数", translationConcurrency, 1, 8);
            GUILayout.Label("启用多个服务时自动轮询分流；429、超时或 5xx 会切换到下一服务。", dimStyle);

            useGoogleTranslate = EditorGUILayout.ToggleLeft("Google Web（免配置）", useGoogleTranslate);
            useMyMemory = EditorGUILayout.ToggleLeft("MyMemory（免配置，公共额度）", useMyMemory);

            useLibreTranslate = EditorGUILayout.ToggleLeft("LibreTranslate（自建或托管）", useLibreTranslate);
            if (useLibreTranslate)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    libreTranslateEndpoint = EditorGUILayout.TextField("接口地址", libreTranslateEndpoint);
                    libreTraditionalLocale = EditorGUILayout.TextField("繁中语言码", libreTraditionalLocale);
                    libreTranslateApiKey = EditorGUILayout.PasswordField("API Key（本次会话）", libreTranslateApiKey);
                }
            }

            useDeepL = EditorGUILayout.ToggleLeft("DeepL API", useDeepL);
            if (useDeepL)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    deepLFreeApi = EditorGUILayout.Toggle("Free API 地址", deepLFreeApi);
                    deepLApiKey = EditorGUILayout.PasswordField("API Key（本次会话）", deepLApiKey);
                }
            }

            useAzureTranslator = EditorGUILayout.ToggleLeft("Azure Translator", useAzureTranslator);
            if (useAzureTranslator)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    azureTranslatorRegion = EditorGUILayout.TextField("Region（Global 可留空）", azureTranslatorRegion);
                    azureTranslatorKey = EditorGUILayout.PasswordField("API Key（本次会话）", azureTranslatorKey);
                }
            }

            useOpenAITranslation = EditorGUILayout.ToggleLeft("AI 翻译（OpenAI Responses API）", useOpenAITranslation);
            if (useOpenAITranslation)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    openAIEndpoint = EditorGUILayout.TextField("接口地址", openAIEndpoint);
                    openAIModel = EditorGUILayout.TextField("模型", openAIModel);
                    openAIApiKey = EditorGUILayout.PasswordField("API Key（本次会话）", openAIApiKey);
                    EditorGUILayout.LabelField("翻译上下文", dimStyle);
                    aiTranslationContext = EditorGUILayout.TextArea(aiTranslationContext, GUILayout.MinHeight(42));
                    if (GUILayout.Button("仅使用 AI 翻译已勾选", primaryButtonStyle, GUILayout.Height(27)))
                        _ = TranslateWithOpenAIAsync(SelectedRecords());
                    GUILayout.Label("普通“翻译已勾选”会把 AI 加入多服务调度；上面的按钮只使用 AI。", dimStyle);
                }
            }

            var enabledCount = GetEnabledProviders().Count;
            GUILayout.Label($"当前可用服务：{enabledCount}/6（未填写凭据的服务不会加入调度）", dimStyle);
        }
    }

    private void DrawDynamicPreview()
    {
        if (dynamicPreviewItems == null || dynamicPreviewItems.Count == 0) return;
        showDynamicPreview = EditorGUILayout.Foldout(showDynamicPreview,
            $"动态文本接入预览（{dynamicPreviewItems.Count}）", true, sectionStyle);
        if (!showDynamicPreview) return;

        using (new EditorGUILayout.VerticalScope(cardStyle))
        {
            EditorGUILayout.HelpBox("当前仅为分析结果，尚未修改对象或 Localization 表。文本组件会挂载可逆桥接；由原文转换器处理的事件或 Inspector 配置只生成“原文 → Key”表项，不修改调用代码。", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全选", flatButtonStyle, GUILayout.Width(56)))
                    dynamicPreviewItems.ForEach(item => item.selected = true);
                if (GUILayout.Button("全不选", flatButtonStyle, GUILayout.Width(64)))
                    dynamicPreviewItems.ForEach(item => item.selected = false);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("取消并清空", flatButtonStyle, GUILayout.Width(92)))
                {
                    dynamicPreviewItems.Clear();
                    status = "已取消动态文本接入";
                }
                using (new EditorGUI.DisabledScope(!dynamicPreviewItems.Any(item => item.selected && item.candidate != null &&
                    (item.sourceResolverOnly || item.target != null))))
                    if (GUILayout.Button("应用勾选项", successButtonStyle, GUILayout.Width(112), GUILayout.Height(26)))
                        _ = ApplyDynamicPreviewAsync();
            }

            foreach (var item in dynamicPreviewItems)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(18));
                        GUILayout.Label(item.candidate.source.Replace("\n", "↵"), sectionStyle);
                        GUILayout.FlexibleSpace();
                        DrawMiniTag(item.translationExists ? "已有翻译" : "待翻译",
                            item.translationExists ? HubSuccess : HubWarning);
                        GUILayout.Space(4);
                        DrawMiniTag(item.sourceResolverOnly ? "原文转 Key" : item.receiverExists ? "更新桥接" : "新增桥接", HubAccent);
                        if (item.occurrenceCount > 1)
                        {
                            GUILayout.Space(4);
                            DrawMiniTag($"{item.occurrenceCount} 处", new Color32(137, 102, 215, 255));
                        }
                    }
                    GUILayout.Label(item.targetPath, dimStyle);
                    GUILayout.Label($"来源  {item.scriptName} · {item.candidate.origin}", dimStyle);
                    GUILayout.Label("Key  " + item.entryKey, dimStyle);
                }
            }
        }
    }

    private void DrawBindingMismatchPreview()
    {
        if (bindingMismatches == null || bindingMismatches.Count == 0) return;
        DrawSectionTitle($"本地化 Key / Text 错配（{bindingMismatches.Count}）", HubWarning);
        using (new EditorGUILayout.VerticalScope(cardStyle))
        {
            EditorGUILayout.HelpBox("只列出资源自身文本和真实 Prefab/场景 Override；继承且未修改的实例不会重复出现。修复以 Text/TMP 序列化的实际中文为准。", MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全选", flatButtonStyle, GUILayout.Width(56)))
                    bindingMismatches.ForEach(item => item.selected = true);
                if (GUILayout.Button("全不选", flatButtonStyle, GUILayout.Width(64)))
                    bindingMismatches.ForEach(item => item.selected = false);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("清空", flatButtonStyle, GUILayout.Width(58))) bindingMismatches.Clear();
                using (new EditorGUI.DisabledScope(!bindingMismatches.Any(item => item.selected)))
                    if (GUILayout.Button("批量修复勾选项", successButtonStyle, GUILayout.Width(128), GUILayout.Height(26)))
                        _ = RepairSelectedBindingMismatchesAsync();
            }

            foreach (var item in bindingMismatches)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(18));
                        GUILayout.Label(Shorten(item.actualText, 72), sectionStyle);
                        GUILayout.FlexibleSpace();
                        DrawMiniTag("Key 错配", HubWarning);
                        GUILayout.Space(6);
                        if (GUILayout.Button("定位", flatButtonStyle, GUILayout.Width(52), GUILayout.Height(20)))
                            LocateAsset(item.assetPath);
                    }
                    GUILayout.Label(item.assetPath + "  /  " + item.hierarchyPath, dimStyle);
                    DrawTextRow("实际", item.actualText);
                    DrawTextRow("表中", string.IsNullOrEmpty(item.tableSourceText) ? "—" : item.tableSourceText);
                    GUILayout.Label($"当前 Key  {item.currentKey}  ·  {item.reason}", dimStyle);
                }
            }
        }
    }

    private void DrawRecords()
    {
        var visible = records.Where(r => string.IsNullOrWhiteSpace(filter) ||
            Contains(r.sourceText, filter) || Contains(r.assetPath, filter) || Contains(r.entryKey, filter)).ToList();
        DrawSectionTitle($"扫描结果  {visible.Count}/{records.Count}", HubAccent);
        using (new EditorGUILayout.HorizontalScope(cardStyle))
        {
            GUILayout.Label("搜索", dimStyle, GUILayout.Width(34));
            filter = EditorGUILayout.TextField(filter, GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarSearchField);
            if (GUILayout.Button("清除", flatButtonStyle, GUILayout.Width(48))) filter = string.Empty;
            if (GUILayout.Button("全选", flatButtonStyle, GUILayout.Width(48))) records.ForEach(r => r.selected = true);
            if (GUILayout.Button("全不选", flatButtonStyle, GUILayout.Width(56))) records.ForEach(r => r.selected = false);
            if (GUILayout.Button("选择筛选结果", flatButtonStyle, GUILayout.Width(92)))
            {
                records.ForEach(r => r.selected = visible.Contains(r));
            }
            if (GUILayout.Button("清空结果", flatButtonStyle, GUILayout.Width(68))) { records.Clear(); status = "已清空"; }
        }
        GUILayout.Space(4);
        foreach (var record in visible)
        {
            using (new EditorGUILayout.VerticalScope(cardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    record.selected = EditorGUILayout.Toggle(record.selected, GUILayout.Width(18));
                    record.expanded = EditorGUILayout.Foldout(record.expanded, Shorten(record.sourceText, 72), true, sectionStyle);
                    GUILayout.FlexibleSpace();
                    DrawMiniTag(string.IsNullOrEmpty(record.traditional) || string.IsNullOrEmpty(record.english) ? "待翻译" : "已翻译",
                        string.IsNullOrEmpty(record.traditional) || string.IsNullOrEmpty(record.english) ? HubWarning : HubSuccess);
                    GUILayout.Space(4);
                    if (record.codeControlled)
                    {
                        DrawMiniTag("动态", HubWarning);
                        GUILayout.Space(4);
                    }
                    DrawMiniTag(record.componentType.Replace("UnityEngine.UI.", string.Empty), HubAccent);
                    GUILayout.Space(6);
                    if (GUILayout.Button("定位", flatButtonStyle, GUILayout.Width(52), GUILayout.Height(20)))
                        Locate(record);
                }
                GUILayout.Label(record.assetPath + "  /  " + record.hierarchyPath, dimStyle);
                if (!record.expanded) continue;
                DrawThinDivider();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("翻译", flatButtonStyle, GUILayout.Width(58))) _ = TranslateRecordsAsync(new[] { record });
                    if (GUILayout.Button("生成", flatButtonStyle, GUILayout.Width(58))) GenerateTables(new[] { record }, false);
                    if (GUILayout.Button("挂载", flatButtonStyle, GUILayout.Width(58))) ApplyComponents(new[] { record });
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(record.entryKey, dimStyle);
                }
                DrawTextRow("简中", record.sourceText);
                DrawTextRow("繁中", string.IsNullOrEmpty(record.traditional) ? "—" : record.traditional);
                DrawTextRow("英文", string.IsNullOrEmpty(record.english) ? "—" : record.english);
                GUILayout.Label("分段  " + DescribeSegments(record.sourceText), dimStyle);
                if (record.codeControlled)
                    EditorGUILayout.HelpBox($"检测到代码控制：{record.codeControlReason}\n当前值只是运行时快照，不会生成静态 Key。请对该标签、父物体或脚本执行“分析脚本并接入动态文本”。", MessageType.Warning);
            }
            GUILayout.Space(3);
        }
        if (visible.Count == 0)
            EditorGUILayout.HelpBox(records.Count == 0 ? "尚无结果。请先执行扫描。" : "没有符合当前搜索条件的结果。", MessageType.Info);
    }

    private void DrawScanFolderList()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            for (var i = 0; i < scanFolders.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var next = EditorGUILayout.ObjectField(scanFolders[i], typeof(DefaultAsset), false) as DefaultAsset;
                    if (next != scanFolders[i])
                    {
                        if (next == null || AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(next)))
                        {
                            scanFolders[i] = next;
                            SaveScanFolders();
                        }
                        else ShowNotification(new GUIContent("只能添加 Assets 内的文件夹"));
                    }
                    if (GUILayout.Button("移除", GUILayout.Width(48)))
                    {
                        scanFolders.RemoveAt(i--);
                        SaveScanFolders();
                    }
                }
            }

            var dropArea = GUILayoutUtility.GetRect(0, 54, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(dropArea, new Color32(37, 40, 46, 255));
            GUI.Box(dropArea, "拖入一个或多个 Assets 文件夹\n目录会自动保存，下次打开无需重新拖入", dimStyle);
            var currentEvent = Event.current;
            if ((currentEvent.type == UnityEngine.EventType.DragUpdated || currentEvent.type == UnityEngine.EventType.DragPerform) && dropArea.Contains(currentEvent.mousePosition))
            {
                var validFolders = DragAndDrop.objectReferences.OfType<DefaultAsset>()
                    .Where(folder => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(folder))).ToList();
                DragAndDrop.visualMode = validFolders.Count > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                if (currentEvent.type == UnityEngine.EventType.DragPerform && validFolders.Count > 0)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var folder in validFolders)
                    {
                        var path = AssetDatabase.GetAssetPath(folder);
                        if (!scanFolders.Any(existing => existing != null && AssetDatabase.GetAssetPath(existing) == path)) scanFolders.Add(folder);
                    }
                    SaveScanFolders();
                }
                currentEvent.Use();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("添加空项", flatButtonStyle)) { scanFolders.Add(null); SaveScanFolders(); }
                if (GUILayout.Button("清空目录", flatButtonStyle)) { scanFolders.Clear(); SaveScanFolders(); }
            }
        }
    }

    private void EnsureStyles()
    {
        if (cardStyle != null) return;
        cardTexture = CreateColorTexture(HubCard);
        primaryTexture = CreateColorTexture(HubAccent);
        successTexture = CreateColorTexture(HubSuccess);
        flatTexture = CreateColorTexture(new Color32(58, 62, 70, 255));
        cardStyle = new GUIStyle { normal = { background = cardTexture }, padding = new RectOffset(10, 10, 8, 8), margin = new RectOffset(0, 0, 2, 4) };
        titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, normal = { textColor = new Color32(238, 240, 244, 255) } };
        subtitleStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color32(150, 157, 169, 255) } };
        sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, normal = { textColor = new Color32(225, 228, 234, 255) } };
        dimStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = new Color32(160, 166, 177, 255) } };
        primaryButtonStyle = CreateButtonStyle(primaryTexture);
        successButtonStyle = CreateButtonStyle(successTexture);
        flatButtonStyle = CreateButtonStyle(flatTexture);
        statusStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
    }

    private static GUIStyle CreateButtonStyle(Texture2D texture)
    {
        return new GUIStyle(GUI.skin.button)
        {
            normal = { background = texture, textColor = Color.white },
            hover = { background = texture, textColor = Color.white },
            active = { background = texture, textColor = Color.white },
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(8, 8, 4, 4)
        };
    }

    private static Texture2D CreateColorTexture(Color color)
    {
        var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private void DrawSectionTitle(string text, Color accent)
    {
        var rect = GUILayoutUtility.GetRect(0, 25, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + 4, 3, 17), accent);
        GUI.Label(new Rect(rect.x + 9, rect.y, rect.width - 9, rect.height), text, sectionStyle);
    }

    private void DrawStatCard(string label, string value, Color accent)
    {
        using (new EditorGUILayout.VerticalScope(cardStyle, GUILayout.MinHeight(55)))
        {
            var valueStyle = new GUIStyle(titleStyle) { normal = { textColor = accent } };
            GUILayout.Label(value, valueStyle);
            GUILayout.Label(label, dimStyle);
        }
    }

    private void DrawMiniTag(string text, Color color)
    {
        var content = new GUIContent(text);
        var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        var size = style.CalcSize(content);
        var rect = GUILayoutUtility.GetRect(size.x + 12, 19, GUILayout.Width(size.x + 12));
        EditorGUI.DrawRect(rect, color);
        GUI.Label(rect, content, style);
    }

    private void DrawTextRow(string locale, string text)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(locale, dimStyle, GUILayout.Width(34));
            GUILayout.Label(text, EditorStyles.wordWrappedLabel);
        }
    }

    private static string Shorten(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "（空文本）";
        var oneLine = value.Replace("\r", " ").Replace("\n", "  ");
        return oneLine.Length <= maxLength ? oneLine : oneLine.Substring(0, maxLength) + "…";
    }

    private static void DrawThinDivider()
    {
        var rect = GUILayoutUtility.GetRect(0, 5, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2, rect.width, 1), HubDivider);
    }

    private void DrawStatusBar()
    {
        var rect = new Rect(0, position.height - 22, position.width, 22);
        EditorGUI.DrawRect(rect, HubToolbar);
        EditorGUI.DrawRect(new Rect(0, rect.y, rect.width, 1), HubDivider);
        GUI.Label(new Rect(12, rect.y + 1, rect.width - 24, 20), "状态  ·  " + status, dimStyle);
    }

    private async Task RunAllAsync()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // Phase 1: author prefab assets first. Scene instances will inherit these changes.
        var prefabPaths = FindAssetPaths("t:Prefab", null);
        if (!ScanPaths(prefabPaths, Array.Empty<string>(), false)) return;
        var prefabRecords = records.ToList();
        await TranslateRecordsAsync(prefabRecords);
        if (HasMissingTranslations(prefabRecords)) { status = "Prefab 阶段存在未完成翻译，流水线已停止"; return; }
        GenerateTables(prefabRecords, false);
        ApplyComponents(prefabRecords);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        // Phase 2: rescan scenes after prefab propagation. Only scene-owned text and true overrides remain.
        var scenePaths = FindAssetPaths("t:Scene", null);
        if (!ScanPaths(Array.Empty<string>(), scenePaths, false)) return;
        var sceneRecords = records.ToList();
        await TranslateRecordsAsync(sceneRecords);
        if (HasMissingTranslations(sceneRecords)) { status = "场景阶段存在未完成翻译，流水线已停止"; return; }
        GenerateTables(sceneRecords, false);
        ApplyComponents(sceneRecords);

        // Full-project cleanup is safe only after both phases have contributed their active keys.
        records = prefabRecords.Concat(sceneRecords).OrderBy(record => record.assetPath).ThenBy(record => record.hierarchyPath).ToList();
        lastScanWasFullProject = true;
        GenerateTables(records, true);
        status = $"两阶段流水线完成：Prefab {prefabRecords.Count} 个，场景独立/Override {sceneRecords.Count} 个";
        Repaint();
    }

    private void ScanAllPrefabs()
    {
        ScanPaths(FindAssetPaths("t:Prefab", null), Array.Empty<string>(), false);
    }

    private void ScanAllScenes()
    {
        ScanPaths(Array.Empty<string>(), FindAssetPaths("t:Scene", null), false);
    }

    private void ScanBindingMismatches()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        busy = true;
        bindingMismatches.Clear();
        var prefabPaths = FindAssetPaths("t:Prefab", null);
        var scenePaths = FindAssetPaths("t:Scene", null);
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var completed = true;
        try
        {
            var total = Mathf.Max(1, prefabPaths.Length + scenePaths.Length);
            for (var i = 0; i < prefabPaths.Length; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("检查本地化 Key / Text",
                        $"Prefab {i + 1}/{prefabPaths.Length}  {prefabPaths[i]}", (float)i / total))
                {
                    completed = false;
                    break;
                }
                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(prefabPaths[i]);
                    ScanRootBindingMismatches(root, prefabPaths[i], false, 0);
                }
                finally
                {
                    if (root != null) PrefabUtility.UnloadPrefabContents(root);
                }
            }

            if (completed)
            {
                for (var i = 0; i < scenePaths.Length; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("检查本地化 Key / Text",
                            $"场景 {i + 1}/{scenePaths.Length}  {scenePaths[i]}",
                            (prefabPaths.Length + i) / (float)total))
                    {
                        completed = false;
                        break;
                    }
                    var scene = EditorSceneManager.OpenScene(scenePaths[i], OpenSceneMode.Additive);
                    try
                    {
                        var roots = scene.GetRootGameObjects();
                        for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                            ScanRootBindingMismatches(roots[rootIndex], scenePaths[i], true, rootIndex);
                    }
                    finally
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }

            bindingMismatches = bindingMismatches.OrderBy(item => item.assetPath)
                .ThenBy(item => item.hierarchyPath).ToList();
            status = completed
                ? $"Key/Text 检查完成：发现 {bindingMismatches.Count} 个真实错配"
                : $"检查已取消：保留 {bindingMismatches.Count} 个已发现错配";
        }
        catch (Exception exception)
        {
            status = "Key/Text 检查失败，请查看 Console";
            Debug.LogException(exception);
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(setup);
            EditorUtility.ClearProgressBar();
            busy = false;
            Repaint();
        }
    }

    private void ScanRootBindingMismatches(GameObject root, string assetPath, bool isScene, int rootIndex)
    {
        foreach (var text in root.GetComponentsInChildren<Text>(true))
            AddBindingMismatch(text, assetPath, root.transform, isScene, rootIndex);
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            AddBindingMismatch(text, assetPath, root.transform, isScene, rootIndex);
    }

    private void AddBindingMismatch(Component textComponent, string assetPath, Transform root,
        bool isScene, int rootIndex)
    {
        var receiver = textComponent.GetComponent<LocalizedTextReceiver>();
        var localizer = receiver == null
            ? textComponent.GetComponents<LocalizeStringEvent>().FirstOrDefault()
            : textComponent.GetComponents<LocalizeStringEvent>().FirstOrDefault(item => HasPersistentTarget(item, receiver)) ??
              textComponent.GetComponents<LocalizeStringEvent>().FirstOrDefault();
        if (localizer == null) return;

        // A plain inherited instance is owned and repaired by its source Prefab. Only a real Text
        // override belongs in an outer Prefab or scene result.
        if (PrefabUtility.IsPartOfPrefabInstance(textComponent) && IsUnmodifiedPrefabInstanceText(textComponent))
            return;

        var serializedText = new SerializedObject(textComponent)
            .FindProperty(textComponent is Text ? "m_Text" : "m_text")?.stringValue;
        if (string.IsNullOrWhiteSpace(serializedText) ||
            !ChineseRegex.IsMatch(RemoveProtectedTokens(serializedText))) return;

        var collection = LocalizationEditorSettings.GetStringTableCollection(localizer.StringReference.TableReference);
        var shared = collection?.SharedData.GetEntryFromReference(localizer.StringReference.TableEntryReference);
        var hans = collection?.GetTable(new LocaleIdentifier("zh-Hans")) as StringTable;
        var tableSource = shared == null ? string.Empty : DecodeTableValue(hans?.GetEntry(shared.Id)?.Value);
        if (BindingTextEquals(serializedText, tableSource)) return;

        var reason = collection == null ? "找不到 String Table Collection" :
            shared == null ? "Key 不存在" : string.IsNullOrEmpty(tableSource) ? "简中表内容为空" : "Key 指向了其它文本";
        bindingMismatches.Add(new BindingMismatch
        {
            assetPath = assetPath,
            hierarchyPath = MakeIndexedPath(textComponent.transform, root, isScene ? rootIndex : -1),
            componentType = textComponent.GetType().FullName,
            actualText = serializedText,
            tableSourceText = tableSource,
            currentKey = shared?.Key ?? localizer.StringReference.TableEntryReference.ToString(),
            collectionName = collection?.TableCollectionName ?? DefaultCollection,
            reason = reason
        });
    }

    private static bool BindingTextEquals(string actual, string tableSource)
    {
        static string Normalize(string value) => (value ?? string.Empty).Replace("\r\n", "\n").Trim();
        return string.Equals(Normalize(actual), Normalize(tableSource), StringComparison.Ordinal);
    }

    private async Task RepairSelectedBindingMismatchesAsync()
    {
        if (busy) return;
        var selected = bindingMismatches.Where(item => item.selected).ToList();
        if (selected.Count == 0) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        busy = true;
        var failures = 0;
        var repaired = 0;
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var uniqueSources = selected.GroupBy(item => item.collectionName + "|" + item.actualText,
                StringComparer.Ordinal).Select(group => group.First()).ToList();
            for (var i = 0; i < uniqueSources.Count; i++)
            {
                var item = uniqueSources[i];
                try
                {
                    var collection = LocalizationEditorSettings.GetStringTableCollection(item.collectionName) ??
                                     GetOrCreateDefaultStringCollection();
                    item.collectionName = collection.TableCollectionName;
                    item.correctedKey = EnsureDefaultStringEntry(collection, item.actualText);
                    await EnsureDefaultStringTranslationsAsync(collection, item.correctedKey, item.actualText);
                    foreach (var duplicate in selected.Where(candidate =>
                                 string.Equals(candidate.actualText, item.actualText, StringComparison.Ordinal) &&
                                 string.Equals(candidate.collectionName, item.collectionName, StringComparison.Ordinal)))
                        duplicate.correctedKey = item.correctedKey;
                }
                catch (Exception exception)
                {
                    failures++;
                    Debug.LogWarning($"Key/Text 修复准备失败：{item.assetPath} | {item.actualText}\n{exception.Message}");
                }
                status = $"准备修复：{i + 1}/{uniqueSources.Count}";
                EditorUtility.DisplayProgressBar("准备 Key/Text 批量修复", status, (i + 1f) / uniqueSources.Count);
                Repaint();
            }

            var prefabPaths = OrderPrefabPathsDependencyFirst(selected.Where(item =>
                    item.assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(item.correctedKey)).Select(item => item.assetPath).Distinct().ToArray(), true);
            foreach (var path in prefabPaths)
            foreach (var collectionGroup in selected.Where(item => item.assetPath == path && !string.IsNullOrEmpty(item.correctedKey))
                         .GroupBy(item => item.collectionName))
            {
                var collection = LocalizationEditorSettings.GetStringTableCollection(collectionGroup.Key) ??
                                 GetOrCreateDefaultStringCollection();
                repaired += ApplyPrefab(path, collectionGroup.Select(ToScanRecord).ToList(), collection);
            }

            foreach (var pathGroup in selected.Where(item =>
                         item.assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrEmpty(item.correctedKey)).GroupBy(item => item.assetPath))
            foreach (var collectionGroup in pathGroup.GroupBy(item => item.collectionName))
            {
                var collection = LocalizationEditorSettings.GetStringTableCollection(collectionGroup.Key) ??
                                 GetOrCreateDefaultStringCollection();
                repaired += ApplyScene(pathGroup.Key, collectionGroup.Select(ToScanRecord).ToList(), collection);
            }

            AssetDatabase.SaveAssets();
            var fixedItems = selected.Where(item => !string.IsNullOrEmpty(item.correctedKey)).ToList();
            bindingMismatches.RemoveAll(item => fixedItems.Contains(item));
            status = $"Key/Text 批量修复完成：组件 {repaired} 个，准备失败 {failures} 条";
            Debug.Log(status);
        }
        catch (Exception exception)
        {
            status = "Key/Text 批量修复失败，请查看 Console；已完成部分会保留";
            Debug.LogException(exception);
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(setup);
            EditorUtility.ClearProgressBar();
            busy = false;
            Repaint();
        }
    }

    private static ScanRecord ToScanRecord(BindingMismatch item) => new ScanRecord
    {
        assetPath = item.assetPath,
        hierarchyPath = item.hierarchyPath,
        componentType = item.componentType,
        sourceText = item.actualText,
        entryKey = item.correctedKey
    };

    private void ScanSelectedScriptInstancesAcrossProject(IEnumerable<MonoScript> selectedScripts)
    {
        var scripts = selectedScripts?.Where(script => script != null && script.GetClass() != null).Distinct().ToList()
                      ?? new List<MonoScript>();
        if (scripts.Count == 0)
        {
            status = "请先在 Project 窗口选择一个或多个 MonoScript";
            Repaint();
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        busy = true;
        dynamicPreviewItems.Clear();
        var bySource = new Dictionary<string, DynamicPreviewItem>(StringComparer.Ordinal);
        var selectedTypes = scripts.Select(script => script.GetClass()).ToHashSet();
        var prefabPaths = FindAssetPaths("t:Prefab", null);
        var scenePaths = FindAssetPaths("t:Scene", null);
        var completed = true;
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            // Code-owned strings do not require an instantiated component to be discoverable.
            // Include resolver/event sources from every selected script before inspecting its
            // serialized instances across Prefabs and scenes.
            var collection = LocalizationEditorSettings.GetStringTableCollection(DefaultCollection);
            foreach (var script in scripts)
            foreach (var candidate in ExtractDynamicSourceCandidates(script.text).Where(IsSourceResolverCandidate))
                AddSourceResolverPreview(candidate, AssetDatabase.GetAssetPath(script), script.name, collection, bySource);

            var total = Mathf.Max(1, prefabPaths.Length + scenePaths.Length);
            for (var i = 0; i < prefabPaths.Length; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("扫描选中脚本的 Inspector 文本",
                        $"Prefab {i + 1}/{prefabPaths.Length}  {prefabPaths[i]}", (float)i / total))
                {
                    completed = false;
                    break;
                }

                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(prefabPaths[i]);
                    CollectSelectedScriptMessages(root, prefabPaths[i], selectedTypes, bySource);
                }
                finally
                {
                    if (root != null) PrefabUtility.UnloadPrefabContents(root);
                }
            }

            if (completed)
            {
                for (var i = 0; i < scenePaths.Length; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("扫描选中脚本的 Inspector 文本",
                            $"场景 {i + 1}/{scenePaths.Length}  {scenePaths[i]}",
                            (prefabPaths.Length + i) / (float)total))
                    {
                        completed = false;
                        break;
                    }

                    var scene = EditorSceneManager.OpenScene(scenePaths[i], OpenSceneMode.Additive);
                    try
                    {
                        foreach (var root in scene.GetRootGameObjects())
                            CollectSelectedScriptMessages(root, scenePaths[i], selectedTypes, bySource);
                    }
                    finally
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }

            dynamicPreviewItems = bySource.Values.OrderBy(item => item.candidate.source).ToList();
            var occurrences = dynamicPreviewItems.Sum(item => item.occurrenceCount);
            status = completed
                ? $"选中脚本扫描完成：{scripts.Count} 个类型，{occurrences} 处中文配置，去重后 {dynamicPreviewItems.Count} 条待确认"
                : $"扫描已取消：保留 {occurrences} 处配置，去重后 {dynamicPreviewItems.Count} 条";
            showDynamicPreview = true;
        }
        catch (Exception exception)
        {
            status = "选中脚本全项目扫描失败，请查看 Console";
            Debug.LogException(exception);
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(setup);
            EditorUtility.ClearProgressBar();
            busy = false;
            Repaint();
        }
    }

    private static void CollectSelectedScriptMessages(GameObject root, string assetPath,
        ISet<Type> selectedTypes, IDictionary<string, DynamicPreviewItem> bySource)
    {
        if (root == null) return;
        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || !selectedTypes.Contains(behaviour.GetType())) continue;
            var typeName = behaviour.GetType().Name;
            foreach (var candidate in ExtractInspectorStringCandidates(behaviour))
            {
                candidate.origin = "原文转换器: " + candidate.origin;
                AddSourceResolverPreview(candidate, assetPath + " / " + GetObjectDisplayPath(behaviour),
                    typeName, LocalizationEditorSettings.GetStringTableCollection(DefaultCollection), bySource);
            }
        }
    }

    private static void AddSourceResolverPreview(DynamicSourceCandidate candidate, string targetPath,
        string scriptName, StringTableCollection collection, IDictionary<string, DynamicPreviewItem> bySource)
    {
        if (candidate == null || string.IsNullOrEmpty(candidate.source)) return;
        if (bySource.TryGetValue(candidate.source, out var existing))
        {
            existing.occurrenceCount++;
            return;
        }

        var normalKey = MakeEntryKey(candidate.source);
        var key = "dynamic." + normalKey.Substring(GeneratedPrefix.Length);
        bySource.Add(candidate.source, new DynamicPreviewItem
        {
            target = null,
            sourceResolverOnly = true,
            candidate = candidate,
            targetPath = targetPath,
            scriptName = scriptName,
            entryKey = key,
            receiverExists = false,
            translationExists = HasCompleteTranslation(collection, key)
        });
    }

    private void ScanProjectSelection()
    {
        var selectedPaths = CollectSelectedAssetPaths();
        if (selectedPaths.Count == 0) { status = "请在 Project 窗口选择一个或多个场景、Prefab 或文件夹"; return; }
        var prefabPaths = scanPrefabs ? FindAssetPaths("t:Prefab", selectedPaths) : Array.Empty<string>();
        var scenePaths = scanScenes ? FindAssetPaths("t:Scene", selectedPaths) : Array.Empty<string>();
        ScanPaths(prefabPaths, scenePaths, false);
    }

    private void ScanConfiguredFolders()
    {
        var paths = new HashSet<string>(scanFolders.Where(folder => folder != null).Select(AssetDatabase.GetAssetPath)
            .Where(AssetDatabase.IsValidFolder), StringComparer.OrdinalIgnoreCase);
        if (paths.Count == 0) { status = "请先拖入一个或多个 Assets 文件夹"; return; }
        var prefabPaths = scanPrefabs ? FindAssetPaths("t:Prefab", paths) : Array.Empty<string>();
        var scenePaths = scanScenes ? FindAssetPaths("t:Scene", paths) : Array.Empty<string>();
        ScanPaths(prefabPaths, scenePaths, false);
    }

    private bool ScanPaths(string[] prefabPaths, string[] scenePaths, bool fullProject)
    {
        if (!ValidateSettings()) return false;
        if (scenePaths.Length > 0 && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
        busy = true;
        records.Clear();
        dynamicTextDetected = 0;
        lastScanWasFullProject = fullProject;
        try
        {
            prefabPaths = OrderPrefabPathsDependencyFirst(prefabPaths, true);
            var completed = ScanPrefabs(prefabPaths) && ScanSceneAssets(scenePaths);
            records = records.OrderBy(r => r.assetPath).ThenBy(r => r.hierarchyPath).ToList();
            LoadExistingTranslations();
            var summary = $"{records.Count} 个组件，{records.Select(r => r.entryKey).Distinct().Count()} 条唯一文本，动态文本 {dynamicTextDetected} 个";
            status = completed ? $"扫描完成：{summary}" : $"扫描已取消，保留部分结果：{summary}";
            return completed;
        }
        catch (Exception exception)
        {
            status = "扫描失败，请查看 Console";
            Debug.LogException(exception);
            return false;
        }
        finally
        {
            busy = false;
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }

    private bool ScanPrefabs(string[] paths)
    {
        for (var i = 0; i < paths.Length; i++)
        {
            if (EditorUtility.DisplayCancelableProgressBar(
                    "扫描 Prefab",
                    $"{i + 1}/{paths.Length}  {paths[i]}",
                    paths.Length == 0 ? 1 : (float)i / paths.Length))
                return false;
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(paths[i]);
                ScanRoot(root, paths[i], false, 0);
            }
            finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
        }
        return true;
    }

    private bool ScanSceneAssets(string[] paths)
    {
        if (paths.Length == 0) return true;
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            for (var i = 0; i < paths.Length; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "扫描场景",
                        $"{i + 1}/{paths.Length}  {paths[i]}",
                        (float)i / paths.Length))
                    return false;
                var scene = EditorSceneManager.OpenScene(paths[i], OpenSceneMode.Additive);
                try
                {
                    var roots = scene.GetRootGameObjects();
                    for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                    {
                        var sceneProgress = (i + (float)rootIndex / Mathf.Max(1, roots.Length)) / paths.Length;
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "扫描场景",
                                $"{i + 1}/{paths.Length}  {paths[i]}  ·  根节点 {rootIndex + 1}/{roots.Length}",
                                sceneProgress))
                            return false;
                        ScanRoot(roots[rootIndex], paths[i], true, rootIndex);
                    }
                }
                finally { EditorSceneManager.CloseScene(scene, true); }
            }
            return true;
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }

    private void ScanRoot(GameObject root, string assetPath, bool isScene, int rootIndex)
    {
        foreach (var text in root.GetComponentsInChildren<Text>(true)) AddRecord(text, text.text, assetPath, root.transform, isScene, rootIndex);
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) AddRecord(text, text.text, assetPath, root.transform, isScene, rootIndex);
    }

    private void AddRecord(Component component, string value, string assetPath, Transform root, bool isScene, int rootIndex)
    {
        if (string.IsNullOrWhiteSpace(value) || !ChineseRegex.IsMatch(RemoveProtectedTokens(value))) return;
        if (IsUnmodifiedPrefabInstanceText(component)) return;
        var codeControlled = TryFindCodeControlledText(component, root, out var codeControlReason);
        if (codeControlled) dynamicTextDetected++;
        var hierarchyPath = MakeIndexedPath(component.transform, root, isScene ? rootIndex : -1);
        var key = MakeEntryKey(value);
        records.Add(new ScanRecord
        {
            assetPath = assetPath,
            hierarchyPath = hierarchyPath,
            componentType = component.GetType().FullName,
            sourceText = value,
            entryKey = key,
            codeControlled = codeControlled,
            codeControlReason = codeControlReason
        });
    }

    private async Task TranslateRecordsAsync(IEnumerable<ScanRecord> targetRecords)
    {
        if (busy || records.Count == 0) return;
        RefreshRecordKeys();
        LoadExistingTranslations();
        RefreshRecordKeys();
        var providers = GetEnabledProviders();
        // Code-controlled components contain runtime snapshots such as "操作记录：0".
        // Their templates are handled by dynamic script analysis, never as static translation rows.
        var unique = targetRecords.Where(r => r != null && !r.codeControlled)
            .GroupBy(r => r.entryKey).Select(g => g.First()).ToList();
        var pendingRecords = unique.Where(record => string.IsNullOrEmpty(record.traditional) || string.IsNullOrEmpty(record.english)).ToList();
        var skipped = unique.Count - pendingRecords.Count;
        if (pendingRecords.Count == 0)
        {
            status = $"所选 {unique.Count} 条文本均已有翻译，无需请求";
            Repaint();
            return;
        }
        if (providers.Count == 0)
        {
            status = "存在缺失翻译，但没有可用的翻译服务";
            return;
        }
        busy = true;
        try
        {
            var concurrencyGate = new SemaphoreSlim(Mathf.Clamp(translationConcurrency, 1, 8));
            var pending = pendingRecords.Select(record => TranslateRecordAsync(record, concurrencyGate)).ToList();
            var completed = 0;
            var failed = 0;
            while (pending.Count > 0)
            {
                var finishedTask = await Task.WhenAny(pending);
                pending.Remove(finishedTask);
                var result = await finishedTask;
                completed++;
                if (result.error != null)
                {
                    failed++;
                    Debug.LogWarning($"翻译失败：{result.record.entryKey}\n{result.error.Message}");
                }
                foreach (var duplicate in records.Where(r => r.entryKey == result.record.entryKey))
                {
                    duplicate.traditional = result.record.traditional;
                    duplicate.english = result.record.english;
                }
                status = $"翻译中：{completed}/{pendingRecords.Count}，已跳过 {skipped} 条，服务 {providers.Count} 个";
                EditorUtility.DisplayProgressBar("多服务并发翻译", status, (float)completed / pendingRecords.Count);
                Repaint();
            }
            status = failed == 0
                ? $"翻译完成：新增 {pendingRecords.Count} 条，跳过已有翻译 {skipped} 条"
                : $"翻译完成：新增 {pendingRecords.Count - failed}，跳过 {skipped}，失败 {failed}";
        }
        catch (Exception exception)
        {
            status = "翻译失败，请查看 Console；已完成的结果会保留";
            Debug.LogException(exception);
        }
        finally
        {
            busy = false;
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }

    private async Task TranslateCheckedItemsAsync()
    {
        if (busy) return;
        var selectedRecords = SelectedRecords().ToList();
        var selectedDynamicItems = dynamicPreviewItems
            .Where(item => item != null && item.selected && item.candidate != null).ToList();
        if (selectedRecords.Count == 0 && selectedDynamicItems.Count == 0)
        {
            status = "没有勾选需要翻译的固定文本或动态文本";
            Repaint();
            return;
        }

        if (selectedRecords.Count > 0)
            await TranslateRecordsAsync(selectedRecords);
        if (selectedDynamicItems.Count > 0)
            await TranslateDynamicPreviewItemsAsync(selectedDynamicItems);
    }

    private async Task TranslateDynamicPreviewItemsAsync(IEnumerable<DynamicPreviewItem> targetItems)
    {
        if (busy) return;
        var items = targetItems.Where(item => item != null && item.candidate != null).ToList();
        var unique = items.GroupBy(item => item.candidate.source, StringComparer.Ordinal)
            .Select(group => group.First()).ToList();
        if (unique.Count == 0) return;

        busy = true;
        var translated = 0;
        var skipped = 0;
        var failed = 0;
        try
        {
            var collection = GetOrCreateDefaultStringCollection();
            for (var index = 0; index < unique.Count; index++)
            {
                var item = unique[index];
                var key = EnsureDynamicStringEntry(collection, item.candidate);
                item.entryKey = key;
                try
                {
                    if (HasCompleteTranslation(collection, key))
                    {
                        skipped++;
                    }
                    else if (!string.IsNullOrEmpty(item.candidate.english))
                    {
                        EnsureExplicitTranslations(collection, key, item.candidate);
                        translated++;
                    }
                    else
                    {
                        await EnsureDefaultStringTranslationsAsync(collection, key, item.candidate.source);
                        translated++;
                    }
                }
                catch (Exception exception)
                {
                    failed++;
                    Debug.LogWarning($"动态文本翻译失败：{item.candidate.source}\n{exception.Message}");
                }

                var complete = HasCompleteTranslation(collection, key);
                foreach (var duplicate in items.Where(candidate =>
                             string.Equals(candidate.candidate.source, item.candidate.source, StringComparison.Ordinal)))
                    duplicate.translationExists = complete;
                status = $"动态文本翻译中：{index + 1}/{unique.Count}";
                EditorUtility.DisplayProgressBar("翻译动态文本", status, (index + 1f) / unique.Count);
                Repaint();
            }

            AssetDatabase.SaveAssets();
            status = failed == 0
                ? $"动态文本翻译完成：新增/更新 {translated} 条，跳过已有翻译 {skipped} 条"
                : $"动态文本翻译完成：新增/更新 {translated}，跳过 {skipped}，失败 {failed}";
        }
        catch (Exception exception)
        {
            status = "动态文本翻译失败，请查看 Console；已完成结果会保留";
            Debug.LogException(exception);
        }
        finally
        {
            busy = false;
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }

    private async Task<TranslationJobResult> TranslateRecordAsync(ScanRecord record, SemaphoreSlim concurrencyGate)
    {
        await concurrencyGate.WaitAsync();
        try
        {
            Task<string> traditionalTask = null;
            Task<string> englishTask = null;
            try
            {
                traditionalTask = string.IsNullOrEmpty(record.traditional)
                    ? TranslateTemplateAsync(record.sourceText, "zh-TW")
                    : Task.FromResult(record.traditional);
                englishTask = string.IsNullOrEmpty(record.english)
                    ? TranslateTemplateAsync(record.sourceText, "en")
                    : Task.FromResult(record.english);
                await Task.WhenAll(traditionalTask, englishTask);
                record.traditional = traditionalTask.Result;
                record.english = englishTask.Result;
                return new TranslationJobResult { record = record };
            }
            catch (Exception exception)
            {
                if (traditionalTask?.Status == TaskStatus.RanToCompletion) record.traditional = traditionalTask.Result;
                if (englishTask?.Status == TaskStatus.RanToCompletion) record.english = englishTask.Result;
                return new TranslationJobResult { record = record, error = exception };
            }
        }
        finally { concurrencyGate.Release(); }
    }

    private async Task TranslateWithOpenAIAsync(IEnumerable<ScanRecord> targetRecords)
    {
        if (string.IsNullOrWhiteSpace(openAIApiKey) || string.IsNullOrWhiteSpace(openAIEndpoint) || string.IsNullOrWhiteSpace(openAIModel))
        {
            status = "请先填写 AI 接口地址、模型和 API Key";
            return;
        }
        var google = useGoogleTranslate;
        var memory = useMyMemory;
        var libre = useLibreTranslate;
        var deepL = useDeepL;
        var azure = useAzureTranslator;
        var openAI = useOpenAITranslation;
        useGoogleTranslate = useMyMemory = useLibreTranslate = useDeepL = useAzureTranslator = false;
        useOpenAITranslation = true;
        try { await TranslateRecordsAsync(targetRecords); }
        finally
        {
            useGoogleTranslate = google;
            useMyMemory = memory;
            useLibreTranslate = libre;
            useDeepL = deepL;
            useAzureTranslator = azure;
            useOpenAITranslation = openAI;
            Repaint();
        }
    }

    private void GenerateTables(IEnumerable<ScanRecord> targetRecords, bool requestCleanup)
    {
        if (!ValidateSettings()) return;
        RefreshRecordKeys();
        var requested = targetRecords.Where(r => r != null).ToList();
        var selected = requested.Where(r => !r.codeControlled)
            .GroupBy(r => r.entryKey).Select(g => g.First()).ToList();
        var collection = GetOrCreateCollection();
        var removedSnapshots = RemoveCodeControlledSnapshotEntries(collection, requested);
        if (selected.Count == 0)
        {
            AssetDatabase.SaveAssets();
            status = removedSnapshots > 0
                ? $"没有静态文本需要生成；已清理 {removedSnapshots} 个动态快照 Key"
                : "没有静态文本需要生成；代码控制文本请使用脚本动态分析";
            return;
        }
        var missing = selected.Where(r => string.IsNullOrEmpty(r.traditional) || string.IsNullOrEmpty(r.english)).ToList();
        if (missing.Count > 0) { status = $"仍有 {missing.Count} 条未翻译"; return; }

        Directory.CreateDirectory(outputDirectory);
        AssetDatabase.Refresh();
        var hans = GetOrCreateTable(collection, "zh-Hans");
        var traditional = GetOrCreateTable(collection, "zh-TW");
        var english = GetOrCreateTable(collection, "en");
        var activeKeys = new HashSet<string>(selected.Select(r => r.entryKey));

        var isCompleteSelection = selected.Count == records.Select(r => r.entryKey).Distinct().Count();
        if (removeMissingGeneratedEntries && requestCleanup && lastScanWasFullProject && isCompleteSelection)
        {
            var obsolete = collection.SharedData.Entries.Where(e => e.Key.StartsWith(GeneratedPrefix, StringComparison.Ordinal) && !activeKeys.Contains(e.Key)).ToList();
            foreach (var entry in obsolete) collection.RemoveEntry(entry.Key);
        }

        foreach (var record in selected)
        {
            ValidateProtectedTokens(record);
            var shared = collection.SharedData.GetEntry(record.entryKey) ?? collection.SharedData.AddKey(record.entryKey);
            SetEntry(hans, shared.Id, record.sourceText);
            SetEntry(traditional, shared.Id, record.traditional);
            SetEntry(english, shared.Id, record.english);
        }

        EditorUtility.SetDirty(collection.SharedData);
        EditorUtility.SetDirty(hans);
        EditorUtility.SetDirty(traditional);
        EditorUtility.SetDirty(english);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        status = $"Localization 已生成：{selected.Count} 条 × 3 种语言；清理动态快照 {removedSnapshots} 条";
    }

    private int RemoveCodeControlledSnapshotEntries(StringTableCollection collection,
        IEnumerable<ScanRecord> requestedRecords)
    {
        if (collection == null) return 0;
        var staticSources = new HashSet<string>(records.Where(record => record != null && !record.codeControlled)
            .Select(record => record.sourceText), StringComparer.Ordinal);
        var removed = 0;
        foreach (var source in requestedRecords.Where(record => record.codeControlled)
                     .Select(record => record.sourceText)
                     .Where(source => !string.IsNullOrEmpty(source) && !staticSources.Contains(source)).Distinct())
        {
            foreach (var key in new[] { source, MakeEntryKey(source) }.Distinct())
            {
                if (collection.SharedData.GetEntry(key) == null) continue;
                collection.RemoveEntry(key);
                removed++;
            }
        }
        return removed;
    }

    private void ApplyComponents(IEnumerable<ScanRecord> targetRecords)
    {
        if (!ValidateSettings()) return;
        RefreshRecordKeys();
        var requested = targetRecords.Where(r => r != null).ToList();
        var dynamicSkipped = requested.Count(record => record.codeControlled);
        var selected = requested.Where(record => !record.codeControlled).ToList();
        if (selected.Count == 0)
        {
            status = dynamicSkipped > 0
                ? $"无需挂载：已跳过 {dynamicSkipped} 个代码控制的动态文本（仍可翻译并生成 String Table）"
                : "没有可挂载的文本";
            Repaint();
            return;
        }
        if (selected.Any(record => record.assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) &&
            !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var collection = LocalizationEditorSettings.GetStringTableCollection(collectionName);
        if (collection == null) { status = "请先生成 Localization 表"; return; }
        var changed = 0;
        busy = true;
        try
        {
            var groups = selected.GroupBy(r => r.assetPath).ToList();
            var prefabGroups = groups.Where(group => group.Key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            foreach (var prefabPath in OrderPrefabPathsDependencyFirst(prefabGroups.Keys, false))
            {
                changed += ApplyPrefab(prefabPath, prefabGroups[prefabPath], collection);
            }
            // Apply scene overrides after prefab assets so inherited components already exist.
            foreach (var assetGroup in groups.Where(group => group.Key.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)))
            {
                changed += ApplyScene(assetGroup.Key, assetGroup.ToList(), collection);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            status = dynamicSkipped > 0
                ? $"组件挂载完成：更新 {changed} 个 Text/TMP 组件，跳过动态文本 {dynamicSkipped} 个"
                : $"组件挂载完成：更新 {changed} 个 Text/TMP 组件";
        }
        catch (Exception exception)
        {
            status = "自动挂载失败，请查看 Console";
            Debug.LogException(exception);
        }
        finally { busy = false; EditorUtility.ClearProgressBar(); Repaint(); }
    }

    private int ApplyPrefab(string path, List<ScanRecord> assetRecords, StringTableCollection collection)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        var count = 0;
        try
        {
            foreach (var record in assetRecords)
            {
                var transform = ResolveIndexedPath(new[] { root }, record.hierarchyPath);
                if (transform == null) { Debug.LogWarning($"找不到 Prefab 文本节点：{path} | {record.hierarchyPath}"); continue; }
                var component = FindTextComponent(transform, record.componentType);
                if (component == null) continue;
                ConfigureLocalization(component, record, collection);
                count++;
            }
            if (count > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return count;
    }

    private int ApplyScene(string path, List<ScanRecord> assetRecords, StringTableCollection collection)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        var count = 0;
        try
        {
            var roots = scene.GetRootGameObjects();
            foreach (var record in assetRecords)
            {
                var transform = ResolveIndexedPath(roots, record.hierarchyPath);
                if (transform == null) { Debug.LogWarning($"找不到场景文本节点：{path} | {record.hierarchyPath}"); continue; }
                var component = FindTextComponent(transform, record.componentType);
                if (component == null) continue;
                ConfigureLocalization(component, record, collection);
                count++;
            }
            if (count > 0) EditorSceneManager.SaveScene(scene);
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
        return count;
    }

    private static void ConfigureLocalization(Component textComponent, ScanRecord record, StringTableCollection collection)
    {
        // A nested Prefab instance may override its Text without applying that value to the source
        // Prefab. The scan record is the value actually displayed by this instance, so it must also
        // own both the receiver source and the LocalizedString key override.
        ConfigureLocalization(textComponent, record.entryKey, collection, false, record.sourceText);
    }

    private static void ConfigureLocalization(Component textComponent, string entryKey, StringTableCollection collection,
        bool recordUndo, string authoritativeSource = null)
    {
        var receiver = textComponent.GetComponent<LocalizedTextReceiver>();
        if (receiver == null)
            receiver = recordUndo ? Undo.AddComponent<LocalizedTextReceiver>(textComponent.gameObject) : textComponent.gameObject.AddComponent<LocalizedTextReceiver>();
        if (recordUndo) Undo.RecordObject(receiver, "绑定文本本地化");
        var replaceOriginalSource = !string.IsNullOrEmpty(authoritativeSource);
        if (textComponent is Text legacy) receiver.Configure(legacy, replaceOriginalSource);
        else if (textComponent is TMP_Text tmp)
        {
            receiver.Configure(tmp, replaceOriginalSource);
            EnsureLocalizedTmpSpacing(tmp, recordUndo);
        }
        if (replaceOriginalSource)
            receiver.SetOriginalSource(authoritativeSource);

        var localizer = textComponent.GetComponents<LocalizeStringEvent>().FirstOrDefault(item => HasPersistentTarget(item, receiver));
        if (localizer == null)
        {
            localizer = recordUndo ? Undo.AddComponent<LocalizeStringEvent>(textComponent.gameObject) : textComponent.gameObject.AddComponent<LocalizeStringEvent>();
            if (recordUndo) Undo.RecordObject(localizer, "绑定文本本地化");
            UnityEventTools.AddPersistentListener(localizer.OnUpdateString, receiver.ApplyLocalizedText);
            localizer.OnUpdateString.SetPersistentListenerState(localizer.OnUpdateString.GetPersistentEventCount() - 1, UnityEventCallState.EditorAndRuntime);
        }
        else if (recordUndo) Undo.RecordObject(localizer, "绑定文本本地化");
        localizer.StringReference.TableReference = collection.TableCollectionNameReference;
        localizer.StringReference.TableEntryReference = entryKey;
        EditorUtility.SetDirty(receiver);
        EditorUtility.SetDirty(localizer);
        if (PrefabUtility.IsPartOfPrefabInstance(textComponent))
        {
            // Keep scene-specific changes as local overrides. Never call ApplyPrefabInstance/ApplyPropertyOverride here.
            PrefabUtility.RecordPrefabInstancePropertyModifications(receiver);
            PrefabUtility.RecordPrefabInstancePropertyModifications(localizer);
        }
    }

    internal static async void RefreshReceiverFromCurrentText(LocalizedTextReceiver receiver)
    {
        if (receiver == null) return;
        var textComponent = (Component)receiver.GetComponent<TMP_Text>() ?? receiver.GetComponent<Text>();
        if (textComponent == null)
        {
            EditorUtility.DisplayDialog("刷新本地化", "当前物体没有 Text 或 TMP_Text 组件。", "确定");
            return;
        }

        var source = textComponent is TMP_Text tmp ? tmp.text : ((Text)textComponent).text;
        if (string.IsNullOrWhiteSpace(source) || !ChineseRegex.IsMatch(RemoveProtectedTokens(source)))
        {
            EditorUtility.DisplayDialog("刷新本地化", "当前文本为空或不含需要本地化的中文。", "确定");
            return;
        }

        try
        {
            var collection = GetOrCreateDefaultStringCollection();
            var key = EnsureDefaultStringEntry(collection, source);
            var pipeline = Resources.FindObjectsOfTypeAll<LocalizationPipelineWindow>().FirstOrDefault() ??
                           GetWindow<LocalizationPipelineWindow>("本地化工具");
            await pipeline.EnsureDefaultStringTranslationsAsync(collection, key, source);
            ConfigureLocalization(textComponent, key, collection, true, source);
            EditorUtility.SetDirty(textComponent.gameObject);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            Debug.Log($"已按当前文本刷新本地化：{source} -> {key}", receiver);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, receiver);
            EditorUtility.DisplayDialog("刷新本地化失败", exception.Message, "确定");
        }
    }

    private static void EnsureLocalizedTmpSpacing(TMP_Text tmp, bool recordUndo)
    {
        if (tmp == null) return;
        var spacing = tmp.GetComponent<LocalizedTMPSpacing>();
        if (spacing == null)
        {
            // Zero character spacing does not create the Latin-letter problem and needs no extra component.
            if (Mathf.Approximately(tmp.characterSpacing, 0f)) return;
            spacing = recordUndo ? Undo.AddComponent<LocalizedTMPSpacing>(tmp.gameObject) :
                tmp.gameObject.AddComponent<LocalizedTMPSpacing>();
            spacing.ConfigureFromCurrent();
        }
        else if (!spacing.IsConfigured)
        {
            if (recordUndo) Undo.RecordObject(spacing, "配置 TMP 多语言间距");
            spacing.ConfigureFromCurrent();
        }
        EditorUtility.SetDirty(spacing);
        if (PrefabUtility.IsPartOfPrefabInstance(spacing))
            PrefabUtility.RecordPrefabInstancePropertyModifications(spacing);
    }

    private static void ConfigureImageLocalization(Image image, string entryKey, AssetTableCollection collection)
    {
        var localizer = image.GetComponents<LocalizeSpriteEvent>().FirstOrDefault(item => HasPersistentTarget(item, image));
        if (localizer == null)
        {
            localizer = Undo.AddComponent<LocalizeSpriteEvent>(image.gameObject);
            var setter = image.GetType().GetProperty("sprite")?.GetSetMethod();
            if (setter == null) throw new MissingMethodException(image.GetType().FullName, "set_sprite");
            var listener = Delegate.CreateDelegate(typeof(UnityAction<Sprite>), image, setter) as UnityAction<Sprite>;
            UnityEventTools.AddPersistentListener(localizer.OnUpdateAsset, listener);
            localizer.OnUpdateAsset.SetPersistentListenerState(localizer.OnUpdateAsset.GetPersistentEventCount() - 1, UnityEventCallState.EditorAndRuntime);
        }
        Undo.RecordObject(localizer, "绑定图片本地化");
        localizer.AssetReference.TableReference = collection.TableCollectionNameReference;
        localizer.AssetReference.TableEntryReference = entryKey;
        EditorUtility.SetDirty(localizer);
        if (PrefabUtility.IsPartOfPrefabInstance(image))
            PrefabUtility.RecordPrefabInstancePropertyModifications(localizer);
    }

    private static bool HasPersistentTarget(LocalizeStringEvent localizer, LocalizedTextReceiver receiver)
    {
        for (var i = 0; i < localizer.OnUpdateString.GetPersistentEventCount(); i++)
            if (localizer.OnUpdateString.GetPersistentTarget(i) == receiver) return true;
        return false;
    }

    private static bool HasPersistentTarget(LocalizeSpriteEvent localizer, Image image)
    {
        for (var i = 0; i < localizer.OnUpdateAsset.GetPersistentEventCount(); i++)
            if (localizer.OnUpdateAsset.GetPersistentTarget(i) == image) return true;
        return false;
    }

    private static IEnumerable<Component> GetLocalizableTextComponents(GameObject target)
    {
        foreach (var text in target.GetComponents<Text>()) yield return text;
        foreach (var text in target.GetComponents<TMP_Text>()) yield return text;
    }

    private static StringTableCollection GetOrCreateDefaultStringCollection()
    {
        Directory.CreateDirectory(DefaultDirectory);
        AssetDatabase.Refresh();
        return LocalizationEditorSettings.GetStringTableCollection(DefaultCollection) ??
               LocalizationEditorSettings.CreateStringTableCollection(DefaultCollection, DefaultDirectory);
    }

    private static string EnsureDefaultStringEntry(StringTableCollection collection, string source)
    {
        var key = MakeEntryKey(source);
        var shared = collection.SharedData.GetEntry(key) ?? collection.SharedData.AddKey(key);
        var hans = GetOrCreateTable(collection, "zh-Hans");
        SetEntry(hans, shared.Id, source);
        EditorUtility.SetDirty(collection.SharedData);
        EditorUtility.SetDirty(hans);
        return key;
    }

    private static async Task AnalyzeAndAttachDynamicLocalizationAsync(IEnumerable<MonoBehaviour> sourceBehaviours,
        IEnumerable<MonoScript> sourceScripts = null)
    {
        var behaviours = sourceBehaviours.Distinct().ToList();
        var scripts = sourceScripts?.Where(script => script != null).Distinct().ToList() ?? new List<MonoScript>();
        if (behaviours.Count == 0 && scripts.Count == 0)
        {
            Debug.LogWarning("没有找到选中脚本在当前场景或 Prefab Stage 中的实例");
            return;
        }

        // Analysis is intentionally read-only. All mutations are deferred until the user confirms the preview.
        var preview = new List<DynamicPreviewItem>();
        var collection = LocalizationEditorSettings.GetStringTableCollection(DefaultCollection);
        foreach (var behaviour in behaviours)
        {
            // Never analyze the localization infrastructure as business code. Its serialized source,
            // key and table fields would recursively become new localization candidates.
            if (behaviour is LocalizedTextReceiver || behaviour is LocalizeStringEvent ||
                behaviour is LocalizedTMPSpacing || behaviour is LocalizedObjectSwitcher)
                continue;
            var script = MonoScript.FromMonoBehaviour(behaviour);
            if (script == null) continue;
            var candidatesBySource = ExtractDynamicSourceCandidates(script.text)
                .ToDictionary(candidate => candidate.source, candidate => candidate, StringComparer.Ordinal);
            foreach (var candidate in ExtractInspectorStringCandidates(behaviour))
            {
                if (candidatesBySource.TryGetValue(candidate.source, out var existing))
                    existing.origin = existing.origin + "、" + candidate.origin;
                else candidatesBySource[candidate.source] = candidate;
            }
            if (candidatesBySource.Count == 0) continue;

            foreach (var candidate in candidatesBySource.Values.Where(IsSourceResolverCandidate))
            {
                var normalKey = MakeEntryKey(candidate.source);
                var key = "dynamic." + normalKey.Substring(GeneratedPrefix.Length);
                preview.Add(new DynamicPreviewItem
                {
                    target = behaviour,
                    sourceResolverOnly = true,
                    candidate = candidate,
                    targetPath = GetObjectDisplayPath(behaviour),
                    scriptName = behaviour.GetType().Name,
                    entryKey = key,
                    receiverExists = false,
                    translationExists = HasCompleteTranslation(collection, key)
                });
            }

            foreach (var candidate in candidatesBySource.Values.Where(candidate => !IsSourceResolverCandidate(candidate)))
            foreach (var target in FindTextTargetsForCandidate(behaviour, candidate))
            {
                var normalKey = MakeEntryKey(candidate.source);
                var key = "dynamic." + normalKey.Substring(GeneratedPrefix.Length);
                preview.Add(new DynamicPreviewItem
                {
                    target = target,
                    candidate = candidate,
                    targetPath = GetObjectDisplayPath(target),
                    scriptName = behaviour.GetType().Name,
                    entryKey = key,
                    receiverExists = target.GetComponent<LocalizedTextReceiver>() != null,
                    translationExists = HasCompleteTranslation(collection, key)
                });
            }
        }

        // A source-to-key entry does not need a scene object. This keeps script-asset analysis useful
        // even when the selected caller is only used by an unopened scene or prefab.
        foreach (var script in scripts)
        foreach (var candidate in ExtractDynamicSourceCandidates(script.text).Where(IsSourceResolverCandidate))
        {
            var normalKey = MakeEntryKey(candidate.source);
            var key = "dynamic." + normalKey.Substring(GeneratedPrefix.Length);
            preview.Add(new DynamicPreviewItem
            {
                target = null,
                sourceResolverOnly = true,
                candidate = candidate,
                targetPath = AssetDatabase.GetAssetPath(script),
                scriptName = script.name,
                entryKey = key,
                receiverExists = false,
                translationExists = HasCompleteTranslation(collection, key)
            });
        }

        preview = preview.Where(item => item.candidate != null && (item.sourceResolverOnly || item.target != null))
            .GroupBy(item => (item.sourceResolverOnly ? "resolver" : item.target.GetInstanceID().ToString()) + "|" + item.candidate.source)
            .Select(group => group.First()).ToList();
        if (preview.Count == 0)
        {
            Debug.LogWarning("没有找到可接入的动态文本或 ShowTips 原文");
            return;
        }

        var pipeline = Resources.FindObjectsOfTypeAll<LocalizationPipelineWindow>().FirstOrDefault() ??
                       GetWindow<LocalizationPipelineWindow>("本地化工具");
        pipeline.dynamicPreviewItems = preview;
        pipeline.showDynamicPreview = true;
        pipeline.status = $"动态分析完成：{behaviours.Count + scripts.Count} 个脚本/实例，待确认 {preview.Count} 项；尚未修改任何资源";
        pipeline.Show();
        pipeline.Focus();
        pipeline.Repaint();
        await Task.CompletedTask;
    }

    private async Task ApplyDynamicPreviewAsync()
    {
        var selectedItems = dynamicPreviewItems
            .Where(item => item.selected && item.candidate != null && (item.sourceResolverOnly || item.target != null)).ToList();
        if (selectedItems.Count == 0) return;

        busy = true;
        var failures = 0;
        try
        {
            var collection = GetOrCreateDefaultStringCollection();
            var keyBySource = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var candidate in selectedItems.Select(item => item.candidate)
                         .GroupBy(item => item.source).Select(group => group.First()))
            {
                var key = EnsureDynamicStringEntry(collection, candidate);
                keyBySource[candidate.source] = key;
                try
                {
                    if (!string.IsNullOrEmpty(candidate.english))
                        EnsureExplicitTranslations(collection, key, candidate);
                    else
                        await EnsureDefaultStringTranslationsAsync(collection, key, candidate.source);
                }
                catch (Exception exception)
                {
                    failures++;
                    Debug.LogWarning($"动态文本自动翻译失败：{candidate.source}\n{exception.Message}");
                }
            }

            foreach (var group in selectedItems.Where(item => !item.sourceResolverOnly).GroupBy(item => item.target))
            {
                var target = group.Key;
                var localizer = target.GetComponent<LocalizedTextReceiver>() ??
                                Undo.AddComponent<LocalizedTextReceiver>(target.gameObject);
                Undo.RecordObject(localizer, "接入动态文本本地化");
                if (target is Text legacy) localizer.Configure(legacy);
                else if (target is TMP_Text tmp)
                {
                    localizer.Configure(tmp);
                    EnsureLocalizedTmpSpacing(tmp, true);
                }
                foreach (var candidate in group.Select(item => item.candidate)
                             .GroupBy(item => item.source).Select(items => items.First()))
                    localizer.AddOrUpdateDynamicBinding(candidate.source, collection.TableCollectionName,
                        keyBySource[candidate.source]);
                EditorUtility.SetDirty(localizer);
                if (PrefabUtility.IsPartOfPrefabInstance(target))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(localizer);
            }
            AssetDatabase.SaveAssets();
            dynamicPreviewItems.RemoveAll(item => selectedItems.Contains(item));
            var componentCount = selectedItems.Where(item => !item.sourceResolverOnly)
                .Select(item => item.target).Distinct().Count();
            var resolverCount = selectedItems.Count(item => item.sourceResolverOnly);
            status = $"动态接入完成：组件 {componentCount} 个，原文转换 {resolverCount} 条，源值 {keyBySource.Count} 条，翻译失败 {failures} 条";
            Debug.Log(status);
        }
        finally
        {
            busy = false;
            Repaint();
        }
    }

    private static bool HasCompleteTranslation(StringTableCollection collection, string key)
    {
        if (collection == null) return false;
        var shared = collection.SharedData.GetEntry(key);
        if (shared == null) return false;
        var hans = collection.GetTable(new LocaleIdentifier("zh-Hans")) as StringTable;
        var traditional = collection.GetTable(new LocaleIdentifier("zh-TW")) as StringTable;
        var english = collection.GetTable(new LocaleIdentifier("en")) as StringTable;
        var source = DecodeTableValue(hans?.GetEntry(shared.Id)?.Value);
        var traditionalValue = DecodeTableValue(traditional?.GetEntry(shared.Id)?.Value);
        var englishValue = DecodeTableValue(english?.GetEntry(shared.Id)?.Value);
        var needsEnglish = NeedsEnglishTranslation(source, englishValue);
        return !needsEnglish && !NeedsTraditionalTranslation(source, traditionalValue, needsEnglish);
    }

    private static string GetObjectDisplayPath(Component component)
    {
        var names = new Stack<string>();
        for (var current = component.transform; current != null; current = current.parent) names.Push(current.name);
        var location = component.gameObject.scene.IsValid()
            ? component.gameObject.scene.path
            : AssetDatabase.GetAssetPath(component.gameObject);
        if (string.IsNullOrEmpty(location)) location = "当前对象";
        return location + " / " + string.Join("/", names) + " / " + component.GetType().Name;
    }

    private static List<DynamicSourceCandidate> ExtractDynamicSourceCandidates(string source)
    {
        var result = new Dictionary<string, DynamicSourceCandidate>(StringComparer.Ordinal);
        // Read every string literal in an expression that writes Text/TMP. This covers direct values,
        // ternaries, string.Format and interpolated strings without treating unrelated log text as UI.
        foreach (Match expressionMatch in Regex.Matches(source ?? string.Empty,
                     @"\b(?<field>[A-Za-z_]\w*)\s*(?:\.\s*text\s*=|\.\s*SetText\s*\()[\s\S]{0,2000}?(?:;|\)\s*;)",
                     RegexOptions.Multiline))
        {
            var targetFieldName = expressionMatch.Groups["field"].Value;
            foreach (Match literalMatch in Regex.Matches(expressionMatch.Value,
                         "(?:\\$)?\"((?:\\\\.|[^\"\\\\])*)\""))
            {
                var value = Regex.Unescape(literalMatch.Groups[1].Value);
                if (ChineseRegex.IsMatch(RemoveProtectedTokens(value)))
                {
                    if (!result.TryGetValue(value, out var candidate))
                    {
                        candidate = new DynamicSourceCandidate
                            { source = value, hans = value, origin = "代码文本赋值" };
                        result[value] = candidate;
                    }
                    candidate.targetFieldNames.Add(targetFieldName);
                }
            }
        }

        // Event-driven tips keep their existing Chinese arguments. TipsUI resolves these values to
        // semantic keys at runtime, so analysis only needs to create/translate their table entries.
        foreach (Match eventMatch in Regex.Matches(source ?? string.Empty,
                     "EventTrigger\\s*<[^>]+>\\s*\\(\\s*EventType\\s*\\.\\s*ShowTips\\s*,\\s*(?:\\$)?\"((?:\\\\.|[^\"\\\\])*)\"",
                     RegexOptions.Multiline))
        {
            var value = Regex.Unescape(eventMatch.Groups[1].Value);
            if (ChineseRegex.IsMatch(RemoveProtectedTokens(value)))
                result[value] = new DynamicSourceCandidate
                {
                    source = value,
                    hans = value,
                    origin = "ShowTips 事件原文"
                };
        }

        // Include members only for enums that can be tied to a value assigned through ToString().
        var toStringVariables = Regex.Matches(source ?? string.Empty, @"\.\s*text\s*=\s*([A-Za-z_]\w*)\s*\.\s*ToString\s*\(\s*\)")
            .Cast<Match>().Select(match => match.Groups[1].Value).Distinct().ToList();
        foreach (Match match in Regex.Matches(source ?? string.Empty, @"\b([A-Za-z_]\w*)\s*\."))
        {
            var enumName = match.Groups[1].Value;
            if (!toStringVariables.Any(variable =>
                    Regex.IsMatch(source, $@"\b{Regex.Escape(variable)}\s*==\s*{Regex.Escape(enumName)}\s*\.") ||
                    Regex.IsMatch(source, $@"switch\s*\(\s*{Regex.Escape(variable)}\s*\)[\s\S]{{0,1600}}?case\s+{Regex.Escape(enumName)}\s*\.")))
                continue;
            var enumType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(enumName))
                .FirstOrDefault(type => type != null && type.IsEnum);
            if (enumType == null) continue;
            foreach (var name in Enum.GetNames(enumType).Where(name => ChineseRegex.IsMatch(name)))
                result[name] = new DynamicSourceCandidate { source = name, hans = name, origin = "枚举 ToString" };
        }

        if ((source ?? string.Empty).Contains("QualitySettings.names"))
        {
            AddExplicitCandidate(result, "Low", "低", "低", "Low");
            AddExplicitCandidate(result, "Medium", "中", "中", "Medium");
            AddExplicitCandidate(result, "High", "高", "高", "High");
        }
        OverrideKnownSettingsState(result, "关闭", "關閉", "Off");
        OverrideKnownSettingsState(result, "开启", "開啟", "On");
        OverrideKnownSettingsState(result, "显示", "顯示", "Show");
        OverrideKnownSettingsState(result, "全屏", "全螢幕", "Fullscreen");
        OverrideKnownSettingsState(result, "窗口", "視窗", "Window");
        return result.Values.ToList();
    }

    private static bool IsSourceResolverCandidate(DynamicSourceCandidate candidate) =>
        candidate != null && candidate.origin != null &&
        (candidate.origin.IndexOf("ShowTips 事件原文", StringComparison.Ordinal) >= 0 ||
         candidate.origin.IndexOf("原文转换器:", StringComparison.Ordinal) >= 0);

    private static IEnumerable<DynamicSourceCandidate> ExtractInspectorStringCandidates(MonoBehaviour behaviour)
    {
        if (behaviour == null) yield break;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var serializedObject = new SerializedObject(behaviour);
        var property = serializedObject.GetIterator();
        var enterChildren = true;
        while (property.Next(enterChildren))
        {
            enterChildren = true; // Required for strings nested in arrays, lists and serializable classes.
            if (property.propertyType != SerializedPropertyType.String) continue;
            var value = property.stringValue;
            if (string.IsNullOrWhiteSpace(value) ||
                !ChineseRegex.IsMatch(RemoveProtectedTokens(value)) || !seen.Add(value)) continue;
            yield return new DynamicSourceCandidate
            {
                source = value,
                hans = value,
                origin = "Inspector: " + property.propertyPath
            };
        }
    }

    private static void OverrideKnownSettingsState(IDictionary<string, DynamicSourceCandidate> result, string source,
        string traditional, string english)
    {
        if (result.ContainsKey(source)) AddExplicitCandidate(result, source, source, traditional, english);
    }

    private static void AddExplicitCandidate(IDictionary<string, DynamicSourceCandidate> result, string source,
        string hans, string traditional, string english)
    {
        var origin = result.TryGetValue(source, out var existing) ? existing.origin : "代码推断";
        result[source] = new DynamicSourceCandidate
        {
            source = source,
            hans = hans,
            traditional = traditional,
            english = english,
            origin = origin
        };
    }

    private static IEnumerable<Component> FindTextTargetsForCandidate(MonoBehaviour behaviour,
        DynamicSourceCandidate candidate)
    {
        var found = new HashSet<Component>();
        if (behaviour == null || candidate == null) return found;
        var script = MonoScript.FromMonoBehaviour(behaviour);
        if (script == null) return found;

        var serializedObject = new SerializedObject(behaviour);
        var property = serializedObject.GetIterator();
        while (property.Next(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference ||
                !(property.objectReferenceValue is Component target) ||
                (!(target is Text) && !(target is TMP_Text))) continue;
            var fieldName = property.propertyPath.Split('.')[0];
            if (candidate.targetFieldNames.Count > 0
                    ? candidate.targetFieldNames.Contains(fieldName)
                    : ScriptWritesTextField(script.text, fieldName))
                found.Add(target);
        }

        CollectWrittenTextFieldsByReflection(behaviour, candidate.targetFieldNames, found);
        return found;
    }

    private static void CollectWrittenTextFieldsByReflection(MonoBehaviour behaviour,
        ISet<string> requestedFieldNames, ISet<Component> found)
    {
        if (behaviour == null) return;
        var script = MonoScript.FromMonoBehaviour(behaviour);
        if (script == null || string.IsNullOrEmpty(script.text)) return;

        // SerializedObject can omit an inherited Prefab value in some instance/Prefab Stage states.
        // Reading the actual field value is a reliable fallback, while ScriptWritesTextField keeps
        // unrelated Text references (font/color effects, callbacks, etc.) out of the result.
        for (var type = behaviour.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                  BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!typeof(Text).IsAssignableFrom(field.FieldType) &&
                    !typeof(TMP_Text).IsAssignableFrom(field.FieldType)) continue;
                if (requestedFieldNames != null && requestedFieldNames.Count > 0
                        ? !requestedFieldNames.Contains(field.Name)
                        : !ScriptWritesTextField(script.text, field.Name)) continue;
                try
                {
                    if (field.GetValue(behaviour) is Component target && target != null)
                        found.Add(target);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"读取动态文本字段失败：{behaviour.GetType().Name}.{field.Name}\n{exception.Message}");
                }
            }
        }
    }

    private static void CollectSerializedTextReferences(MonoBehaviour owner, ISet<Component> found, ISet<int> visited, int depth)
    {
        if (owner == null || !visited.Add(owner.GetInstanceID())) return;
        var serializedObject = new SerializedObject(owner);
        var property = serializedObject.GetIterator();
        while (property.Next(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
            if (property.objectReferenceValue is Text text) found.Add(text);
            else if (property.objectReferenceValue is TMP_Text tmp) found.Add(tmp);
            else if (depth == 0 && property.objectReferenceValue is MonoBehaviour nested)
                CollectSerializedTextReferences(nested, found, visited, depth + 1);
        }
    }

    private static void EnsureExplicitTranslations(StringTableCollection collection, string key, DynamicSourceCandidate candidate)
    {
        var shared = collection.SharedData.GetEntry(key) ?? collection.SharedData.AddKey(key);
        var hans = GetOrCreateTable(collection, "zh-Hans");
        var traditional = GetOrCreateTable(collection, "zh-TW");
        var english = GetOrCreateTable(collection, "en");
        SetEntry(hans, shared.Id, candidate.hans);
        SetEntry(traditional, shared.Id, candidate.traditional);
        SetEntry(english, shared.Id, candidate.english);
        EditorUtility.SetDirty(collection.SharedData);
        EditorUtility.SetDirty(hans);
        EditorUtility.SetDirty(traditional);
        EditorUtility.SetDirty(english);
    }

    private static string EnsureDynamicStringEntry(StringTableCollection collection, DynamicSourceCandidate candidate)
    {
        var normalKey = MakeEntryKey(candidate.source);
        var key = "dynamic." + normalKey.Substring(GeneratedPrefix.Length);
        var shared = collection.SharedData.GetEntry(key) ?? collection.SharedData.AddKey(key);
        var hans = GetOrCreateTable(collection, "zh-Hans");
        SetEntry(hans, shared.Id, candidate.hans ?? candidate.source);
        EditorUtility.SetDirty(collection.SharedData);
        EditorUtility.SetDirty(hans);
        return key;
    }

    private async Task EnsureDefaultStringTranslationsAsync(StringTableCollection collection, string key, string source)
    {
        var shared = collection.SharedData.GetEntry(key) ?? collection.SharedData.AddKey(key);
        var hansTable = GetOrCreateTable(collection, "zh-Hans");
        var traditionalTable = GetOrCreateTable(collection, "zh-TW");
        var englishTable = GetOrCreateTable(collection, "en");
        var traditional = DecodeTableValue(traditionalTable.GetEntry(shared.Id)?.Value);
        var english = DecodeTableValue(englishTable.GetEntry(shared.Id)?.Value);
        if (TryGetExactUiTranslation(NormalizeTranslationInput(source), "zh-TW", out var exactTraditional) &&
            !string.Equals(traditional, exactTraditional, StringComparison.Ordinal))
        {
            traditional = exactTraditional;
            SetEntry(traditionalTable, shared.Id, traditional);
            EditorUtility.SetDirty(traditionalTable);
        }
        if (TryGetExactUiTranslation(NormalizeTranslationInput(source), "en", out var exactEnglish) &&
            !string.Equals(english, exactEnglish, StringComparison.Ordinal))
        {
            english = exactEnglish;
            SetEntry(englishTable, shared.Id, english);
            EditorUtility.SetDirty(englishTable);
        }
        var needsEnglish = NeedsEnglishTranslation(source, english);
        var needsTraditional = NeedsTraditionalTranslation(source, traditional, needsEnglish);
        var forceEnglish = needsEnglish && !string.IsNullOrWhiteSpace(english);
        var forceTraditional = needsTraditional && !string.IsNullOrWhiteSpace(traditional);

        // Reuse translation memory even when an older tool version generated a different key.
        if (needsTraditional || needsEnglish)
        {
            foreach (var remembered in collection.SharedData.Entries)
            {
                if (remembered.Id == shared.Id || DecodeTableValue(hansTable.GetEntry(remembered.Id)?.Value) != source) continue;
                var rememberedTraditional = DecodeTableValue(traditionalTable.GetEntry(remembered.Id)?.Value);
                var rememberedEnglish = DecodeTableValue(englishTable.GetEntry(remembered.Id)?.Value);
                var rememberedNeedsEnglish = NeedsEnglishTranslation(source, rememberedEnglish);
                var rememberedNeedsTraditional = NeedsTraditionalTranslation(source, rememberedTraditional, rememberedNeedsEnglish);
                if (needsEnglish && !rememberedNeedsEnglish) english = rememberedEnglish;
                if (needsTraditional && !rememberedNeedsTraditional) traditional = rememberedTraditional;
                needsEnglish = NeedsEnglishTranslation(source, english);
                needsTraditional = NeedsTraditionalTranslation(source, traditional, needsEnglish);
                if (!needsTraditional && !needsEnglish) break;
            }
            if (!needsTraditional) SetEntry(traditionalTable, shared.Id, traditional);
            if (!needsEnglish) SetEntry(englishTable, shared.Id, english);
        }
        if (!needsTraditional && !needsEnglish)
        {
            EditorUtility.SetDirty(traditionalTable);
            EditorUtility.SetDirty(englishTable);
            AssetDatabase.SaveAssets();
            return;
        }
        if (GetEnabledProviders().Count == 0)
            throw new InvalidOperationException("没有启用可用的翻译服务，请先在本地化工具中配置翻译服务");

        Exception failure = null;
        if (needsTraditional)
        {
            try
            {
                traditional = await TranslateTemplateAsync(source, "zh-TW", forceTraditional);
                if (TokenSignature(source) != TokenSignature(traditional))
                    throw new InvalidDataException("繁中译文的富文本标签或占位符不一致");
                SetEntry(traditionalTable, shared.Id, traditional);
                EditorUtility.SetDirty(traditionalTable);
            }
            catch (Exception exception) { failure = exception; }
        }
        if (needsEnglish)
        {
            try
            {
                english = await TranslateTemplateAsync(source, "en", forceEnglish);
                if (TokenSignature(source) != TokenSignature(english))
                    throw new InvalidDataException("英文译文的富文本标签或占位符不一致");
                if (NeedsEnglishTranslation(source, english))
                    throw new InvalidDataException("翻译服务返回的英文仍与中文原文相同");
                SetEntry(englishTable, shared.Id, english);
                EditorUtility.SetDirty(englishTable);
            }
            catch (Exception exception) { failure ??= exception; }
        }
        EditorUtility.SetDirty(collection.SharedData);
        AssetDatabase.SaveAssets();
        if (failure != null) throw failure;
    }

    private static AssetTableCollection GetOrCreateDefaultAssetCollection()
    {
        Directory.CreateDirectory(DefaultAssetDirectory);
        AssetDatabase.Refresh();
        var collection = LocalizationEditorSettings.GetAssetTableCollection(DefaultAssetCollection) ??
                         LocalizationEditorSettings.CreateAssetTableCollection(DefaultAssetCollection, DefaultAssetDirectory);
        AssetDatabase.SaveAssets();
        return collection;
    }

    private static string EnsureDefaultSpriteEntry(AssetTableCollection collection, Sprite sprite)
    {
        var slug = Regex.Replace(sprite.name.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", "_").Trim('_');
        if (string.IsNullOrEmpty(slug)) slug = "sprite";
        if (slug.Length > 24) slug = slug.Substring(0, 24);
        var identity = AssetDatabase.GetAssetPath(sprite) + "|" + sprite.name;
        var key = $"asset.{slug}.{Hash128.Compute(identity).ToString().Substring(0, 10)}";
        collection.AddAssetToTable(new LocaleIdentifier("zh-Hans"), key, sprite, true);
        collection.AddAssetToTable(new LocaleIdentifier("zh-TW"), key, sprite, true);
        collection.AddAssetToTable(new LocaleIdentifier("en"), key, sprite, true);
        return key;
    }

    private StringTableCollection GetOrCreateCollection()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(collectionName);
        return collection ?? LocalizationEditorSettings.CreateStringTableCollection(collectionName, outputDirectory);
    }

    private static StringTable GetOrCreateTable(StringTableCollection collection, string localeCode)
    {
        var id = new LocaleIdentifier(localeCode);
        return collection.GetTable(id) as StringTable ?? collection.AddNewTable(id) as StringTable;
    }

    private static void SetEntry(StringTable table, long id, string value)
    {
        var encodedValue = EncodeTableValue(value);
        var entry = table.GetEntry(id) ?? table.AddEntry(id, encodedValue);
        entry.Value = encodedValue;
        entry.IsSmart = EscapedBraceTokenRegex.IsMatch(encodedValue);
    }

    private static string EncodeTableValue(string value)
    {
        return LiteralBraceTokenRegex.Replace(value ?? string.Empty, "{{$1}}");
    }

    private static string DecodeTableValue(string value)
    {
        return EscapedBraceTokenRegex.Replace(value ?? string.Empty, "{$1}");
    }

    private static int RepairLiteralBraceTokens(StringTableCollection collection)
    {
        if (collection == null) return 0;
        var changed = 0;
        foreach (var localeCode in new[] { "zh-Hans", "zh-TW", "en" })
        {
            var table = collection.GetTable(new LocaleIdentifier(localeCode)) as StringTable;
            if (table == null) continue;
            var tableChanged = false;
            foreach (var entry in table.Values)
            {
                var encodedValue = EncodeTableValue(entry.Value);
                if (!string.Equals(entry.Value, encodedValue, StringComparison.Ordinal))
                {
                    entry.Value = encodedValue;
                    tableChanged = true;
                    changed++;
                }
                // Do not turn off unrelated, intentionally-authored Smart Strings in the same
                // collection. Our escaped business placeholders do require Smart processing.
                var shouldBeSmart = EscapedBraceTokenRegex.IsMatch(encodedValue);
                if (shouldBeSmart && !entry.IsSmart)
                {
                    entry.IsSmart = true;
                    tableChanged = true;
                    changed++;
                }
            }
            if (tableChanged) EditorUtility.SetDirty(table);
        }
        return changed;
    }

    private void LoadExistingTranslations()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(collectionName);
        if (collection == null) return;
        var hans = collection.GetTable(new LocaleIdentifier("zh-Hans")) as StringTable;
        var traditional = collection.GetTable(new LocaleIdentifier("zh-TW")) as StringTable;
        var english = collection.GetTable(new LocaleIdentifier("en")) as StringTable;
        if (hans == null || traditional == null || english == null) return;

        // Persistent translation memory: source text -> an existing shared entry id.
        // This also reuses translations created under an older auto.* key scheme.
        var entryIdBySource = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var sharedEntry in collection.SharedData.Entries)
        {
            var source = DecodeTableValue(hans.GetEntry(sharedEntry.Id)?.Value);
            if (string.IsNullOrEmpty(source) || entryIdBySource.ContainsKey(source)) continue;
            var hasTraditional = !string.IsNullOrEmpty(traditional.GetEntry(sharedEntry.Id)?.Value);
            var hasEnglish = !string.IsNullOrEmpty(english.GetEntry(sharedEntry.Id)?.Value);
            if (hasTraditional || hasEnglish) entryIdBySource[source] = sharedEntry.Id;
        }

        foreach (var record in records)
        {
            var shared = collection.SharedData.GetEntry(record.entryKey);
            var entryId = shared != null && DecodeTableValue(hans.GetEntry(shared.Id)?.Value) == record.sourceText
                ? shared.Id
                : entryIdBySource.TryGetValue(record.sourceText, out var rememberedId) ? rememberedId : 0;
            if (entryId == 0) continue;
            if (string.IsNullOrEmpty(record.traditional)) record.traditional = DecodeTableValue(traditional.GetEntry(entryId)?.Value);
            if (string.IsNullOrEmpty(record.english)) record.english = DecodeTableValue(english.GetEntry(entryId)?.Value);
        }
    }

    private async Task<string> TranslateTemplateAsync(string source, string targetLocale, bool forceRetranslate = false)
    {
        var normalizedSource = NormalizeTranslationInput(source);
        if (TryGetExactUiTranslation(normalizedSource, targetLocale, out var exactTranslation))
            return exactTranslation;
        var segments = ProtectedTokenRegex.Split(source);
        var tasks = new Task<string>[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            tasks[i] = string.IsNullOrEmpty(segment) || ProtectedTokenRegex.IsMatch(segment) && ProtectedTokenRegex.Match(segment).Value == segment
                ? Task.FromResult(segment)
                : TranslatePlainTextAsync(segment, targetLocale, forceRetranslate);
        }
        return string.Concat(await Task.WhenAll(tasks));
    }

    private async Task<string> TranslatePlainTextAsync(string text, string targetLocale, bool forceRetranslate = false)
    {
        if (string.IsNullOrWhiteSpace(text) || !ChineseRegex.IsMatch(text)) return text;
        // 中文 UI 常用空格拉开字距，例如“设     置”。这些是排版信息，不是词语边界。
        // 翻译时合并为“设置”，避免服务把每个汉字当成独立单词。
        var translationInput = NormalizeTranslationInput(text);
        if (TryGetExactUiTranslation(translationInput, targetLocale, out var exactTranslation))
            return exactTranslation;

        var cacheKey = targetLocale + "\n" + translationInput;
        Task<string> translationTask;
        lock (TranslationCache)
        {
            if (forceRetranslate) TranslationCache.Remove(cacheKey);
            if (TranslationCache.TryGetValue(cacheKey, out var cached)) return cached;
            if (!TranslationInFlight.TryGetValue(cacheKey, out translationTask))
            {
                translationTask = TranslatePlainTextUncachedAsync(translationInput, targetLocale);
                TranslationInFlight[cacheKey] = translationTask;
            }
        }
        try
        {
            var translatedText = await translationTask;
            lock (TranslationCache)
            {
                TranslationCache[cacheKey] = translatedText;
                if (TranslationInFlight.TryGetValue(cacheKey, out var current) && current == translationTask)
                    TranslationInFlight.Remove(cacheKey);
            }
            return translatedText;
        }
        catch
        {
            lock (TranslationCache)
                if (TranslationInFlight.TryGetValue(cacheKey, out var current) && current == translationTask)
                    TranslationInFlight.Remove(cacheKey);
            throw;
        }
    }

    private static string NormalizeTranslationInput(string text)
    {
        return HanLayoutWhitespaceRegex.Replace(text ?? string.Empty, string.Empty);
    }

    private static bool NeedsEnglishTranslation(string source, string translation)
    {
        if (string.IsNullOrWhiteSpace(translation)) return true;
        return HasTranslatableChineseContent(source) && TranslationEqualsSource(source, translation);
    }

    private static bool NeedsTraditionalTranslation(string source, string translation, bool englishNeedsTranslation)
    {
        if (string.IsNullOrWhiteSpace(translation)) return true;
        // Simplified and Traditional Chinese can legitimately be identical. Treat equality as
        // missing only while English is also missing/untranslated; this catches the common case
        // where all three table values were copied from the source without repeatedly replacing
        // valid shared Han text such as “中” or “高”.
        return englishNeedsTranslation && HasTranslatableChineseContent(source) &&
               TranslationEqualsSource(source, translation);
    }

    private static bool HasTranslatableChineseContent(string source)
    {
        return ChineseRegex.IsMatch(RemoveProtectedTokens(source ?? string.Empty));
    }

    private static bool TranslationEqualsSource(string source, string translation)
    {
        return string.Equals(NormalizeTranslationInput(source).Trim(),
            NormalizeTranslationInput(translation).Trim(), StringComparison.Ordinal);
    }

    private static bool TryGetExactUiTranslation(string text, string targetLocale, out string translation)
    {
        // 公共翻译服务常把单独出现的“设置”理解为动词 set up；在游戏 UI 中它是菜单名词。
        if (text == "设置")
        {
            if (targetLocale == "zh-TW")
            {
                translation = "設定";
                return true;
            }
            if (targetLocale == "en")
            {
                translation = "Settings";
                return true;
            }
        }

        // Gameplay counter label. Generic services often mistranslate this as an audit/activity log.
        var operationRecordMatch = Regex.Match(text ?? string.Empty,
            @"^操作记录\s*[：:]?\s*(?<count>\{[^{}]+\})?$");
        if (operationRecordMatch.Success)
        {
            var count = operationRecordMatch.Groups["count"].Value;
            if (targetLocale == "zh-TW")
            {
                translation = "操作記錄" + (string.IsNullOrEmpty(count) ? string.Empty : "：" + count);
                return true;
            }
            if (targetLocale == "en")
            {
                translation = "Operation record" + (string.IsNullOrEmpty(count) ? string.Empty : ": " + count);
                return true;
            }
        }

        // Chapter numbers are runtime placeholders. Translate the complete template so the
        // English word order is correct; translating “第” and “章” as separate segments produces
        // broken results such as “No.{chapterIndex}Chapter”.
        var chapterMatch = Regex.Match(text ?? string.Empty,
            @"^第\s*(?<number>\{[^{}]+\}|[xXｘＸ])\s*章$");
        if (chapterMatch.Success)
        {
            var number = chapterMatch.Groups["number"].Value;
            if (targetLocale == "zh-TW")
            {
                translation = $"第{number}章";
                return true;
            }
            if (targetLocale == "en")
            {
                translation = $"Chapter {number}";
                return true;
            }
        }

        translation = null;
        return false;
    }

    private static int RepairKnownUiTranslations(StringTableCollection collection)
    {
        if (collection == null) return 0;
        var hans = collection.GetTable(new LocaleIdentifier("zh-Hans")) as StringTable;
        var traditional = collection.GetTable(new LocaleIdentifier("zh-TW")) as StringTable;
        var english = collection.GetTable(new LocaleIdentifier("en")) as StringTable;
        if (hans == null || traditional == null || english == null) return 0;

        var changed = 0;
        foreach (var shared in collection.SharedData.Entries)
        {
            var source = DecodeTableValue(hans.GetEntry(shared.Id)?.Value);
            if (string.IsNullOrWhiteSpace(source)) continue;
            var normalizedSource = NormalizeTranslationInput(source);
            if (TryGetExactUiTranslation(normalizedSource, "zh-TW", out var exactTraditional) &&
                DecodeTableValue(traditional.GetEntry(shared.Id)?.Value) != exactTraditional)
            {
                SetEntry(traditional, shared.Id, exactTraditional);
                changed++;
            }
            if (TryGetExactUiTranslation(normalizedSource, "en", out var exactEnglish) &&
                DecodeTableValue(english.GetEntry(shared.Id)?.Value) != exactEnglish)
            {
                SetEntry(english, shared.Id, exactEnglish);
                changed++;
            }
        }

        if (changed > 0)
        {
            EditorUtility.SetDirty(traditional);
            EditorUtility.SetDirty(english);
        }
        return changed;
    }

    private async Task<string> TranslatePlainTextUncachedAsync(string text, string targetLocale)
    {
        var chunkTasks = SplitForTranslation(text, 450)
            .Select(chunk => SendTranslationRequestAsync(chunk, targetLocale)).ToArray();
        return string.Concat(await Task.WhenAll(chunkTasks));
    }

    private static IEnumerable<string> SplitForTranslation(string text, int maxUtf8Bytes)
    {
        var start = 0;
        var bytes = 0;
        for (var index = 0; index < text.Length;)
        {
            var charCount = char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
            var charBytes = Encoding.UTF8.GetByteCount(text.Substring(index, charCount));
            if (bytes > 0 && bytes + charBytes > maxUtf8Bytes)
            {
                yield return text.Substring(start, index - start);
                start = index;
                bytes = 0;
            }
            bytes += charBytes;
            index += charCount;
        }
        if (start < text.Length) yield return text.Substring(start);
    }

    private List<TranslationProvider> GetEnabledProviders()
    {
        var providers = new List<TranslationProvider>();
        if (useGoogleTranslate) providers.Add(TranslationProvider.Google);
        if (useMyMemory) providers.Add(TranslationProvider.MyMemory);
        if (useLibreTranslate && !string.IsNullOrWhiteSpace(libreTranslateEndpoint)) providers.Add(TranslationProvider.LibreTranslate);
        if (useDeepL && !string.IsNullOrWhiteSpace(deepLApiKey)) providers.Add(TranslationProvider.DeepL);
        if (useAzureTranslator && !string.IsNullOrWhiteSpace(azureTranslatorKey)) providers.Add(TranslationProvider.Azure);
        if (useOpenAITranslation && !string.IsNullOrWhiteSpace(openAIApiKey) &&
            !string.IsNullOrWhiteSpace(openAIEndpoint) && !string.IsNullOrWhiteSpace(openAIModel))
            providers.Add(TranslationProvider.OpenAI);
        return providers;
    }

    private async Task<string> SendTranslationRequestAsync(string text, string targetLocale)
    {
        var providers = GetEnabledProviders();
        if (providers.Count == 0) throw new InvalidOperationException("没有可用的翻译服务。");
        var start = (Interlocked.Increment(ref providerRoundRobin) & int.MaxValue) % providers.Count;
        Exception lastError = null;
        for (var round = 0; round < MaxProviderRounds; round++)
        {
            for (var offset = 0; offset < providers.Count; offset++)
            {
                var provider = providers[(start + offset + round) % providers.Count];
                try
                {
                    return await SendWithProviderAsync(provider, text, targetLocale);
                }
                catch (Exception exception)
                {
                    lastError = exception;
                }
            }
            if (round < MaxProviderRounds - 1) await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, round)));
        }
        throw new HttpRequestException($"全部 {providers.Count} 个翻译服务均不可用。", lastError);
    }

    private async Task<string> SendWithProviderAsync(TranslationProvider provider, string text, string targetLocale)
    {
        var gate = GetProviderGate(provider);
        await gate.WaitAsync();
        try
        {
            var wait = GetProviderNextRequestUtc(provider) - DateTime.UtcNow;
            if (wait > TimeSpan.FromSeconds(3)) throw new HttpRequestException($"{provider} 正在限流冷却。剩余 {wait.TotalSeconds:0.#} 秒。");
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            string result;
            switch (provider)
            {
                case TranslationProvider.Google: result = await SendGoogleAsync(text, targetLocale, provider); break;
                case TranslationProvider.MyMemory: result = await SendMyMemoryAsync(text, targetLocale, provider); break;
                case TranslationProvider.LibreTranslate: result = await SendLibreTranslateAsync(text, targetLocale, provider); break;
                case TranslationProvider.DeepL: result = await SendDeepLAsync(text, targetLocale, provider); break;
                case TranslationProvider.Azure: result = await SendAzureAsync(text, targetLocale, provider); break;
                case TranslationProvider.OpenAI: result = await SendOpenAIAsync(text, targetLocale, provider); break;
                default: throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
            }
            SetProviderNextRequestUtc(provider, DateTime.UtcNow + TimeSpan.FromMilliseconds(GetProviderIntervalMs(provider)));
            if (string.IsNullOrWhiteSpace(result)) throw new InvalidDataException($"{provider} 返回了空翻译。");
            return result;
        }
        catch
        {
            if (GetProviderNextRequestUtc(provider) < DateTime.UtcNow)
                SetProviderNextRequestUtc(provider, DateTime.UtcNow + TimeSpan.FromSeconds(2));
            throw;
        }
        finally { gate.Release(); }
    }

    private static async Task<string> SendGoogleAsync(string text, string targetLocale, TranslationProvider provider)
    {
        var url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=zh-CN&tl=" +
                  Uri.EscapeDataString(targetLocale) + "&dt=t&q=" + Uri.EscapeDataString(text);
        using (var response = await Http.GetAsync(url))
        {
            EnsureProviderSuccess(provider, response);
            var root = JArray.Parse(await response.Content.ReadAsStringAsync());
            var translatedText = new StringBuilder();
            foreach (var translated in (JArray)root[0]) translatedText.Append((string)translated[0]);
            return translatedText.ToString();
        }
    }

    private static async Task<string> SendMyMemoryAsync(string text, string targetLocale, TranslationProvider provider)
    {
        var url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text) +
                  "&langpair=zh-CN%7C" + Uri.EscapeDataString(targetLocale) + "&mt=1";
        using (var response = await Http.GetAsync(url))
        {
            EnsureProviderSuccess(provider, response);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            var translated = (string)json["responseData"]?["translatedText"];
            return WebUtility.HtmlDecode(translated ?? string.Empty);
        }
    }

    private async Task<string> SendLibreTranslateAsync(string text, string targetLocale, TranslationProvider provider)
    {
        var body = new JObject
        {
            ["q"] = text,
            ["source"] = "zh",
            ["target"] = targetLocale == "zh-TW" ? libreTraditionalLocale : targetLocale,
            ["format"] = "text"
        };
        if (!string.IsNullOrWhiteSpace(libreTranslateApiKey)) body["api_key"] = libreTranslateApiKey;
        using (var content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json"))
        using (var response = await Http.PostAsync(libreTranslateEndpoint, content))
        {
            EnsureProviderSuccess(provider, response);
            return (string)JObject.Parse(await response.Content.ReadAsStringAsync())["translatedText"];
        }
    }

    private async Task<string> SendDeepLAsync(string text, string targetLocale, TranslationProvider provider)
    {
        var endpoint = deepLFreeApi ? "https://api-free.deepl.com/v2/translate" : "https://api.deepl.com/v2/translate";
        var body = new JObject
        {
            ["text"] = new JArray(text),
            ["source_lang"] = "ZH",
            ["target_lang"] = targetLocale == "zh-TW" ? "ZH-HANT" : "EN"
        };
        using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
        {
            request.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + deepLApiKey);
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            using (var response = await Http.SendAsync(request))
            {
                EnsureProviderSuccess(provider, response);
                return (string)JObject.Parse(await response.Content.ReadAsStringAsync())["translations"]?[0]?["text"];
            }
        }
    }

    private async Task<string> SendAzureAsync(string text, string targetLocale, TranslationProvider provider)
    {
        var target = targetLocale == "zh-TW" ? "zh-Hant" : "en";
        var endpoint = "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&from=zh-Hans&to=" + target;
        var body = new JArray(new JObject { ["Text"] = text });
        using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
        {
            request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", azureTranslatorKey);
            if (!string.IsNullOrWhiteSpace(azureTranslatorRegion))
                request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Region", azureTranslatorRegion.Trim());
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            using (var response = await Http.SendAsync(request))
            {
                EnsureProviderSuccess(provider, response);
                return (string)JArray.Parse(await response.Content.ReadAsStringAsync())[0]?["translations"]?[0]?["text"];
            }
        }
    }

    private async Task<string> SendOpenAIAsync(string text, string targetLocale, TranslationProvider provider)
    {
        var targetName = targetLocale == "zh-TW" ? "繁体中文（台湾）" : "英语";
        var instructions =
            $"你是专业的游戏本地化译者。把用户提供的简体中文翻译为{targetName}。" +
            "只输出译文，不要解释，不要添加引号或 Markdown。严格保留原始换行、空白结构、数字和标点用途。" +
            (string.IsNullOrWhiteSpace(aiTranslationContext) ? string.Empty : "\n项目上下文：" + aiTranslationContext.Trim());
        var body = new JObject
        {
            ["model"] = openAIModel.Trim(),
            ["instructions"] = instructions,
            ["input"] = text,
            ["store"] = false,
            ["max_output_tokens"] = 1024
        };
        using (var request = new HttpRequestMessage(HttpMethod.Post, openAIEndpoint.Trim()))
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + openAIApiKey);
            request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            using (var response = await Http.SendAsync(request))
            {
                EnsureProviderSuccess(provider, response);
                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                var directText = (string)json["output_text"];
                if (!string.IsNullOrWhiteSpace(directText)) return directText;
                var outputText = new StringBuilder();
                foreach (var output in json["output"] as JArray ?? new JArray())
                foreach (var content in output["content"] as JArray ?? new JArray())
                    if ((string)content["type"] == "output_text") outputText.Append((string)content["text"]);
                return outputText.ToString();
            }
        }
    }

    private static void EnsureProviderSuccess(TranslationProvider provider, HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(15);
            SetProviderNextRequestUtc(provider, DateTime.UtcNow + delay);
            throw new HttpRequestException($"{provider} 返回 429，已切换其他服务。");
        }
        if ((int)response.StatusCode >= 500)
        {
            SetProviderNextRequestUtc(provider, DateTime.UtcNow + TimeSpan.FromSeconds(5));
            throw new HttpRequestException($"{provider} 暂时不可用：{(int)response.StatusCode}。");
        }
        response.EnsureSuccessStatusCode();
    }

    private static SemaphoreSlim GetProviderGate(TranslationProvider provider)
    {
        switch (provider)
        {
            case TranslationProvider.Google: return GoogleRequestGate;
            case TranslationProvider.MyMemory: return MyMemoryRequestGate;
            case TranslationProvider.LibreTranslate: return LibreRequestGate;
            case TranslationProvider.DeepL: return DeepLRequestGate;
            case TranslationProvider.Azure: return AzureRequestGate;
            case TranslationProvider.OpenAI: return OpenAIRequestGate;
            default: throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }
    }

    private static int GetProviderIntervalMs(TranslationProvider provider)
    {
        switch (provider)
        {
            case TranslationProvider.Google: return 400;
            case TranslationProvider.MyMemory: return 300;
            default: return 100;
        }
    }

    private static DateTime GetProviderNextRequestUtc(TranslationProvider provider)
    {
        switch (provider)
        {
            case TranslationProvider.Google: return nextGoogleRequestUtc;
            case TranslationProvider.MyMemory: return nextMyMemoryRequestUtc;
            case TranslationProvider.LibreTranslate: return nextLibreRequestUtc;
            case TranslationProvider.DeepL: return nextDeepLRequestUtc;
            case TranslationProvider.Azure: return nextAzureRequestUtc;
            case TranslationProvider.OpenAI: return nextOpenAIRequestUtc;
            default: return DateTime.MinValue;
        }
    }

    private static void SetProviderNextRequestUtc(TranslationProvider provider, DateTime value)
    {
        switch (provider)
        {
            case TranslationProvider.Google: nextGoogleRequestUtc = value; break;
            case TranslationProvider.MyMemory: nextMyMemoryRequestUtc = value; break;
            case TranslationProvider.LibreTranslate: nextLibreRequestUtc = value; break;
            case TranslationProvider.DeepL: nextDeepLRequestUtc = value; break;
            case TranslationProvider.Azure: nextAzureRequestUtc = value; break;
            case TranslationProvider.OpenAI: nextOpenAIRequestUtc = value; break;
        }
    }

    private static void ValidateProtectedTokens(ScanRecord record)
    {
        var source = TokenSignature(record.sourceText);
        if (source != TokenSignature(record.traditional) || source != TokenSignature(record.english))
            throw new InvalidDataException($"富文本或占位符不一致：{record.entryKey}");
    }

    private static string TokenSignature(string value) => string.Join("|", ProtectedTokenRegex.Matches(value ?? string.Empty).Cast<Match>().Select(m => m.Value));
    private static string RemoveProtectedTokens(string value) => ProtectedTokenRegex.Replace(value ?? string.Empty, string.Empty);
    private static string DescribeSegments(string value) => string.Join("  +  ", ProtectedTokenRegex.Split(value ?? string.Empty).Where(x => !string.IsNullOrEmpty(x)).Select(x => ProtectedTokenRegex.IsMatch(x) && ProtectedTokenRegex.Match(x).Value == x ? $"[{x}]" : x.Replace("\n", "↵")));
    private static bool IsUnmodifiedPrefabInstanceText(Component component)
    {
        if (!PrefabUtility.IsPartOfPrefabInstance(component)) return false;
        var serializedObject = new SerializedObject(component);
        var textProperty = serializedObject.FindProperty(component is Text ? "m_Text" : "m_text");
        return textProperty != null && !textProperty.prefabOverride;
    }

    private static bool TryFindCodeControlledText(Component component, Transform assetRoot, out string reason)
    {
        reason = string.Empty;
        if (!(component is Text) && !(component is TMP_Text)) return false;

        // Inspect actual serialized object references first. A matching field alone is not enough:
        // UI effects commonly keep a Text reference only to modify color/font state.
        foreach (var behaviour in GetBehavioursInAsset(component, assetRoot))
        {
            if (behaviour == null || behaviour is LocalizedTextReceiver) continue;
            var script = MonoScript.FromMonoBehaviour(behaviour);
            if (script == null || string.IsNullOrEmpty(script.text)) continue;

            var serializedObject = new SerializedObject(behaviour);
            var property = serializedObject.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference ||
                    property.objectReferenceValue != component) continue;

                var fieldName = property.propertyPath.Split('.')[0];
                if (!ScriptWritesTextField(script.text, fieldName)) continue;
                reason = $"{behaviour.GetType().Name}.{fieldName} 的序列化引用指向该组件，且脚本会写入文本";
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<MonoBehaviour> GetBehavioursInAsset(Component component, Transform assetRoot)
    {
        var scene = component.gameObject.scene;
        if (scene.IsValid() && scene.isLoaded)
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true));
        return assetRoot.GetComponentsInChildren<MonoBehaviour>(true);
    }

    private static bool ScriptWritesTextField(string source, string fieldName)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(fieldName)) return false;
        var field = Regex.Escape(fieldName);
        if (Regex.IsMatch(source, $@"\b{field}\s*\.\s*text\s*(?:=|\+=)")) return true;
        if (Regex.IsMatch(source, $@"\b{field}\s*\.\s*SetText\s*\(")) return true;

        // UI controls such as UI_LRBtnAndText hand the referenced Text to a callback which owns
        // the displayed value. Require both a Text delegate declaration and invocation evidence.
        return Regex.IsMatch(source, @"(?:UnityAction|Action)\s*<\s*(?:Text|TMP_Text|TextMeshProUGUI)\s*>") &&
               Regex.IsMatch(source, $@"\b[A-Za-z_]\w*\s*\(\s*{field}\s*\)");
    }

    private static string MakeEntryKey(string sourceText)
    {
        var readableText = RemoveProtectedTokens(sourceText);
        var slug = new StringBuilder(28);
        var pendingSeparator = false;
        foreach (var character in readableText)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && slug.Length > 0 && slug.Length < 24) slug.Append('_');
                if (slug.Length >= 24) break;
                slug.Append(char.ToLowerInvariant(character));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = slug.Length > 0;
            }
        }

        if (slug.Length == 0) slug.Append("text");
        var normalizedTemplate = (sourceText ?? string.Empty).Replace("\r\n", "\n").Trim();
        var textHash = Hash128.Compute(normalizedTemplate).ToString().Substring(0, 10);
        return $"{GeneratedPrefix}{slug}.{textHash}";
    }

    private void RefreshRecordKeys()
    {
        if (records == null) return;
        foreach (var record in records)
        {
            if (record == null || string.IsNullOrEmpty(record.sourceText)) continue;
            record.entryKey = MakeEntryKey(record.sourceText);
        }
        foreach (var group in records.Where(record => record != null && !string.IsNullOrEmpty(record.entryKey)).GroupBy(record => record.entryKey))
        {
            var traditional = group.Select(record => record.traditional).FirstOrDefault(value => !string.IsNullOrEmpty(value));
            var english = group.Select(record => record.english).FirstOrDefault(value => !string.IsNullOrEmpty(value));
            foreach (var record in group)
            {
                if (!string.IsNullOrEmpty(traditional)) record.traditional = traditional;
                if (!string.IsNullOrEmpty(english)) record.english = english;
            }
        }
    }
    private static bool Contains(string value, string query) => !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string MakeIndexedPath(Transform target, Transform root, int sceneRootIndex)
    {
        var indices = new Stack<int>();
        for (var current = target; current != root; current = current.parent) indices.Push(current.GetSiblingIndex());
        var prefix = sceneRootIndex >= 0 ? sceneRootIndex.ToString() : "0";
        return prefix + "/" + string.Join("/", indices);
    }

    private static Transform ResolveIndexedPath(IReadOnlyList<GameObject> roots, string path)
    {
        var parts = path.Split('/');
        if (!int.TryParse(parts[0], out var rootIndex) || rootIndex < 0 || rootIndex >= roots.Count) return null;
        var current = roots[rootIndex].transform;
        for (var i = 1; i < parts.Length; i++)
        {
            if (string.IsNullOrEmpty(parts[i])) continue;
            if (!int.TryParse(parts[i], out var childIndex) || childIndex < 0 || childIndex >= current.childCount) return null;
            current = current.GetChild(childIndex);
        }
        return current;
    }

    private static Component FindTextComponent(Transform transform, string typeName)
    {
        var type = Type.GetType(typeName + ", UnityEngine.UI") ?? Type.GetType(typeName + ", Unity.TextMeshPro") ??
                   AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(typeName)).FirstOrDefault(typeValue => typeValue != null);
        return type == null ? null : transform.GetComponent(type);
    }

    private static string[] OrderPrefabPathsDependencyFirst(IEnumerable<string> sourcePaths, bool includeDependencies)
    {
        var paths = new HashSet<string>(sourcePaths.Where(IsPrefabPath), StringComparer.OrdinalIgnoreCase);
        if (includeDependencies)
        {
            var queue = new Queue<string>(paths);
            while (queue.Count > 0)
            {
                foreach (var dependency in GetDirectPrefabDependencies(queue.Dequeue()))
                    if (paths.Add(dependency)) queue.Enqueue(dependency);
            }
        }

        var result = new List<string>(paths.Count);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            VisitPrefabDependency(path, paths, visiting, visited, result);
        return result.ToArray();
    }

    private static void VisitPrefabDependency(string path, HashSet<string> includedPaths, HashSet<string> visiting,
        HashSet<string> visited, List<string> result)
    {
        if (visited.Contains(path)) return;
        if (!visiting.Add(path))
        {
            Debug.LogWarning($"检测到 Prefab 循环依赖，按当前顺序继续：{path}");
            return;
        }
        foreach (var dependency in GetDirectPrefabDependencies(path)
                     .Where(includedPaths.Contains).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            VisitPrefabDependency(dependency, includedPaths, visiting, visited, result);
        visiting.Remove(path);
        visited.Add(path);
        result.Add(path);
    }

    private static IEnumerable<string> GetDirectPrefabDependencies(string prefabPath)
    {
        return AssetDatabase.GetDependencies(prefabPath, false)
            .Where(path => !string.Equals(path, prefabPath, StringComparison.OrdinalIgnoreCase) && IsPrefabPath(path));
    }

    private static bool IsPrefabPath(string path) =>
        !string.IsNullOrEmpty(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);

    private List<ScanRecord> SelectedRecords() => records.Where(record => record.selected).ToList();
    private static bool HasMissingTranslations(IEnumerable<ScanRecord> targetRecords) =>
        targetRecords.Any(record => !record.codeControlled &&
            (string.IsNullOrEmpty(record.traditional) || string.IsNullOrEmpty(record.english)));

    private static HashSet<string> CollectSelectedAssetPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selectedObject in Selection.objects)
        {
            var path = AssetDatabase.GetAssetPath(selectedObject);
            if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets", StringComparison.Ordinal)) paths.Add(path);
        }
        return paths;
    }

    private static string[] FindAssetPaths(string filter, IEnumerable<string> selectedRoots)
    {
        if (selectedRoots == null)
            return AssetDatabase.FindAssets(filter).Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)).Distinct().ToArray();

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new List<string>();
        var expectedExtension = filter == "t:Scene" ? ".unity" : ".prefab";
        foreach (var path in selectedRoots)
        {
            if (AssetDatabase.IsValidFolder(path)) folders.Add(path);
            else if (path.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase)) result.Add(path);
        }
        if (folders.Count > 0)
        {
            foreach (var guid in AssetDatabase.FindAssets(filter, folders.ToArray())) result.Add(AssetDatabase.GUIDToAssetPath(guid));
        }
        return result.OrderBy(path => path).ToArray();
    }

    private static void Locate(ScanRecord record)
    {
        LocateAsset(record.assetPath);
    }

    private static void LocateAsset(string assetPath)
    {
        var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (asset == null) return;
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private void SaveScanFolders()
    {
        var paths = scanFolders.Where(folder => folder != null).Select(AssetDatabase.GetAssetPath)
            .Where(AssetDatabase.IsValidFolder).Distinct().ToArray();
        EditorPrefs.SetString(FolderPreferenceKey, string.Join("\n", paths));
    }

    private void LoadScanFolders()
    {
        var saved = EditorPrefs.GetString(FolderPreferenceKey, string.Empty);
        scanFolders = new List<DefaultAsset>();
        if (string.IsNullOrEmpty(saved)) return;
        scanFolders = saved.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(AssetDatabase.IsValidFolder)
            .Select(path => AssetDatabase.LoadAssetAtPath<DefaultAsset>(path))
            .Where(folder => folder != null).ToList();
    }

    private bool ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(collectionName)) { status = "String Table 名称不能为空"; return false; }
        if (string.IsNullOrWhiteSpace(outputDirectory) || !outputDirectory.StartsWith("Assets/", StringComparison.Ordinal))
        { status = "资源目录必须位于 Assets/ 下"; return false; }
        return true;
    }
}
#endif
