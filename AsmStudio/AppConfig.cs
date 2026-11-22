// AppConfig.cs - 应用配置存储（INI 节/键结构，可扩展）
//
// 文件：exe 同目录 AsmStudio.ini，UTF-8（无 BOM）。格式：
//
//   [General]
//   Language=zhCN
//
// 用法：
//   AppConfig.Load();                        // 启动时读入（幂等；缺文件/坏行静默跳过）
//   AppConfig.Get("General", "Language", "en");
//   AppConfig.SetInt("Ui", "FontSize", 12);
//   AppConfig.Set("General", "Language", "zhCN");
//   AppConfig.Save();                        // 全量写回（.tmp 交换，失败静默）
//
// 读取在未 Load 时自动补载；Set 之后必须 Save 才落盘。
// 旧版 language.ini（裸文本语言代码）会在首次 Load 时迁移并删除。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AsmStudio
{
    internal static class AppConfig
    {
        public static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AsmStudio.ini"); }
        }

        private static readonly object _lock = new object();
        private static bool _loaded;
        // 保留插入顺序（节序/键序），写回时与文件布局一致
        private static readonly List<string> _sectionOrder = new List<string>();
        private static readonly Dictionary<string, Dictionary<string, string>> _sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<string>> _keyOrder =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>读入配置文件（幂等）。缺文件/坏行静默；顺带迁移旧 language.ini。</summary>
        public static void Load()
        {
            lock (_lock)
            {
                if (_loaded) return;
                _loaded = true;
                try
                {
                    if (File.Exists(FilePath))
                    {
                        string section = "";
                        foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                        {
                            string line = raw.Trim();
                            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                                continue;
                            if (line.StartsWith("[") && line.EndsWith("]"))
                            {
                                section = line.Substring(1, line.Length - 2).Trim();
                                EnsureSection(section);
                                continue;
                            }
                            int eq = line.IndexOf('=');
                            if (eq <= 0) continue;                       // 无键/无值的行跳过
                            string k = line.Substring(0, eq).Trim();
                            string v = line.Substring(eq + 1).Trim();
                            if (section.Length == 0) section = "General"; // 节前的散键归入 General
                            EnsureSection(section);
                            SetRaw(section, k, v);
                        }
                    }
                    MigrateLegacyLanguageIni();
                }
                catch { }   // 配置读不到按默认值走，不影响启动
            }
        }

        /// <summary>迁移旧版 language.ini（裸文本语言代码）→ [General] Language，成功后删除旧文件。</summary>
        private static void MigrateLegacyLanguageIni()
        {
            try
            {
                string legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "language.ini");
                if (!File.Exists(legacy)) return;
                string v = File.ReadAllText(legacy, Encoding.UTF8).Trim();
                if (v.Length > 0)
                {
                    EnsureSection("General");
                    SetRaw("General", "Language", v);
                    Save();
                }
                File.Delete(legacy);
            }
            catch { }
        }

        public static string Get(string section, string key, string fallback)
        {
            lock (_lock)
            {
                Load();
                Dictionary<string, string> sec;
                if (_sections.TryGetValue(section, out sec))
                {
                    string v;
                    if (sec.TryGetValue(key, out v)) return v;
                }
                return fallback;
            }
        }

        public static int GetInt(string section, string key, int fallback)
        {
            int v;
            string s = Get(section, key, null);
            return (s != null && int.TryParse(s, out v)) ? v : fallback;
        }

        public static bool GetBool(string section, string key, bool fallback)
        {
            bool v;
            string s = Get(section, key, null);
            return (s != null && bool.TryParse(s, out v)) ? v : fallback;
        }

        /// <summary>写入内存（不落盘；随后调用 Save）。value 为 null 视为删除该键。</summary>
        public static void Set(string section, string key, string value)
        {
            lock (_lock)
            {
                Load();
                if (value == null)
                {
                    Dictionary<string, string> sec;
                    List<string> order;
                    if (_sections.TryGetValue(section, out sec) && sec.Remove(key) &&
                        _keyOrder.TryGetValue(section, out order))
                        order.Remove(key);
                    return;
                }
                EnsureSection(section);
                SetRaw(section, key, value);
            }
        }

        public static void SetInt(string section, string key, int value)
        {
            Set(section, key, value.ToString());
        }

        public static void SetBool(string section, string key, bool value)
        {
            Set(section, key, value ? "true" : "false");
        }

        /// <summary>全量写回磁盘（.tmp 交换，失败静默——配置写不进不影响主功能）。</summary>
        public static void Save()
        {
            lock (_lock)
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (string section in _sectionOrder)
                    {
                        sb.Append('[').Append(section).Append(']').AppendLine();
                        List<string> order;
                        Dictionary<string, string> sec = _sections[section];
                        if (_keyOrder.TryGetValue(section, out order))
                        {
                            foreach (string k in order)
                                sb.Append(k).Append('=').Append(sec[k]).AppendLine();
                        }
                        sb.AppendLine();
                    }
                    string tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                    if (File.Exists(FilePath)) File.Delete(FilePath);
                    File.Move(tmp, FilePath);
                }
                catch { }
            }
        }

        private static void EnsureSection(string section)
        {
            if (!_sections.ContainsKey(section))
            {
                _sections[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _keyOrder[section] = new List<string>();
                _sectionOrder.Add(section);
            }
        }

        private static void SetRaw(string section, string key, string value)
        {
            Dictionary<string, string> sec = _sections[section];
            if (!sec.ContainsKey(key)) _keyOrder[section].Add(key);
            sec[key] = value;
        }
    }
}
