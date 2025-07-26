﻿using BridgeWE;
using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using VanillaThemeOverride.Functions;
using VanillaThemeOverride.Systems;

namespace VanillaThemeOverride
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{typeof(Mod).Assembly.GetName().Name}.{nameof(Mod)}");
        private static readonly BindingFlags allFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly | BindingFlags.GetField | BindingFlags.GetProperty;

        private ModData modData;
        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));
            modData = new ModData(this);
            modData.RegisterInOptionsUI();

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            GameManager.instance.RegisterUpdater(DoWhenLoaded);
            GameManager.instance.RegisterUpdater(LoadLocales);

            updateSystem.UpdateAt<EdgeExtraDataUpdater2B>(SystemUpdatePhase.Modification2B);
            updateSystem.UpdateAt<EdgeExtraDataUpdater>(SystemUpdatePhase.Rendering);
        }

        private void DoWhenLoaded()
        {
            log.Info($"Loading patches");
            if (DoPatches())
            {
                RegisterModFiles();
                AbbreviationsFn.Initialize();
            }
        }

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
        }

        private bool DoPatches()
        {
            var weAsset = AssetDatabase.global.GetAsset(SearchFilter<ExecutableAsset>.ByCondition(asset => asset.isEnabled && asset.isLoaded && asset.name.Equals("BelzontWE")));
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

        internal void LoadLocales()
        {
            var baseModData = new ModGenI18n(modData);
            foreach (var lang in GameManager.instance.localizationManager.GetSupportedLocales())
            {
                GameManager.instance.localizationManager.AddSource(lang, baseModData);
            }
        }

        private class ModGenI18n(ModData data) : IDictionarySource
        {
            public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
            {
                return new Dictionary<string, string>
                {
                    [data.GetOptionLabelLocaleID(nameof(ModData.GoToAbbreviationsFolder))] = "Go to abbreviations folder",
                    [data.GetOptionDescLocaleID(nameof(ModData.GoToAbbreviationsFolder))] = "Change the abbreviation.txt file contents and it will be automatically loaded inside the game.\nGeneral instructions are available on first file created automatically at first mod run. Erase it and reset the game to get it again if necessary.",
                    [data.GetOptionLabelLocaleID(nameof(ModData.CheckAbbreviationsFilesModels))] = "Check model abbreviation files at github",
                    [data.GetOptionDescLocaleID(nameof(ModData.CheckAbbreviationsFilesModels))] = "See some pre mounted abbreviation files. Just copy their contents into your abbreviations.txt file and start using, it's automatically reloaded.",
                    [data.GetSettingsLocaleID()] = "Vanilla Theme Override",
                };
            }

            public void Unload()
            {
            }
        }
    }
}
