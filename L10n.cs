using System;
using System.IO;
using System.Linq;
using DV.Localization;
using DV.Shops;
using DVLangHelper.Runtime;
using I2.Loc;
using UnityEngine;

namespace DVCabooseBreak
{
    internal static class L10n
    {
        internal const string Prefix = "dv-caboose-break/";
        private static TranslationInjector translations;

        internal static string Key(string key) => Prefix + key;

        internal static void Initialize(string modPath)
        {
            translations = new TranslationInjector("dv-caboose-break");
            string csvPath = Path.Combine(modPath, "i18n", "translations.csv");
            if (!File.Exists(csvPath)) throw new FileNotFoundException("Missing translations CSV", csvPath);
            translations.AddTranslationsFromCsv(csvPath);
            if (!translations.Terms.Any()) throw new InvalidDataException("No translations loaded from " + csvPath);
            LocalizationManager.OnLocalizeEvent += RefreshShopLabels;
        }

        internal static string Get(string key, params string[] args)
        {
            if (LocalizationAPI.HasTranslation(Key(key))) return LocalizationAPI.L(Key(key), args);
            // Before the game's localization loader has injected language
            // sources, the settings panel can already be opened in UMM.
            // TranslationInjector initializes English as its first language.
            string fallback = translations?.Terms.FirstOrDefault(t => t.Term == Key(key))?.GetTranslation(0) ?? key;
            for (int i = 0; i < args.Length; i++) fallback = fallback.Replace("{[" + i + "]}", args[i]);
            return fallback;
        }

        private static void RefreshShopLabels()
        {
            foreach (var module in UnityEngine.Object.FindObjectsOfType<ScanItemCashRegisterModule>())
                if (module.sellingItemSpec != null && module.sellingItemSpec.ItemPrefabName == Main.PrefabId)
                    module.UpdateTexts();
        }
    }
}
