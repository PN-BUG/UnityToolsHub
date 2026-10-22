using System;
using System.Collections.Generic;
using System.Reflection;
using Nodin;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 测试窗口 —— 聚合展示场景中所有标记了 [Test] 的方法、字段和属性。
/// 按组件及显式业务分组展示，并在刷新时保留折叠状态和方法参数。
/// 快捷键 Ctrl+T 打开/关闭。
/// </summary>
[ToolInfo("测试窗口", "调试工具", Description = "聚合展示场景中所有标记了 [Test] 的方法、字段和属性。\n\n方法显示为按钮，字段和属性显示为输入或只读结果。\n支持显式业务分组、排序、折叠和搜索；刷新保留展开状态，快捷键 Ctrl+T 打开/关闭。", Icon = "⌘", Tags = new[] { "测试", "调试", "Test" }, Shortcut = "Ctrl+T", ShowInUnityMenuByDefault = true)]
public class TestWindow : EditorWindow
{
#if UNITY_6000_0_OR_NEWER
    static int GetEntityKey(UnityEngine.Object obj) => obj.GetEntityId();
#else
    static int GetEntityKey(UnityEngine.Object obj) => obj.GetInstanceID();
#endif

    #region 展示数据结构

    /// <summary>统一保存测试成员的展示元数据，使输入、按钮和结果可以在同一分组中排序。</summary>
    private abstract class TestMemberEntry
    {
        // 成员标签、实际所属组件以及显式布局配置。
        public string DisplayName;
        public MonoBehaviour Target;
        public TestAttribute Layout;

        /// <summary>原始反射成员，仅用于确定声明顺序和继承归属，不改变调用目标。</summary>
        public abstract MemberInfo Member { get; }
    }

    /// <summary>测试方法及其独立参数输入，执行仍使用原有反射入口。</summary>
    private class TestMethodEntry : TestMemberEntry
    {
        // 方法声明、参数类型与工具窗口缓存的参数值。
        public MethodInfo Method;
        public ParameterInfo[] Parameters;
        public object[] ParameterValues;

        /// <summary>返回测试方法的原始声明。</summary>
        public override MemberInfo Member => Method;
    }

    /// <summary>测试字段的展示信息，只读标记沿用现有 Nodin 契约。</summary>
    private class TestFieldEntry : TestMemberEntry
    {
        // 实际字段及工具窗口是否允许编辑。
        public FieldInfo Field;
        public bool IsReadOnly;

        /// <summary>返回测试字段的原始声明。</summary>
        public override MemberInfo Member => Field;
    }

    /// <summary>测试属性的展示信息，仍区分只读标记与属性自身是否提供 setter。</summary>
    private class TestPropertyEntry : TestMemberEntry
    {
        // 实际属性、只读限制与 setter 是否存在。
        public PropertyInfo Property;
        public bool IsReadOnly;
        public bool HasSetter;

        /// <summary>返回测试属性的原始声明。</summary>
        public override MemberInfo Member => Property;
    }

    /// <summary>缓存一个声明类型自身的测试成员，避免窗口重绘时重新扫描类型。</summary>
    private class TestTypeMetadata
    {
        // 保留声明类型各类成员，继承成员由现有收集过程去重。
        public MethodInfo[] Methods;
        public FieldInfo[] Fields;
        public PropertyInfo[] Properties;
    }

    /// <summary>一个实际组件实例的测试区域，内含独立业务分组。</summary>
    private class TestGroup
    {
        // 实例身份用于刷新后匹配折叠状态，不能使用每次重建的区域对象作为键。
        public int TargetKey;
        public string GroupName;           // 组件类型名
        public GameObject GameObject;      // 所属 GameObject
        public List<TestMethodEntry> Methods = new List<TestMethodEntry>();
        public List<TestFieldEntry> Fields = new List<TestFieldEntry>();
        public List<TestPropertyEntry> Properties = new List<TestPropertyEntry>();
        public HashSet<MemberInfo> Members = new HashSet<MemberInfo>();
        // 业务分组由标记明确指定，不根据中文按钮名称推测。
        public List<TestSection> Sections = new List<TestSection>();
    }

    /// <summary>组件内的业务分组，统一排列输入、操作和结果；未分组成员保持原展示分类。</summary>
    private class TestSection
    {
        // 分组名称与显示顺序，以及首次显示时的折叠配置。
        public string Name;
        public int Order;
        public bool IsFoldout;
        public bool ExpandedByDefault;
        public List<TestMemberEntry> Members = new List<TestMemberEntry>();
    }

    #endregion

    // ── 状态 ─────────────────────────────────────────────────
    private Vector2 _scrollPos;
    private List<TestGroup> _groups = new List<TestGroup>();
    // 折叠状态仅由窗口持有，刷新沿用相同组件和分组的选择，不写入场景或存档。
    private Dictionary<int, bool> _foldouts = new Dictionary<int, bool>();
    private Dictionary<int, Dictionary<string, bool>> _sectionFoldouts = new Dictionary<int, Dictionary<string, bool>>();
    private string _searchFilter = "";
    private Vector2 _scrollPosGroups;
    // 缓存参数值，避免刷新时丢失用户输入
    private Dictionary<string, object[]> _paramCache = new Dictionary<string, object[]>();
    private static readonly Dictionary<Type, TestTypeMetadata> TypeMetadataCache = new Dictionary<Type, TestTypeMetadata>();
    private static bool _typeMetadataInitialized;

    // ── 快捷键注册 ──────────────────────────────────────────
    [MenuItem("UnityToolsHub/测试窗口 %t")]   // Ctrl+T
    public static void ToggleWindow()
    {
        if (HasOpenInstances<TestWindow>())
        {
            GetWindow<TestWindow>().Close();
        }
        else
        {
            ShowWindow();
        }
    }

    [MenuItem("Window/测试窗口")]
    public static void ShowWindow()
    {
        var win = GetWindow<TestWindow>("测试窗口");
        win.minSize = new Vector2(320, 200);
        // OnEnable 中会自动调用 RefreshEntries，无需重复刷新
        win.Show();
    }

    private void OnEnable()
    {
        RefreshEntries();
        // 监听场景变化，标记为脏（防抖，避免频繁 FindObjectsOfType 全场景扫描）
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        EditorApplication.update -= OnEditorUpdate;
    }

    // ── 防抖：层级变化后延迟 1 秒再刷新，避免拖拽/编辑时频繁扫描 ──
    private bool _isDirty;
    private double _dirtyTime;

    private void OnHierarchyChanged()
    {
        // Play Mode 中对象和层级可能频繁变化。自动全场景扫描会阻塞 Editor 主线程，
        // 此时由工具栏“刷新”按钮显式更新即可。
        if (EditorApplication.isPlaying) return;

        _isDirty = true;
        _dirtyTime = EditorApplication.timeSinceStartup;
    }

    private void OnEditorUpdate()
    {
        // 延迟 1 秒后自动刷新（避免编辑过程中频繁 FindObjectsOfType）
        if (_isDirty && EditorApplication.timeSinceStartup - _dirtyTime > 1.0)
        {
            _isDirty = false;
            RefreshEntries();
            Repaint();
        }
    }

    /// <summary>
    /// 保存当前所有方法的参数值到缓存
    /// </summary>
    private void SaveParamCache()
    {
        foreach (var group in _groups)
        {
            foreach (var entry in group.Methods)
            {
                if (entry.Target == null || entry.Parameters.Length == 0) continue;
                string key = $"{GetEntityKey(entry.Target)}_{entry.Method.Name}";
                _paramCache[key] = entry.ParameterValues;
            }
        }
    }

    // ── 刷新数据 ────────────────────────────────────────────
    /// <summary>重建测试入口与业务布局，保留同一组件的参数输入及两层折叠状态。</summary>
    private void RefreshEntries()
    {
        // 1. 参数和折叠均属于工具状态，重建入口时不能清空已有用户选择。
        SaveParamCache();
        
        _groups.Clear();

        EnsureTypeMetadataCache();
        var groupsByTarget = new Dictionary<int, TestGroup>();

        // 只查询实际声明了 [Test] 成员的组件类型，不再扫描场景中的全部 MonoBehaviour。
        foreach (var pair in TypeMetadataCache)
        {
            var metadata = pair.Value;
            foreach (var found in FindObjectsOfType(pair.Key, true))
            {
                if (found is not MonoBehaviour mb || mb == null) continue;
                int targetKey = GetEntityKey(mb);
                if (!groupsByTarget.TryGetValue(targetKey, out var group))
                {
                    group = new TestGroup
                    {
                        TargetKey = targetKey,
                        GroupName = ObjectNames.NicifyVariableName(mb.GetType().Name),
                        GameObject = mb.gameObject
                    };
                    groupsByTarget.Add(targetKey, group);
                }

                foreach (var method in metadata.Methods)
                {
                    if (!group.Members.Add(method)) continue;
                    var attr = method.GetCustomAttribute<TestAttribute>();

                    var parameters = method.GetParameters();
                    var entry = new TestMethodEntry
                    {
                        DisplayName = attr.Name,
                        Layout = attr,
                        Target = mb,
                        Method = method,
                        Parameters = parameters,
                        ParameterValues = new object[parameters.Length]
                    };

                    string cacheKey = $"{targetKey}_{method.Name}";
                    if (_paramCache.TryGetValue(cacheKey, out var cached) && cached.Length == parameters.Length)
                        entry.ParameterValues = cached;
                    else
                    {
                        for (int i = 0; i < parameters.Length; i++)
                            entry.ParameterValues[i] = GetDefaultValue(parameters[i].ParameterType);
                    }

                    group.Methods.Add(entry);
                }

                foreach (var field in metadata.Fields)
                {
                    if (!group.Members.Add(field)) continue;
                    var attr = field.GetCustomAttribute<TestAttribute>();
                    group.Fields.Add(new TestFieldEntry
                    {
                        DisplayName = attr.Name,
                        Layout = attr,
                        Target = mb,
                        Field = field,
                        IsReadOnly = field.IsDefined(typeof(Nodin.ReadOnlyAttribute), true)
                    });
                }

                foreach (var prop in metadata.Properties)
                {
                    if (!group.Members.Add(prop)) continue;
                    var attr = prop.GetCustomAttribute<TestAttribute>();
                    group.Properties.Add(new TestPropertyEntry
                    {
                        DisplayName = attr.Name,
                        Layout = attr,
                        Target = mb,
                        Property = prop,
                        IsReadOnly = prop.IsDefined(typeof(Nodin.ReadOnlyAttribute), true),
                        HasSetter = prop.GetSetMethod(true) != null
                    });
                }
            }
        }

        foreach (var group in groupsByTarget.Values)
        {
            // 2. 显式组装业务区域，再按配置顺序和声明位置排序，消除同继承层级的混排。
            BuildSections(group);
            _groups.Add(group);
            if (!_foldouts.ContainsKey(group.TargetKey)) _foldouts[group.TargetKey] = true;
        }
    }

    #region 业务分组装配

    /// <summary>从组件已有测试入口生成业务分组，不增加场景扫描或修改测试业务。</summary>
    /// <param name="group">已经收集完整成员的实际组件区域。</param>
    private void BuildSections(TestGroup group)
    {
        // 1. 未填写分组的标记仍放在组件顶部，按字段、属性、方法的原分类展示。
        var sectionsByName = new Dictionary<string, TestSection>(StringComparer.Ordinal);
        foreach (var entry in group.Fields) AddSectionMember(group, sectionsByName, entry);
        foreach (var entry in group.Properties) AddSectionMember(group, sectionsByName, entry);
        foreach (var entry in group.Methods) AddSectionMember(group, sectionsByName, entry);

        // 2. 有序元数据只在刷新时创建，重绘直接读取现有布局。
        group.Sections.AddRange(sectionsByName.Values);
        group.Sections.Sort(CompareSections);
        if (!_sectionFoldouts.TryGetValue(group.TargetKey, out var states))
        {
            states = new Dictionary<string, bool>(StringComparer.Ordinal);
            _sectionFoldouts.Add(group.TargetKey, states);
        }
        foreach (var section in group.Sections)
        {
            section.Members.Sort((left, right) => CompareSectionMembers(left, right, string.IsNullOrEmpty(section.Name)));
            if (section.IsFoldout && !states.ContainsKey(section.Name))
                states.Add(section.Name, section.ExpandedByDefault);
        }
    }

    /// <summary>将测试成员加入明确分组；同名分组配置冲突时记录错误并跳过该成员。</summary>
    /// <param name="group">成员所属的实际组件。</param>
    /// <param name="sectionsByName">本组件正在构建的分组映射。</param>
    /// <param name="entry">保留原反射目标的测试入口。</param>
    private void AddSectionMember(TestGroup group, Dictionary<string, TestSection> sectionsByName, TestMemberEntry entry)
    {
        // 1. 空分组是旧标记的明确展示语义；命名分组必须使用相同的布局配置。
        string name = entry.Layout.Group ?? string.Empty;
        if (!sectionsByName.TryGetValue(name, out var section))
        {
            section = new TestSection
            {
                Name = name,
                Order = entry.Layout.GroupOrder,
                IsFoldout = !string.IsNullOrEmpty(name) && entry.Layout.IsFoldout,
                ExpandedByDefault = entry.Layout.ExpandedByDefault
            };
            sectionsByName.Add(name, section);
        }
        else if (!string.IsNullOrEmpty(name) && (section.Order != entry.Layout.GroupOrder
            || section.IsFoldout != entry.Layout.IsFoldout || section.ExpandedByDefault != entry.Layout.ExpandedByDefault))
        {
            Debug.LogError($"[测试窗口] 组件 {group.GroupName} 的分组“{name}”布局配置不一致，跳过成员“{entry.DisplayName}”。请统一 GroupOrder、IsFoldout 和 ExpandedByDefault。", entry.Target);
            return;
        }
        section.Members.Add(entry);
    }

    /// <summary>先显示未分组成员，其余业务分组按明确顺序排列，重名顺序使用名称确定。</summary>
    /// <param name="left">左侧业务区域。</param>
    /// <param name="right">右侧业务区域。</param>
    /// <returns>用于排序的先后比较结果。</returns>
    private static int CompareSections(TestSection left, TestSection right)
    {
        // 1. 保留未分组输入的原有顶部位置，业务区域的次序由标记提供。
        bool leftUnnamed = string.IsNullOrEmpty(left.Name);
        bool rightUnnamed = string.IsNullOrEmpty(right.Name);
        if (leftUnnamed != rightUnnamed) return leftUnnamed ? -1 : 1;
        int result = left.Order.CompareTo(right.Order);
        return result != 0 ? result : string.Compare(left.Name, right.Name, StringComparison.Ordinal);
    }

    /// <summary>排序分组中的混合成员，明确顺序相同时使用成员类别、继承深度及声明位置。</summary>
    /// <param name="left">左侧测试入口。</param>
    /// <param name="right">右侧测试入口。</param>
    /// <param name="isUnnamedSection">是否需要保留旧标记的字段、属性、方法展示分类。</param>
    /// <returns>用于排序的确定性比较结果。</returns>
    private static int CompareSectionMembers(TestMemberEntry left, TestMemberEntry right, bool isUnnamedSection)
    {
        // 1. 命名分组优先使用显式顺序，输入、按钮和结果可以交错布局。
        int result = isUnnamedSection ? 0 : left.Layout.Order.CompareTo(right.Layout.Order);
        if (result != 0) return result;
        if (left is TestFieldEntry && right is not TestFieldEntry) return -1;
        if (right is TestFieldEntry && left is not TestFieldEntry) return 1;
        if (left is TestPropertyEntry && right is TestMethodEntry) return -1;
        if (right is TestPropertyEntry && left is TestMethodEntry) return 1;

        // 2. 保留基类成员在前，同层级不再只返回相等而交给不稳定排序。
        result = GetInheritanceDepth(left.Member.DeclaringType).CompareTo(GetInheritanceDepth(right.Member.DeclaringType));
        if (result != 0) return result;
        result = string.Compare(left.Member.DeclaringType.FullName, right.Member.DeclaringType.FullName, StringComparison.Ordinal);
        return result != 0 ? result : left.Member.MetadataToken.CompareTo(right.Member.MetadataToken);
    }

    #endregion

    private static void EnsureTypeMetadataCache()
    {
        if (_typeMetadataInitialized) return;
        _typeMetadataInitialized = true;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (var method in TypeCache.GetMethodsWithAttribute<TestAttribute>())
            AddMetadata(method.DeclaringType, method: method);

        foreach (var field in TypeCache.GetFieldsWithAttribute<TestAttribute>())
            AddMetadata(field.DeclaringType, field: field);

        // TypeCache 没有属性查询接口；这里只扫描各类型自身声明的属性。
        // 继承属性会由声明它的基类收集，避免基类和派生类各收集一次而重复显示。
        foreach (var type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
        {
            foreach (var property in type.GetProperties(flags))
                if (property.GetIndexParameters().Length == 0 && property.IsDefined(typeof(TestAttribute), true))
                    AddMetadata(property.DeclaringType, property: property);
        }

        foreach (var metadata in TypeMetadataCache.Values)
        {
            metadata.Methods ??= Array.Empty<MethodInfo>();
            metadata.Fields ??= Array.Empty<FieldInfo>();
            metadata.Properties ??= Array.Empty<PropertyInfo>();
        }
    }

    private static void AddMetadata(Type type, MethodInfo method = null, FieldInfo field = null, PropertyInfo property = null)
    {
        if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type)) return;
        if (!TypeMetadataCache.TryGetValue(type, out var metadata))
        {
            metadata = new TestTypeMetadata();
            TypeMetadataCache.Add(type, metadata);
        }

        if (method != null)
            metadata.Methods = Append(metadata.Methods, method);
        if (field != null)
            metadata.Fields = Append(metadata.Fields, field);
        if (property != null)
            metadata.Properties = Append(metadata.Properties, property);
    }

    private static T[] Append<T>(T[] source, T value)
    {
        int count = source?.Length ?? 0;
        var result = new T[count + 1];
        if (count > 0) Array.Copy(source, result, count);
        result[count] = value;
        return result;
    }

    // ── GUI 绘制 ────────────────────────────────────────────
    private void OnGUI()
    {
        DrawToolbar();
        DrawSearchBar();

        _scrollPosGroups = EditorGUILayout.BeginScrollView(_scrollPosGroups);

        if (_groups.Count == 0)
        {
            EditorGUILayout.HelpBox("场景中未找到任何标记了 [Test] 的成员。\n\n" +
                "用法示例：\n" +
                "  [Test(\"创建存档\")] public void CreateSaveData() { ... }\n" +
                "  [Test(\"玩家速度\")] public float speed = 5f;\n" +
                "  [Test(\"生命值\")] public int Health { get; set; }",
                MessageType.Info);
        }
        else
        {
            bool anyShown = false;
            foreach (var group in _groups)
            {
                if (!MatchesFilter(group)) continue;
                anyShown = true;
                DrawGroup(group);
            }

            if (!anyShown)
            {
                EditorGUILayout.HelpBox("没有匹配的测试项。", MessageType.Info);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    /// <summary>绘制工具栏，全部展开和折叠同时应用于组件与业务两层区域。</summary>
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label($"共 {_groups.Count} 组", EditorStyles.miniLabel);

        if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(50)))
        {
            RefreshEntries();
        }

        if (GUILayout.Button("全部展开", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            // 1. 工具栏只更新窗口布局状态，不调用业务或修改场景。
            SetAllFoldouts(true);
        }

        if (GUILayout.Button("全部折叠", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            SetAllFoldouts(false);
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSearchBar()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("搜索", GUILayout.Width(30));
        _searchFilter = EditorGUILayout.TextField(_searchFilter);
        if (GUILayout.Button("✕", GUILayout.Width(22)))
        {
            _searchFilter = "";
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>判断组件或其中业务区域是否匹配当前搜索；不修改折叠状态。</summary>
    /// <param name="group">待判断的实际组件区域。</param>
    /// <returns>组件身份或任一业务区域匹配时返回 true。</returns>
    private bool MatchesFilter(TestGroup group)
    {
        // 1. 搜索组件时展示其完整区域，搜索业务时保留整组相关输入和结果。
        if (MatchesComponentFilter(group)) return true;
        foreach (var section in group.Sections)
            if (MatchesSectionFilter(section)) return true;
        return false;
    }

    /// <summary>判断搜索是否直接命中组件类型或所属对象。</summary>
    /// <param name="group">待判断的组件区域。</param>
    /// <returns>无搜索或组件身份命中时返回 true。</returns>
    private bool MatchesComponentFilter(TestGroup group)
    {
        return string.IsNullOrEmpty(_searchFilter)
            || group.GroupName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0
            || (group.GameObject != null && group.GameObject.name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>按业务分组名称或成员标签搜索，不将整组输入和结果拆散。</summary>
    /// <param name="section">待判断的业务分组。</param>
    /// <returns>分组名称或成员标签匹配时返回 true。</returns>
    private bool MatchesSectionFilter(TestSection section)
    {
        if (section.Name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        foreach (var entry in section.Members)
            if (entry.DisplayName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>绘制组件折叠及其业务区域，持续显示非折叠共用输入。</summary>
    /// <param name="group">已完成排序和状态装配的实际组件。</param>
    private void DrawGroup(TestGroup group)
    {
        // 分组标题：组件名 @ GameObject名
        string header = $"{group.GroupName}";
        if (group.GameObject != null)
            header += $"  ({group.GameObject.name})";

        EditorGUILayout.BeginVertical("box");

        // 1. 实例键在窗口刷新后保持一致，不再以每次新建的分组对象保存状态。
        _foldouts[group.TargetKey] = EditorGUILayout.Foldout(_foldouts[group.TargetKey], header, true, EditorStyles.foldoutHeader);

        if (_foldouts[group.TargetKey])
        {
            EditorGUI.indentLevel++;

            // 2. 搜索保留显式共用输入；其他区域按分组或成员标签整体过滤。
            bool showAllSections = MatchesComponentFilter(group);
            foreach (var section in group.Sections)
            {
                bool isSharedInput = !string.IsNullOrEmpty(section.Name) && !section.IsFoldout;
                if (showAllSections || isSharedInput || MatchesSectionFilter(section)) DrawSection(group, section);
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
    }

    #region 业务折叠绘制

    /// <summary>绘制一个业务区域，输入、按钮和只读结果使用同一有序成员集合。</summary>
    /// <param name="group">业务区域所属组件。</param>
    /// <param name="section">具有明确折叠配置和显示顺序的业务区域。</param>
    private void DrawSection(TestGroup group, TestSection section)
    {
        // 1. 未分组成员保持原布局，共用输入只显示标题且不创建可折叠按钮。
        bool isNamed = !string.IsNullOrEmpty(section.Name);
        if (isNamed) EditorGUILayout.BeginVertical("box");
        bool expanded = true;
        if (section.IsFoldout)
        {
            var states = _sectionFoldouts[group.TargetKey];
            expanded = EditorGUILayout.Foldout(states[section.Name], section.Name, true);
            states[section.Name] = expanded;
        }
        else if (isNamed)
        {
            EditorGUILayout.LabelField(section.Name, EditorStyles.boldLabel);
        }

        // 2. 绘制沿用原字段编辑和方法执行入口，不在布局层解释技能或存档规则。
        if (expanded)
        {
            foreach (var entry in section.Members)
            {
                if (entry is TestFieldEntry field) DrawFieldEntry(field);
                else if (entry is TestPropertyEntry property) DrawPropertyEntry(property);
                else if (entry is TestMethodEntry method) DrawMethodEntry(method);
            }
        }
        if (isNamed) EditorGUILayout.EndVertical();
    }

    /// <summary>统一更新全部已装配组件和业务分组的折叠状态，不影响共用输入的展示配置。</summary>
    /// <param name="expanded">true 全部展开，false 全部折叠。</param>
    private void SetAllFoldouts(bool expanded)
    {
        // 1. 包含当前隐藏区域，展开组件后各业务分组也保持本次明确选择。
        foreach (var group in _groups)
        {
            _foldouts[group.TargetKey] = expanded;
            foreach (var section in group.Sections)
                if (section.IsFoldout) _sectionFoldouts[group.TargetKey][section.Name] = expanded;
        }
    }

    #endregion

    private void DrawFieldEntry(TestFieldEntry entry)
    {
        if (entry.Target == null) return;

        EditorGUILayout.BeginHorizontal();

        // 显示标签
        EditorGUILayout.PrefixLabel(entry.DisplayName);

        // 只读字段禁用编辑
        EditorGUI.BeginDisabledGroup(entry.IsReadOnly);

        // 获取当前值
        var value = entry.Field.GetValue(entry.Target);
        var fieldType = entry.Field.FieldType;

        EditorGUI.BeginChangeCheck();

        object newValue = null;
        bool changed = false;

        // 根据字段类型绘制对应的编辑器控件
        if (fieldType == typeof(bool))
        {
            newValue = EditorGUILayout.Toggle((bool)(value ?? false));
            changed = true;
        }
        else if (fieldType == typeof(int))
        {
            newValue = EditorGUILayout.IntField((int)(value ?? 0));
            changed = true;
        }
        else if (fieldType == typeof(float))
        {
            newValue = EditorGUILayout.FloatField((float)(value ?? 0f));
            changed = true;
        }
        else if (fieldType == typeof(string))
        {
            newValue = EditorGUILayout.TextField((string)(value ?? ""));
            changed = true;
        }
        else if (fieldType == typeof(Vector2))
        {
            newValue = EditorGUILayout.Vector2Field("", (Vector2)(value ?? Vector2.zero));
            changed = true;
        }
        else if (fieldType == typeof(Vector3))
        {
            newValue = EditorGUILayout.Vector3Field("", (Vector3)(value ?? Vector3.zero));
            changed = true;
        }
        else if (fieldType == typeof(Color))
        {
            newValue = EditorGUILayout.ColorField((Color)(value ?? Color.white));
            changed = true;
        }
        else if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
        {
            newValue = EditorGUILayout.ObjectField(value as UnityEngine.Object, fieldType, true);
            changed = true;
        }
        else if (fieldType.IsEnum)
        {
            newValue = EditorGUILayout.EnumPopup((Enum)(value ?? Enum.GetValues(fieldType).GetValue(0)));
            changed = true;
        }
        else
        {
            // 不支持的类型，只读显示
            EditorGUILayout.LabelField(value?.ToString() ?? "null");
        }

        if (changed && EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(entry.Target, $"[Test] 修改 {entry.DisplayName}");
            entry.Field.SetValue(entry.Target, newValue);
            EditorUtility.SetDirty(entry.Target);
        }

        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPropertyEntry(TestPropertyEntry entry)
    {
        if (entry.Target == null) return;

        var prop = entry.Property;

        EditorGUILayout.BeginHorizontal();

        // 显示标签
        EditorGUILayout.PrefixLabel(entry.DisplayName);

        // 只读字段或无 setter 禁用编辑
        EditorGUI.BeginDisabledGroup(entry.IsReadOnly || !entry.HasSetter);

        // 获取当前值
        object value = null;
        try
        {
            value = prop.GetValue(entry.Target);
        }
        catch { }

        var propType = prop.PropertyType;

        EditorGUI.BeginChangeCheck();

        object newValue = null;
        bool changed = false;

        // 根据属性类型绘制对应的编辑器控件
        if (propType == typeof(bool))
        {
            newValue = EditorGUILayout.Toggle((bool)(value ?? false));
            changed = true;
        }
        else if (propType == typeof(int))
        {
            newValue = EditorGUILayout.IntField((int)(value ?? 0));
            changed = true;
        }
        else if (propType == typeof(float))
        {
            newValue = EditorGUILayout.FloatField((float)(value ?? 0f));
            changed = true;
        }
        else if (propType == typeof(string))
        {
            newValue = EditorGUILayout.TextField((string)(value ?? ""));
            changed = true;
        }
        else if (propType == typeof(Vector2))
        {
            newValue = EditorGUILayout.Vector2Field("", (Vector2)(value ?? Vector2.zero));
            changed = true;
        }
        else if (propType == typeof(Vector3))
        {
            newValue = EditorGUILayout.Vector3Field("", (Vector3)(value ?? Vector3.zero));
            changed = true;
        }
        else if (propType == typeof(Color))
        {
            newValue = EditorGUILayout.ColorField((Color)(value ?? Color.white));
            changed = true;
        }
        else if (typeof(UnityEngine.Object).IsAssignableFrom(propType))
        {
            newValue = EditorGUILayout.ObjectField(value as UnityEngine.Object, propType, true);
            changed = true;
        }
        else if (propType.IsEnum)
        {
            newValue = EditorGUILayout.EnumPopup((Enum)(value ?? Enum.GetValues(propType).GetValue(0)));
            changed = true;
        }
        else
        {
            // 不支持的类型，只读显示
            EditorGUILayout.LabelField(value?.ToString() ?? "null");
        }

        if (changed && EditorGUI.EndChangeCheck() && entry.HasSetter)
        {
            Undo.RecordObject(entry.Target, $"[Test] 修改 {entry.DisplayName}");
            prop.SetValue(entry.Target, newValue);
            EditorUtility.SetDirty(entry.Target);
        }

        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawMethodEntry(TestMethodEntry entry)
    {
        if (entry.Target == null) return;

        // 检查目标是否为 prefab（不能执行方法）
        bool isPrefab = !entry.Target.gameObject.scene.IsValid();

        EditorGUI.BeginDisabledGroup(isPrefab);

        // 有参数的方法：先绘制参数输入框，再绘制按钮
        if (entry.Parameters != null && entry.Parameters.Length > 0)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(entry.DisplayName, EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            for (int i = 0; i < entry.Parameters.Length; i++)
            {
                var param = entry.Parameters[i];
                entry.ParameterValues[i] = DrawParameterField(param.Name, param.ParameterType, entry.ParameterValues[i]);
            }
            EditorGUI.indentLevel--;

            if (GUILayout.Button($"执行: {entry.DisplayName}"))
            {
                InvokeMethod(entry);
            }

            EditorGUILayout.EndVertical();
        }
        else
        {
            if (GUILayout.Button(entry.DisplayName))
            {
                InvokeMethod(entry);
            }
        }

        EditorGUI.EndDisabledGroup();
    }

    private void InvokeMethod(TestMethodEntry entry)
    {
        // 记录 Undo
        Undo.RecordObject(entry.Target, $"[Test] 调用 {entry.DisplayName}");

        try
        {
            entry.Method.Invoke(entry.Target, entry.ParameterValues);
            Debug.Log($"<color=cyan>[Test]</color> 已执行: {entry.DisplayName}  ({entry.Target.gameObject.name})");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Test] 执行失败: {entry.DisplayName}\n{ex}");
        }
    }

    private object DrawParameterField(string paramName, Type paramType, object currentValue)
    {
        object newValue = currentValue;

        if (paramType == typeof(bool))
        {
            newValue = EditorGUILayout.Toggle(paramName, (bool)(currentValue ?? false));
        }
        else if (paramType == typeof(int))
        {
            newValue = EditorGUILayout.IntField(paramName, (int)(currentValue ?? 0));
        }
        else if (paramType == typeof(float))
        {
            newValue = EditorGUILayout.FloatField(paramName, (float)(currentValue ?? 0f));
        }
        else if (paramType == typeof(string))
        {
            newValue = EditorGUILayout.TextField(paramName, (string)(currentValue ?? ""));
        }
        else if (paramType == typeof(Vector2))
        {
            newValue = EditorGUILayout.Vector2Field(paramName, (Vector2)(currentValue ?? Vector2.zero));
        }
        else if (paramType == typeof(Vector3))
        {
            newValue = EditorGUILayout.Vector3Field(paramName, (Vector3)(currentValue ?? Vector3.zero));
        }
        else if (paramType == typeof(Color))
        {
            newValue = EditorGUILayout.ColorField(paramName, (Color)(currentValue ?? Color.white));
        }
        else if (typeof(UnityEngine.Object).IsAssignableFrom(paramType))
        {
            newValue = EditorGUILayout.ObjectField(paramName, currentValue as UnityEngine.Object, paramType, true);
        }
        else if (paramType.IsEnum)
        {
            if (currentValue == null)
                currentValue = Enum.GetValues(paramType).GetValue(0);
            newValue = EditorGUILayout.EnumPopup(paramName, (Enum)currentValue);
        }
        else
        {
            EditorGUILayout.LabelField(paramName, currentValue?.ToString() ?? "null");
        }

        return newValue;
    }

    private static object GetDefaultValue(Type type)
    {
        if (type == typeof(string)) return "";
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.Length > 0 ? values.GetValue(0) : null;
        }
        if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return null;
        if (type.IsValueType) return Activator.CreateInstance(type);
        return null;
    }

    /// <summary>
    /// 获取类型的继承深度（用于排序：基类成员在前，当前类成员在后）
    /// </summary>
    private static int GetInheritanceDepth(Type type)
    {
        int depth = 0;
        var current = type;
        while (current != null && current != typeof(MonoBehaviour) && current != typeof(object))
        {
            depth++;
            current = current.BaseType;
        }
        return depth;
    }
}
