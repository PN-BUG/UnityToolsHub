using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityToolsHub.JoystickIcons;

public static class PlatformStringReplace
{
    private const string ResourceName = "PlatformStringReplaceDatabase";

    private sealed class RuntimeSpriteAsset
    {
        public TMP_SpriteAsset Asset;
        public string CharacterName;
    }

    private readonly struct RuntimeSpriteAssetKey : IEquatable<RuntimeSpriteAssetKey>
    {
        public readonly int SpriteInstanceId;
        public readonly float Scale;

        public RuntimeSpriteAssetKey(int spriteInstanceId, float scale)
        {
            SpriteInstanceId = spriteInstanceId;
            Scale = scale;
        }

        public bool Equals(RuntimeSpriteAssetKey other)
        {
            return SpriteInstanceId == other.SpriteInstanceId && Scale.Equals(other.Scale);
        }

        public override bool Equals(object obj)
        {
            return obj is RuntimeSpriteAssetKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (SpriteInstanceId * 397) ^ Scale.GetHashCode();
            }
        }
    }

    private static readonly Dictionary<RuntimeSpriteAssetKey, RuntimeSpriteAsset> RuntimeSpriteAssets =
        new Dictionary<RuntimeSpriteAssetKey, RuntimeSpriteAsset>();
    private static readonly HashSet<RuntimeSpriteAssetKey> FailedRuntimeSpriteAssets =
        new HashSet<RuntimeSpriteAssetKey>();
    private static readonly Dictionary<int, TMP_SpriteAsset> OriginalTargetSpriteAssets =
        new Dictionary<int, TMP_SpriteAsset>();
    private static readonly HashSet<TMP_SpriteAsset> ExternalSpriteAssets = new HashSet<TMP_SpriteAsset>();
    private static readonly FieldInfo TmpAssetVersionField = typeof(TMP_Asset).GetField(
        "m_Version",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static PlatformStringReplaceDatabase cachedDatabase;
    private static PlatformStringReplaceDatabase databaseOverride;
    private static bool warnedMissingDatabase;

    public static PlatformStringReplaceDatabase Database
    {
        get
        {
            if (databaseOverride != null)
            {
                return databaseOverride;
            }

            if (cachedDatabase == null)
            {
                cachedDatabase = Resources.Load<PlatformStringReplaceDatabase>(ResourceName);
            }

            if (cachedDatabase == null && !warnedMissingDatabase)
            {
                warnedMissingDatabase = true;
                Debug.LogWarning($"PlatformStringReplace: Resources/{ResourceName}.asset 不存在，未执行占位符替换。");
            }

            return cachedDatabase;
        }
    }

    /// <summary>Optional runtime/test override. Assign null to return to the Resources asset.</summary>
    public static void SetDatabaseOverride(PlatformStringReplaceDatabase database)
    {
        databaseOverride = database;
    }

    public static Dictionary<string, string> GetReplaceDict(bool hasJoystick)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!TryResolveProfile(hasJoystick, out _, out var profile))
        {
            return result;
        }

        for (var i = 0; i < profile.Replacements.Count; i++)
        {
            var replacement = profile.Replacements[i];
            if (replacement != null && !string.IsNullOrEmpty(replacement.Token))
            {
                result[replacement.Token] = replacement.Text ?? string.Empty;
            }
        }

        return result;
    }

    public static string Replace(string str, bool hasJoystick)
    {
        return ReplaceText(str, hasJoystick, null);
    }

    public static string Replace(string str, bool hasJoystick, Color color)
    {
        return Replace(str, hasJoystick, ColorUtility.ToHtmlStringRGBA(color));
    }

    public static string Replace(string str, bool hasJoystick, string color)
    {
        return ReplaceText(str, hasJoystick, color);
    }

    /// <summary>
    /// Replaces placeholders for a TMP target. Joystick profiles prefer the configured
    /// button sprites and fall back to their text values when an icon is unavailable.
    /// </summary>
    public static string Replace(TMP_Text target, string str, bool hasJoystick)
    {
        return ReplaceTmp(target, str, hasJoystick, null);
    }

    public static string Replace(TMP_Text target, string str, bool hasJoystick, Color color)
    {
        return Replace(target, str, hasJoystick, ColorUtility.ToHtmlStringRGBA(color));
    }

    public static string Replace(TMP_Text target, string str, bool hasJoystick, string color)
    {
        return ReplaceTmp(target, str, hasJoystick, color);
    }

    public static PlatformInputDeviceType GetActiveInputDeviceType(bool hasJoystick)
    {
        return ResolveInputDeviceType(hasJoystick, Database, out _);
    }

    private static string ReplaceText(string source, bool hasJoystick, string color)
    {
        if (string.IsNullOrEmpty(source) || !TryResolveProfile(hasJoystick, out _, out var profile))
        {
            return source;
        }

        for (var i = 0; i < profile.Replacements.Count; i++)
        {
            var item = profile.Replacements[i];
            if (item == null || string.IsNullOrEmpty(item.Token))
            {
                continue;
            }

            var replacement = ApplyColor(item.Text ?? string.Empty, color);
            source = ReplaceTokenWithLatinSpacing(source, item.Token, replacement);
        }

        return source;
    }

    private static string ReplaceTmp(TMP_Text target, string source, bool hasJoystick, string color)
    {
        if (target == null)
        {
            return ReplaceText(source, hasJoystick, color);
        }

        if (string.IsNullOrEmpty(source) ||
            !TryResolveProfile(hasJoystick, out var joystickType, out var profile))
        {
            RestoreOriginalSpriteAsset(target);
            return source;
        }

        var usedAssets = new List<TMP_SpriteAsset>();
        for (var i = 0; i < profile.Replacements.Count; i++)
        {
            var item = profile.Replacements[i];
            if (item == null || string.IsNullOrEmpty(item.Token))
            {
                continue;
            }

            var replacement = TryBuildTmpIconReplacement(item, joystickType, usedAssets, out var iconValue)
                ? iconValue
                : item.Text ?? string.Empty;
            source = ReplaceTokenWithLatinSpacing(source, item.Token, ApplyColor(replacement, color));
        }

        ConfigureTmpSpriteAssets(target, usedAssets);
        return source;
    }

    private static bool TryBuildTmpIconReplacement(
        PlatformStringReplacement replacement,
        JoystickDeviceType joystickType,
        List<TMP_SpriteAsset> usedAssets,
        out string value)
    {
        value = string.Empty;

        if (replacement.TmpIconSprite != null)
        {
            var directRuntimeAsset = GetOrCreateRuntimeSpriteAsset(
                replacement.TmpIconSprite,
                replacement.TmpIconScale);
            if (directRuntimeAsset == null)
            {
                return false;
            }

            if (!usedAssets.Contains(directRuntimeAsset.Asset))
            {
                usedAssets.Add(directRuntimeAsset.Asset);
            }

            value = (replacement.TmpIconPrefix ?? string.Empty) +
                    $"<sprite name=\"{directRuntimeAsset.CharacterName}\">" +
                    (replacement.TmpIconSuffix ?? string.Empty);
            return true;
        }

        var database = Database;
        var iconDatabase = database != null ? database.JoystickIconDatabase : null;
        if (iconDatabase == null || replacement.TmpIconButtonIds.Count == 0)
        {
            return false;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < replacement.TmpIconButtonIds.Count; i++)
        {
            var buttonId = replacement.TmpIconButtonIds[i];
            if (!iconDatabase.TryGetIcon(joystickType, buttonId, out var sprite) || sprite == null)
            {
                return false;
            }

            var runtimeAsset = GetOrCreateRuntimeSpriteAsset(sprite, 1f);
            if (runtimeAsset == null)
            {
                return false;
            }

            if (i > 0)
            {
                builder.Append(replacement.TmpIconSeparator);
            }

            builder.Append("<sprite name=\"").Append(runtimeAsset.CharacterName).Append("\">");
            if (!usedAssets.Contains(runtimeAsset.Asset))
            {
                usedAssets.Add(runtimeAsset.Asset);
            }
        }

        value = builder.ToString();
        return builder.Length > 0;
    }

    private static RuntimeSpriteAsset GetOrCreateRuntimeSpriteAsset(Sprite sprite, float scale)
    {
        var instanceId = sprite.GetInstanceID();
        scale = scale > 0f ? scale : 1f;
        var cacheKey = new RuntimeSpriteAssetKey(instanceId, scale);
        if (RuntimeSpriteAssets.TryGetValue(cacheKey, out var cached) && cached.Asset != null)
        {
            return cached;
        }

        if (FailedRuntimeSpriteAssets.Contains(cacheKey))
        {
            return null;
        }

        var shader = Shader.Find("TextMeshPro/Sprite");
        if (shader == null || sprite.texture == null)
        {
            return null;
        }

        var characterName = $"UTH_Joystick_{instanceId}_{scale.GetHashCode()}";
        TMP_SpriteAsset spriteAsset = null;
        Material material = null;
        try
        {
            spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            spriteAsset.name = characterName;
            spriteAsset.hideFlags = HideFlags.HideAndDontSave;
            spriteAsset.spriteSheet = sprite.texture;
            spriteAsset.hashCode = TMP_TextUtilities.GetSimpleHashCode(spriteAsset.name);

            // TMP 4 / UGUI 2 treats a material-backed asset without a version as a
            // legacy sprite asset and tries to upgrade the null legacy spriteInfoList.
            // Runtime-created assets already use the current glyph/character tables.
            if (TmpAssetVersionField == null)
            {
                throw new MissingFieldException(typeof(TMP_Asset).FullName, "m_Version");
            }

            TmpAssetVersionField.SetValue(spriteAsset, "1.1.0");

            var rect = sprite.textureRect;
            var glyph = new TMP_SpriteGlyph(
                0,
                // TMP lays inline sprites out from the text baseline. Unity UI sprites normally
                // use a centred pivot, which would shift this quad half a glyph left and down.
                new GlyphMetrics(rect.width, rect.height, 0f, rect.height * 0.9f, rect.width),
                new GlyphRect(rect),
                1f,
                0,
                sprite);
            var character = new TMP_SpriteCharacter(0xFFFE, spriteAsset, glyph)
            {
                name = characterName,
                scale = scale
            };
            spriteAsset.spriteGlyphTable.Add(glyph);
            spriteAsset.spriteCharacterTable.Add(character);
            spriteAsset.UpdateLookupTables();

            material = new Material(shader)
            {
                name = characterName + " Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetTexture("_MainTex", sprite.texture);
            spriteAsset.material = material;

            var result = new RuntimeSpriteAsset
            {
                Asset = spriteAsset,
                CharacterName = characterName
            };
            RuntimeSpriteAssets[cacheKey] = result;
            RefreshRuntimeFallbacks();
            return result;
        }
        catch (Exception exception)
        {
            FailedRuntimeSpriteAssets.Add(cacheKey);
            Debug.LogWarning(
                $"PlatformStringReplace: 无法为图标 '{sprite.name}' 创建 TMP SpriteAsset，已回退为文字。{exception.Message}",
                sprite);

            if (material != null)
            {
                UnityEngine.Object.Destroy(material);
            }

            if (spriteAsset != null)
            {
                UnityEngine.Object.Destroy(spriteAsset);
            }

            return null;
        }
    }

    private static void ConfigureTmpSpriteAssets(TMP_Text target, List<TMP_SpriteAsset> usedAssets)
    {
        var targetId = target.GetInstanceID();
        if (!OriginalTargetSpriteAssets.ContainsKey(targetId))
        {
            OriginalTargetSpriteAssets[targetId] = target.spriteAsset;
            if (target.spriteAsset != null)
            {
                ExternalSpriteAssets.Add(target.spriteAsset);
            }
        }

        if (usedAssets.Count == 0)
        {
            RestoreOriginalSpriteAsset(target);
            return;
        }

        RefreshRuntimeFallbacks();
        target.richText = true;
        target.spriteAsset = usedAssets[0];
    }

    private static void RestoreOriginalSpriteAsset(TMP_Text target)
    {
        if (target != null && OriginalTargetSpriteAssets.TryGetValue(target.GetInstanceID(), out var original))
        {
            target.spriteAsset = original;
        }
    }

    private static void RefreshRuntimeFallbacks()
    {
        foreach (var pair in RuntimeSpriteAssets)
        {
            var asset = pair.Value.Asset;
            if (asset == null)
            {
                continue;
            }

            if (asset.fallbackSpriteAssets == null)
            {
                asset.fallbackSpriteAssets = new List<TMP_SpriteAsset>();
            }
            else
            {
                asset.fallbackSpriteAssets.Clear();
            }

            foreach (var other in RuntimeSpriteAssets)
            {
                if (other.Value.Asset != null && other.Value.Asset != asset)
                {
                    asset.fallbackSpriteAssets.Add(other.Value.Asset);
                }
            }

            foreach (var external in ExternalSpriteAssets)
            {
                if (external != null && external != asset && !asset.fallbackSpriteAssets.Contains(external))
                {
                    asset.fallbackSpriteAssets.Add(external);
                }
            }
        }
    }

    private static bool TryResolveProfile(
        bool hasJoystick,
        out JoystickDeviceType joystickType,
        out PlatformStringReplacementProfile profile)
    {
        var database = Database;
        joystickType = JoystickDeviceType.Generic;
        profile = null;
        if (database == null)
        {
            return false;
        }

        var inputDeviceType = ResolveInputDeviceType(hasJoystick, database, out joystickType);
        if (database.TryGetProfile(inputDeviceType, out profile))
        {
            return true;
        }

        return inputDeviceType != PlatformInputDeviceType.GenericJoystick &&
               IsJoystick(inputDeviceType) &&
               database.TryGetProfile(PlatformInputDeviceType.GenericJoystick, out profile);
    }

    private static PlatformInputDeviceType ResolveInputDeviceType(
        bool hasJoystick,
        PlatformStringReplaceDatabase database,
        out JoystickDeviceType joystickType)
    {
        joystickType = JoystickDeviceType.Generic;
        hasJoystick = JoystickIconDeviceEvents.LastInputWasJoystick ?? hasJoystick;

#if UNITY_PS4 || UNITY_PS5
        joystickType = JoystickDeviceType.PlayStation;
        return PlatformInputDeviceType.PlayStation;
#elif UNITY_XBOXONE || UNITY_GAMECORE
        joystickType = JoystickDeviceType.Xbox;
        return PlatformInputDeviceType.Xbox;
#elif UNITY_ANDROID || UNITY_IOS
        if (!hasJoystick)
        {
            return PlatformInputDeviceType.Mobile;
        }
#else
        if (!hasJoystick)
        {
            return PlatformInputDeviceType.KeyboardMouse;
        }
#endif

        var iconDatabase = database != null ? database.JoystickIconDatabase : null;
        joystickType = iconDatabase != null
            ? iconDatabase.IdentifyDevice(ResolveActiveJoystickName())
            : JoystickDeviceType.Generic;
        return ToInputDeviceType(joystickType);
    }

    private static string ResolveActiveJoystickName()
    {
        if (!string.IsNullOrWhiteSpace(JoystickIconDeviceEvents.ActiveDeviceName))
        {
            return JoystickIconDeviceEvents.ActiveDeviceName;
        }

        try
        {
            var names = Input.GetJoystickNames();
            if (names != null)
            {
                for (var i = 0; i < names.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(names[i]))
                    {
                        return names[i].Trim();
                    }
                }
            }
        }
        catch (Exception)
        {
            // Some platforms do not expose legacy joystick names. The generic profile is safe.
        }

        return string.Empty;
    }

    private static PlatformInputDeviceType ToInputDeviceType(JoystickDeviceType joystickType)
    {
        switch (joystickType)
        {
            case JoystickDeviceType.Xbox: return PlatformInputDeviceType.Xbox;
            case JoystickDeviceType.PlayStation: return PlatformInputDeviceType.PlayStation;
            case JoystickDeviceType.NintendoSwitch: return PlatformInputDeviceType.NintendoSwitch;
            case JoystickDeviceType.SteamDeck: return PlatformInputDeviceType.SteamDeck;
            default: return PlatformInputDeviceType.GenericJoystick;
        }
    }

    private static bool IsJoystick(PlatformInputDeviceType type)
    {
        return type == PlatformInputDeviceType.Xbox ||
               type == PlatformInputDeviceType.PlayStation ||
               type == PlatformInputDeviceType.NintendoSwitch ||
               type == PlatformInputDeviceType.SteamDeck ||
               type == PlatformInputDeviceType.GenericJoystick;
    }

    private static string ApplyColor(string value, string color)
    {
        return string.IsNullOrWhiteSpace(color) ? value : $"<color={color}>{value}</color>";
    }

    private static string ReplaceTokenWithLatinSpacing(string source, string token, string replacement)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(token))
        {
            return source;
        }

        var firstIndex = source.IndexOf(token, StringComparison.Ordinal);
        if (firstIndex < 0)
        {
            return source;
        }

        var result = new StringBuilder(source.Length + replacement.Length);
        var sourceIndex = 0;
        var matchIndex = firstIndex;
        while (matchIndex >= 0)
        {
            result.Append(source, sourceIndex, matchIndex - sourceIndex);

            var tokenEnd = matchIndex + token.Length;
            if (matchIndex > 0 && IsLatinWordCharacter(source[matchIndex - 1]))
            {
                result.Append(' ');
            }

            result.Append(replacement);

            if (tokenEnd < source.Length && IsLatinWordCharacter(source[tokenEnd]))
            {
                result.Append(' ');
            }

            sourceIndex = tokenEnd;
            matchIndex = source.IndexOf(token, sourceIndex, StringComparison.Ordinal);
        }

        result.Append(source, sourceIndex, source.Length - sourceIndex);
        return result.ToString();
    }

    private static bool IsLatinWordCharacter(char value)
    {
        return value <= 0x7F && (char.IsLetterOrDigit(value) || value == '_');
    }
}
