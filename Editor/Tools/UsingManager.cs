using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityFramework
{
    [ToolInfo("Using 管理", "文件工具",
        Description = "扫描 .cs 文件，管理 using 语句。\n\n"
            + "• 自动添加缺失的 using 语句\n"
            + "• 清理未使用的 using 语句\n"
            + "• 感知宏定义 (#if / #elif / #else)\n"
            + "• 扫描列表显示，支持批量处理",
        Icon = "📄", Tags = new[] { "C#", "using语句", "清理" })]
    public class UsingManager : EditorWindow
    {
        #region ── 数据结构 ──────────────────────────────────────

        private class UsingInfo
        {
            public string Directive;
            public string Namespace;
            public string Alias;
            public bool IsStatic;
            public string MacroContext = "";
            public int LineNumber;
            public bool IsUnused;
        }

        private class UsingScanResult
        {
            public string FilePath;
            public string FileName;
            public string RelativePath;
            public List<UsingInfo> AllUsings = new List<UsingInfo>();
            public List<string> MissingUsings = new List<string>();
            public List<UsingInfo> UnusedUsings = new List<UsingInfo>();
            public bool HasMacroUsings;
            public bool IsSelected = true;
        }

        #endregion

        #region ── 静态数据：常见命名空间 → 类型映射 ──────────────

        private static readonly Dictionary<string, string[]> CommonTypesByNamespace =
            new Dictionary<string, string[]>
            {
                ["UnityEngine"] = new[]
                {
                    "GameObject", "MonoBehaviour", "Transform", "Vector3", "Vector2", "Vector4",
                    "Quaternion", "Color", "Color32", "Material", "Rigidbody", "Rigidbody2D",
                    "Camera", "Light", "Collider", "MeshRenderer", "MeshFilter",
                    "AudioSource", "AudioClip", "Texture2D", "Texture", "Sprite", "Animator",
                    "Animation", "Canvas", "CanvasGroup", "RectTransform",
                    "Image", "RawImage", "Input", "Time", "Physics", "Physics2D",
                    "Debug", "Application", "Screen", "PlayerPrefs", "Mathf", "Random",
                    "Coroutine", "WaitForSeconds", "WaitForSecondsRealtime", "WaitForEndOfFrame",
                    "WaitForFixedUpdate", "AsyncOperation", "Scene", "SceneManager",
                    "SerializeField", "HideInInspector", "Header", "Tooltip", "Space",
                    "ContextMenu", "Range", "CreateAssetMenu", "RequireComponent",
                    "Object", "Component", "ScriptableObject", "Resources", "JsonUtility",
                    "KeyCode", "Shader", "RenderTexture", "Graphics", "QualitySettings",
                    "RuntimePlatform", "Rect", "Bounds", "LayerMask", "Matrix4x4",
                    "GUILayout", "GUILayoutOption", "GUILayoutUtility", "GUI", "GUIStyle",
                    "GUIContent", "GUIUtility", "Event", "RectOffset", "Font",
                    "WaitUntil", "WaitWhile", "CustomYieldInstruction", "YieldInstruction",
                    "UnityWebRequest", "WWW", "WWWForm", "SkinnedMeshRenderer", "LineRenderer",
                    "TrailRenderer", "SpriteRenderer", "ParticleSystem", "NavMeshAgent",
                    "NavMeshObstacle", "NavMesh", "Terrain", "TerrainData", "PhysicMaterial",
                    "HingeJoint", "FixedJoint", "SpringJoint", "CharacterJoint",
                    "ConfigurableJoint", "BoxCollider", "SphereCollider", "CapsuleCollider",
                    "MeshCollider", "RaycastHit", "RaycastHit2D", "Ray", "Ray2D",
                    "ContactPoint", "Collision", "Collision2D", "VideoPlayer", "VideoClip",
                    "WebCamTexture", "TextAsset", "AnimationClip", "AnimationCurve",
                    "Avatar", "RuntimeAnimatorController", "Gradient", "SortingLayer",
                    "SortingGroup", "SpriteAtlas", "NativeArray", "BuildTarget",
                    "AudioListener", "AudioSettings", "LightType", "LightShadows",
                    "RenderSettings", "Touch", "TouchPhase", "Cursor", "CursorLockMode"
                },
                ["UnityEditor"] = new[]
                {
                    "EditorWindow", "Editor", "EditorGUILayout", "EditorGUI", "EditorGUIUtility",
                    "EditorUtility", "Selection", "AssetDatabase", "SerializedObject",
                    "SerializedProperty", "EditorStyles", "EditorPrefs", "AssetImporter",
                    "MonoImporter", "TextureImporter", "ModelImporter", "AudioImporter",
                    "ShaderImporter", "PluginImporter", "AssetPostprocessor",
                    "MenuCommand", "MenuItem", "PropertyDrawer", "PropertyAttribute",
                    "DecoratorDrawer", "PrefabUtility", "Undo", "HandleUtility", "Handles",
                    "Gizmos", "GizmoType", "EditorSceneManager", "BuildPipeline",
                    "BuildPlayerOptions", "PlayerSettings", "EditorApplication",
                    "EditorCoroutine", "EditorWaitForSeconds", "Progress", "SceneView",
                    "InspectorWindow", "HierarchyWindow", "ProjectWindow", "ConsoleWindow",
                    "ReorderableList", "AssetPreview", "PreviewRenderUtility",
                    "CompilationPipeline", "AssemblyBuilder", "EditorUserBuildSettings",
                    "AdvancedDropdown", "SearchService", "DelayedAction"
                },
                ["System"] = new[]
                {
                    "Action", "Func", "Predicate", "Converter", "Comparison",
                    "Type", "EventArgs", "EventHandler", "IDisposable", "Convert", "Math",
                    "Environment", "Console", "AppDomain", "Attribute", "AttributeUsageAttribute",
                    "FlagsAttribute", "ObsoleteAttribute", "SerializableAttribute",
                    "NonSerializedAttribute", "AsyncCallback", "IAsyncResult", "IComparable",
                    "IEquatable", "IFormattable", "IConvertible", "IFormatProvider",
                    "CancellationToken", "CancellationTokenSource", "Task", "TaskFactory",
                    "TaskScheduler", "TaskCreationOptions", "TaskContinuationOptions",
                    "AggregateException", "TimeoutException", "OperationCanceledException",
                    "NotSupportedException", "NotImplementedException", "ArgumentNullException",
                    "ArgumentException", "InvalidOperationException", "FormatException",
                    "IndexOutOfRangeException", "NullReferenceException", "Exception",
                    "SystemException", "TimeSpan", "DateTime", "DateTimeOffset",
                    "TimeZoneInfo", "Stopwatch", "Guid", "Uri", "Version", "Tuple",
                    "ValueTuple", "Nullable", "Lazy", "WeakReference", "GC", "BitConverter",
                    "StringComparison", "StringSplitOptions", "AppDomain"
                },
                ["System.Collections"] = new[]
                {
                    "IEnumerator", "IEnumerable", "ICollection", "IDictionary",
                    "IList", "IComparer", "IEqualityComparer", "DictionaryEntry",
                    "ArrayList", "Hashtable", "Queue", "Stack", "SortedList",
                    "BitArray", "Comparer", "StructuralComparisons"
                },
                ["System.Collections.Generic"] = new[]
                {
                    "List", "Dictionary", "HashSet", "SortedSet", "SortedDictionary",
                    "SortedList", "Queue", "Stack", "LinkedList", "KeyValuePair",
                    "IList", "IDictionary", "ICollection", "IEnumerable", "IEnumerator",
                    "IComparer", "IEqualityComparer", "IReadOnlyList", "IReadOnlyCollection",
                    "IReadOnlyDictionary", "ISet", "KeyNotFoundException", "EqualityComparer",
                    "Comparer"
                },
                ["System.Linq"] = new[]
                {
                    "Enumerable", "Queryable", "ILookup", "Lookup", "IGrouping",
                    "IOrderedEnumerable", "IOrderedQueryable"
                },
                ["System.IO"] = new[]
                {
                    "File", "Path", "Directory", "FileStream", "StreamReader",
                    "StreamWriter", "BinaryReader", "BinaryWriter", "StringReader",
                    "StringWriter", "MemoryStream", "BufferedStream", "TextReader",
                    "TextWriter", "FileInfo", "DirectoryInfo", "FileSystemInfo",
                    "DriveInfo", "FileAttributes", "FileMode", "FileAccess",
                    "FileShare", "FileOptions", "SearchOption", "FileSystemWatcher",
                    "IOException", "FileNotFoundException", "DirectoryNotFoundException"
                },
                ["System.Text"] = new[]
                {
                    "StringBuilder", "Encoding", "Encoder", "Decoder",
                    "EncoderFallback", "DecoderFallback", "Rune"
                },
                ["System.Text.RegularExpressions"] = new[]
                {
                    "Regex", "Match", "MatchCollection", "Group", "GroupCollection",
                    "Capture", "CaptureCollection", "RegexOptions", "RegexMatchTimeoutException",
                    "MatchEvaluator"
                },
                ["System.Reflection"] = new[]
                {
                    "Assembly", "AssemblyName", "MethodInfo", "FieldInfo",
                    "PropertyInfo", "ConstructorInfo", "EventInfo", "ParameterInfo",
                    "MemberInfo", "BindingFlags", "FieldAttributes", "PropertyAttributes",
                    "MethodAttributes", "TypeAttributes", "ParameterAttributes",
                    "Activator", "Binder", "CustomAttributeData", "MemberTypes",
                    "Module", "TargetException", "TargetInvocationException",
                    "TargetParameterCountException", "TypeInfo"
                },
                ["System.Globalization"] = new[]
                {
                    "CultureInfo", "NumberFormatInfo", "DateTimeFormatInfo",
                    "RegionInfo", "TextInfo", "Calendar", "CompareInfo",
                    "SortKey", "UnicodeCategory", "CalendarWeekRule", "CalendarAlgorithmType",
                    "DateTimeStyles", "NumberStyles", "CompareOptions", "CultureTypes",
                    "DayOfWeek", "GregorianCalendar", "HijriCalendar", "JapaneseCalendar"
                },
                ["UnityEditor.SceneManagement"] = new[]
                {
                    "EditorSceneManager", "PrefabStage", "PrefabStageUtility",
                    "StageUtility", "SceneAsset", "Stage"
                },
                ["UnityEngine.SceneManagement"] = new[]
                {
                    "Scene", "SceneManager", "LoadSceneMode", "LoadSceneParameters"
                },
                ["UnityEngine.UI"] = new[]
                {
                    "Button", "Toggle", "Slider", "Scrollbar", "ScrollView",
                    "Dropdown", "InputField", "ToggleGroup", "AspectRatioFitter",
                    "CanvasScaler", "CanvasRenderer", "ContentSizeFitter", "Graphic",
                    "GraphicRaycaster", "LayoutElement", "LayoutGroup",
                    "HorizontalLayoutGroup", "VerticalLayoutGroup", "GridLayoutGroup",
                    "Outline", "Shadow", "RectMask2D", "ImageType", "HorizontalWrapMode",
                    "VerticalWrapMode"
                },
                ["TMPro"] = new[]
                {
                    "TextMeshPro", "TextMeshProUGUI", "TMP_Text", "TMP_FontAsset",
                    "TMP_SpriteAsset", "TMP_Settings", "TMP_InputField", "TMP_Dropdown",
                    "TMP_ColorGradient", "FaceInfo", "FontStyles", "FontWeights",
                    "HorizontalAlignmentOptions", "VerticalAlignmentOptions",
                    "TextAlignmentOptions", "TMP_TextInfo", "TMP_CharacterInfo"
                },
            };

        private static readonly Dictionary<string, string> TypeToNamespace;
        private static readonly string[] LinqMethods =
        {
            "Where", "Select", "SelectMany", "FirstOrDefault", "LastOrDefault",
            "First", "Last", "Single", "SingleOrDefault", "Any", "All", "Count",
            "LongCount", "Sum", "Min", "Max", "Average", "Aggregate", "OrderBy",
            "OrderByDescending", "ThenBy", "ThenByDescending", "GroupBy", "GroupJoin",
            "Join", "Distinct", "Skip", "Take", "SkipWhile", "TakeWhile", "Reverse",
            "Concat", "Union", "Intersect", "Except", "Zip", "ToDictionary",
            "ToLookup", "ToList", "ToArray", "ToHashSet", "ElementAt", "ElementAtOrDefault",
            "Contains", "SequenceEqual", "DefaultIfEmpty", "Empty", "Range", "Repeat",
            "OfType", "Cast", "AsEnumerable", "AsQueryable", "Append", "Prepend",
            "SkipLast", "TakeLast", "MaxBy", "MinBy", "DistinctBy", "Chunk"
        };

        static UsingManager()
        {
            TypeToNamespace = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in CommonTypesByNamespace)
            {
                foreach (var typeName in kv.Value)
                {
                    if (!TypeToNamespace.ContainsKey(typeName))
                        TypeToNamespace[typeName] = kv.Key;
                }
            }
        }

        #endregion

        #region ── 窗口状态 ──────────────────────────────────────

        private string _scanPath = "Assets";
        private Vector2 _listScrollPos;
        private Vector2 _detailScrollPos;
        private List<UsingScanResult> _results = new List<UsingScanResult>();
        private List<UsingScanResult> _filteredResults = new List<UsingScanResult>();
        private int _selectedIndex = -1;
        private bool _hasScanned;
        private string _searchKeyword = "";
        private bool _filterDirty = true;
        private int _selectedCount;

        private enum FilterMode { All, HasMissing, HasUnused, HasIssue }
        private FilterMode _filterMode = FilterMode.All;

        // ── Shift 多选 ──
        private int _lastClickedIndex = -1;

        // ── 拖拽区域 ──
        private Rect _dropAreaRect;

        #endregion

        #region ── 深色调色板（引用 HubPalette 单一来源，仅保留工具特有颜色）───────────────

        private static readonly Color ClrBg           = HubPalette.Bg;
        private static readonly Color ClrToolbarBg    = HubPalette.ToolbarBg;
        private static readonly Color ClrSearchBg     = HubPalette.SearchBg;
        private static readonly Color ClrItemBg       = HubPalette.ItemBg;
        private static readonly Color ClrItemHover    = HubPalette.ItemHover;
        private static readonly Color ClrItemSelected = HubPalette.ItemSelected;
        private static readonly Color ClrText         = HubPalette.Text;
        private static readonly Color ClrTextDim      = HubPalette.TextDim;
        private static readonly Color ClrTextBright   = HubPalette.TextBright;
        private static readonly Color ClrAccent       = HubPalette.Accent;
        private static readonly Color ClrAccentDim    = HubPalette.AccentDim;
        private static readonly Color ClrCardBg       = HubPalette.CardBg;
        private static readonly Color ClrTagBg        = HubPalette.TagBg;
        private static readonly Color ClrDivider      = HubPalette.Divider;
        private static readonly Color ClrDropOverlay  = HubPalette.DropOverlay;
        private static readonly Color ClrDropBorder   = HubPalette.DropBorder;
        private static readonly Color ClrBtnNormal    = HubPalette.BtnNormal;
        private static readonly Color ClrBtnHover     = HubPalette.BtnHover;
        private static readonly Color ClrBtnDanger    = HubPalette.BtnDanger;
        private static readonly Color ClrBtnDangerHov = HubPalette.BtnDangerHov;
        private static readonly Color ClrBtnSuccess   = HubPalette.BtnSuccess;
        private static readonly Color ClrBtnSuccessHov= HubPalette.BtnSuccessHov;
        private static readonly Color ClrIconBg       = HubPalette.IconBg;
        private static readonly Color ClrStatusBar    = HubPalette.StatusBar;
        // ── 工具特有颜色 ──
        private static readonly Color ClrMissTag      = new Color(0.90f, 0.45f, 0.25f, 1f);
        private static readonly Color ClrUnusedTag    = new Color(0.95f, 0.75f, 0.25f, 1f);
        private static readonly Color ClrOkTag        = new Color(0.35f, 0.75f, 0.45f, 1f);
        private static readonly Color ClrHeaderBg     = new Color(0.17f, 0.17f, 0.18f, 1f);
        private static readonly Color ClrRightBg      = HubPalette.RightBg;

        #endregion

        #region ── 纹理 & 样式缓存 ───────────────────────────────

        private Texture2D _texWhite;
        private Texture2D _texHover;
        private Texture2D _texSelected;
        private Texture2D _texTransparent;
        private bool _stylesInitialized;

        // ── 样式 ──
        private GUIStyle _styleLabel;
        private GUIStyle _styleLabelDim;
        private GUIStyle _styleLabelSmall;
        private GUIStyle _styleLabelBold;
        private GUIStyle _styleHeaderLabel;
        private GUIStyle _styleCenterLabel;
        private GUIStyle _styleCenterLabelLarge;
        private GUIStyle _styleStatusBar;
        private GUIStyle _styleBtnPrimary;
        private GUIStyle _styleBtnFlat;
        private GUIStyle _styleBtnDanger;
        private GUIStyle _styleBtnSuccess;
        private GUIStyle _styleTag;
        private GUIStyle _styleTagOk;
        private GUIStyle _styleTagMiss;
        private GUIStyle _styleTagUnused;
        private GUIStyle _styleCard;
        private GUIStyle _styleSearchField;
        private GUIStyle _styleSearchPlaceholder;
        private GUIStyle _styleRowNormal;
        private GUIStyle _styleRowHover;
        private GUIStyle _styleRowSelected;
        private GUIStyle _styleToolbarLabel;
        private GUIStyle _styleTableCell;

        // MakeTex 已统一到 HubPalette.MakeTex，此处不再重复定义

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _texWhite       = HubPalette.MakeTex(1, 1, Color.white);
            _texHover       = HubPalette.MakeTex(1, 1, ClrItemHover);
            _texSelected    = HubPalette.MakeTex(1, 1, ClrItemSelected);
            _texTransparent = HubPalette.MakeTex(1, 1, new Color(0, 0, 0, 0));

            // ── 文字标签 ──
            _styleLabel = new GUIStyle()
            {
                fontSize = 12,
                normal = { textColor = ClrText },
                padding = new RectOffset(0, 0, 0, 0)
            };

            _styleLabelDim = new GUIStyle()
            {
                fontSize = 10,
                normal = { textColor = ClrTextDim },
                padding = new RectOffset(0, 0, 0, 0)
            };

            _styleLabelSmall = new GUIStyle()
            {
                fontSize = 9,
                normal = { textColor = ClrTextDim },
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 0, 0, 0)
            };

            _styleLabelBold = new GUIStyle()
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = ClrTextBright },
                padding = new RectOffset(0, 0, 0, 0)
            };

            _styleHeaderLabel = new GUIStyle()
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = ClrTextDim },
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0)
            };

            // ── 居中标签 ──
            _styleCenterLabel = new GUIStyle()
            {
                normal = { textColor = ClrTextDim },
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 12
            };

            _styleCenterLabelLarge = new GUIStyle()
            {
                normal = { textColor = new Color(0.40f, 0.40f, 0.42f, 1f) },
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };

            // ── 状态栏 ──
            _styleStatusBar = new GUIStyle()
            {
                fontSize = 10,
                normal = { textColor = ClrTextDim },
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 0, 0)
            };

            // ── 按钮 ──
            _styleBtnPrimary = new GUIStyle()
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white, background = HubPalette.MakeTex(1, 1, ClrBtnNormal) },
                hover = { textColor = Color.white, background = HubPalette.MakeTex(1, 1, ClrBtnHover) },
                active = { textColor = new Color(0.85f, 0.85f, 0.85f), background = HubPalette.MakeTex(1, 1, ClrAccent) },
                padding = new RectOffset(12, 12, 4, 4)
            };

            _styleBtnFlat = new GUIStyle()
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ClrTextDim },
                hover = { textColor = ClrText },
                padding = new RectOffset(8, 8, 4, 4)
            };

            _styleBtnDanger = new GUIStyle()
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white, background = HubPalette.MakeTex(1, 1, ClrBtnDanger) },
                hover = { textColor = Color.white, background = HubPalette.MakeTex(1, 1, ClrBtnDangerHov) },
                active = { textColor = new Color(0.85f, 0.85f, 0.85f), background = HubPalette.MakeTex(1, 1, new Color(0.65f, 0.22f, 0.22f)) },
                padding = new RectOffset(12, 12, 4, 4)
            };

            _styleBtnSuccess = new GUIStyle()
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white, background = HubPalette.MakeTex(1, 1, ClrBtnSuccess) },
                hover = { textColor = Color.white, background = HubPalette.MakeTex(1, 1, ClrBtnSuccessHov) },
                active = { textColor = new Color(0.85f, 0.85f, 0.85f), background = HubPalette.MakeTex(1, 1, new Color(0.20f, 0.55f, 0.30f)) },
                padding = new RectOffset(12, 12, 4, 4)
            };

            // ── 标签 ──
            _styleTag = new GUIStyle()
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ClrText },
                padding = new RectOffset(6, 6, 2, 2)
            };

            _styleTagOk = new GUIStyle(_styleTag)
            {
                normal = { textColor = ClrOkTag }
            };

            _styleTagMiss = new GUIStyle(_styleTag)
            {
                normal = { textColor = ClrMissTag }
            };

            _styleTagUnused = new GUIStyle(_styleTag)
            {
                normal = { textColor = ClrUnusedTag }
            };

            // ── 卡片 ──
            _styleCard = new GUIStyle()
            {
                padding = new RectOffset(12, 12, 8, 8)
            };

            // ── 搜索框 ──
            _styleSearchField = new GUIStyle("ToolbarSeachTextField")
            {
                fontSize = 12,
                padding = new RectOffset(28, 8, 4, 4),
                normal = { textColor = ClrText },
                fixedHeight = 22
            };

            _styleSearchPlaceholder = new GUIStyle()
            {
                fontSize = 12,
                normal = { textColor = ClrTextDim },
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(28, 8, 4, 4)
            };

            // ── 行样式 ──
            _styleRowNormal = new GUIStyle()
            {
                normal = { background = _texTransparent }
            };

            _styleRowHover = new GUIStyle()
            {
                normal = { background = _texHover }
            };

            _styleRowSelected = new GUIStyle()
            {
                normal = { background = _texSelected }
            };

            // ── 工具栏标签（与按钮字号一致、垂直居中）──
            _styleToolbarLabel = new GUIStyle()
            {
                fontSize = 11,
                normal = { textColor = ClrTextDim },
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0)
            };

            // ── 表格单元格（数字居中）──
            _styleTableCell = new GUIStyle()
            {
                fontSize = 12,
                normal = { textColor = ClrText },
                alignment = TextAnchor.MiddleCenter
            };

            _stylesInitialized = true;
        }

        private void OnDestroy()
        {
            if (_texWhite != null) DestroyImmediate(_texWhite);
            if (_texHover != null) DestroyImmediate(_texHover);
            if (_texSelected != null) DestroyImmediate(_texSelected);
            if (_texTransparent != null) DestroyImmediate(_texTransparent);
        }

        #endregion

        #region ── 菜单 / 初始化 ─────────────────────────────────

        [MenuItem("UnityToolsHub/Using 管理")]
        public static void ShowWindow()
        {
            var window = GetWindow<UsingManager>("Using 管理");
            window.minSize = new Vector2(800, 500);
        }

        #endregion

        #region ── GUI ───────────────────────────────────────────

        private int _hoverRowIndex = -1;

        private void OnGUI()
        {
            InitStyles();

            // 整体背景
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), ClrBg);

            DrawToolbar();

            // 分割线
            var divRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(divRect, ClrDivider);

            DrawDropArea();

            // 分割线
            divRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(divRect, ClrDivider);

            DrawBatchActions();

            // 分割线
            divRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(divRect, ClrDivider);

            using (new EditorGUILayout.HorizontalScope())
            {
                // 左侧：结果列表
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    DrawResultsTable();
                }

                // 左右分隔线（1px 宽，布局自动撑满高度）
                var divLineRect = EditorGUILayout.GetControlRect(GUILayout.Width(1), GUILayout.ExpandHeight(true));
                EditorGUI.DrawRect(divLineRect, ClrDivider);

                // 右侧：详情面板
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(299), GUILayout.ExpandHeight(true)))
                {
                    DrawDetailPanel();
                }
            }

            // 底部状态栏
            DrawStatusBar();
        }

        private void DrawToolbar()
        {
            var rect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, ClrToolbarBg);

            float x = rect.x + 12;

            // 搜索
            var searchLabelRect = new Rect(x, rect.y + 7, 30, 22);
            GUI.Label(searchLabelRect, "搜索", _styleToolbarLabel);
            x += 34;

            var searchRect = new Rect(x, rect.y + 7, 120, 22);
            EditorGUI.DrawRect(searchRect, ClrSearchBg);
            GUI.SetNextControlName("ToolbarSearch");
            _searchKeyword = EditorGUI.TextField(searchRect, _searchKeyword, _styleSearchField);

            // Placeholder
            if (string.IsNullOrEmpty(_searchKeyword) && GUI.GetNameOfFocusedControl() != "ToolbarSearch")
            {
                GUI.Label(searchRect, "  搜索文件名...", _styleSearchPlaceholder);
            }
            x += 126;

            // 扫描路径
            var pathLabelRect = new Rect(x, rect.y + 7, 50, 22);
            GUI.Label(pathLabelRect, "扫描路径", _styleToolbarLabel);
            x += 54;

            // 路径输入框自适应宽度：填满到浏览按钮之前的剩余空间
            // 右侧固定控件: 浏览(52) + 筛选标签(30) + 筛选下拉(95) + 扫描按钮(64) + 间距(26) = 267
            float pathFieldWidth = Mathf.Max(100, rect.width - x - 267);
            var pathFieldRect = new Rect(x, rect.y + 7, pathFieldWidth, 22);
            EditorGUI.DrawRect(pathFieldRect, ClrSearchBg);
            _scanPath = EditorGUI.TextField(pathFieldRect, _scanPath, _styleSearchField);
            x += pathFieldWidth + 4;

            var browseRect = new Rect(x, rect.y + 7, 52, 22);
            bool browseHover = browseRect.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(browseRect, browseHover ? ClrItemHover : ClrTagBg);
            if (GUI.Button(browseRect, "浏览…", _styleBtnFlat))
            {
                string selected = EditorUtility.OpenFolderPanel("选择扫描目录", _scanPath, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    string dataPath = Application.dataPath;
                    if (selected.StartsWith(dataPath))
                        _scanPath = "Assets" + selected.Substring(dataPath.Length);
                    else
                        _scanPath = selected;
                }
            }
            x += 58;

            // 筛选
            var filterLabelRect = new Rect(x, rect.y + 7, 30, 22);
            GUI.Label(filterLabelRect, "筛选", _styleToolbarLabel);
            x += 34;

            var filterRect = new Rect(x, rect.y + 7, 95, 22);
            EditorGUI.DrawRect(filterRect, ClrSearchBg);
            var newFilter = (FilterMode)EditorGUI.EnumPopup(filterRect, _filterMode, _styleBtnFlat);
            if (newFilter != _filterMode) { _filterMode = newFilter; _filterDirty = true; }

            // 扫描按钮（始终靠右）
            var scanBtnWidth = 64f;
            var scanRect = new Rect(rect.xMax - scanBtnWidth - 8, rect.y + 6, scanBtnWidth, 24);
            bool scanHover = scanRect.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(scanRect, scanHover ? ClrBtnSuccessHov : ClrBtnSuccess);
            if (GUI.Button(scanRect, "🔍 扫描", _styleBtnPrimary))
            {
                ScanFiles();
            }
        }

        private void DrawDropArea()
        {
            // 拖拽区域：接受从 Project 窗口拖入的文件夹
            Rect rect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            _dropAreaRect = rect;

            bool isDragging = Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform;
            bool isHovering = isDragging && rect.Contains(Event.current.mousePosition);

            if (isHovering)
            {
                bool hasFolder = false;
                if (DragAndDrop.paths != null)
                {
                    foreach (var p in DragAndDrop.paths)
                    {
                        if (AssetDatabase.IsValidFolder(p))
                        { hasFolder = true; break; }
                    }
                }
                DragAndDrop.visualMode = hasFolder ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

                if (Event.current.type == EventType.DragPerform && hasFolder)
                {
                    DragAndDrop.AcceptDrag();
                    string firstFolder = null;
                    foreach (var p in DragAndDrop.paths)
                    {
                        if (AssetDatabase.IsValidFolder(p)) { firstFolder = p; break; }
                    }
                    if (firstFolder != null)
                    {
                        _scanPath = firstFolder;
                        GUI.changed = true;
                    }
                    Event.current.Use();
                }
            }

            // 绘制背景
            EditorGUI.DrawRect(rect, isHovering ? ClrDropOverlay : ClrSearchBg);

            // 绘制边框（虚线效果）
            if (isHovering)
            {
                // 上下左右边框
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), ClrDropBorder);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), ClrDropBorder);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), ClrDropBorder);
                EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), ClrDropBorder);
            }

            // 绘制文字
            var style = new GUIStyle()
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                normal = { textColor = isHovering ? ClrAccent : ClrTextDim },
                fontStyle = isHovering ? FontStyle.Bold : FontStyle.Normal
            };
            string label = string.IsNullOrEmpty(_scanPath) || _scanPath == "Assets"
                ? "📂  拖入文件夹设置扫描路径  (当前: Assets)"
                : $"📂  拖入文件夹设置扫描路径  (当前: {_scanPath})";
            GUI.Label(rect, label, style);
        }

        private void DrawBatchActions()
        {
            var rect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, ClrToolbarBg);

            float x = rect.x + 10;

            // 全选 Toggle
            bool allSelected = _filteredResults.Count > 0 && _selectedCount == _filteredResults.Count;
            var toggleRect = new Rect(x, rect.y + 8, 16, 16);
            bool newAll = EditorGUI.Toggle(toggleRect, allSelected);
            if (newAll != allSelected)
            {
                foreach (var r in _filteredResults) r.IsSelected = newAll;
                _lastClickedIndex = -1;
                UpdateSelectedCount();
            }
            x += 20;

            // 全选 / 取消按钮
            var selectAllRect = new Rect(x, rect.y + 5, 48, 22);
            bool selectAllHover = selectAllRect.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(selectAllRect, selectAllHover ? ClrItemHover : ClrTagBg);
            if (GUI.Button(selectAllRect, "全选", _styleBtnFlat))
            {
                foreach (var r in _filteredResults) r.IsSelected = true;
                _lastClickedIndex = -1;
                UpdateSelectedCount();
            }
            x += 52;

            var deselectAllRect = new Rect(x, rect.y + 5, 60, 22);
            bool deselectHover = deselectAllRect.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(deselectAllRect, deselectHover ? ClrItemHover : ClrTagBg);
            if (GUI.Button(deselectAllRect, "取消全选", _styleBtnFlat))
            {
                foreach (var r in _filteredResults) r.IsSelected = false;
                _lastClickedIndex = -1;
                UpdateSelectedCount();
            }
            x += 66;

            // 已选计数
            var countRect = new Rect(x, rect.y + 8, 120, 20);
            GUI.Label(countRect, $"已选: {_selectedCount} / {_filteredResults.Count}", _styleLabelDim);

            // 批量操作按钮（右侧）
            bool hasSelection = _selectedCount > 0 && _hasScanned;

            // 清理未使用 Using（右侧第一个）
            var removeRect = new Rect(rect.xMax - 130, rect.y + 4, 120, 24);
            if (hasSelection)
            {
                bool removeHover = removeRect.Contains(Event.current.mousePosition);
                EditorGUI.DrawRect(removeRect, removeHover ? ClrBtnDangerHov : ClrBtnDanger);
                if (GUI.Button(removeRect, "🗑 清理未使用 Using", _styleBtnDanger))
                {
                    RemoveUnusedFromSelected();
                }
            }
            else
            {
                EditorGUI.DrawRect(removeRect, ClrTagBg);
                GUI.enabled = false;
                GUI.Button(removeRect, "🗑 清理未使用 Using", _styleBtnFlat);
                GUI.enabled = true;
            }

            // 添加缺失 Using（右侧第二个）
            var addRect = new Rect(rect.xMax - 260, rect.y + 4, 120, 24);
            if (hasSelection)
            {
                bool addHover = addRect.Contains(Event.current.mousePosition);
                EditorGUI.DrawRect(addRect, addHover ? ClrBtnSuccessHov : ClrBtnSuccess);
                if (GUI.Button(addRect, "➕ 添加缺失 Using", _styleBtnSuccess))
                {
                    ApplyMissingToSelected();
                }
            }
            else
            {
                EditorGUI.DrawRect(addRect, ClrTagBg);
                GUI.enabled = false;
                GUI.Button(addRect, "➕ 添加缺失 Using", _styleBtnFlat);
                GUI.enabled = true;
            }
        }

        private void DrawResultsTable()
        {
            if (!_hasScanned)
            {
                // 空状态提示
                var emptyRect = GUILayoutUtility.GetRect(0, 200, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(emptyRect, ClrBg);
                GUILayout.BeginArea(emptyRect);
                GUILayout.FlexibleSpace();
                GUILayout.Label("📄", new GUIStyle()
                {
                    fontSize = 48,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.40f, 0.40f, 0.42f, 1f) }
                }, GUILayout.Height(60));
                GUILayout.Space(8);
                GUILayout.Label("点击「🔍 扫描」开始分析 .cs 文件", _styleCenterLabel);
                GUILayout.FlexibleSpace();
                GUILayout.EndArea();
                return;
            }

            RefreshFilteredCache();
            if (_filteredResults.Count == 0)
            {
                var emptyRect = GUILayoutUtility.GetRect(0, 200, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(emptyRect, ClrBg);
                GUILayout.BeginArea(emptyRect);
                GUILayout.FlexibleSpace();
                GUILayout.Label("🔍", new GUIStyle()
                {
                    fontSize = 48,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.40f, 0.40f, 0.42f, 1f) }
                }, GUILayout.Height(60));
                GUILayout.Space(8);
                GUILayout.Label("没有匹配的文件", _styleCenterLabel);
                GUILayout.FlexibleSpace();
                GUILayout.EndArea();
                return;
            }

            // ── 表头 ──
            var headerRect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, ClrHeaderBg);

            // 分割线
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 1, headerRect.width, 1), ClrDivider);

            float hx = headerRect.x + 10;
            GUI.Label(new Rect(hx, headerRect.y, 18, headerRect.height), "", _styleHeaderLabel);
            hx += 22;
            GUI.Label(new Rect(hx, headerRect.y, 200, headerRect.height), "文件名", _styleHeaderLabel);
            hx += 204;

            // 表头数字列居中，与数据对齐
            var headerCellStyle = new GUIStyle(_styleHeaderLabel) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(hx, headerRect.y, 45, headerRect.height), "Using", headerCellStyle);
            hx += 48;
            GUI.Label(new Rect(hx, headerRect.y, 40, headerRect.height), "缺失", headerCellStyle);
            hx += 44;
            GUI.Label(new Rect(hx, headerRect.y, 50, headerRect.height), "未使用", headerCellStyle);
            hx += 54;
            GUI.Label(new Rect(hx, headerRect.y, 30, headerRect.height), "宏", headerCellStyle);

            // ── 表格内容 ──
            bool shiftHeld = Event.current.shift;

            _listScrollPos = EditorGUILayout.BeginScrollView(_listScrollPos);

            for (int i = 0; i < _filteredResults.Count; i++)
            {
                var result = _filteredResults[i];
                bool isFocused = i == _selectedIndex;
                bool isHover = i == _hoverRowIndex;

                var rowRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));

                // 行背景
                Color bgColor = ClrItemBg;
                if (isFocused) bgColor = ClrItemSelected;
                else if (isHover) bgColor = ClrItemHover;
                EditorGUI.DrawRect(rowRect, bgColor);

                // 选中态左侧色条
                if (isFocused)
                {
                    EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y + 4, 3, rowRect.height - 8), ClrAccent);
                }

                // 底部分割线
                EditorGUI.DrawRect(new Rect(rowRect.x + 10, rowRect.yMax - 1, rowRect.width - 20, 1), ClrDivider);

                float rx = rowRect.x + 10;

                // Toggle
                var toggleRect = new Rect(rx, rowRect.y + 3, 18, 16);
                bool oldSel = result.IsSelected;
                bool newSel = EditorGUI.Toggle(toggleRect, oldSel);
                if (newSel != oldSel)
                {
                    HandleSelectionToggle(i, newSel, shiftHeld);
                }
                rx += 22;

                // 文件名
                var nameRect = new Rect(rx, rowRect.y, 200, rowRect.height);
                if (GUI.Button(nameRect, result.FileName, isFocused ? _styleLabelBold : _styleLabel))
                {
                    _selectedIndex = i;
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(result.RelativePath);
                    if (obj != null) EditorGUIUtility.PingObject(obj);
                    Repaint();
                }
                rx += 204;

                // Using 数量
                var usingCountRect = new Rect(rx, rowRect.y, 45, rowRect.height);
                GUI.Label(usingCountRect, result.AllUsings.Count.ToString(), _styleTableCell);
                rx += 48;

                // 缺失
                var missRect = new Rect(rx, rowRect.y, 40, rowRect.height);
                if (result.MissingUsings.Count > 0)
                {
                    // 标签样式（列宽40，标签宽28，居中偏移6）
                    var tagRect = new Rect(rx + 6, rowRect.y + 3, 28, 16);
                    EditorGUI.DrawRect(tagRect, new Color(0.90f, 0.45f, 0.25f, 0.15f));
                    GUI.Label(tagRect, result.MissingUsings.Count.ToString(), _styleTagMiss);
                }
                else
                {
                    GUI.Label(missRect, result.MissingUsings.Count.ToString(), _styleTableCell);
                }
                rx += 44;

                // 未使用
                var unusedRect = new Rect(rx, rowRect.y, 50, rowRect.height);
                if (result.UnusedUsings.Count > 0)
                {
                    // 标签样式（列宽50，标签宽28，居中偏移11）
                    var tagRect = new Rect(rx + 11, rowRect.y + 3, 28, 16);
                    EditorGUI.DrawRect(tagRect, new Color(0.95f, 0.75f, 0.25f, 0.15f));
                    GUI.Label(tagRect, result.UnusedUsings.Count.ToString(), _styleTagUnused);
                }
                else
                {
                    GUI.Label(unusedRect, result.UnusedUsings.Count.ToString(), _styleTableCell);
                }
                rx += 54;

                // 宏
                var macroRect = new Rect(rx, rowRect.y, 30, rowRect.height);
                if (result.HasMacroUsings)
                {
                    GUI.Label(macroRect, "🔒", new GUIStyle()
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 11,
                        normal = { textColor = ClrAccent }
                    });
                }
                else
                {
                    GUI.Label(macroRect, "-", _styleTableCell);
                }

                // Hover 跟踪
                if (rowRect.Contains(Event.current.mousePosition))
                {
                    if (_hoverRowIndex != i)
                    {
                        _hoverRowIndex = i;
                        Repaint();
                    }
                }

                // 行点击选中
                if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
                {
                    _selectedIndex = i;
                    Repaint();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawDetailPanel()
        {
            _detailScrollPos = EditorGUILayout.BeginScrollView(_detailScrollPos);

            // 标题
            var titleRect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(titleRect, ClrHeaderBg);
            GUI.Label(new Rect(titleRect.x + 12, titleRect.y, titleRect.width - 12, titleRect.height),
                "📋 文件详情", new GUIStyle()
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = ClrTextBright },
                    alignment = TextAnchor.MiddleLeft
                });

            // 分割线
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), ClrDivider);

            if (_selectedIndex < 0 || _selectedIndex >= _filteredResults.Count)
            {
                var emptyRect = GUILayoutUtility.GetRect(0, 150, GUILayout.ExpandWidth(true));
                GUILayout.BeginArea(emptyRect);
                GUILayout.FlexibleSpace();
                GUILayout.Label("📄", new GUIStyle()
                {
                    fontSize = 36,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.40f, 0.40f, 0.42f, 1f) }
                }, GUILayout.Height(45));
                GUILayout.Space(6);
                GUILayout.Label("在左侧列表中点击文件查看详情", _styleCenterLabel);
                GUILayout.FlexibleSpace();
                GUILayout.EndArea();
                EditorGUILayout.EndScrollView();
                return;
            }

            var result = _filteredResults[_selectedIndex];

            // 基本信息卡片
            EditorGUILayout.Space(6);
            DrawCard("📁 基本信息", () =>
            {
                DrawDetailRow("文件", result.FileName);
                DrawDetailRow("路径", result.RelativePath);
                DrawDetailRow("Using 总数", result.AllUsings.Count.ToString());
                DrawDetailRow("宏定义 Using", result.HasMacroUsings ? "是" : "否");
            });

            EditorGUILayout.Space(8);

            // 缺失的 Using
            DrawCard($"➕ 缺失的 Using ({result.MissingUsings.Count})", () =>
            {
                if (result.MissingUsings.Count == 0)
                {
                    EditorGUILayout.LabelField("  ✓ 无缺失", new GUIStyle(_styleLabel) { normal = { textColor = ClrOkTag } });
                }
                else
                {
                    foreach (var ns in result.MissingUsings)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label("  ➕", GUILayout.Width(24));
                            EditorGUILayout.SelectableLabel($"using {ns};", _styleLabel, GUILayout.Height(18));
                        }
                    }
                }
            });

            EditorGUILayout.Space(8);

            // 未使用的 Using
            DrawCard($"🗑 未使用的 Using ({result.UnusedUsings.Count})", () =>
            {
                if (result.UnusedUsings.Count == 0)
                {
                    EditorGUILayout.LabelField("  ✓ 无未使用的 Using", new GUIStyle(_styleLabel) { normal = { textColor = ClrOkTag } });
                }
                else
                {
                    foreach (var u in result.UnusedUsings)
                    {
                        using (new EditorGUILayout.VerticalScope())
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUILayout.Label("  🗑", GUILayout.Width(24));
                                EditorGUILayout.SelectableLabel(u.Directive, _styleLabel, GUILayout.Height(18));
                            }
                            if (!string.IsNullOrEmpty(u.MacroContext))
                            {
                                EditorGUILayout.LabelField($"    宏: {u.MacroContext}", _styleLabelDim);
                            }
                            EditorGUILayout.LabelField($"    行: {u.LineNumber}", _styleLabelDim);
                        }
                    }
                }
            });

            EditorGUILayout.Space(8);

            // 所有 Using 列表
            DrawCard($"📋 全部 Using ({result.AllUsings.Count})", () =>
            {
                foreach (var u in result.AllUsings)
                {
                    string status = u.IsUnused ? "🗑" : "✓";
                    string macro = !string.IsNullOrEmpty(u.MacroContext) ? $"  [{u.MacroContext}]" : "";
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"  {status}", GUILayout.Width(24));
                        EditorGUILayout.SelectableLabel($"{u.Directive}{macro}", _styleLabel, GUILayout.Height(18));
                    }
                }
            });

            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>绘制带标题的卡片容器</summary>
        private void DrawCard(string title, System.Action drawContent)
        {
            // 卡片背景
            var cardRect = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true));
            float startY = cardRect.y;

            // 标题
            var headerRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, ClrHeaderBg);
            GUI.Label(new Rect(headerRect.x + 8, headerRect.y, headerRect.width - 8, headerRect.height),
                title, _styleHeaderLabel);

            // 内容区域
            var contentRect = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true));
            float contentStartY = contentRect.y;

            drawContent();

            float endY = contentRect.y;

            // 绘制卡片背景（包含标题和内容）
            var fullRect = new Rect(startY, startY, cardRect.width, endY - startY);
            // 注意：这里只绘制内容区域的背景，标题背景已经在上面绘制
            EditorGUI.DrawRect(new Rect(startY, contentStartY, cardRect.width, endY - contentStartY), ClrCardBg);

            // 底部分割线
            EditorGUI.DrawRect(new Rect(startY, endY, cardRect.width, 1), ClrDivider);
        }

        private void DrawDetailRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"  {label}", _styleLabelDim, GUILayout.Width(70));
                // 路径等长文本支持换行显示
                var wrapStyle = new GUIStyle(_styleLabel) { wordWrap = true };
                EditorGUILayout.LabelField(value, wrapStyle, GUILayout.ExpandHeight(true));
            }
        }

        private void DrawHeader(string label, float width)
        {
            GUILayout.Label(label, _styleHeaderLabel, GUILayout.Width(width));
        }

        private void DrawStatusBar()
        {
            var rect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, ClrStatusBar);

            // 顶部分割线
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), ClrDivider);

            string statusText;
            if (!_hasScanned)
            {
                statusText = "就绪 - 点击扫描开始分析";
            }
            else if (_filteredResults.Count == 0)
            {
                statusText = "无匹配结果";
            }
            else
            {
                int totalMissing = _filteredResults.Sum(r => r.MissingUsings.Count);
                int totalUnused = _filteredResults.Sum(r => r.UnusedUsings.Count);
                statusText = $"文件: {_filteredResults.Count}  |  缺失: {totalMissing}  |  未使用: {totalUnused}  |  已选: {_selectedCount}";
            }

            GUI.Label(rect, statusText, _styleStatusBar);
        }

        #endregion

        #region ── 扫描逻辑 ──────────────────────────────────────

        private void ScanFiles()
        {
            _results.Clear();
            _selectedIndex = -1;
            _filterDirty = true;

            string fullPath = _scanPath;
            if (_scanPath.StartsWith("Assets"))
            {
                fullPath = Path.Combine(Application.dataPath, _scanPath.Substring("Assets".Length).TrimStart('/', '\\'));
            }

            if (!Directory.Exists(fullPath))
            {
                EditorUtility.DisplayDialog("错误", $"目录不存在: {fullPath}", "确定");
                return;
            }

            string[] scriptFiles = Directory.GetFiles(fullPath, "*.cs", SearchOption.AllDirectories);
            int total = scriptFiles.Length;

            if (total == 0)
            {
                EditorUtility.DisplayDialog("提示", $"在 {_scanPath} 下未找到 .cs 文件", "确定");
                return;
            }

            try
            {
                for (int i = 0; i < total; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("扫描中...", $"({i + 1}/{total}) {Path.GetFileName(scriptFiles[i])}", (float)(i + 1) / total))
                        break;

                    try
                    {
                        var result = AnalyzeFile(scriptFiles[i]);
                        if (result.MissingUsings.Count > 0 || result.UnusedUsings.Count > 0 || result.AllUsings.Count > 0)
                            _results.Add(result);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[UsingManager] 分析失败: {scriptFiles[i]}\n{ex.Message}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            _hasScanned = true;
            UpdateSelectedCount();
            Debug.Log($"[UsingManager] 扫描完成: {_results.Count} 个文件, 缺失 {_results.Sum(r => r.MissingUsings.Count)} 条, 未使用 {_results.Sum(r => r.UnusedUsings.Count)} 条");
        }

        private static UsingScanResult AnalyzeFile(string filePath)
        {
            string content = File.ReadAllText(filePath);
            string cleanedContent = StripCommentsAndStrings(content);
            string[] lines = cleanedContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var usings = new List<UsingInfo>();
            var codeBodyBuilder = new StringBuilder();
            var macroStack = new Stack<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();

                // 预处理指令
                if (trimmed.StartsWith("#"))
                {
                    ProcessPreprocessor(trimmed, macroStack);
                    continue;
                }

                string macroContext = macroStack.Count > 0
                    ? string.Join(" / ", macroStack.Reverse())
                    : "";

                var usingInfo = ParseUsingLine(trimmed, macroContext, i + 1);
                if (usingInfo != null)
                {
                    usings.Add(usingInfo);
                    continue;
                }

                codeBodyBuilder.AppendLine(lines[i]);
            }

            string codeBody = codeBodyBuilder.ToString();

            // 检测缺失
            var existingNamespaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (var u in usings)
            {
                if (!u.IsStatic && u.Alias == null)
                    existingNamespaces.Add(u.Namespace);
            }

            var missingUsings = DetectMissingUsings(codeBody, existingNamespaces);

            // 检测未使用
            foreach (var u in usings)
                u.IsUnused = !IsUsingUsed(u, codeBody);

            return new UsingScanResult
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                RelativePath = GetRelativePath(filePath),
                AllUsings = usings,
                MissingUsings = missingUsings,
                UnusedUsings = usings.Where(u => u.IsUnused).ToList(),
                HasMacroUsings = usings.Any(u => !string.IsNullOrEmpty(u.MacroContext))
            };
        }

        private static void ProcessPreprocessor(string line, Stack<string> macroStack)
        {
            if (line.StartsWith("#if "))
                macroStack.Push(line.Substring(4).Trim());
            else if (line.StartsWith("#elif "))
            {
                if (macroStack.Count > 0) macroStack.Pop();
                macroStack.Push(line.Substring(6).Trim());
            }
            else if (line.StartsWith("#else"))
            {
                if (macroStack.Count > 0) macroStack.Pop();
                macroStack.Push("else");
            }
            else if (line.StartsWith("#endif"))
            {
                if (macroStack.Count > 0) macroStack.Pop();
            }
        }

        private static UsingInfo ParseUsingLine(string line, string macroContext, int lineNumber)
        {
            string trimmed = line.Trim();

            // global using (C# 10+)
            if (trimmed.StartsWith("global using "))
            {
                trimmed = "using " + trimmed.Substring(13);
            }

            if (!trimmed.StartsWith("using ", StringComparison.Ordinal))
                return null;

            // 排除 using 语句: using (var x = ...)
            string afterUsing = trimmed.Substring(6).TrimStart();
            if (afterUsing.StartsWith("("))
                return null;

            if (!trimmed.EndsWith(";"))
                return null;

            string content = afterUsing.Substring(0, afterUsing.Length - 1).Trim();

            bool isStatic = false;
            string alias = null;
            string namespaceName;

            if (content.StartsWith("static "))
            {
                isStatic = true;
                namespaceName = content.Substring(7).Trim();
            }
            else
            {
                int eqIndex = content.IndexOf('=');
                if (eqIndex >= 0)
                {
                    alias = content.Substring(0, eqIndex).Trim();
                    namespaceName = content.Substring(eqIndex + 1).Trim();
                }
                else
                {
                    namespaceName = content;
                }
            }

            return new UsingInfo
            {
                Directive = "using " + content + ";",
                Namespace = namespaceName,
                Alias = alias,
                IsStatic = isStatic,
                MacroContext = macroContext,
                LineNumber = lineNumber
            };
        }

        private static List<string> DetectMissingUsings(string codeBody, HashSet<string> existingNamespaces)
        {
            var missing = new List<string>();
            var checkedNamespaces = new HashSet<string>(StringComparer.Ordinal);

            foreach (var kv in TypeToNamespace)
            {
                string typeName = kv.Key;
                string namespaceName = kv.Value;

                if (existingNamespaces.Contains(namespaceName))
                    continue;

                if (checkedNamespaces.Contains(namespaceName))
                    continue;

                // 检查类型名是否在代码体中出现（全字匹配）
                if (Regex.IsMatch(codeBody, @"\b" + Regex.Escape(typeName) + @"\b"))
                {
                    // 排除被完全限定的情况: Namespace.TypeName
                    // 如果类型名前面紧跟 "."，可能是限定引用
                    // 但我们检查的是非限定引用，所以用 \b 即可
                    checkedNamespaces.Add(namespaceName);
                    missing.Add(namespaceName);
                }
            }

            return missing;
        }

        private static bool IsUsingUsed(UsingInfo usingInfo, string codeBody)
        {
            // 别名: 检查别名是否在代码中使用
            if (usingInfo.Alias != null)
            {
                return Regex.IsMatch(codeBody, @"\b" + Regex.Escape(usingInfo.Alias) + @"\b");
            }

            // static using: 保守起见视为已使用
            if (usingInfo.IsStatic)
                return true;

            string ns = usingInfo.Namespace;

            // 1. 检查完全限定引用: "Namespace."
            if (codeBody.Contains(ns + "."))
                return true;

            // 2. 检查最后一段作为限定符: "LastPart."
            string lastPart = ns.Contains('.') ? ns.Substring(ns.LastIndexOf('.') + 1) : ns;
            if (lastPart.Length > 0 && Regex.IsMatch(codeBody, @"\b" + Regex.Escape(lastPart) + @"\."))
                return true;

            // 3. 检查常见类型名
            if (CommonTypesByNamespace.TryGetValue(ns, out var types))
            {
                foreach (var typeName in types)
                {
                    if (Regex.IsMatch(codeBody, @"\b" + Regex.Escape(typeName) + @"\b"))
                        return true;
                }
            }

            // 4. LINQ 特殊处理: 检查扩展方法调用
            if (ns == "System.Linq")
            {
                foreach (var method in LinqMethods)
                {
                    if (Regex.IsMatch(codeBody, @"\." + Regex.Escape(method) + @"\("))
                        return true;
                }
                // 查询语法
                if (Regex.IsMatch(codeBody, @"\bfrom\s+\w+\s+in\b"))
                    return true;
            }

            return false;
        }

        #endregion

        #region ── 批量操作 ──────────────────────────────────────

        private void ApplyMissingToSelected()
        {
            var targets = _results.Where(r => r.IsSelected && r.MissingUsings.Count > 0).ToList();
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "选中的文件中没有需要添加的 Using", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog("确认",
                $"将对 {targets.Count} 个文件添加缺失的 using 语句，是否继续？",
                "确认", "取消"))
                return;

            int success = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("添加缺失 Using",
                    $"({i + 1}/{targets.Count}) {targets[i].FileName}",
                    (float)(i + 1) / targets.Count))
                    break;

                try
                {
                    ApplyChangesToFile(targets[i], addMissing: true, removeUnused: false);
                    success++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UsingManager] 添加失败: {targets[i].RelativePath}\n{ex.Message}");
                }
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();

            // 重新扫描
            ScanFiles();
            EditorUtility.DisplayDialog("完成", $"已对 {success} 个文件添加缺失的 Using", "确定");
        }

        private void RemoveUnusedFromSelected()
        {
            var targets = _results.Where(r => r.IsSelected && r.UnusedUsings.Count > 0).ToList();
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "选中的文件中没有未使用的 Using", "确定");
                return;
            }

            int totalUnused = targets.Sum(r => r.UnusedUsings.Count);
            if (!EditorUtility.DisplayDialog("确认",
                $"将对 {targets.Count} 个文件移除 {totalUnused} 条未使用的 using 语句。\n\n" +
                "⚠️ 结果基于启发式分析，可能存在误判。建议操作前确认已备份。\n\n是否继续？",
                "确认", "取消"))
                return;

            int success = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("清理未使用 Using",
                    $"({i + 1}/{targets.Count}) {targets[i].FileName}",
                    (float)(i + 1) / targets.Count))
                    break;

                try
                {
                    ApplyChangesToFile(targets[i], addMissing: false, removeUnused: true);
                    success++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UsingManager] 清理失败: {targets[i].RelativePath}\n{ex.Message}");
                }
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();

            ScanFiles();
            EditorUtility.DisplayDialog("完成", $"已对 {success} 个文件清理未使用的 Using", "确定");
        }

        private static void ApplyChangesToFile(UsingScanResult result, bool addMissing, bool removeUnused)
        {
            string content = File.ReadAllText(result.FilePath);
            string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var newLines = new List<string>(lines);

            // 移除未使用的 using（逆序，保持行号正确）
            if (removeUnused && result.UnusedUsings.Count > 0)
            {
                var indices = result.UnusedUsings
                    .Select(u => u.LineNumber - 1)
                    .Where(idx => idx >= 0 && idx < newLines.Count)
                    .OrderByDescending(x => x)
                    .ToList();

                foreach (int idx in indices)
                {
                    newLines.RemoveAt(idx);
                }
            }

            // 添加缺失的 using
            if (addMissing && result.MissingUsings.Count > 0)
            {
                int insertIndex = 0;

                // 查找最后一个 using 指令的位置
                for (int i = 0; i < newLines.Count; i++)
                {
                    string trimmed = newLines[i].Trim();
                    if (trimmed.StartsWith("using ") || trimmed.StartsWith("global using "))
                        insertIndex = i + 1;
                }

                // 如果没有 using，查找 namespace 或 class 声明
                if (insertIndex == 0)
                {
                    for (int i = 0; i < newLines.Count; i++)
                    {
                        string trimmed = newLines[i].Trim();
                        if (trimmed.StartsWith("namespace ") || trimmed.StartsWith("public class")
                            || trimmed.StartsWith("public partial class") || trimmed.StartsWith("internal class")
                            || trimmed.StartsWith("internal partial class") || trimmed.StartsWith("class "))
                        {
                            insertIndex = i;
                            break;
                        }
                    }
                }

                var usingLines = result.MissingUsings.Select(ns => $"using {ns};").ToList();
                newLines.InsertRange(insertIndex, usingLines);
            }

            File.WriteAllText(result.FilePath, string.Join("\n", newLines));
        }

        #endregion

        #region ── 辅助方法 ──────────────────────────────────────

        private static string StripCommentsAndStrings(string code)
        {
            var sb = new StringBuilder(code.Length);
            int i = 0;
            int len = code.Length;

            while (i < len)
            {
                char c = code[i];

                // 单行注释
                if (c == '/' && i + 1 < len && code[i + 1] == '/')
                {
                    while (i < len && code[i] != '\n') i++;
                    continue;
                }

                // 多行注释
                if (c == '/' && i + 1 < len && code[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < len && !(code[i] == '*' && code[i + 1] == '/'))
                    {
                        if (code[i] == '\n' || code[i] == '\r') sb.Append(code[i]);
                        i++;
                    }
                    i += 2;
                    sb.Append(' ');
                    continue;
                }

                // 逐字字符串: @"...", $@"...", @$"..."
                if (c == '@' && i + 1 < len && code[i + 1] == '"')
                {
                    i += 2;
                    while (i < len)
                    {
                        if (code[i] == '"')
                        {
                            if (i + 1 < len && code[i + 1] == '"') { i += 2; continue; }
                            i++; break;
                        }
                        if (code[i] == '\n' || code[i] == '\r') sb.Append(code[i]);
                        i++;
                    }
                    sb.Append(' ');
                    continue;
                }
                if (c == '$' && i + 2 < len && code[i + 1] == '@' && code[i + 2] == '"')
                {
                    i += 3;
                    while (i < len)
                    {
                        if (code[i] == '"')
                        {
                            if (i + 1 < len && code[i + 1] == '"') { i += 2; continue; }
                            i++; break;
                        }
                        if (code[i] == '\n' || code[i] == '\r') sb.Append(code[i]);
                        i++;
                    }
                    sb.Append(' ');
                    continue;
                }
                if (c == '@' && i + 2 < len && code[i + 1] == '$' && code[i + 2] == '"')
                {
                    i += 3;
                    while (i < len)
                    {
                        if (code[i] == '"')
                        {
                            if (i + 1 < len && code[i + 1] == '"') { i += 2; continue; }
                            i++; break;
                        }
                        if (code[i] == '\n' || code[i] == '\r') sb.Append(code[i]);
                        i++;
                    }
                    sb.Append(' ');
                    continue;
                }

                // 普通字符串: "...", $"..."
                if (c == '"')
                {
                    i++;
                    while (i < len)
                    {
                        if (code[i] == '\\') { i += 2; continue; }
                        if (code[i] == '"') { i++; break; }
                        i++;
                    }
                    sb.Append(' ');
                    continue;
                }
                if (c == '$' && i + 1 < len && code[i + 1] == '"')
                {
                    i += 2;
                    while (i < len)
                    {
                        if (code[i] == '\\') { i += 2; continue; }
                        if (code[i] == '"') { i++; break; }
                        i++;
                    }
                    sb.Append(' ');
                    continue;
                }

                // 字符字面量: '...'
                if (c == '\'')
                {
                    i++;
                    if (i < len && code[i] == '\\') i += 2;
                    else if (i < len) i++;
                    if (i < len && code[i] == '\'') i++;
                    sb.Append(' ');
                    continue;
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        private static string GetRelativePath(string fullPath)
        {
            string dataPath = Application.dataPath.Replace("/", "\\");
            string normalizedPath = fullPath.Replace("/", "\\");
            if (normalizedPath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return "Assets" + normalizedPath.Substring(dataPath.Length);
            return fullPath;
        }

        private void RefreshFilteredCache()
        {
            if (!_filterDirty
                && _filteredResults != null
                && _searchKeyword == _lastSearchKeyword
                && _filterMode == _lastFilterMode)
                return;

            _lastSearchKeyword = _searchKeyword;
            _lastFilterMode = _filterMode;
            _filterDirty = false;

            _filteredResults.Clear();
            bool hasSearch = !string.IsNullOrEmpty(_searchKeyword);

            foreach (var r in _results)
            {
                if (hasSearch)
                {
                    if (!r.FileName.Contains(_searchKeyword, StringComparison.OrdinalIgnoreCase)
                        && !r.RelativePath.Contains(_searchKeyword, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                bool pass = _filterMode switch
                {
                    FilterMode.HasMissing => r.MissingUsings.Count > 0,
                    FilterMode.HasUnused => r.UnusedUsings.Count > 0,
                    FilterMode.HasIssue => r.MissingUsings.Count > 0 || r.UnusedUsings.Count > 0,
                    _ => true
                };

                if (pass) _filteredResults.Add(r);
            }

            UpdateSelectedCount();
        }

        private string _lastSearchKeyword = "";
        private FilterMode _lastFilterMode;

        private void UpdateSelectedCount()
        {
            _selectedCount = _filteredResults.Count(r => r.IsSelected);
        }

        /// <summary>
        /// 处理 Toggle 点击，支持 Shift 范围选择
        /// </summary>
        private void HandleSelectionToggle(int clickedIndex, bool value, bool shiftHeld)
        {
            if (shiftHeld && _lastClickedIndex >= 0 && _lastClickedIndex != clickedIndex)
            {
                // Shift 范围选择：上次点击到本次点击之间的所有项
                int from = Mathf.Min(_lastClickedIndex, clickedIndex);
                int to = Mathf.Max(_lastClickedIndex, clickedIndex);
                for (int j = from; j <= to; j++)
                {
                    _filteredResults[j].IsSelected = value;
                }
            }
            else
            {
                _filteredResults[clickedIndex].IsSelected = value;
                _lastClickedIndex = clickedIndex;
            }

            UpdateSelectedCount();
        }

        #endregion
    }
}
