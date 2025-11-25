using Colossal.Serialization.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Unity.Entities;
using UnityEngine;

#if BURST
using Unity.Burst;
#endif
namespace VanillaThemeOverride.Systems
{
    public partial class AbbreviationManagementSystem : SystemBase, IDefaultSerializable
    {
        public static AbbreviationManagementSystem Instance { get; private set; }
        public static readonly string ABBREVIATIONS_FOLDER = Path.Combine(Application.persistentDataPath, "ModsData", ".Klyte45Mods", "VanillaThemeOverride", "abbreviations");
        private const uint CURRENT_VERSION = 0;

        private readonly List<(string originalRegex, Regex Matching, string Replacement)> LoadedAbbreviations = [];
        private readonly Dictionary<string, string> AbbreviatedNamesCache = [];

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

        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            if (!Directory.Exists(ABBREVIATIONS_FOLDER))
            {
                EnsureFolderCreation(ABBREVIATIONS_FOLDER);
                File.WriteAllText(Path.Combine(ABBREVIATIONS_FOLDER, "_default.txt"), """
                    # Example:
                    # Street = St
                    # Remove the initial '#' to make the line effective!
                    # The entries accepts C# Regex features. See full docs for reference: 
                    # https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference
                    # Plus, starting an line with (?i) will make the regex case insensitive. Use backslash (\) to escape the "=" used inside the regexes
                    # Abbreviations files from Write Everywhere/Write the Signs for Cities Skylines 1 may work too:
                    # https://github.com/klyte45/WriteTheSignsFiles/tree/master/abbreviations
                    # 
                    # This file (_default.txt) will be loaded by default if no abbreviations are present on savegame. You may add other files to this folder and switch the currently used in a savegame at:
                    # Write Everywhere window > Prefab Layouts Setup tab > Vanilla Theme Override section > Options subtab
                    #
                    # You also may be able to export abbreviations that were registered inside the savegame at that location.
                    """);
            }
        }

        public void LoadAbbreviations(string file)
        {
            LoadedAbbreviations.Clear();
            try
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;
                    var parts = line.Replace("\\=", "≠").Split('=', 2);
                    if (parts.Length == 2)
                    {
                        try
                        {
                            var effectiveRegex = parts[0].Replace("≠", "=").Trim();
                            var replacement = parts[1].Replace("≠", "=").Trim();
                            AddRegex(effectiveRegex, replacement);
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

        private void AddRegex(string regex, string replacement)
        {
            var regexOptions = RegexOptions.CultureInvariant;
            var parsedRegex = regex;
            if (parsedRegex.StartsWith("(?i)"))
            {
                regexOptions |= RegexOptions.IgnoreCase;
                parsedRegex = parsedRegex[4..];
            }
            if (Regex.IsMatch(parsedRegex, @"^[\p{L}\w 0-9]*$"))
            {
                parsedRegex = $@"\b{parsedRegex}\b";
            }

            LoadedAbbreviations.Add((regex, new Regex(parsedRegex, regexOptions, TimeSpan.FromMilliseconds(100)), replacement));
        }

        public string ApplyAbbreviations(string text)
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

        public string ToExportFile()
        {
            using var sw = new StringWriter();
            foreach (var (originalRegex, _, replacement) in LoadedAbbreviations)
            {
                sw.WriteLine($"{originalRegex.Replace("=", "\\=")}={replacement.Replace("=", "\\=")}");
            }
            return sw.ToString();
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            LoadedAbbreviations.Clear();
            reader.Read(out uint currentVersion);
            if (currentVersion > CURRENT_VERSION)
            {
                throw new Exception($"Incompatible version {currentVersion} for AbbreviationStorageManagement (max supported {CURRENT_VERSION})");
            }
            reader.Read(out int count);
            for (int i = 0; i < count; i++)
            {
                reader.Read(out string originalRegex);
                reader.Read(out string replacement);
                AddRegex(originalRegex, replacement);
            }
        }
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CURRENT_VERSION);
            writer.Write(LoadedAbbreviations.Count);
            for (int i = 0; i < LoadedAbbreviations.Count; i++)
            {
                writer.Write(LoadedAbbreviations[i].originalRegex);
                writer.Write(LoadedAbbreviations[i].Replacement);
            }
        }

        public void SetDefaults(Context context)
        {
            LoadedAbbreviations.Clear();
            AbbreviatedNamesCache.Clear();
            var defaultFilePath = Path.Combine(ABBREVIATIONS_FOLDER, "_default.txt");
            if (File.Exists(defaultFilePath))
            {
                LoadAbbreviations(defaultFilePath);
            }
        }

        protected override void OnUpdate()
        {
        }
    }
}