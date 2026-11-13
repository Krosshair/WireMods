using System;

namespace WiresNotificationOverhaul
{
    // Use Valheim's currently selected language rather than providing
    // English-only replacements for non-English notification strings.
    internal static class CompactLocalization
    {
        internal static bool IsEnglish
        {
            get
            {
                Localization language = Localization.instance;
                return language == null ||
                    string.Equals(language.GetSelectedLanguage(),
                        "English", StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static string NativeToken(string token, string fallback)
        {
            Localization language = Localization.instance;
            if (language == null || string.IsNullOrWhiteSpace(token))
                return fallback;
            string result = language.Localize(token);
            if (string.IsNullOrWhiteSpace(result) ||
                string.Equals(result, token, StringComparison.Ordinal) ||
                result.IndexOf('$') >= 0)
                return fallback;
            return result.Trim();
        }
    }
}
