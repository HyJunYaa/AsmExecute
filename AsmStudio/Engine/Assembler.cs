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
            bool converged = false;
            /* 收敛判据是"连续两遍产出完全相同"而不是"某遍没有 restart"：
             * restart 那一遍的 labels 停在 break 点之前（偏移按旧尺寸算），
             * 下一遍的前向引用会拿到这份过期值——长度仍然稳定、也不触发 restart，
             * 但 rel32 位移会整整差一个补丁量（静默跳到错误地址）。
             * 只有字节级稳定才说明尺寸与位移都定了。 */
            byte[] prevBytes = null;
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
                                "Duplicate label definition: '" + st.LabelDef + "'"));
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
                byte[] cur = code.ToArray();
                bool stable = prevBytes != null && BytesEqual(prevBytes, cur);
                prevBytes = cur;
                if (stable && pass >= 1) { converged = true; break; }
            }

            if (aborted)
            {
                result.Success = false;
                return result;
            }
            /* 轮次用尽仍在 restart：code 是某一遍被 break 截断的半成品，绝不能当成功结果返回
             * （旧实现会静默产出"少了尾巴"的机器码）。这里明确失败并丢弃字节。 */
            if (!converged)
            {
                errors.Add(new AsmError(0, 0,
                    "Assembly did not converge after " + MaxPasses + " passes (branch sizing never stabilised); output discarded"));
                result.Success = false;
                return result;
            }
            if (code.Count > 0x4000000)
            {
                errors.Add(new AsmError(0, 0, "Output exceeds 64MB — assembly probably not converging"));
                result.Success = false;
                return result;
            }

            result.Success = errors.Count == 0;
            result.Code = code.ToArray();
            foreach (KeyValuePair<string, ulong> kv in labels) result.Labels[kv.Key] = kv.Value;
            return result;
        }

        /// <summary>两遍输出逐字节相同 = 尺寸与位移都已稳定</summary>
        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>解析全部行；任何解析Error都会记录并返回 false。</summary>
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
        /// 校验模式：逐行独立汇编，收集所有行的Error（适合编辑器实时诊断）。
        /// 标签在整段范围内预先登记（避免"undefined label"噪声），外部符号走真实符号表。
        /// </summary>
        public static List<AsmError> Validate(string text, AssembleOptions options)
        {
            List<AsmError> errors = new List<AsmError>();
            if (text == null) return errors;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            // 解析全部行；标签名收集到假表（偏移 0，让分支/data引用成立）
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
                // 每行独立编码；所有标签都"already defined"且偏移为 0 → 分支 rel 极小不会溢出
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
