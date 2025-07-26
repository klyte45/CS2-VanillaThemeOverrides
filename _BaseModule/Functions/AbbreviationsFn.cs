using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VanillaThemeOverride.Functions
{
    public static class AbbreviationsFn
    {
        private static readonly Dictionary<string, string> AbbreviatedNamesCache = [];
        private static readonly List<(Regex Matching, string Replacement)> LoadedAbbreviations = [];
        public static readonly string ABBREVIATIONS_FILE_LOCATION = Path.Combine(Application.persistentDataPath, "ModsData", "Klyte45Mods", "VanillaThemeOverriding", "abbreviations.txt");
        private static FileSystemWatcher AbbreviationsFileWatcher { get; set; }

        internal static void Initialize()
        {
            if (AbbreviationsFileWatcher == null)
            {
                if (!File.Exists(ABBREVIATIONS_FILE_LOCATION))
                {
                    EnsureFolderCreation(Path.GetDirectoryName(ABBREVIATIONS_FILE_LOCATION));
                    File.WriteAllText(ABBREVIATIONS_FILE_LOCATION, """
                    # Example:
                    # Street = St
                    # Remove the initial '#' to make the line effective!
                    # The entries accepts C# Regex features. See full docs for reference: 
                    # https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference
                    # Plus, starting an line with (?i) will make the regex case insensitive. Use backslash (\) to escape the "=" used inside the regexes
                    # Abbreviations files from Write Everywhere/Write the Signs for Cities Skylines 1 may work too:
                    # https://github.com/klyte45/WriteTheSignsFiles/tree/master/abbreviations
                    """);
                }
                AbbreviationsFileWatcher = new(Path.GetDirectoryName(ABBREVIATIONS_FILE_LOCATION), Path.GetFileName(ABBREVIATIONS_FILE_LOCATION))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };
                LoadAbbreviations();
                AbbreviationsFileWatcher.Changed += (sender, args) => LoadAbbreviations();
                AbbreviationsFileWatcher.EnableRaisingEvents = true;
            }
        }

        private static FileInfo EnsureFolderCreation(string folderName)
        {
            if (File.Exists(folderName) && (File.GetAttributes(folderName) & FileAttributes.Directory) != FileAttributes.Directory)
            {
                File.Delete(folderName);
            }
            if (!Directory.Exists(folderName))
            {
                Directory.CreateDirectory(folderName);
            }
            return new FileInfo(folderName);
        }

        private static void LoadAbbreviations()
        {
            LoadedAbbreviations.Clear();
            try
            {
                foreach (var line in File.ReadAllLines(ABBREVIATIONS_FILE_LOCATION))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;
                    var parts = line.Replace("\\=", "≠").Split('=', 2);
                    if (parts.Length == 2)
                    {
                        try
                        {
                            var effectiveRegex = parts[0].Replace("≠", "=").Trim();
                            var regexOptions = RegexOptions.CultureInvariant;
                            if (effectiveRegex.StartsWith("(?i)"))
                            {
                                regexOptions |= RegexOptions.IgnoreCase;
                                effectiveRegex = effectiveRegex[4..];
                            }
                            if (Regex.IsMatch(effectiveRegex, @"^[\p{L}\w 0-9]*$"))
                            {
                                effectiveRegex = $@"\b{effectiveRegex}\b";
                            }

                            LoadedAbbreviations.Add((new Regex(effectiveRegex, regexOptions, TimeSpan.FromMilliseconds(100)), parts[1].Replace("≠", "=").Trim()));
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Error processing line '{line}': {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading abbreviations: {ex.Message}");
            }
            AbbreviatedNamesCache.Clear();
        }

        public static string ApplyAbbreviations(string text)
        {
            if (AbbreviatedNamesCache.TryGetValue(text, out var cachedValue))
            {
                return cachedValue;
            }
            var replacementText = text;
            foreach (var kvp in LoadedAbbreviations)
            {
                var pattern = kvp.Matching;
                replacementText = kvp.Matching.Replace(
                    replacementText,
                    kvp.Replacement
                ).Trim();
            }
            return AbbreviatedNamesCache[text] = replacementText;
        }
    }
}
