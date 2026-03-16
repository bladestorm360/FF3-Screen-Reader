using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Field;
using Il2CppLast.Management;

namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Translates Japanese entity names to the current game language using an embedded translation resource.
    /// Uses a 2-tier lookup: exact → strip prefix, with language fallback to English.
    /// Detects language via MessageManager.Instance.currentLanguage.
    /// </summary>
    internal static class EntityTranslator
    {
        private static Dictionary<string, Dictionary<string, string>> translations;
        private static bool isInitialized = false;
        private static string cachedLanguageCode = "en";
        private static bool hasLoggedLanguage = false;
        private static HashSet<string> loggedMisses = new HashSet<string>();

        private static readonly Dictionary<int, string> LanguageCodeMap = new()
        {
            {1,"ja"},{2,"en"},{3,"fr"},{4,"it"},{5,"de"},{6,"es"},
            {7,"ko"},{8,"zht"},{9,"zhc"},{10,"ru"},{11,"th"},{12,"pt"}
        };

        // Track untranslated names by map for dumping
        private static Dictionary<string, HashSet<string>> untranslatedNamesByMap = new Dictionary<string, HashSet<string>>();

        // Matches numeric prefix (e.g., "6:") or SC prefix (e.g., "SC01:") at start of entity names
        private static readonly Regex EntityPrefixRegex = new Regex(
            @"^((?:SC)?\d+:)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Detects the current game language via MessageManager and returns a language code.
        /// Caches the result; defaults to "en" if MessageManager is unavailable.
        /// </summary>
        public static string DetectLanguage()
        {
            try
            {
                var mgr = MessageManager.Instance;
                if (mgr != null)
                {
                    int langId = (int)mgr.currentLanguage;
                    if (!hasLoggedLanguage)
                        MelonLogger.Msg($"[EntityTranslator] Raw langId from MessageManager: {langId}");

                    if (LanguageCodeMap.TryGetValue(langId, out string code))
                    {
                        cachedLanguageCode = code;
                        if (!hasLoggedLanguage)
                        {
                            MelonLogger.Msg($"[EntityTranslator] Detected language: {cachedLanguageCode}");
                            hasLoggedLanguage = true;
                        }
                    }
                    else if (!hasLoggedLanguage)
                    {
                        MelonLogger.Msg($"[EntityTranslator] langId {langId} not in map, keeping default: {cachedLanguageCode}");
                    }
                }
                else if (!hasLoggedLanguage)
                {
                    MelonLogger.Msg("[EntityTranslator] MessageManager.Instance is null, using default: " + cachedLanguageCode);
                }
            }
            catch (Exception ex)
            {
                if (!hasLoggedLanguage)
                    MelonLogger.Msg($"[EntityTranslator] DetectLanguage exception: {ex.Message}, using default: {cachedLanguageCode}");
            }
            return cachedLanguageCode;
        }

        /// <summary>
        /// Loads the embedded translation resource into the multi-language lookup dictionary.
        /// Format: { "japaneseName": { "en": "English", "fr": "French", ... }, ... }
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;

            translations = new Dictionary<string, Dictionary<string, string>>();

            try
            {
                using var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("translation.json");

                if (stream != null)
                {
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string json = reader.ReadToEnd();
                    MelonLogger.Msg($"[EntityTranslator] Embedded resource length: {json.Length} chars");

                    var data = ParseNestedJson(json);
                    MelonLogger.Msg($"[EntityTranslator] Parsed {data.Count} raw entries from embedded resource");

                    foreach (var entry in data)
                    {
                        // Only include entries where at least one language value is non-empty
                        bool hasValue = false;
                        foreach (var langEntry in entry.Value)
                        {
                            if (!string.IsNullOrEmpty(langEntry.Value))
                            {
                                hasValue = true;
                                break;
                            }
                        }
                        if (hasValue)
                            translations[entry.Key] = entry.Value;
                    }

                    MelonLogger.Msg($"[EntityTranslator] Loaded {translations.Count} translations from embedded resource");
                }
                else
                {
                    MelonLogger.Warning("[EntityTranslator] Embedded translation resource not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[EntityTranslator] Error loading translations: {ex.Message}");
            }

            isInitialized = true;
        }

        /// <summary>
        /// Translates a Japanese entity name to the current game language.
        /// Returns original name if no translation found.
        /// 2-tier lookup: exact → strip prefix, with language fallback to English.
        /// </summary>
        public static string Translate(string japaneseName)
        {
            if (string.IsNullOrEmpty(japaneseName))
                return japaneseName;

            if (!isInitialized)
                Initialize();

            if (translations.Count == 0)
                return japaneseName;

            // When game is in Japanese, entity names are already Japanese — no translation needed
            if (DetectLanguage() == "ja")
                return japaneseName;

            // 1. Exact match
            if (TryLookup(japaneseName, out string exactMatch))
                return exactMatch;

            // 2. Strip numeric/SC prefix and try base name lookup
            StripPrefix(japaneseName, out string prefix, out string baseName);
            if (prefix != null && TryLookup(baseName, out string baseTranslation))
                return prefix + " " + baseTranslation;

            // 3. Track untranslated name by current map (use base name to deduplicate)
            string trackingName = prefix != null ? baseName : japaneseName;
            if (ContainsJapanese(trackingName))
            {
                string mapName = MapNameResolver.GetCurrentMapName();
                if (!string.IsNullOrEmpty(mapName))
                {
                    if (!untranslatedNamesByMap.ContainsKey(mapName))
                        untranslatedNamesByMap[mapName] = new HashSet<string>();
                    untranslatedNamesByMap[mapName].Add(trackingName);
                }

                // Log untranslated entities (once per unique name)
                if (loggedMisses.Add(japaneseName))
                    MelonLogger.Msg($"[EntityTranslator] MISS: \"{japaneseName}\"");
            }

            // Return original if no translation
            return japaneseName;
        }

        /// <summary>
        /// Looks up a Japanese key in the translations dictionary for the current game language.
        /// Returns false if no translation found, so the caller can fall back to the original Japanese.
        /// </summary>
        private static bool TryLookup(string key, out string result)
        {
            result = null;
            if (!translations.TryGetValue(key, out var langDict))
                return false;
            string lang = DetectLanguage();
            if (langDict.TryGetValue(lang, out string localized) && !string.IsNullOrEmpty(localized))
            {
                result = localized;
                return true;
            }
            // Fallback to English if detected language wasn't found
            if (lang != "en" && langDict.TryGetValue("en", out string english) && !string.IsNullOrEmpty(english))
            {
                result = english;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a string contains Japanese characters (hiragana, katakana, or kanji).
        /// </summary>
        public static bool ContainsJapanese(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (char c in text)
            {
                // Hiragana: U+3040 - U+309F
                // Katakana: U+30A0 - U+30FF
                // Kanji: U+4E00 - U+9FFF (common CJK)
                if ((c >= '\u3040' && c <= '\u309F') ||  // Hiragana
                    (c >= '\u30A0' && c <= '\u30FF') ||  // Katakana
                    (c >= '\u4E00' && c <= '\u9FFF'))    // Common Kanji
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Strips a numeric or SC prefix from an entity name.
        /// Returns the prefix (e.g., "6:" or "SC01:") and the base name.
        /// If no prefix is found, prefix will be null and baseName will equal the input.
        /// </summary>
        private static void StripPrefix(string name, out string prefix, out string baseName)
        {
            Match match = EntityPrefixRegex.Match(name);
            if (match.Success)
            {
                prefix = match.Groups[1].Value;
                baseName = name.Substring(prefix.Length);
            }
            else
            {
                prefix = null;
                baseName = name;
            }
        }

        /// <summary>
        /// Dumps untranslated entity names for the current map to EntityNames.json.
        /// Appends by map name with duplicate detection.
        /// Returns a status string for TTS feedback.
        /// </summary>
        public static string DumpUntranslatedNames()
        {
            try
            {
                string currentMap = MapNameResolver.GetCurrentMapName();
                if (string.IsNullOrEmpty(currentMap))
                    return "Could not determine current map.";

                string gameDataPath = Application.dataPath;
                string gameRoot = Path.GetDirectoryName(gameDataPath);
                string userDataPath = Path.Combine(gameRoot, "UserData", "FFIII_ScreenReader");
                if (!Directory.Exists(userDataPath))
                    Directory.CreateDirectory(userDataPath);

                string dumpPath = Path.Combine(userDataPath, "EntityNames.json");

                // Load existing data from file
                var existingData = new Dictionary<string, Dictionary<string, string>>();
                if (File.Exists(dumpPath))
                {
                    string existingJson = File.ReadAllText(dumpPath);
                    existingData = ParseNestedJson(existingJson);
                }

                // Check if map already exists in file
                if (existingData.ContainsKey(currentMap))
                    return "Entity data already exists for this map.";

                // Check if we have untranslated names for this map
                if (!untranslatedNamesByMap.ContainsKey(currentMap) || untranslatedNamesByMap[currentMap].Count == 0)
                    return "No untranslated names for this map.";

                // Add current map's names to data
                var mapNames = new Dictionary<string, string>();
                foreach (string name in untranslatedNamesByMap[currentMap])
                {
                    mapNames[name] = "";
                }
                existingData[currentMap] = mapNames;

                // Write nested JSON
                WriteNestedJson(dumpPath, existingData);

                int count = mapNames.Count;
                return $"Dumped {count} names for {currentMap}";
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[EntityTranslator] Failed to save EntityNames.json: {ex.Message}");
                return "Failed to dump entity names.";
            }
        }

        /// <summary>
        /// Reloads translations from embedded resource.
        /// </summary>
        public static void Reload()
        {
            isInitialized = false;
            translations = null;
            Initialize();
        }

        /// <summary>
        /// Gets the count of loaded translations.
        /// </summary>
        public static int TranslationCount => translations?.Count ?? 0;

        // ─────────────────────────────────────────────
        //  JSON parsing — used for embedded translation data and dump files
        //  Format: { "outerKey": { "innerKey": "value", ... }, ... }
        // ─────────────────────────────────────────────

        /// <summary>
        /// Parses a two-level nested JSON dictionary: outerKey → (innerKey → value).
        /// Used for the embedded translation resource (jpName → {lang → translation})
        /// and for reading/writing EntityNames.json dump files.
        /// </summary>
        internal static Dictionary<string, Dictionary<string, string>> ParseNestedJson(string json)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            if (string.IsNullOrEmpty(json)) return result;

            json = json.Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}"))
                return result;

            // Strip outer braces
            string inner = json.Substring(1, json.Length - 2);

            int pos = 0;
            while (pos < inner.Length)
            {
                // Find next quoted key
                int keyStart = inner.IndexOf('"', pos);
                if (keyStart < 0) break;
                int keyEnd = FindClosingQuote(inner, keyStart + 1);
                if (keyEnd < 0) break;

                string outerKey = UnescapeJsonString(inner.Substring(keyStart + 1, keyEnd - keyStart - 1));

                // Find the opening brace for this key's entries
                int braceStart = inner.IndexOf('{', keyEnd);
                if (braceStart < 0) break;

                int braceEnd = FindMatchingBrace(inner, braceStart);
                if (braceEnd < 0) break;

                string innerJson = inner.Substring(braceStart + 1, braceEnd - braceStart - 1);
                result[outerKey] = ParseStringDictionary(innerJson);

                pos = braceEnd + 1;
            }

            return result;
        }

        /// <summary>
        /// Parses a flat JSON object of string→string pairs.
        /// </summary>
        private static Dictionary<string, string> ParseStringDictionary(string json)
        {
            var dict = new Dictionary<string, string>();
            int pos = 0;
            while (pos < json.Length)
            {
                int keyStart = json.IndexOf('"', pos);
                if (keyStart < 0) break;
                int keyEnd = FindClosingQuote(json, keyStart + 1);
                if (keyEnd < 0) break;

                string key = UnescapeJsonString(json.Substring(keyStart + 1, keyEnd - keyStart - 1));

                // Find colon
                int colonIdx = json.IndexOf(':', keyEnd);
                if (colonIdx < 0) break;

                // Find value (quoted string)
                int valStart = json.IndexOf('"', colonIdx);
                if (valStart < 0) break;
                int valEnd = FindClosingQuote(json, valStart + 1);
                if (valEnd < 0) break;

                string value = UnescapeJsonString(json.Substring(valStart + 1, valEnd - valStart - 1));
                dict[key] = value;

                pos = valEnd + 1;
            }
            return dict;
        }

        /// <summary>
        /// Finds the closing quote, handling escaped quotes.
        /// </summary>
        private static int FindClosingQuote(string s, int startAfterOpenQuote)
        {
            for (int i = startAfterOpenQuote; i < s.Length; i++)
            {
                if (s[i] == '\\') { i++; continue; } // skip escaped char
                if (s[i] == '"') return i;
            }
            return -1;
        }

        /// <summary>
        /// Finds the matching closing brace for an opening brace.
        /// </summary>
        private static int FindMatchingBrace(string s, int openBracePos)
        {
            int depth = 1;
            bool inString = false;
            for (int i = openBracePos + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && inString) { i++; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static string UnescapeJsonString(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    switch (next)
                    {
                        case '"': sb.Append('"'); i++; break;
                        case '\\': sb.Append('\\'); i++; break;
                        case 'n': sb.Append('\n'); i++; break;
                        case 'r': sb.Append('\r'); i++; break;
                        case 't': sb.Append('\t'); i++; break;
                        case '/': sb.Append('/'); i++; break;
                        default: sb.Append(s[i]); break;
                    }
                }
                else
                {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Writes a nested dictionary as formatted JSON to a file.
        /// </summary>
        private static void WriteNestedJson(string path, Dictionary<string, Dictionary<string, string>> data)
        {
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.WriteLine("{");
                var mapKeys = new List<string>(data.Keys);
                for (int m = 0; m < mapKeys.Count; m++)
                {
                    string mapKey = mapKeys[m];
                    string escapedMapKey = EscapeJsonString(mapKey);
                    writer.WriteLine($"  \"{escapedMapKey}\": {{");

                    var names = new List<string>(data[mapKey].Keys);
                    for (int n = 0; n < names.Count; n++)
                    {
                        string escapedName = EscapeJsonString(names[n]);
                        string escapedValue = EscapeJsonString(data[mapKey][names[n]]);
                        string comma = (n < names.Count - 1) ? "," : "";
                        writer.WriteLine($"    \"{escapedName}\": \"{escapedValue}\"{comma}");
                    }

                    string mapComma = (m < mapKeys.Count - 1) ? "," : "";
                    writer.WriteLine($"  }}{mapComma}");
                }
                writer.WriteLine("}");
            }
        }

        private static string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return str
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
