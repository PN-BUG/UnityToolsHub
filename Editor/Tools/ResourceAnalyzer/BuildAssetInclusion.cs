#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ResourceAnalyzer
{
    /// <summary>按当前构建入口追踪资源依赖，不依赖已有构建报告。</summary>
    internal static class BuildAssetInclusion
    {
        public static bool TryCollect(out HashSet<string> includedPaths, out string error)
        {
            includedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            error = null;
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                    roots.Add(scene.path);
            }

            foreach (var asset in PlayerSettings.GetPreloadedAssets())
            {
                if (asset == null) continue;
                AddAssetPath(roots, AssetDatabase.GetAssetPath(asset));
            }

            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                // Resources 下的资源即使没有场景引用，也会进入 Player。
                if ((path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                     path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) &&
                    path.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !IsEditorOnly(path))
                    AddAssetPath(roots, path);
            }

            foreach (var bundleName in AssetDatabase.GetAllAssetBundleNames())
            {
                foreach (var path in AssetDatabase.GetAssetPathsFromAssetBundle(bundleName))
                    AddAssetPath(roots, path);
            }

            if (!TryAddAddressableRoots(roots, out error)) return false;
            if (roots.Count == 0)
            {
                error = "没有找到启用场景、Resources 或已纳入构建的 Addressables 入口，无法判断哪些资源进入构建。";
                return false;
            }

            try
            {
                includedPaths = new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true),
                    StringComparer.OrdinalIgnoreCase);
                return true;
            }
            catch (Exception exception)
            {
                error = $"读取构建资源依赖失败：{exception.Message}";
                return false;
            }
        }

        private static bool TryAddAddressableRoots(HashSet<string> roots, out string error)
        {
            error = null;
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Unity.Addressables.Editor");
            if (assembly == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/AddressableAssetsData")) return true;
                error = "项目存在 Addressables 配置，但 Addressables Editor 未加载；无法安全筛除未打包资源。";
                return false;
            }

            try
            {
                var settingsType = assembly.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject", true);
                var settings = settingsType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)
                    .GetValue(null);
                if (settings == null) return true;

                foreach (var group in (IEnumerable)GetProperty(settings, "groups"))
                {
                    if (group == null) continue;
                    var schemas = (IEnumerable)GetProperty(group, "Schemas");
                    bool includeInBuild = false;
                    foreach (var schema in schemas)
                    {
                        if (schema == null || schema.GetType().Name != "BundledAssetGroupSchema") continue;
                        includeInBuild = (bool)GetProperty(schema, "IncludeInBuild");
                        break;
                    }
                    if (!includeInBuild) continue;

                    foreach (var entry in (IEnumerable)GetProperty(group, "entries"))
                    {
                        if (entry == null) continue;
                        string path = (string)GetProperty(entry, "AssetPath");
                        if (string.IsNullOrEmpty(path)) continue;

                        if ((bool)GetProperty(entry, "IsFolder"))
                        {
                            foreach (var guid in AssetDatabase.FindAssets("", new[] { path }))
                                AddAssetPath(roots, AssetDatabase.GUIDToAssetPath(guid));
                        }
                        else
                        {
                            AddAssetPath(roots, path);
                        }
                    }
                }
                return true;
            }
            catch (Exception exception)
            {
                error = $"读取 Addressables 构建分组失败：{exception.Message}";
                return false;
            }
        }

        private static object GetProperty(object target, string propertyName)
        {
            return target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                .GetValue(target);
        }

        private static void AddAssetPath(HashSet<string> roots, string path)
        {
            if (!string.IsNullOrEmpty(path) && !AssetDatabase.IsValidFolder(path) && !IsEditorOnly(path))
                roots.Add(path);
        }

        private static bool IsEditorOnly(string path)
        {
            return path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("/Editor Default Resources/", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
#endif
