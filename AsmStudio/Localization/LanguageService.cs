// LanguageService.cs - 多区域语言服务（resx 资源 + 显式资源集切换，类 SharpDevelop）
//
// 资源：Properties\StringResources.resx（英文基准）
//       Properties\StringResources.zhCN.resx / .zhHant.resx（文化变体，键=英文原文）
// 文件名不用标准文化后缀（避免 GenerateResource 触发卫星程序集生成——本机无 SDK/AL.exe），
// 三个资源集全部内嵌主程序集，T(key) 按 CurrentCulture 显式选择资源集。
// 未命中返回键本身（源码即英文基准，键=英文原文 = 正确回退）。
// 语言选择持久化 exe\language.ini（en / zhCN / zhHant）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Text;
using System.Threading;

namespace AsmStudio.Localization
{
    public static class LanguageService
    {
        public sealed class LanguageDef
        {
            public readonly string Culture;      // "en" / "zh-CN" / "zh-Hant"
            public readonly string DisplayName;  // 菜单显示名（不参与翻译）
            public LanguageDef(string culture, string displayName)
            {
                Culture = culture;
                DisplayName = displayName;
            }
        }

        private static readonly ResourceManager _rmEn = new ResourceManager(
            "AsmStudio.StringResources", typeof(LanguageService).Assembly);
        private static readonly ResourceManager _rmZhCn = new ResourceManager(
            "AsmStudio.StringResources.zhCN", typeof(LanguageService).Assembly);
        private static readonly ResourceManager _rmZhHant = new ResourceManager(
            "AsmStudio.StringResources.zhHant", typeof(LanguageService).Assembly);

        private static readonly List<LanguageDef> _languages = new List<LanguageDef>
        {
            new LanguageDef("en", "English"),
            new LanguageDef("zhCN", "简体中文"),
        };

        /// <summary>当前语言代码（en / zhCN / zhHant）</summary>
        public static string CurrentCulture { get; private set; }

        static LanguageService()
        {
            CurrentCulture = "en";
        }

        public static IList<LanguageDef> Languages
        {
            get { return _languages; }
        }

        /// <summary>翻译查询：按当前语言选资源集，未命中回退键本身（英文基准）</summary>
        public static string T(string key)
        {
            if (key == null) return null;
            ResourceManager rm;
            switch (CurrentCulture)
            {
                case "zhCN": rm = _rmZhCn; break;
                case "zhHant": rm = _rmZhHant; break;
                default: rm = _rmEn; break;
            }
            string v = rm.GetString(key);
            return v ?? key;
        }

        /// <summary>切换语言（culture = "en" / "zhCN" / "zhHant"）</summary>
        public static void SetCulture(string culture)
        {
            CurrentCulture = string.IsNullOrWhiteSpace(culture) ? "en" : culture;
        }

        /// <summary>持久化语言选择</summary>
        public static void SaveChoice(string culture)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "language.ini"),
                    culture ?? "en", Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>读取持久化的语言选择（无文件/未知代码返回 en）</summary>
        public static string LoadChoice()
        {
            try
            {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "language.ini");
                if (File.Exists(p))
                {
                    string s = File.ReadAllText(p, Encoding.UTF8).Trim();
                    foreach (LanguageDef d in _languages)
                        if (d.Culture == s) return s;
                }
            }
            catch { }
            return "en";
        }
    }
}
