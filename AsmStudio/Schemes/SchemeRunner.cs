// SchemeRunner.cs - 方案执行器：按顺序跑完一个 .asmscheme 的所有步骤
// 占位符: {SymbolName} → 已解析符号地址（注入为 0x 十六进制文本）
using System;
using AsmStudio.Localization;
using System.Collections.Generic;
using System.Text;
using AsmStudio.Engine;
using AsmStudio.Remote;

namespace AsmStudio.Schemes
{
    /// <summary>单步执行记录（供日志输出）</summary>
    public sealed class StepLog
    {
        public int Index;
        public string Title;
        public bool Success;
        public string Detail;
        public ExecutionResult Result;
    }

    internal sealed class SchemeRunner
    {
        private readonly TargetSession _session;
        private readonly Action<string> _log;

        public SchemeRunner(TargetSession session, Action<string> log)
        {
            _session = session;
            _log = log ?? delegate(string s) { };
        }

        public List<StepLog> Run(SchemeFile scheme)
        {
            List<StepLog> logs = new List<StepLog>();
            for (int i = 0; i < scheme.Steps.Count; i++)
            {
                SchemeStep st = scheme.Steps[i];
                StepLog entry = new StepLog();
                entry.Index = i + 1;
                entry.Title = string.IsNullOrEmpty(st.Title) ? ("Step " + (i + 1)) : st.Title;
                try
                {
                    entry.Result = ExecuteStep(scheme, st);
                    entry.Success = entry.Result != null && entry.Result.Success;
                    entry.Detail = Describe(entry.Result);
                }
                catch (Exception ex)
                {
                    entry.Success = false;
                    entry.Detail = "Exception: " + ex.Message;
                }
                logs.Add(entry);
                _log(string.Format("[{0}/{1}] {2}: {3}",
                    entry.Index, scheme.Steps.Count,
                    entry.Success ? "OK " : "FAILED",
                    entry.Title));
                _log("    " + entry.Detail);
                if (!entry.Success)
                {
                    _log(LanguageService.T("Scheme aborted at step ") + entry.Index + " FAILED, stopping remaining steps.");
                    break;
                }
            }
            return logs;
        }

        private static string Describe(ExecutionResult r)
        {
            if (r == null) return "(no result)";
            StringBuilder sb = new StringBuilder();
            switch (r.Status)
            {
                case ExecStatus.Ok:
                    sb.Append("ret=0x").Append(r.RetValue.ToString("X"));
                    sb.Append("  rax=0x").Append(r.RegsFinal[RegIdx.Rax].ToString("X"));
                    break;
                case ExecStatus.Exception:
                    sb.Append("exception ").Append(r.ExceptionName);
                    sb.Append(" addr=0x").Append(r.ExcAddr.ToString("X"));
                    sb.Append(r.ExcWasAccessViolation
                        ? (r.ExcReadOrWrite == 0 ? LanguageService.T(" (read)") : (r.ExcReadOrWrite == 1 ? LanguageService.T(" (write)") : ""))
                        : "");
                    sb.Append("  [VEH recovered]");
                    break;
                case ExecStatus.Timeout:
                    sb.Append("timeout");
                    break;
                default:
                    sb.Append(r.Message);
                    break;
            }
            if (!string.IsNullOrEmpty(r.Message))
                sb.Append("  ").Append(r.Message);
            return sb.ToString();
        }

        /// <summary>执行单步：解析占位符 → 汇编 → 执行</summary>
        public ExecutionResult ExecuteStep(SchemeFile scheme, SchemeStep st)
        {
            // 语法/架构（步骤覆盖方案）
            SyntaxKind syntax = ParseSyntax(
                string.IsNullOrEmpty(st.Syntax) ? scheme.Syntax : st.Syntax);
            ArchKind arch = ParseArch(
                string.IsNullOrEmpty(st.Arch) ? scheme.Arch : st.Arch);

            // 符号表
            SymbolTable symbols = new SymbolTable();
            foreach (SymbolInfo s in _session.Symbols)
                symbols.Add(s.Name, s.Address);

            // call 目标地址解析
            ulong callAddr = 0;
            bool modeCall = st.Mode == "call" && !string.IsNullOrEmpty(st.Call);
            if (modeCall)
            {
                ulong symAddr;
                if (symbols.TryGet(st.Call, out symAddr))
                    callAddr = symAddr;
                else
                {
                    ulong parsed = 0;
                    string t = st.Call.Trim();
                    if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        ulong.TryParse(t.Substring(2), System.Globalization.NumberStyles.HexNumber,
                            null, out parsed);
                    else
                        ulong.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out parsed);
                    callAddr = parsed;
                }
            }

            // 参数解析（数字或符号）
            List<ulong> args = new List<ulong>();
            if (st.Args != null)
            {
                foreach (string a in st.Args)
                {
                    ulong v;
                    if (symbols.TryGet(a.Trim(), out v)) { args.Add(v); continue; }
                    string t = a.Trim();
                    bool hex = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                    ulong p;
                    bool ok = hex
                        ? ulong.TryParse(t.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out p)
                        : ulong.TryParse(t, out p);
                    args.Add(ok ? p : 0);
                }
            }

            // 占位符替换: {SymbolName} → 0x<addr>
            string source = ReplacePlaceholders(st.Code ?? "", symbols);

            // 汇编
            AssembleOptions opt = new AssembleOptions();
            opt.Syntax = syntax;
            opt.Arch = arch;
            opt.Symbols = symbols;
            opt.BaseAddress = 0;
            AssembleResult asm = AsmEngine.Assemble(source, opt);
            if (!asm.Success)
            {
                ExecutionResult err = new ExecutionResult();
                err.Status = ExecStatus.BadArg;
                err.Message = LanguageService.T("Assembly failed: ") +
                    (asm.Errors.Count > 0 ? asm.Errors[0].ToString() : LanguageService.T("unknown error"));
                return err;
            }

            // 执行
            return _session.Execute(asm.Code, args.ToArray(), args.Count,
                callAddr, modeCall, st.TimeoutMs);
        }

        private static string ReplacePlaceholders(string source, SymbolTable symbols)
        {
            if (source.IndexOf('{') < 0) return source;
            StringBuilder sb = new StringBuilder(source);
            foreach (KeyValuePair<string, ulong> kv in symbols.Items)
            {
                string token = "{" + kv.Key + "}";
                string rep = "0x" + kv.Value.ToString("x");
                int idx;
                while ((idx = sb.ToString().IndexOf(token, StringComparison.OrdinalIgnoreCase)) >= 0)
                    sb.Remove(idx, token.Length).Insert(idx, rep);
            }
            return sb.ToString();
        }

        internal static SyntaxKind ParseSyntax(string s)
        {
            return string.Equals(s, "att", StringComparison.OrdinalIgnoreCase)
                ? SyntaxKind.Att : SyntaxKind.Intel;
        }
        internal static ArchKind ParseArch(string s)
        {
            return string.Equals(s, "x86", StringComparison.OrdinalIgnoreCase)
                ? ArchKind.X86 : ArchKind.X64;
        }
    }
}
