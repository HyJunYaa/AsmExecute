// Assembler.cs - 两遍汇编驱动器 + 标签解析
// 语义规则：前向分支引用一律 rel32（粘性）；后向引用优先 rel8（溢出时自动升级 rel32 重汇编）。
// 第 1 遍确定尺寸与标签偏移，第 2 遍产出最终字节（前向引用占位 → 真实值，尺寸不变）。
using System;
using System.Collections.Generic;

namespace AsmStudio.Engine
{
    internal static class Assembler
    {
        private const string Force32Sentinel = "__FORCE32__";
        private const int MaxPasses = 16;

        public static AssembleResult Assemble(string text, AssembleOptions options)
        {
            AssembleResult result = new AssembleResult();
            List<AsmError> errors = result.Errors;
            if (text == null) text = string.Empty;

            // ---- 解析所有行 → Statements ----
            List<Statement> stmts;
            if (!ParseAll(text, options, errors, out stmts))
            {
                result.Success = false;
                return result;
            }

            if (stmts.Count == 0)
            {
                result.Success = errors.Count == 0;
                if (result.Success) result.Code = new byte[0];
                return result;
            }

            // ---- 预收集全部标签名（前向引用判断用）----
            HashSet<string> allLabelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Statement st in stmts)
                if (st.LabelDef != null) allLabelNames.Add(st.LabelDef);

            HashSet<string> force32 = new HashSet<string>();
            Dictionary<string, ulong> labels = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            SymbolTable symbols = options.Symbols ?? new SymbolTable();
            List<byte> code = new List<byte>();

            // ---- 多遍汇编 ----
            // 关键：labels 字典跨遍保留（第 1 遍收集全部标签，第 2 遍起前向引用可取真实值）。
            bool aborted = false;
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                if (pass == 0) labels.Clear();
                code.Clear();
                bool restart = false;
                HashSet<string> definedThisPass = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                Encoder enc = new Encoder(options.Arch, options.BaseAddress, symbols,
                    labels, allLabelNames, force32, options.RetTrapAddress);
                for (int si = 0; si < stmts.Count; si++)
                {
                    Statement st = stmts[si];
                    if (st.LabelDef != null)
                    {
                        if (!definedThisPass.Add(st.LabelDef))
                        {
                            errors.Add(new AsmError(st.Line, st.Column,
                                "标签重复定义: '" + st.LabelDef + "'"));
                            result.Success = false;
                            return result;
                        }
                        labels[st.LabelDef] = (ulong)code.Count;
                    }
                    if (st.Mnemonic == null) continue;
                    try
                    {
                        byte[] bytes = enc.Encode(st, si, code.Count);
                        code.AddRange(bytes);
                    }
                    catch (EncodeException ex)
                    {
                        if (ex.Message == Force32Sentinel)
                        {
                            restart = true;     // 后向短跳溢出 → 强制 rel32 重跑
                            break;
                        }
                        errors.Add(new AsmError(ex.Line, ex.Column, ex.Message));
                        aborted = true;
                        break;
                    }
                }
                if (aborted) break;
                if (restart) continue;
                if (pass >= 1) break;       // 第 2 遍起即为稳定输出
            }

            if (aborted)
            {
                result.Success = false;
                return result;
            }
            if (code.Count > 0x4000000)
            {
                errors.Add(new AsmError(0, 0, "输出超过 64MB，疑似汇编不收敛"));
                result.Success = false;
                return result;
            }

            result.Success = errors.Count == 0;
            result.Code = code.ToArray();
            foreach (KeyValuePair<string, ulong> kv in labels) result.Labels[kv.Key] = kv.Value;
            return result;
        }

        /// <summary>解析全部行；任何解析错误都会记录并返回 false。</summary>
        private static bool ParseAll(string text, AssembleOptions options,
            List<AsmError> errors, out List<Statement> stmts)
        {
            stmts = new List<Statement>();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool ok = true;
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNo = i + 1;
                Statement st = new Statement();
                st.Line = lineNo;
                List<AsmError> lineErrors = new List<AsmError>();
                bool lineOk;
                if (options.Syntax == SyntaxKind.Intel)
                    lineOk = IntelParser.ParseLine(lines[i], lineNo, st, lineErrors);
                else
                    lineOk = AtAndTParser.ParseLine(lines[i], lineNo, st, lineErrors);
                if (!lineOk)
                {
                    errors.AddRange(lineErrors);
                    ok = false;
                    continue;
                }
                if (st.LabelDef == null && st.Mnemonic == null) continue;   // 空行
                stmts.Add(st);
            }
            return ok;
        }

        /// <summary>
        /// 校验模式：逐行独立汇编，收集所有行的错误（适合编辑器实时诊断）。
        /// 标签在整段范围内预先登记（避免"未定义标签"噪声），外部符号走真实符号表。
        /// </summary>
        public static List<AsmError> Validate(string text, AssembleOptions options)
        {
            List<AsmError> errors = new List<AsmError>();
            if (text == null) return errors;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            // 解析全部行；标签名收集到假表（偏移 0，让分支/数据引用成立）
            Dictionary<string, ulong> fakeLabels = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            List<Statement> parsed = new List<Statement>();
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNo = i + 1;
                Statement st = new Statement();
                st.Line = lineNo;
                bool ok;
                if (options.Syntax == SyntaxKind.Intel)
                    ok = IntelParser.ParseLine(lines[i], lineNo, st, errors);
                else
                    ok = AtAndTParser.ParseLine(lines[i], lineNo, st, errors);
                if (!ok) continue;
                if (st.LabelDef != null && !fakeLabels.ContainsKey(st.LabelDef))
                    fakeLabels[st.LabelDef] = 0;
                if (st.Mnemonic != null) parsed.Add(st);
            }

            foreach (Statement st in parsed)
            {
                // 每行独立编码；所有标签都"已定义"且偏移为 0 → 分支 rel 极小不会溢出
                Encoder enc = new Encoder(options.Arch, 0, options.Symbols, fakeLabels, null, null, 0);
                try
                {
                    enc.Encode(st, 0, 0);
                }
                catch (EncodeException ex)
                {
                    if (ex.Message == "__FORCE32__")
                        continue;
                    errors.Add(new AsmError(ex.Line, ex.Column, ex.Message));
                }
            }
            return errors;
        }
    }
}
