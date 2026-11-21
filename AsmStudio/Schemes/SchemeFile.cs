// SchemeFile.cs - .asmscheme 方案文件解析（JSON，System.Web.Extensions）
// 方案 = 一组按顺序执行的步骤，每步可独立指定语法/架构/模式/参数。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AsmStudio.Schemes
{
    /// <summary>方案中的一个步骤</summary>
    public sealed class SchemeStep
    {
        public string Title = "";
        public string Mode = "code";        // code | call
        public string Call;                 // call 模式：目标符号名或十六进制地址
        public string[] Args = new string[0];      // 参数（十进制/0x十六进制/符号名）
        public string Code = "";            // 汇编源码（可含 {SymbolName} 占位）
        public string Syntax;               // intel | att（空=跟随全局）
        public string Arch;                 // x86 | x64（空=跟随全局）
        public int TimeoutMs = 3000;
    }

    /// <summary>方案文件</summary>
    public sealed class SchemeFile
    {
        public string Name = "";
        public string Description = "";
        public string Syntax = "intel";
        public string Arch = "x64";
        public List<SchemeStep> Steps = new List<SchemeStep>();
        public string FilePath;

        public static SchemeFile Load(string path)
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            string json = File.ReadAllText(path);
            Dictionary<string, object> root = ser.Deserialize<Dictionary<string, object>>(json);
            SchemeFile scheme = new SchemeFile();
            scheme.FilePath = path;

            object v;
            if (root.TryGetValue("name", out v)) scheme.Name = (string)v;
            if (root.TryGetValue("description", out v)) scheme.Description = (string)v;
            if (root.TryGetValue("syntax", out v)) scheme.Syntax = ((string)v).ToLowerInvariant();
            if (root.TryGetValue("arch", out v)) scheme.Arch = ((string)v).ToLowerInvariant();

            if (root.TryGetValue("steps", out v))
            {
                object[] steps = (object[])v;
                foreach (object so in steps)
                {
                    Dictionary<string, object> d = (Dictionary<string, object>)so;
                    SchemeStep st = new SchemeStep();
                    if (d.TryGetValue("title", out v)) st.Title = (string)v;
                    if (d.TryGetValue("mode", out v)) st.Mode = ((string)v).ToLowerInvariant();
                    if (d.TryGetValue("call", out v)) st.Call = (string)v;
                    if (d.TryGetValue("code", out v)) st.Code = (string)v;
                    if (d.TryGetValue("syntax", out v)) st.Syntax = ((string)v).ToLowerInvariant();
                    if (d.TryGetValue("arch", out v)) st.Arch = ((string)v).ToLowerInvariant();
                    if (d.TryGetValue("timeout_ms", out v))
                    {
                        int t;
                        if (int.TryParse(v.ToString(), out t)) st.TimeoutMs = t;
                    }
                    if (d.TryGetValue("args", out v))
                    {
                        object[] arr = (object[])v;
                        st.Args = new string[arr.Length];
                        for (int i = 0; i < arr.Length; i++) st.Args[i] = arr[i].ToString();
                    }
                    scheme.Steps.Add(st);
                }
            }
            return scheme;
        }

        /// <summary>扫描目录下全部方案文件</summary>
        public static List<SchemeFile> ScanDirectory(string dir)
        {
            List<SchemeFile> list = new List<SchemeFile>();
            if (!Directory.Exists(dir)) return list;
            foreach (string f in Directory.GetFiles(dir, "*.asmscheme"))
            {
                try
                {
                    SchemeFile s = Load(f);
                    if (string.IsNullOrEmpty(s.Name)) s.Name = Path.GetFileNameWithoutExtension(f);
                    list.Add(s);
                }
                catch (Exception)
                {
                    /* 跳过坏文件 */
                }
            }
            return list;
        }

        /// <summary>保存方案（JSON 缩进输出）</summary>
        public void Save(string path)
        {
            StringBuilder json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"name\": \"" + Escape(Name) + "\",");
            if (!string.IsNullOrEmpty(Description))
                json.AppendLine("  \"description\": \"" + Escape(Description) + "\",");
            json.AppendLine("  \"syntax\": \"" + Syntax + "\",");
            json.AppendLine("  \"arch\": \"" + Arch + "\",");
            json.AppendLine("  \"steps\": [");
            for (int i = 0; i < Steps.Count; i++)
            {
                SchemeStep st = Steps[i];
                json.AppendLine("    {");
                json.AppendLine("      \"title\": \"" + Escape(st.Title) + "\",");
                json.AppendLine("      \"mode\": \"" + st.Mode + "\",");
                if (!string.IsNullOrEmpty(st.Call))
                    json.AppendLine("      \"call\": \"" + Escape(st.Call) + "\",");
                if (st.Args != null && st.Args.Length > 0)
                {
                    List<string> arr = new List<string>();
                    foreach (string a in st.Args) arr.Add("\"" + Escape(a) + "\"");
                    json.AppendLine("      \"args\": [" + string.Join(", ", arr.ToArray()) + "],");
                }
                json.Append("      \"code\": \"");
                json.Append(Escape(st.Code.Replace("\r\n", "\n")));
                json.AppendLine("\",");
                json.AppendLine("      \"timeout_ms\": " + st.TimeoutMs);
                json.AppendLine("    }" + (i < Steps.Count - 1 ? "," : ""));
            }
            json.AppendLine("  ]");
            json.AppendLine("}");
            File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
            FilePath = path;
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "")
                    .Replace("\t", "\\t");
        }
    }
}
