/*************************************************************************
 *  Copyright © 2023-2030 FXB CO.,LTD. All rights reserved.
 *------------------------------------------------------------------------
 *  公司：DefaultCompany
 *  项目：UnityPackageCreator
 *  文件：PackageCreatorWindow.cs
 *  作者：Administrator
 *  日期：2024/11/20 20:18:52
 *  功能：Unity 自定义包快速创建器
*************************************************************************/

using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityPackageCreator.Runtime;
using Debug = UnityEngine.Debug;

namespace UnityPackageCreator.Editor
{
    /// <summary>
    /// Unity Package Creator — 快速创建符合 UPM 规范的自定义包
    /// </summary>
    [ToolInfo("包创建器", "项目工具",
        Description = "快速创建符合 UPM 规范的自定义包。\n\n" +
                      "• 填写包名、版本、描述等信息\n" +
                      "• 自动生成 package.json / asmdef / CHANGELOG / README\n" +
                      "• 支持添加依赖、关键字、示例\n" +
                      "• 创建后自动安装到当前工程",
        Icon = "📦",
        Tags = new[] { "package", "upm", "包", "创建", "Unity Package" },
        Priority = 10)]
    public class PackageCreatorWindow : EditorWindow
    {
        #region 字段

        private bool isCreateScriptsFolder = true;
        private bool isCreateTestsFolder = false;
        private bool isCreateChangeLog = true;
        private bool isCreateReadme = true;

        private string packageName = "";
        private string version = "";
        private string displayName = "";
        private string description = "";
        private string unity = "";
        private string unityRelease = "";
        private string documentationUrl = "";
        private string changelogUrl = "";
        private string licensesUrl = "";
        private List<Dependency> dependencies = new List<Dependency>();
        private List<string> keywords = new List<string>();
        private Author author = new Author();
        private List<Sample> samples = new List<Sample>();

        private ReorderableList keywordsReorderableList;
        private ReorderableList samplesReorderableList;
        private ReorderableList dependenciesReorderableList;

        private bool authorToggleGroup;
        private Vector2 scrollPos;

        // 验证警告：字段名 → (是否显示, 提示信息)
        private readonly Dictionary<string, ValidationWarning> _warnings = new Dictionary<string, ValidationWarning>();

        // 正则表达式（预编译）
        private static readonly Regex UrlRegex = new Regex(
            @"^(http|https)://([\w-]+\.)+[\w-]+(/[\w- ./?%&=]*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EmailRegex = new Regex(
            @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private struct ValidationWarning
        {
            public bool show;
            public string message;
        }

        #endregion

        #region 生命周期

        [MenuItem("Window/Unity Package Creator")]
        public static void ShowWindow()
        {
            var window = GetWindow<PackageCreatorWindow>("包创建器");
            window.Show();
        }

        private void OnEnable()
        {
            InitKeywordsList();
            InitSamplesList();
            InitDependenciesList();
        }

        private void OnGUI()
        {
            DrawGUI();
        }

        #endregion

        #region 绘制面板信息

        private void InitKeywordsList()
        {
            keywordsReorderableList = new ReorderableList(
                keywords, typeof(string),
                true, true, true, true);

            keywordsReorderableList.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, "关键字 (Keywords)", EditorStyles.boldLabel);
            };

            keywordsReorderableList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                keywords[index] = EditorGUI.TextField(
                    new Rect(rect.x, rect.y + 2, rect.width, EditorGUIUtility.singleLineHeight),
                    keywords[index]);
            };

            keywordsReorderableList.onAddCallback = list => keywords.Add("");
            keywordsReorderableList.onRemoveCallback = list =>
            {
                if (list.index >= 0 && list.index < keywords.Count)
                    keywords.RemoveAt(list.index);
            };
        }

        private void InitSamplesList()
        {
            samplesReorderableList = new ReorderableList(
                samples, typeof(Sample),
                true, true, true, true);

            samplesReorderableList.elementHeight = EditorGUIUtility.singleLineHeight;

            samplesReorderableList.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, "示例 (Samples)", EditorStyles.boldLabel);
            };

            samplesReorderableList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var element = samples[index];
                rect.y += 2;
                float lineHeight = EditorGUIUtility.singleLineHeight;
                float spacing = 2f;

                element.displayName = EditorGUI.TextField(
                    new Rect(rect.x, rect.y, rect.width, lineHeight),
                    "DisplayName", element.displayName);

                element.description = EditorGUI.TextField(
                    new Rect(rect.x, rect.y + lineHeight + spacing, rect.width, lineHeight),
                    "Description", element.description);

                element.path = EditorGUI.TextField(
                    new Rect(rect.x, rect.y + (lineHeight + spacing) * 2, rect.width, lineHeight),
                    "Path", element.path);
            };

            samplesReorderableList.onAddCallback = list =>
            {
                samplesReorderableList.elementHeight = EditorGUIUtility.singleLineHeight * 3 + 10;
                samples.Add(new Sample());
            };

            samplesReorderableList.onRemoveCallback = list =>
            {
                if (list.index >= 0 && list.index < samples.Count)
                {
                    samples.RemoveAt(list.index);
                    if (list.count == 0)
                        samplesReorderableList.elementHeight = EditorGUIUtility.singleLineHeight;
                }
            };
        }

        private void InitDependenciesList()
        {
            dependenciesReorderableList = new ReorderableList(
                dependencies, typeof(Dependency),
                true, true, true, true);

            dependenciesReorderableList.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, "依赖 (Dependencies)", EditorStyles.boldLabel);
            };

            dependenciesReorderableList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var element = dependencies[index];
                rect.y += 2;
                float nameWidth = rect.width * 0.75f;
                float versionWidth = rect.width - nameWidth - 5;

                element.packageName = EditorGUI.TextField(
                    new Rect(rect.x, rect.y, nameWidth, EditorGUIUtility.singleLineHeight),
                    element.packageName);

                element.version = EditorGUI.TextField(
                    new Rect(rect.x + nameWidth + 5, rect.y, versionWidth, EditorGUIUtility.singleLineHeight),
                    element.version);
            };

            dependenciesReorderableList.onAddCallback = list => dependencies.Add(new Dependency());
            dependenciesReorderableList.onRemoveCallback = list =>
            {
                if (list.index >= 0 && list.index < dependencies.Count)
                    dependencies.RemoveAt(list.index);
            };
        }

        /// <summary>
        /// 绘制面板
        /// </summary>
        private void DrawGUI()
        {
            GUILayout.Space(5f);

            GUILayout.Label("包配置 (Package Config)", new GUIStyle
            {
                fontSize = 20,
                padding = new RectOffset { left = 5 },
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            });

            GUILayout.Space(5f);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, EditorStyles.helpBox);

            DrawPackageOptions();

            GUILayout.Space(5f);

            DrawPackageConfigGUI();

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("创建包 (Create Package)"))
            {
                if (!ValidatePackageInfo()) return;

                string selectPath = EditorUtility.OpenFolderPanel("Select Folder for New Package", "", "");
                if (!ValidatePackagePath(selectPath)) return;

                if (!CreateNewPackage(selectPath, packageName, out string packagePath)) return;

                InstallPackage(selectPath, packagePath);
            }
        }

        /// <summary>
        /// 绘制 package 选项
        /// </summary>
        private void DrawPackageOptions()
        {
            isCreateScriptsFolder = EditorGUILayout.ToggleLeft("创建 Scripts 文件夹", isCreateScriptsFolder);
            GUILayout.Space(5f);

            isCreateTestsFolder = EditorGUILayout.ToggleLeft("创建 Tests 文件夹", isCreateTestsFolder);
            GUILayout.Space(5f);

            isCreateChangeLog = EditorGUILayout.ToggleLeft("生成 CHANGELOG.md", isCreateChangeLog);
            GUILayout.Space(5f);

            isCreateReadme = EditorGUILayout.ToggleLeft("生成 README.md", isCreateReadme);
        }

        /// <summary>
        /// 绘制 package 配置信息
        /// </summary>
        private void DrawPackageConfigGUI()
        {
            DrawValidatedField("packageName", "包名 (Package Name) *", ref packageName);
            DrawValidatedField("version", "版本 (Version) *", ref version);
            DrawValidatedField("displayName", "显示名称 (Display Name) *", ref displayName);
            DrawDescriptionField();
            DrawValidatedField("unity", "Unity 版本 *", ref unity);
            DrawValidatedField("unityRelease", "Unity 发行版 *", ref unityRelease);
            DrawValidatedUrlField("documentationUrl", "文档地址 (Documentation URL)", ref documentationUrl);
            DrawValidatedUrlField("changelogUrl", "更新日志 (Changelog URL)", ref changelogUrl);
            DrawValidatedUrlField("licensesUrl", "许可证 (Licenses URL)", ref licensesUrl);

            dependenciesReorderableList.DoLayoutList();
            GUILayout.Space(5);

            keywordsReorderableList.DoLayoutList();

            DrawAuthorInfo();

            GUILayout.Space(5);

            samplesReorderableList.DoLayoutList();
        }

        /// <summary>
        /// 通用验证字段绘制（必填）
        /// </summary>
        private void DrawValidatedField(string key, string label, ref string value)
        {
            DrawWarningIfAny(key);
            value = EditorGUILayout.TextField(label, value);

            if (!string.IsNullOrEmpty(value))
                _warnings.Remove(key);
        }

        /// <summary>
        /// 绘制 URL 验证字段（可选，但填写时必须合法）
        /// </summary>
        private void DrawValidatedUrlField(string key, string label, ref string value)
        {
            DrawWarningIfAny(key);
            value = EditorGUILayout.TextField(label, value);

            if (string.IsNullOrEmpty(value) || IsValidUrl(value))
                _warnings.Remove(key);
        }

        /// <summary>
        /// 绘制描述字段
        /// </summary>
        private void DrawDescriptionField()
        {
            DrawWarningIfAny("description");
            EditorGUILayout.LabelField("包描述 (Description) *");
            description = EditorGUILayout.TextArea(description, GUILayout.Height(50));

            if (!string.IsNullOrEmpty(description))
                _warnings.Remove("description");
        }

        /// <summary>
        /// 绘制作者信息
        /// </summary>
        private void DrawAuthorInfo()
        {
            DrawWarningIfAny("authorEmail");
            DrawWarningIfAny("authorUrl");

            authorToggleGroup = EditorGUILayout.BeginFoldoutHeaderGroup(authorToggleGroup, "作者信息 (Author)");
            if (authorToggleGroup)
            {
                EditorGUI.indentLevel += 1;
                author.name = EditorGUILayout.TextField("Name", author.name);
                author.email = EditorGUILayout.TextField("Email", author.email);
                author.url = EditorGUILayout.TextField("Url", author.url);
                EditorGUI.indentLevel -= 1;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            if (string.IsNullOrEmpty(author.email) || IsValidEmail(author.email))
                _warnings.Remove("authorEmail");

            if (string.IsNullOrEmpty(author.url) || IsValidUrl(author.url))
                _warnings.Remove("authorUrl");
        }

        /// <summary>
        /// 绘制警告信息
        /// </summary>
        private void DrawWarningIfAny(string key)
        {
            if (_warnings.TryGetValue(key, out var warning) && warning.show)
                EditorGUILayout.HelpBox(warning.message, MessageType.Warning);
        }

        #endregion

        #region 包验证

        /// <summary>
        /// 验证包信息
        /// </summary>
        private bool ValidatePackageInfo()
        {
            _warnings.Clear();

            if (TryValidateEmpty("packageName", packageName, "请输入包名")) return false;
            if (TryValidateEmpty("version", version, "请输入版本号")) return false;
            if (TryValidateEmpty("displayName", displayName, "请输入显示名称")) return false;
            if (TryValidateEmpty("description", description, "请输入包描述")) return false;
            if (TryValidateEmpty("unity", unity, "请输入 Unity 版本")) return false;
            if (TryValidateEmpty("unityRelease", unityRelease, "请输入 Unity 发行版")) return false;

            if (!string.IsNullOrEmpty(documentationUrl) && !IsValidUrl(documentationUrl))
                return SetWarningAndReturn("documentationUrl", "请输入有效的文档地址");

            if (!string.IsNullOrEmpty(changelogUrl) && !IsValidUrl(changelogUrl))
                return SetWarningAndReturn("changelogUrl", "请输入有效的更新日志地址");

            if (!string.IsNullOrEmpty(licensesUrl) && !IsValidUrl(licensesUrl))
                return SetWarningAndReturn("licensesUrl", "请输入有效的许可证地址");

            if (!string.IsNullOrEmpty(author.email) && !IsValidEmail(author.email))
                return SetWarningAndReturn("authorEmail", "请输入有效的邮箱地址");

            if (!string.IsNullOrEmpty(author.url) && !IsValidUrl(author.url))
                return SetWarningAndReturn("authorUrl", "请输入有效的网址");

            return true;
        }

        private bool TryValidateEmpty(string key, string value, string message)
        {
            if (string.IsNullOrEmpty(value))
            {
                _warnings[key] = new ValidationWarning { show = true, message = message };
                return true;
            }
            return false;
        }

        private bool SetWarningAndReturn(string key, string message)
        {
            _warnings[key] = new ValidationWarning { show = true, message = message };
            return false;
        }

        /// <summary>
        /// 网址验证
        /// </summary>
        public bool IsValidUrl(string url)
        {
            return !string.IsNullOrWhiteSpace(url) && UrlRegex.IsMatch(url);
        }

        /// <summary>
        /// 邮箱验证
        /// </summary>
        public bool IsValidEmail(string email)
        {
            return !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email);
        }

        /// <summary>
        /// 验证包路径
        /// </summary>
        private bool ValidatePackagePath(string selectPath)
        {
            if (string.IsNullOrEmpty(selectPath))
            {
                Debug.LogError("无效路径，请选择一个有效的文件夹。");
                return false;
            }

            if (File.Exists(Path.Combine(selectPath, "package.json")))
            {
                Debug.LogError("不允许在已有包内创建新包，请选择其他文件夹。");
                return false;
            }

            return true;
        }

        #endregion

        #region 创建包

        /// <summary>
        /// 创建一个新包
        /// </summary>
        private bool CreateNewPackage(string selectPath, string packageName, out string packagePath)
        {
            packagePath = Path.Combine(selectPath, packageName);
            if (Directory.Exists(packagePath))
            {
                Debug.LogError($"目录已存在：{packagePath}");
                return false;
            }

            Directory.CreateDirectory(packagePath);

            CreatePackageFolder(packagePath, packageName);
            CreatePackageFile(packagePath);
            CreateChangeLogFile(packagePath);
            CreateReadMeFile(packagePath);

            Debug.Log($"包创建成功：{packagePath}");

            if (!string.IsNullOrEmpty(packagePath))
            {
                Process.Start(new ProcessStartInfo(packagePath)
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized
                });
            }

            return true;
        }

        /// <summary>
        /// 创建 package.json 文件
        /// </summary>
        private void CreatePackageFile(string packagePath)
        {
            PackageConfigProcess.CreatePackageJson(packagePath, GetPackageJsonContent());
        }

        /// <summary>
        /// 获取包配置信息
        /// </summary>
        private PackageConfig GetPackageJsonContent()
        {
            var packageConfig = new PackageConfig
            {
                name = packageName,
                version = version,
                displayName = displayName,
                description = description,
                unity = unity,
                unityRelease = unityRelease,
                documentationUrl = documentationUrl,
                changelogUrl = changelogUrl,
                licensesUrl = licensesUrl,
                keywords = keywords,
                author = author,
                samples = samples
            };

            var dependenciesJObject = new JObject();
            foreach (var dependency in dependencies)
            {
                dependenciesJObject.Add(new JProperty(dependency.packageName, dependency.version));
            }
            packageConfig.dependencies = dependenciesJObject;

            return packageConfig;
        }

        /// <summary>
        /// 创建 CHANGELOG.md 文件
        /// </summary>
        private void CreateChangeLogFile(string packagePath)
        {
            if (!isCreateChangeLog) return;
            string changeLogPath = Path.Combine(packagePath, "CHANGELOG.md");
            File.WriteAllText(changeLogPath, "# Changelog\nAll notable changes to this package will be documented in this file.\n\n");
        }

        /// <summary>
        /// 创建 README.md 文件
        /// </summary>
        private void CreateReadMeFile(string packagePath)
        {
            if (!isCreateReadme) return;
            string readmePath = Path.Combine(packagePath, "README.md");
            File.WriteAllText(readmePath, $"# {Path.GetFileName(packagePath)}\n\n");
        }

        /// <summary>
        /// 创建包文件夹结构
        /// </summary>
        private void CreatePackageFolder(string packagePath, string packageName)
        {
            if (isCreateScriptsFolder)
            {
                string scriptsFolderPath = Path.Combine(packagePath, "Scripts");
                Directory.CreateDirectory(scriptsFolderPath);

                string editorFolderPath = Path.Combine(scriptsFolderPath, "Editor");
                string runtimeFolderPath = Path.Combine(scriptsFolderPath, "Runtime");
                Directory.CreateDirectory(editorFolderPath);
                Directory.CreateDirectory(runtimeFolderPath);

                string editorAsmdefPath = Path.Combine(editorFolderPath, $"{packageName}.Editor.asmdef");
                string runtimeAsmdefPath = Path.Combine(runtimeFolderPath, $"{packageName}.Runtime.asmdef");
                AsmdefConfigProcess.CreateAsmdefContent(editorAsmdefPath, true);
                AsmdefConfigProcess.CreateAsmdefContent(runtimeAsmdefPath, false);
            }

            if (isCreateTestsFolder)
            {
                string testsFolderPath = Path.Combine(packagePath, "Tests");
                Directory.CreateDirectory(testsFolderPath);

                string testsEditorFolderPath = Path.Combine(testsFolderPath, "Editor");
                string testsRuntimeFolderPath = Path.Combine(testsFolderPath, "Runtime");
                Directory.CreateDirectory(testsEditorFolderPath);
                Directory.CreateDirectory(testsRuntimeFolderPath);

                string testsEditorAsmdefPath = Path.Combine(testsEditorFolderPath, $"{packageName}.Editor.Tests.asmdef");
                string testsRuntimeAsmdefPath = Path.Combine(testsRuntimeFolderPath, $"{packageName}.Runtime.Tests.asmdef");
                AsmdefConfigProcess.CreateAsmdefContent(testsEditorAsmdefPath, true);
                AsmdefConfigProcess.CreateAsmdefContent(testsRuntimeAsmdefPath, false);
            }
        }

        #endregion

        #region 安装包

        /// <summary>
        /// 安装包到当前工程
        /// </summary>
        private void InstallPackage(string selectPath, string packagePath)
        {
            // 如果直接创建到了 Packages 文件夹中，Unity 会自动添加，无需手动安装
            if (File.Exists(Path.Combine(selectPath, "manifest.json"))) return;

            PackageInstaller.InstallPackageFromDisk(packagePath);
        }

        #endregion
    }
}
