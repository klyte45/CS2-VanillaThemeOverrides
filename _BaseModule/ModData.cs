using Colossal;
using Game.Modding;
using System.IO;
using UnityEngine;
using VanillaThemeOverride.Functions;

namespace VanillaThemeOverride
{
    public class ModData(IMod mod) : ModSetting(mod)
    {
        public bool GoToAbbreviationsFolder { set { RemoteProcess.OpenFolder(Path.GetDirectoryName(AbbreviationsFn.ABBREVIATIONS_FILE_LOCATION)); } }
        public bool CheckAbbreviationsFilesModels { set => Application.OpenURL("https://github.com/klyte45/CS2-VanillaThemeOverrides/blob/master/SampleFiles/Abbreviations"); }
        public override void SetDefaults()
        {
        }
    }
}
