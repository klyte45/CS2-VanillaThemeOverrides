using BridgeWE;
using Colossal;
using Colossal.Core;
using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Colossal.Logging;
using Colossal.OdinSerializer.Utilities;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.UI;
using Game.UI.Localization;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using VanillaThemeOverride.Systems;

namespace VanillaThemeOverride
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{typeof(Mod).Assembly.GetName().Name}.{nameof(Mod)}");
        private static readonly BindingFlags allFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly | BindingFlags.GetField | BindingFlags.GetProperty;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            MainThreadDispatcher.RegisterUpdater(DoWhenLoaded);
            MainThreadDispatcher.RegisterUpdater(LoadLocales);

            updateSystem.UpdateAt<EdgeExtraDataUpdater2B>(SystemUpdatePhase.Modification2B);
            updateSystem.UpdateAt<EdgeExtraDataUpdater>(SystemUpdatePhase.Rendering);
            updateSystem.World.CreateSystemManaged<AbbreviationManagementSystem>();

        }

        private void DoWhenLoaded()
        {
            log.Info($"Loading patches");
            if (DoPatches())
            {
                RegisterModFiles();
            }
        }

        private Dictionary<string, string> fileNames = [];
        private string currentSelection = "";

        private void RegisterModFiles()
        {
            GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset);
            var modDir = Path.GetDirectoryName(asset.path);

            var imagesDirectory = Path.Combine(modDir, "atlases");
            if (Directory.Exists(imagesDirectory))
            {
                var atlases = Directory.GetDirectories(imagesDirectory, "*", SearchOption.TopDirectoryOnly);
                foreach (var atlasFolder in atlases)
                {
                    WEImageManagementBridge.RegisterImageAtlas(typeof(Mod).Assembly, Path.GetFileName(atlasFolder), Directory.GetFiles(atlasFolder, "*.png"));
                }
            }

            var layoutsDirectory = Path.Combine(modDir, "layouts");
            WETemplatesManagementBridge.RegisterCustomTemplates(typeof(Mod).Assembly, layoutsDirectory);
            WETemplatesManagementBridge.RegisterLoadableTemplatesFolder(typeof(Mod).Assembly, layoutsDirectory);


            var fontsDirectory = Path.Combine(modDir, "fonts");
            WEFontManagementBridge.RegisterModFonts(typeof(Mod).Assembly, fontsDirectory);

            var objDirctory = Path.Combine(modDir, "objMeshes");

            if (Directory.Exists(objDirctory))
            {
                var meshes = Directory.GetFiles(objDirctory, "*.obj", SearchOption.AllDirectories);
                foreach (var meshFile in meshes)
                {
                    var meshName = Path.GetFileNameWithoutExtension(meshFile);
                    if (!WEMeshManagementBridge.RegisterMesh(typeof(Mod).Assembly, meshName, meshFile))
                    {
                        log.Warn($"Failed to register mesh: {meshName} from {meshFile}");
                    }
                }
            }


            WEModuleOptionsBridge.CreateBuilder(typeof(Mod).Assembly, "K45::we_vto.weOptions")
               .Section("Abbreviations")
               .Dropdown("FileToLoad", () => currentSelection, x =>
               {
                   currentSelection = x;
                   WEModuleOptionsBridge.ForceReloadOptions();
               }, () =>
               {
                   fileNames = Directory.GetFiles(AbbreviationManagementSystem.ABBREVIATIONS_FOLDER, "*.txt").ToDictionary(x => x, x => Path.GetFileNameWithoutExtension(x));
                   return fileNames;
               })
               .ButtonRow("__",
                   x =>
                   {
                       switch (x)
                       {
                           case "ImportFromFile":
                               log.Info("Loading settings from file");
                               if (fileNames.ContainsKey(currentSelection ?? ""))
                               {
                                   AbbreviationManagementSystem.Instance.LoadAbbreviations(currentSelection);
                               }
                               WEModuleOptionsBridge.ForceReloadOptions();
                               break;
                           case "ReloadFiles":
                               fileNames = Directory.GetFiles(AbbreviationManagementSystem.ABBREVIATIONS_FOLDER, "*.txt").ToDictionary(x => x, x => Path.GetFileNameWithoutExtension(x));
                               break;
                           case "ExportToFile":
                               log.Info("Saving settings to file");
                               var targetFilename = Path.Combine(AbbreviationManagementSystem.ABBREVIATIONS_FOLDER, "_exported-" + DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-F") + ".txt");
                               var content = AbbreviationManagementSystem.Instance.ToExportFile();
                               File.WriteAllText(targetFilename, content);

                               var dialog2 = new MessageDialog(
                                    LocalizedString.Id("K45::we_vto.abbreviationExportDialog.title"),
                                    LocalizedString.Id("K45::we_vto.abbreviationExportDialog.description"),
                                    LocalizedString.Value(targetFilename),
                                    true,
                                    LocalizedString.Id("Common.OK"),
                                    LocalizedString.Value("K45::we_vto.abbreviationExportDialog.openFolder")
                                    );
                               GameManager.instance.userInterface.appBindings.ShowMessageDialog(dialog2, (x) =>
                               {
                                   switch (x)
                                   {
                                       case 2:
                                           RemoteProcess.OpenFolder(Path.GetDirectoryName(targetFilename));
                                           break;
                                   }

                               });

                               WEModuleOptionsBridge.ForceReloadOptions();
                               break;
                       }
                   },
                   () =>
                   {
                       var result = new Dictionary<string, string>
                       {
                           ["ReloadFiles"] = "K45::we_vto.weOptions[ReloadFiles]",
                           ["ExportToFile"] = "K45::we_vto.weOptions[ExportToFile]"
                       };
                       if (fileNames.ContainsKey(currentSelection ?? ""))
                       {
                           result["ImportFromFile"] = "K45::we_vto.weOptions[ImportFromFile]";
                       }
                       return result;
                   })
               .Register();
        }

        private bool DoPatches()
        {
            var weAsset = AssetDatabase.global.GetAsset(SearchFilter<ExecutableAsset>.ByCondition(asset => asset.isLoaded && asset.name.Equals("BelzontWE")));
            if (weAsset?.assembly is null)
            {
                log.Error($"The module {GetType().Assembly.GetName().Name} requires Write Everywhere mod to work!");
                return false;

            }

            var exportedTypes = weAsset.assembly.ExportedTypes;
            foreach (var (type, sourceClassName) in new List<(Type, string)>() {
                    (typeof(WEFontManagementBridge), "FontManagementBridge"),
                    (typeof(WEImageManagementBridge), "ImageManagementBridge"),
                    (typeof(WETemplatesManagementBridge), "TemplatesManagementBridge"),
                    (typeof(WEMeshManagementBridge), "MeshManagementBridge"),
                    (typeof(WERoadFnBridge), "RoadFnBridge"),
                    (typeof(WELocalizationBridge), "LocalizationBridge"),
                    (typeof(WEModuleOptionsBridge), "ModuleOptionsBridge"),
                })
            {
                var targetType = exportedTypes.First(x => x.Name == sourceClassName);
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    var srcMethod = targetType.GetMethod(method.Name, allFlags, null, method.GetParameters().Select(x => x.ParameterType).ToArray(), null);
                    if (srcMethod != null) Harmony.ReversePatch(srcMethod, method);
                    else log.Warn($"Method not found while patching WE: {targetType.FullName} {srcMethod.Name}({string.Join(", ", method.GetParameters().Select(x => $"{x.ParameterType}"))})");
                }
            }
            return true;
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
        }

        #region Localization
        private static string m_modInstallFolder;
        public static string ModInstallFolder
        {
            get
            {
                if (m_modInstallFolder is null)
                {
                    var thisFullName = typeof(Mod).Assembly.FullName;
                    ExecutableAsset thisInfo = AssetDatabase.global.GetAsset(SearchFilter<ExecutableAsset>.ByCondition(x => x.definition?.FullName == thisFullName))
                        ?? throw new Exception("This mod info was not found!!!!");
                    m_modInstallFolder = Path.GetDirectoryName(thisInfo.GetMeta().path);

                    log.Info($"Mod location: {m_modInstallFolder}");
                }
                return m_modInstallFolder;
            }
        }
        internal string AdditionalI18nFilesFolder => Path.Combine(ModInstallFolder, $"i18n/");
        private Queue<(string, IDictionarySource)> previouslyLoadedDictionaries;
        internal void LoadLocales()
        {
            var file = Path.Combine(ModInstallFolder, $"i18n/i18n.csv");
            previouslyLoadedDictionaries ??= new();
            UnloadLocales();
            if (File.Exists(file))
            {
                var fileLines = File.ReadAllLines(file).Select(x => x.Split('\t'));
                var enColumn = Array.IndexOf(fileLines.First(), "en-US");
                var enMemoryFile = new MemorySource(LocaleFileForColumn(fileLines, enColumn));
                foreach (var lang in GameManager.instance.localizationManager.GetSupportedLocales())
                {
                    previouslyLoadedDictionaries.Enqueue((lang, enMemoryFile));
                    GameManager.instance.localizationManager.AddSource(lang, enMemoryFile);
                    if (lang != "en-US")
                    {
                        var valueColumn = Array.IndexOf(fileLines.First(), lang);
                        if (valueColumn > 0)
                        {
                            var i18nFile = new MemorySource(LocaleFileForColumn(fileLines, valueColumn));
                            previouslyLoadedDictionaries.Enqueue((lang, i18nFile));
                            GameManager.instance.localizationManager.AddSource(lang, i18nFile);
                        }
                        else if (File.Exists(Path.Combine(AdditionalI18nFilesFolder, lang + ".csv")))
                        {
                            var csvFileEntries = File.ReadAllLines(Path.Combine(AdditionalI18nFilesFolder, lang + ".csv")).Select(x => x.Split("\t")).ToDictionary(x => x[0], x => x.ElementAtOrDefault(1));
                            var i18nFile = new MemorySource(csvFileEntries);
                            previouslyLoadedDictionaries.Enqueue((lang, i18nFile));
                            GameManager.instance.localizationManager.AddSource(lang, i18nFile);
                        }
                    }
                }
            }
        }
        private static Dictionary<string, string> LocaleFileForColumn(IEnumerable<string[]> fileLines, int valueColumn)
        {
            return fileLines.Skip(1).GroupBy(x => x[0]).Select(x => x.First()).ToDictionary(x => ProcessKey(x[0], null), x => ReplaceSpecialChars(RemoveQuotes(x.ElementAtOrDefault(valueColumn) is string s && !s.IsNullOrWhitespace() ? s : x.ElementAtOrDefault(1))));
        }

        private static string ReplaceSpecialChars(string v)
        {
            return v.Replace("\\n", "\n").Replace("\\t", "\t");
        }

        private static string ProcessKey(string key, ModSetting modData)
        {
            if (!key.StartsWith("::") || modData is null) return key;
            if (key == "::M") return modData.GetBindingMapLocaleID();
            var prefix = key[..3];
            var suffix = key[3..];
            return prefix switch
            {
                "::L" => modData.GetOptionLabelLocaleID(suffix),
                "::G" => modData.GetOptionGroupLocaleID(suffix),
                "::D" => modData.GetOptionDescLocaleID(suffix),
                "::T" => modData.GetOptionTabLocaleID(suffix),
                "::W" => modData.GetOptionWarningLocaleID(suffix),
                //"::E" => suffix.Split(".", 2) is string[] enumVal && enumVal.Length == 2 ? modData.GetEnumValueLocaleID(enumVal[0], enumVal[1]) : suffix,
                "::B" => modData.GetBindingKeyLocaleID(suffix),
                "::H" => modData.GetBindingKeyHintLocaleID(suffix),
                _ => suffix
            };
        }
        private static string RemoveQuotes(string v) => v != null && v.StartsWith("\"") && v.EndsWith("\"") ? v[1..^1].Replace("\"\"", "\"") : v;
        private void UnloadLocales()
        {
            while (previouslyLoadedDictionaries.TryDequeue(out var src))
            {
                GameManager.instance.localizationManager.RemoveSource(src.Item1, src.Item2);
            }
        }
        #endregion
    }
}
