using VanillaThemeOverride.Systems;

namespace VanillaThemeOverride.Functions
{
    public static class AbbreviationsFn
    {
        public static string ApplyAbbreviations(string text) => AbbreviationManagementSystem.Instance.ApplyAbbreviations(text);
    }
}
