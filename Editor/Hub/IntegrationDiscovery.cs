#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UpmPackageInfo = UnityEditor.PackageManager.PackageInfo;

public partial class UnityToolsHub
{
    private const string SdkAttributeName = "UnityToolsHub.SDK.UnityToolAttribute";
    private const string HubPackageName = "com.zko.unitytoolshub";

    // The Hub intentionally discovers the optional SDK by name. This avoids a
    // compile-time dependency in either direction and keeps integrated tools standalone.
    private static IEnumerable<ToolEntry> DiscoverSdkTools()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception) { types = exception.Types; }
            catch { continue; }

            foreach (var type in types)
            {
                if (type == null) continue;
                object[] attributes;
                try { attributes = type.GetCustomAttributes(false); }
                catch { continue; }
                foreach (var attribute in attributes)
                {
                    if (attribute.GetType().FullName != SdkAttributeName) continue;
                    string category = ReadSdkString(attribute, "Category") ?? "Third-party Tools";
                    string kind = ReadSdkString(attribute, "EntryKind");
                    if (string.IsNullOrEmpty(kind))
                        kind = typeof(EditorWindow).IsAssignableFrom(type) ? "window" : "static";

                    yield return new ToolEntry
                    {
                        name = ReadSdkString(attribute, "Name") ?? type.Name,
                        category = category,
                        originalCategory = category,
                        description = ReadSdkString(attribute, "Description") ?? "",
                        icon = ReadSdkString(attribute, "Icon") ?? "Tool",
                        tags = ReadSdkStringArray(attribute, "Tags"),
                        priority = ReadSdkInt(attribute, "Priority"),
                        author = ReadSdkString(attribute, "Author") ?? "",
                        authorLink = ReadSdkString(attribute, "AuthorLink") ?? "",
                        typeName = type.FullName,
                        isThirdParty = true,
                        entryKind = kind.ToLowerInvariant(),
                        menuItem = ReadSdkString(attribute, "MenuItem"),
                        staticMethod = ReadSdkString(attribute, "StaticMethod")
                    };
                }
            }
        }
    }

    private static object ReadSdkMember(object attribute, string name)
    {
        var type = attribute.GetType();
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property != null) return property.GetValue(attribute, null);
        return type.GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(attribute);
    }

    private static string ReadSdkString(object attribute, string name) => ReadSdkMember(attribute, name) as string;
    private static string[] ReadSdkStringArray(object attribute, string name) => ReadSdkMember(attribute, name) as string[];
    private static int ReadSdkInt(object attribute, string name) => ReadSdkMember(attribute, name) is int value ? value : 0;

    // Registers package windows as external recipes; no source file is modified.
    private void RegisterPackageWindows(UpmPackageInfo package)
    {
        if (package == null || string.IsNullOrEmpty(package.name)) return;
        string root = "Packages/" + package.name;
        foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            var type = script != null ? script.GetClass() : null;
            if (type == null || type.IsAbstract || !typeof(EditorWindow).IsAssignableFrom(type)) continue;
            if (_thirdPartyRegistry.Find(type.FullName) != null) continue;

            _thirdPartyRegistry.AddOrUpdate(new ThirdPartyToolState
            {
                typeName = type.FullName,
                toolName = ObjectNames.NicifyVariableName(type.Name),
                category = "Third-party Tools",
                description = "Automatically discovered from " + package.displayName,
                scriptPath = path,
                isEnabled = false,
                importSource = "git",
                gitUrl = package.packageId,
                packagePath = package.name,
                packageName = package.name,
                installPath = package.resolvedPath,
                isInstalled = true,
                entryKind = "window"
            });
        }
        SaveThirdPartyRegistry();
    }

    // Package installation can trigger an assembly reload before delayCall runs.
    // Reconcile known Git recipes on every Hub enable to make discovery durable.
    private void RefreshInstalledPackageRecipes()
    {
        UpmPackageInfo[] packages;
        try { packages = UpmPackageInfo.GetAllRegisteredPackages(); }
        catch { return; }

        foreach (var package in packages)
        {
            if (package == null || package.name == HubPackageName) continue;
            bool isInstalledGitPackage = package.source == UnityEditor.PackageManager.PackageSource.Git;
            bool isKnown = false;
            foreach (var state in _thirdPartyRegistry.tools)
            {
                if (state.importSource != "git" && state.importSource != "local") continue;
                if ((!string.IsNullOrEmpty(state.packageName) && state.packageName == package.name)
                    || (!string.IsNullOrEmpty(state.gitUrl) && !string.IsNullOrEmpty(package.packageId)
                        && package.packageId.IndexOf(state.gitUrl, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    isKnown = true;
                    break;
                }
            }
            // Existing Git dependencies from Packages/manifest.json are discovered
            // even when they were installed before UnityToolsHub.
            if (isInstalledGitPackage || isKnown) RegisterPackageWindows(package);
        }
    }
}
#endif
