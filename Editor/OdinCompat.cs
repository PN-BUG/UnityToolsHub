// ═══════════════════════════════════════════════════════════════
//  Odin Inspector 兼容层 —— 无 Odin 时的反射式自动绘制
//
//  通过反射读取 [FoldoutGroup]、[ShowIf]、[EnableIf]、[Button]、
//  [LabelText]、[InfoBox]、[ReadOnly]、[MultiLineProperty] 等属性，
//  在 OnGUI 中自动绘制所有 public 字段和标记了 [Button] 的方法。
//
//  支持：FoldoutGroup 分组折叠 / ShowIf 条件显示 / EnableIf 条件启用 /
//        Button 按钮调用 / LabelText 自定义标签 / InfoBox 信息提示 /
//        ReadOnly 只读 / MultiLineProperty 多行文本 / FolderPath 文件夹选择
// ═══════════════════════════════════════════════════════════════

#if !ODIN_INSPECTOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Sirenix.OdinInspector
{
    // ── 属性桩（与 Odin 真实属性签名一致，业务代码零 #if） ──

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class FoldoutGroupAttribute : Attribute
    {
        public int Order { get; set; }
        public string GroupName { get; }
        public bool Expanded { get; }
        public FoldoutGroupAttribute(string groupName) { GroupName = groupName; }
        public FoldoutGroupAttribute(string groupName, bool expanded = false) { GroupName = groupName; Expanded = expanded; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class BoxGroupAttribute : Attribute
    {
        public string GroupName { get; }
        public BoxGroupAttribute(string groupName) { GroupName = groupName; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class LabelTextAttribute : Attribute
    {
        public string Text { get; }
        public LabelTextAttribute(string text) { Text = text; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class HideLabelAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ValueDropdownAttribute : Attribute
    {
        public string MemberName { get; }
        public ValueDropdownAttribute(string memberName) { MemberName = memberName; }
    }

    public enum ButtonSizes { Small, Medium, Large }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
    public class ButtonAttribute : Attribute
    {
        public string Name { get; }
        public ButtonSizes Size { get; }
        public ButtonAttribute() { Name = null; Size = ButtonSizes.Medium; }
        public ButtonAttribute(string name) { Name = name; Size = ButtonSizes.Medium; }
        public ButtonAttribute(ButtonSizes size) { Name = null; Size = size; }
        public ButtonAttribute(string name, ButtonSizes size) { Name = name; Size = size; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
    public class GUIColorAttribute : Attribute
    {
        public Color Color { get; }
        public GUIColorAttribute(float r, float g, float b) { Color = new Color(r, g, b); }
        public GUIColorAttribute(string hex) { Color = ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ShowIfAttribute : Attribute
    {
        public string MemberName { get; }
        public object Value { get; }
        public ShowIfAttribute(string memberName) { MemberName = memberName; Value = true; }
        public ShowIfAttribute(string memberName, object value) { MemberName = memberName; Value = value; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class HideIfAttribute : Attribute
    {
        public string MemberName { get; }
        public object Value { get; }
        public HideIfAttribute(string memberName) { MemberName = memberName; Value = true; }
        public HideIfAttribute(string memberName, object value) { MemberName = memberName; Value = value; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class EnableIfAttribute : Attribute
    {
        public string MemberName { get; }
        public object Value { get; }
        public EnableIfAttribute(string memberName) { MemberName = memberName; Value = true; }
        public EnableIfAttribute(string memberName, object value) { MemberName = memberName; Value = value; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class DisableIfAttribute : Attribute
    {
        public string MemberName { get; }
        public object Value { get; }
        public DisableIfAttribute(string memberName) { MemberName = memberName; Value = true; }
        public DisableIfAttribute(string memberName, object value) { MemberName = memberName; Value = value; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
    public class OnInspectorGUIAttribute : Attribute
    {
        public string MethodName { get; }
        public OnInspectorGUIAttribute() { MethodName = null; }
        public OnInspectorGUIAttribute(string methodName) { MethodName = methodName; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ShowInInspectorAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ReadOnlyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class MultiLinePropertyAttribute : Attribute
    {
        public int Lines { get; }
        public MultiLinePropertyAttribute(int lines) { Lines = lines; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class FolderPathAttribute : Attribute
    {
        public bool AbsolutePath { get; set; }
        public bool RequireExistingPath { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class AssetsOnlyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class OnValueChangedAttribute : Attribute
    {
        public string MethodName { get; }
        public OnValueChangedAttribute(string methodName) { MethodName = methodName; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ListDrawerSettingsAttribute : Attribute
    {
        public bool ShowFoldout { get; set; } = true;
        public bool DraggableItems { get; set; } = true;
        public int NumberOfItemsPerPage { get; set; }
        public bool ShowIndexLabels { get; set; }
        public bool HideAddButton { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = true)]
    public class InfoBoxAttribute : Attribute
    {
        public string Message { get; }
        public InfoMessageType Type { get; }
        public string VisibleIfMemberName { get; }
        public InfoBoxAttribute(string message) { Message = message; Type = InfoMessageType.Info; VisibleIfMemberName = null; }
        public InfoBoxAttribute(string message, object type) { Message = message; Type = (InfoMessageType)type; VisibleIfMemberName = null; }
        public InfoBoxAttribute(string message, object type, string visibleIfMemberName) { Message = message; Type = (InfoMessageType)type; VisibleIfMemberName = visibleIfMemberName; }
    }

    public enum InfoMessageType { None, Info, Warning, Error }
}

namespace Sirenix.OdinInspector.Editor
{
    /// <summary>
    /// OdinEditorWindow 桩 —— 无 Odin 时通过反射自动绘制 Inspector。
    /// 业务代码 protected override void OnEnable() 可编译。
    /// 子类无需手写 OnGUI。
    /// </summary>
    public class OdinEditorWindow : UnityEditor.EditorWindow
    {
        private OdinCompatDrawer _drawer;
        private Vector2 _scrollPos;

        protected virtual void OnEnable()
        {
            _drawer = new OdinCompatDrawer(this);
        }

        protected virtual void OnDisable() { }

        private void OnGUI()
        {
            _drawer?.Draw();
        }
    }

    /// <summary>ValueDropdownItem 桩</summary>
    public struct ValueDropdownItem<T>
    {
        public string Text { get; }
        public T Value { get; }
        public ValueDropdownItem(string text, T value) { Text = text; Value = value; }
    }

    /// <summary>ValueDropdownList 桩</summary>
    public class ValueDropdownList<T> : List<ValueDropdownItem<T>>
    {
        public void Add(string name, T value) => Add(new ValueDropdownItem<T>(name, value));
    }

    // ═══════════════════════════════════════════════════════════════
    //  反射式自动绘制器
    // ═══════════════════════════════════════════════════════════════

    internal class OdinCompatDrawer
    {
        private readonly object _target;
        private readonly Type _type;
        private readonly FieldInfo[] _fields;
        private readonly MethodInfo[] _methods;
        private readonly Dictionary<string, bool> _foldoutStates = new();

        public OdinCompatDrawer(object target)
        {
            _target = target;
            _type = target.GetType();
            _fields = _type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(f => f.IsPublic || f.GetCustomAttribute<ShowInInspectorAttribute>() != null)
                .Where(f => f.GetCustomAttribute<HideInInspector>() == null || f.GetCustomAttribute<ShowInInspectorAttribute>() != null)
                .ToArray();
            _methods = _type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.GetCustomAttribute<ButtonAttribute>() != null)
                .ToArray();
        }

        public void Draw()
        {
            // 绘制无分组的独立字段和方法
            DrawUngroupedFields();
            DrawUngroupedButtons();

            // 绘制分组
            var groupNames = GetOrderedGroupNames();
            foreach (var groupName in groupNames)
            {
                if (!_foldoutStates.ContainsKey(groupName))
                {
                    // 从属性中读取初始展开状态
                    var firstField = _fields.FirstOrDefault(f => f.GetCustomAttribute<FoldoutGroupAttribute>()?.GroupName == groupName);
                    var expanded = firstField?.GetCustomAttribute<FoldoutGroupAttribute>()?.Expanded ?? false;
                    _foldoutStates[groupName] = expanded;
                }

                EditorGUILayout.Space(2);
                _foldoutStates[groupName] = EditorGUILayout.Foldout(_foldoutStates[groupName], groupName, true, EditorStyles.foldoutHeader);

                if (_foldoutStates[groupName])
                {
                    EditorGUI.indentLevel++;
                    DrawFieldsInGroup(groupName);
                    DrawButtonsInGroup(groupName);
                    EditorGUI.indentLevel--;
                }
            }
        }

        // ── 分组排序 ──────────────────────────────────────

        private List<string> GetOrderedGroupNames()
        {
            var groups = new List<string>();
            var orders = new Dictionary<string, int>();

            foreach (var f in _fields)
            {
                var attr = f.GetCustomAttribute<FoldoutGroupAttribute>();
                if (attr != null && !groups.Contains(attr.GroupName))
                {
                    groups.Add(attr.GroupName);
                    orders[attr.GroupName] = attr.Order;
                }
            }

            foreach (var m in _methods)
            {
                var attr = m.GetCustomAttribute<FoldoutGroupAttribute>();
                if (attr != null && !groups.Contains(attr.GroupName))
                {
                    groups.Add(attr.GroupName);
                    orders[attr.GroupName] = attr.Order;
                }
            }

            groups.Sort((a, b) => orders[a].CompareTo(orders[b]));
            return groups;
        }

        // ── 字段绘制 ──────────────────────────────────────

        private void DrawUngroupedFields()
        {
            foreach (var field in _fields)
            {
                if (field.GetCustomAttribute<FoldoutGroupAttribute>() != null) continue;
                if (!ShouldShow(field)) continue;
                DrawField(field);
            }
        }

        private void DrawFieldsInGroup(string groupName)
        {
            foreach (var field in _fields)
            {
                var attr = field.GetCustomAttribute<FoldoutGroupAttribute>();
                if (attr == null || attr.GroupName != groupName) continue;
                if (!ShouldShow(field)) continue;
                DrawField(field);
            }
        }

        private bool ShouldShow(FieldInfo field)
        {
            var showIf = field.GetCustomAttribute<ShowIfAttribute>();
            if (showIf != null && !EvaluateCondition(showIf.MemberName, showIf.Value))
                return false;

            var hideIf = field.GetCustomAttribute<HideIfAttribute>();
            if (hideIf != null && EvaluateCondition(hideIf.MemberName, hideIf.Value))
                return false;

            return true;
        }

        private bool ShouldEnable(FieldInfo field)
        {
            var enableIf = field.GetCustomAttribute<EnableIfAttribute>();
            if (enableIf != null && !EvaluateCondition(enableIf.MemberName, enableIf.Value))
                return false;

            var disableIf = field.GetCustomAttribute<DisableIfAttribute>();
            if (disableIf != null && EvaluateCondition(disableIf.MemberName, disableIf.Value))
                return false;

            var readOnly = field.GetCustomAttribute<ReadOnlyAttribute>();
            if (readOnly != null) return false;

            return true;
        }

        private bool EvaluateCondition(string memberName, object expectedValue)
        {
            if (string.IsNullOrEmpty(memberName)) return true;

            var field = _type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                var actual = field.GetValue(_target);
                return actual?.Equals(expectedValue) ?? expectedValue == null;
            }

            var prop = _type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null)
            {
                var actual = prop.GetValue(_target);
                return actual?.Equals(expectedValue) ?? expectedValue == null;
            }

            var method = _type.GetMethod(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null && method.ReturnType == typeof(bool) && method.GetParameters().Length == 0)
            {
                return (bool)method.Invoke(_target, null);
            }

            return true;
        }

        private void DrawField(FieldInfo field)
        {
            // InfoBox
            DrawInfoBox(field);

            var labelAttr = field.GetCustomAttribute<LabelTextAttribute>();
            var hideLabelAttr = field.GetCustomAttribute<HideLabelAttribute>();
            var label = labelAttr != null ? labelAttr.Text : ObjectNames.NicifyVariableName(field.Name);

            var enabled = ShouldEnable(field);
            var prevEnabled = GUI.enabled;
            GUI.enabled = enabled;

            EditorGUI.BeginChangeCheck();

            var fieldType = field.FieldType;
            var value = field.GetValue(_target);
            object newValue = DrawFieldByType(label, value, fieldType, field, hideLabelAttr != null);

            if (EditorGUI.EndChangeCheck())
            {
                field.SetValue(_target, newValue);
                InvokeOnValueChanged(field);
            }

            GUI.enabled = prevEnabled;
        }

        private void DrawInfoBox(FieldInfo field)
        {
            var infoBoxes = field.GetCustomAttributes<InfoBoxAttribute>();
            foreach (var info in infoBoxes)
            {
                if (!string.IsNullOrEmpty(info.VisibleIfMemberName) && !EvaluateCondition(info.VisibleIfMemberName, true))
                    continue;

                var msgType = info.Type switch
                {
                    InfoMessageType.Info => MessageType.Info,
                    InfoMessageType.Warning => MessageType.Warning,
                    InfoMessageType.Error => MessageType.Error,
                    _ => MessageType.None,
                };
                EditorGUILayout.HelpBox(info.Message, msgType);
            }
        }

        private object DrawFieldByType(string label, object value, Type type, FieldInfo field, bool hideLabel)
        {
            // FolderPath
            var folderPathAttr = field.GetCustomAttribute<FolderPathAttribute>();
            if (folderPathAttr != null && type == typeof(string))
            {
                var path = (string)value;
                var btnLabel = hideLabel ? "📁 选择文件夹" : label;
                EditorGUILayout.BeginHorizontal();
                if (!hideLabel) EditorGUILayout.PrefixLabel(label);
                path = EditorGUILayout.TextField(path ?? "");
                if (GUILayout.Button("📁", GUILayout.Width(28)))
                {
                    var selected = EditorUtility.OpenFolderPanel("选择文件夹", path ?? "", "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        if (!folderPathAttr.AbsolutePath && selected.StartsWith(Application.dataPath))
                            path = "Assets" + selected.Substring(Application.dataPath.Length);
                        else
                            path = selected;
                    }
                }
                EditorGUILayout.EndHorizontal();
                return path;
            }

            // MultiLineProperty
            var multiLineAttr = field.GetCustomAttribute<MultiLinePropertyAttribute>();
            if (multiLineAttr != null && type == typeof(string))
            {
                var lines = Mathf.Max(1, multiLineAttr.Lines);
                if (hideLabel)
                    return EditorGUILayout.TextArea((string)value ?? "", GUILayout.MinHeight(lines * 18));
                return EditorGUILayout.TextField(label, (string)value ?? "");
            }

            // ValueDropdown
            var dropdownAttr = field.GetCustomAttribute<ValueDropdownAttribute>();
            if (dropdownAttr != null)
            {
                var options = InvokeValueDropdownMember(dropdownAttr.MemberName);
                if (options != null && options.Length > 0)
                {
                    var currentStr = value?.ToString() ?? "";
                    var idx = Array.FindIndex(options, o => o == currentStr);
                    if (idx < 0) idx = 0;
                    idx = EditorGUILayout.Popup(hideLabel ? GUIContent.none : new GUIContent(label), idx, options.Select(o => new GUIContent(o)).ToArray());
                    return options[idx];
                }
            }

            // 基本类型
            if (type == typeof(bool)) return EditorGUILayout.Toggle(hideLabel ? GUIContent.none : new GUIContent(label), (bool)value);
            if (type == typeof(int)) return EditorGUILayout.IntField(hideLabel ? GUIContent.none : new GUIContent(label), (int)value);
            if (type == typeof(long)) return EditorGUILayout.LongField(hideLabel ? GUIContent.none : new GUIContent(label), (long)value);
            if (type == typeof(float)) return EditorGUILayout.FloatField(hideLabel ? GUIContent.none : new GUIContent(label), (float)value);
            if (type == typeof(double)) return EditorGUILayout.DoubleField(hideLabel ? GUIContent.none : new GUIContent(label), (double)value);
            if (type == typeof(string)) return EditorGUILayout.TextField(hideLabel ? GUIContent.none : new GUIContent(label), (string)value ?? "");
            if (type == typeof(Vector2)) return EditorGUILayout.Vector2Field(hideLabel ? "" : label, (Vector2)value);
            if (type == typeof(Vector3)) return EditorGUILayout.Vector3Field(hideLabel ? "" : label, (Vector3)value);
            if (type == typeof(Vector4)) return EditorGUILayout.Vector4Field(hideLabel ? "" : label, (Vector4)value);
            if (type == typeof(Color)) return EditorGUILayout.ColorField(hideLabel ? GUIContent.none : new GUIContent(label), (Color)value);
            if (type == typeof(Rect)) return EditorGUILayout.RectField(hideLabel ? GUIContent.none : new GUIContent(label), (Rect)value);
            if (type.IsEnum) return EditorGUILayout.EnumPopup(hideLabel ? GUIContent.none : new GUIContent(label), (Enum)value);
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                return EditorGUILayout.ObjectField(hideLabel ? GUIContent.none : new GUIContent(label), (UnityEngine.Object)value, type, true);

            // List<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                DrawListField(label, value, type, hideLabel);
                return value;
            }

            // 兜底
            EditorGUILayout.LabelField(label, value?.ToString() ?? "null");
            return value;
        }

        private void DrawListField(string label, object value, Type type, bool hideLabel)
        {
            if (value == null) return;
            var listType = type.GetGenericArguments()[0];
            var list = (System.Collections.IList)value;

            if (!hideLabel)
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            for (int i = 0; i < list.Count; i++)
            {
                var itemLabel = $"[{i}]";
                var itemValue = list[i];

                if (listType == typeof(bool))
                    list[i] = EditorGUILayout.Toggle(itemLabel, (bool)itemValue);
                else if (listType == typeof(int))
                    list[i] = EditorGUILayout.IntField(itemLabel, (int)itemValue);
                else if (listType == typeof(float))
                    list[i] = EditorGUILayout.FloatField(itemLabel, (float)itemValue);
                else if (listType == typeof(string))
                    list[i] = EditorGUILayout.TextField(itemLabel, (string)itemValue ?? "");
                else if (listType == typeof(Vector3))
                    list[i] = EditorGUILayout.Vector3Field(itemLabel, (Vector3)itemValue);
                else if (listType == typeof(Vector2))
                    list[i] = EditorGUILayout.Vector2Field(itemLabel, (Vector2)itemValue);
                else if (listType == typeof(Color))
                    list[i] = EditorGUILayout.ColorField(itemLabel, (Color)itemValue);
                else if (listType.IsEnum)
                    list[i] = EditorGUILayout.EnumPopup(itemLabel, (Enum)itemValue);
                else if (typeof(UnityEngine.Object).IsAssignableFrom(listType))
                    list[i] = EditorGUILayout.ObjectField(itemLabel, (UnityEngine.Object)itemValue, listType, true);
                else
                    EditorGUILayout.LabelField(itemLabel, itemValue?.ToString() ?? "null");
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 添加"))
            {
                object defaultVal = listType.IsValueType ? Activator.CreateInstance(listType) : null;
                list.Add(defaultVal);
            }
            if (GUILayout.Button("✕ 移除最后一个") && list.Count > 0)
            {
                list.RemoveAt(list.Count - 1);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
        }

        // ── 按钮绘制 ──────────────────────────────────────

        private void DrawUngroupedButtons()
        {
            foreach (var method in _methods)
            {
                if (method.GetCustomAttribute<FoldoutGroupAttribute>() != null) continue;
                if (!ShouldShowMethod(method)) continue;
                DrawButton(method);
            }
        }

        private void DrawButtonsInGroup(string groupName)
        {
            foreach (var method in _methods)
            {
                var attr = method.GetCustomAttribute<FoldoutGroupAttribute>();
                if (attr == null || attr.GroupName != groupName) continue;
                if (!ShouldShowMethod(method)) continue;
                DrawButton(method);
            }
        }

        private bool ShouldShowMethod(MethodInfo method)
        {
            var showIf = method.GetCustomAttribute<ShowIfAttribute>();
            if (showIf != null && !EvaluateCondition(showIf.MemberName, showIf.Value))
                return false;
            return true;
        }

        private void DrawButton(MethodInfo method)
        {
            var btnAttr = method.GetCustomAttribute<ButtonAttribute>();
            var labelAttr = method.GetCustomAttribute<LabelTextAttribute>();
            var colorAttr = method.GetCustomAttribute<GUIColorAttribute>();
            var enableIf = method.GetCustomAttribute<EnableIfAttribute>();

            var label = btnAttr.Name ?? labelAttr?.Text ?? ObjectNames.NicifyVariableName(method.Name);
            var height = btnAttr.Size switch
            {
                ButtonSizes.Small => 20,
                ButtonSizes.Medium => 28,
                ButtonSizes.Large => 36,
                _ => 28,
            };

            var enabled = enableIf == null || EvaluateCondition(enableIf.MemberName, enableIf.Value);
            var prevColor = GUI.backgroundColor;
            if (colorAttr != null) GUI.backgroundColor = colorAttr.Color;

            var prevEnabled = GUI.enabled;
            GUI.enabled = enabled;

            if (GUILayout.Button(label, GUILayout.Height(height)))
            {
                var paramStrs = method.GetParameters();
                var args = new object[paramStrs.Length];
                for (int i = 0; i < paramStrs.Length; i++)
                    args[i] = paramStrs[i].DefaultValue != DBNull.Value ? paramStrs[i].DefaultValue : (paramStrs[i].ParameterType.IsValueType ? Activator.CreateInstance(paramStrs[i].ParameterType) : null);
                method.Invoke(_target, args);
            }

            GUI.enabled = prevEnabled;
            GUI.backgroundColor = prevColor;
        }

        // ── 辅助 ──────────────────────────────────────────

        private void InvokeOnValueChanged(FieldInfo field)
        {
            var attr = field.GetCustomAttribute<OnValueChangedAttribute>();
            if (attr == null) return;
            var method = _type.GetMethod(attr.MethodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            method?.Invoke(_target, null);
        }

        private string[] InvokeValueDropdownMember(string memberName)
        {
            if (string.IsNullOrEmpty(memberName)) return null;

            var method = _type.GetMethod(memberName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null)
            {
                var result = method.Invoke(method.IsStatic ? null : _target, null);
                return ConvertToStringArray(result);
            }

            var field = _type.GetField(memberName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                var result = field.GetValue(field.IsStatic ? null : _target);
                return ConvertToStringArray(result);
            }

            return null;
        }

        private static string[] ConvertToStringArray(object result)
        {
            if (result == null) return null;
            if (result is string[] arr) return arr;
            if (result is System.Collections.IEnumerable enumerable)
            {
                var list = new List<string>();
                foreach (var item in enumerable)
                    list.Add(item?.ToString() ?? "");
                return list.ToArray();
            }
            return new[] { result.ToString() };
        }
    }
}
#endif
