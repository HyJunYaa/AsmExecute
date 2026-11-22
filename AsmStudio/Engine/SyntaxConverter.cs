// SyntaxConverter.cs - Intel(NASM) ↔ AT&T(GAS) 源码级语法转换
//
// 转换范围（尽力而为，无法识别的行原样保留）：
//   - 两/三操作数指令的操作数反序（AT&T 源在前）
//   - 寄存器 % 前缀、立即数 $ 前缀、间接 call/jmp 的 *
//   - memory [base+index*scale+disp] ↔ disp(base,index,scale)；[rip+X] ↔ X(%rip)
//   - 尺寸后缀 b/w/l/q（由寄存器或 byte/word/dword/qword ptr 推断）
//   - movzx/movsx → movz??/movs??（movzbl/movslq/...）
//   - 段前缀 fs:[x] ↔ %fs:x；Intel ';' 注释 ↔ AT&T '#' 注释
//   - 标签/伪指令行原样保留
using System;
using System.Collections.Generic;
using System.Text;

namespace AsmStudio.Engine
{
    public static class SyntaxConverter
    {
        public static string Convert(string text, SyntaxKind from, SyntaxKind to)
        {
            if (text == null) return text;
            if (from == to) return text;
            if (from == SyntaxKind.Intel && to == SyntaxKind.Att)
                return ConvertLines(text, true);
            if (from == SyntaxKind.Att && to == SyntaxKind.Intel)
                return ConvertLines(text, false);
            return text;
        }

        // ================= 行级分发 =================

        private static string ConvertLines(string text, bool toAtt)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            StringBuilder sb = new StringBuilder(text.Length + 64);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(ConvertLine(lines[i], toAtt));
            }
            return sb.ToString();
        }

        private static string ConvertLine(string line, bool toAtt)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0) return line;

            // 拆注释
            string body = trimmed;
            string comment = "";
            int cPos = FindComment(body, toAtt);
            if (cPos >= 0)
            {
                comment = body.Substring(cPos).Trim();
                body = body.Substring(0, cPos).Trim();
            }

            // 标签Row / 伪指令Row / 纯注释：原样保留
            if (body.Length == 0 || body.EndsWith(":") ||
                body.StartsWith(".") || body.StartsWith("#"))
                return line;

            // 拆助记符（可能带 lock/rep 等前缀）
            List<string> prefixes = new List<string>();
            string mnem;
            string rest;
            SplitPrefixAndMnem(body, prefixes, out mnem, out rest);
            if (mnem.Length == 0) return line;

            // 分割操作数（尊重 []）
            List<string> ops = SplitOperands(rest);

            string newMnem;
            List<string> newOps;
            string err;
            if (!ConvertInstruction(mnem, ops, toAtt, out newMnem, out newOps, out err))
                return line;   // 无法识别的行原样保留

            StringBuilder sb = new StringBuilder();
            foreach (string p in prefixes) { sb.Append(p); sb.Append(' '); }
            sb.Append(newMnem);
            string opSep = toAtt ? "  " : " ";   // AT&T 惯例双空格对齐；Intel 单空格
            for (int i = 0; i < newOps.Count; i++)
            {
                sb.Append(i == 0 ? opSep : ", ");
                sb.Append(newOps[i]);
            }
            if (comment.Length > 0)
            {
                sb.Append("    ");
                if (toAtt && comment.StartsWith(";")) comment = "#" + comment.Substring(1);
                else if (!toAtt && comment.StartsWith("#")) comment = ";" + comment.Substring(1);
                sb.Append(comment);
            }
            // 保留行首缩进
            int indent = line.Length - line.TrimStart().Length;
            if (indent > 0) sb.Insert(0, line.Substring(0, indent));
            return sb.ToString();
        }

        private static int FindComment(string body, bool toAtt)
        {
            // Intel 用 ';'，AT&T 用 '#'（';' 在 GAS 中也是注释符，一并识别）
            int a = body.IndexOf(';');
            int b = body.IndexOf('#');
            if (a < 0) return b;
            if (b < 0) return a;
            return Math.Min(a, b);
        }

        private static void SplitPrefixAndMnem(string body, List<string> prefixes,
            out string mnem, out string rest)
        {
            mnem = ""; rest = "";
            int i = 0;
            while (true)
            {
                int sp = body.IndexOf(' ', i);
                if (sp < 0) break;
                string word = body.Substring(i, sp - i).ToLowerInvariant();
                if (word == "lock" || word == "rep" || word == "repe" || word == "repz" ||
                    word == "repne" || word == "repnz" || word == "notrack" || word == "bnd")
                {
                    prefixes.Add(word);
                    i = sp + 1;
                    while (i < body.Length && body[i] == ' ') i++;
                    continue;
                }
                break;
            }
            if (i >= body.Length) return;
            int sp2 = body.IndexOf(' ', i);
            if (sp2 < 0)
            {
                mnem = body.Substring(i).ToLowerInvariant();
                rest = "";
            }
            else
            {
                mnem = body.Substring(i, sp2 - i).ToLowerInvariant();
                rest = body.Substring(sp2 + 1).Trim();
            }
        }

        private static List<string> SplitOperands(string rest)
        {
            List<string> ops = new List<string>();
            if (rest.Length == 0) return ops;
            int depth = 0;
            int start = 0;
            for (int i = 0; i < rest.Length; i++)
            {
                char c = rest[i];
                if (c == '[' || c == '(') depth++;
                else if (c == ']' || c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    ops.Add(rest.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            ops.Add(rest.Substring(start).Trim());
            return ops;
        }

        // ================= 指令转换 =================

        private static bool ConvertInstruction(string mnem, List<string> ops, bool toAtt,
            out string newMnem, out List<string> newOps, out string err)
        {
            newMnem = null; newOps = null; err = null;
            string baseMnem = mnem;
            string sizeSuffix = "";   // 'b'/'w'/'l'/'q' 或空

            if (toAtt)
            {
                // Intel 助记符 → AT&T：补尺寸后缀
                if (mnem == "movzx" || mnem == "movsx")
                {
                    if (ops.Count != 2) { err = "movzx/movsx needs 2 operands"; return false; }
                    int dstW = OperandRegWidth(ops[0]);
                    int srcW = OperandSrcSize(ops[1]);
                    if (dstW <= 0 || srcW <= 0) { err = "Cannot infer movzx/movsx width"; return false; }
                    char sl = SizeLetter(srcW);
                    char dl = SizeLetter(dstW);
                    newMnem = (mnem == "movzx" ? "movz" : "movs") + sl.ToString() + dl.ToString();
                    newOps = new List<string>();
                    newOps.Add(ConvertOperandIntelToAtt(ops[1], false));
                    newOps.Add(ConvertOperandIntelToAtt(ops[0], false));
                    return true;
                }
                if (mnem == "movsxd")
                {
                    // Intel movsxd r64, r/m32 → AT&T movslq
                    newMnem = "movslq";
                    newOps = new List<string>();
                    newOps.Add(ConvertOperandIntelToAtt(ops.Count > 1 ? ops[1] : "", false));
                    newOps.Add(ConvertOperandIntelToAtt(ops[0], false));
                    return true;
                }
                // 尺寸后缀推断：优先操作数寄存器宽度，其次 ptr 声明
                int width = 0;
                foreach (string op in ops)
                {
                    int w = OperandRegWidth(op);
                    if (w > width) width = w;
                }
                if (width == 0)
                {
                    foreach (string op in ops)
                    {
                        int w = OperandPtrWidth(op);
                        if (w > width) width = w;
                    }
                }
                if (width > 0 && NeedsSizeSuffix(mnem))
                    sizeSuffix = SizeLetter(width).ToString();

                newMnem = baseMnem + sizeSuffix;
                newOps = new List<string>();
                foreach (string op in ops)
                    newOps.Add(ConvertOperandIntelToAtt(op, IsIndirectBranch(mnem)));
                // 操作数反序（2/3 操作数；单操作数不变）
                if (newOps.Count == 2) { string t = newOps[0]; newOps[0] = newOps[1]; newOps[1] = t; }
                else if (newOps.Count == 3)
                {
                    // imul dst, src1, src2 → src1, src2, dst
                    string dst = newOps[0];
                    newOps.RemoveAt(0);
                    newOps.Add(dst);
                }
                return true;
            }
            else
            {
                // AT&T → Intel
                if (mnem.StartsWith("movz") && mnem.Length >= 5)
                {
                    if (ops.Count != 2) { err = "movz?? needs 2 operands"; return false; }
                    char srcL = mnem[4];
                    string ptr = LetterToPtr(srcL);
                    newMnem = "movzx";
                    newOps = new List<string>();
                    // ptr 修饰 src（AT&T 尺寸字母描述源），反转后 src 在第二位
                    newOps.Add(ptr + " " + ConvertOperandAttToIntel(ops[0], false));
                    newOps.Add(ConvertOperandAttToIntel(ops[1], false));
                    string t = newOps[0]; newOps[0] = newOps[1]; newOps[1] = t;
                    return true;
                }
                if (mnem.StartsWith("movs") && mnem.Length >= 5 && mnem != "movsb" && mnem != "movsw" && mnem != "movsd")
                {
                    if (ops.Count != 2) { err = "movs?? needs 2 operands"; return false; }
                    char srcL = mnem[4];
                    string ptr = LetterToPtr(srcL);
                    newMnem = (srcL == 'l' && ops[1].Contains("%r")) ? "movsxd" : "movsx";
                    newOps = new List<string>();
                    newOps.Add(ptr + " " + ConvertOperandAttToIntel(ops[0], false));
                    newOps.Add(ConvertOperandAttToIntel(ops[1], false));
                    string t = newOps[0]; newOps[0] = newOps[1]; newOps[1] = t;
                    return true;
                }
                // 尺寸后缀剥离：剥掉尾字母后须仍是已知助记符（防 call→cal / shl→sh 误剥）
                string srcL2 = "";
                if (mnem.Length > 2)
                {
                    char last = mnem[mnem.Length - 1];
                    if ((last == 'b' || last == 'w' || last == 'l' || last == 'q') &&
                        InstructionLexicon.IsMnemonicKeyword(
                            mnem.Substring(0, mnem.Length - 1)))
                    {
                        srcL2 = last.ToString();
                        baseMnem = mnem.Substring(0, mnem.Length - 1);
                    }
                }
                newMnem = baseMnem;
                newOps = new List<string>();
                foreach (string op in ops)
                    newOps.Add(ConvertOperandAttToIntel(op, IsIndirectBranch(baseMnem)));
                if (newOps.Count == 2) { string t = newOps[0]; newOps[0] = newOps[1]; newOps[1] = t; }
                else if (newOps.Count == 3)
                {
                    string dst = newOps[newOps.Count - 1];
                    newOps.RemoveAt(newOps.Count - 1);
                    newOps.Insert(0, dst);
                }
                // 有后缀信息 → 当其它操作数均无data寄存器（尺寸不可推断）时给memory操作数补 ptr
                if (srcL2.Length > 0)
                {
                    for (int i = 0; i < newOps.Count; i++)
                    {
                        if (!newOps[i].StartsWith("[")) continue;
                        bool otherHasReg = false;
                        for (int j = 0; j < newOps.Count; j++)
                        {
                            if (j == i) continue;
                            string o = newOps[j].Trim();
                            if (o.StartsWith("[")) continue;
                            if (o.StartsWith("$")) o = o.Substring(1);
                            if (IsDataRegister(o)) { otherHasReg = true; break; }
                        }
                        if (!otherHasReg)
                        {
                            newOps[i] = LetterToPtr(srcL2[0]) + " " + newOps[i];
                            break;
                        }
                    }
                }
                return true;
            }
        }

        // ================= 操作数转换 =================

        private static string ConvertOperandIntelToAtt(string op, bool indirectBranch)
        {
            op = op.Trim();
            if (op.Length == 0) return op;

            // 剥 byte/word/dword/qword (ptr) 前缀（循环剥净，兼容历史累积文本）
            while (true)
            {
                string lower = op.ToLowerInvariant();
                bool stripped = false;
                string[] ptrWords = { "qword ptr", "dword ptr", "word ptr", "byte ptr", "qword", "dword", "word", "byte" };
                foreach (string pw in ptrWords)
                {
                    if (lower.StartsWith(pw))
                    {
                        string rest2 = op.Substring(pw.Length).Trim();
                        op = rest2;
                        lower = op.ToLowerInvariant();
                        stripped = true;
                        break;
                    }
                }
                if (!stripped) break;
            }

            // 段前缀 fs:[x]
            int colon = op.IndexOf(':');
            if (colon > 0 && IsSegmentReg(op.Substring(0, colon).Trim()))
            {
                string seg = op.Substring(0, colon).Trim();
                string inner = op.Substring(colon + 1).Trim().Trim('[', ']');
                return "%" + seg.ToLowerInvariant() + ":" + ConvertMemIntelToAttNoBracket(inner);
            }

            if (op.StartsWith("["))
                return MaybeStar(indirectBranch, ConvertMemIntelToAtt(op.Substring(1, op.Length - 2).Trim()));

            if (IsRegisterOperand(op))
                return MaybeStar(indirectBranch, "%" + op.ToLowerInvariant());

            // 立即数 / 符号 → $
            if (op[0] == '-' || char.IsDigit(op[0]) || IsHexLiteral(op))
                return "$" + NormalizeNumber(op);
            return "$" + op;   // 符号名（mov reg, Sym = 装载地址）
        }

        private static string MaybeStar(bool indirectBranch, string mem)
        {
            return indirectBranch ? "*" + mem : mem;
        }

        private static bool IsIndirectBranch(string mnem)
        {
            return mnem == "call" || mnem == "jmp";
        }

        private static bool IsHexLiteral(string s)
        {
            return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                   s.StartsWith("-0x", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeNumber(string s)
        {
            // NASM 风格 1234h → 0x1234（尽力）
            if (s.Length > 1 && char.ToLowerInvariant(s[s.Length - 1]) == 'h' &&
                Uri.IsHexDigit(s[s.Length - 2]))
            {
                string body = s.Substring(0, s.Length - 1);
                return "0x" + body.TrimStart('+');
            }
            return s;
        }

        /// <summary>Intel memory表达式（不含方括号）→ AT&T disp(base,index,scale)</summary>
        private static string ConvertMemIntelToAtt(string expr)
        {
            // 拆 +/-
            List<KeyValuePair<string, int>> terms = SplitTerms(expr);
            string breg = "", ireg = "";
            int scale = 0;
            long disp = 0;
            bool hasDispSym = false;
            string dispSym = "";
            int dispSign = 1;

            foreach (KeyValuePair<string, int> kv in terms)
            {
                string t = kv.Key.Trim();
                int sign = kv.Value;
                if (t.Length == 0) continue;

                int star = t.IndexOf('*');
                if (star > 0)
                {
                    string left = t.Substring(0, star).Trim();
                    string right = t.Substring(star + 1).Trim();
                    if (IsRegisterOperand(left) && char.IsDigit(right[0]))
                    {
                        ireg = left.ToLowerInvariant();
                        scale = int.Parse(right);
                    }
                    else if (IsRegisterOperand(right) && char.IsDigit(left[0]))
                    {
                        ireg = right.ToLowerInvariant();
                        scale = int.Parse(left);
                    }
                    continue;
                }
                if (IsRegisterOperand(t))
                {
                    if (breg.Length == 0) breg = t.ToLowerInvariant();
                    else if (ireg.Length == 0) { ireg = breg == "" ? "" : t.ToLowerInvariant(); if (ireg.Length == 0) breg = t.ToLowerInvariant(); }
                    continue;
                }
                // rip 特判（Intel rip 相对通常写作 [rip+label]）
                if (t.ToLowerInvariant() == "rip" && breg.Length == 0)
                {
                    breg = "rip";
                    continue;
                }
                // 数字或符号位移
                long v;
                string lower = t.ToLowerInvariant();
                if (lower.StartsWith("0x") && long.TryParse(lower.Substring(2),
                        System.Globalization.NumberStyles.HexNumber, null, out v))
                {
                    disp += sign * v;
                }
                else if (long.TryParse(t, out v))
                {
                    disp += sign * v;
                }
                else if (t.Length > 1 && char.ToLowerInvariant(t[t.Length - 1]) == 'h' &&
                         Uri.IsHexDigit(t[t.Length - 2]) &&
                         long.TryParse(t.Substring(0, t.Length - 1),
                         System.Globalization.NumberStyles.HexNumber, null, out v))
                {
                    disp += sign * v;
                }
                else
                {
                    hasDispSym = true;
                    dispSym = t;
                    dispSign = sign;
                }
            }

            StringBuilder sb = new StringBuilder();
            if (hasDispSym)
            {
                if (dispSign < 0) sb.Append('-');
                sb.Append(dispSym);
                if (disp != 0) sb.Append(disp > 0 ? "+" + disp : disp.ToString());
            }
            else if (disp != 0)
            {
                sb.Append(disp.ToString());
            }

            if (breg.Length == 0 && ireg.Length == 0)
            {
                // 纯符号/绝对位移memory：AT&T 需括号形式明确"memory"语义（g_score(,1)）
                sb.Append("(,1)");
                return sb.ToString();
            }
            sb.Append('(');
            if (breg.Length > 0) sb.Append('%').Append(breg);
            if (ireg.Length > 0)
            {
                sb.Append(",%").Append(ireg).Append(',').Append(scale <= 0 ? 1 : scale);
            }
            sb.Append(')');
            return sb.ToString();
        }

        private static List<KeyValuePair<string, int>> SplitTerms(string expr)
        {
            List<KeyValuePair<string, int>> terms = new List<KeyValuePair<string, int>>();
            int sign = 1;
            int start = 0;
            for (int i = 0; i < expr.Length; i++)
            {
                char c = expr[i];
                if (c == '+' || c == '-')
                {
                    if (i > 0 && (c == '-' ) && !char.IsWhiteSpace(expr[i - 1]) && expr[i - 1] != '+' && expr[i - 1] != '-')
                    {
                        // 可能是负立即数的一部分（如 -0x10）——按符号处理亦可
                    }
                    string t = expr.Substring(start, i - start).Trim();
                    if (t.Length > 0) terms.Add(new KeyValuePair<string, int>(t, sign));
                    sign = (c == '-') ? -sign : sign;
                    start = i + 1;
                }
            }
            string last = expr.Substring(start).Trim();
            if (last.Length > 0) terms.Add(new KeyValuePair<string, int>(last, sign));
            return terms;
        }

        private static string ConvertOperandAttToIntel(string op, bool indirectBranch)
        {
            op = op.Trim();
            if (op.Length == 0) return op;

            // 段前缀 %fs:x
            if (op.StartsWith("%"))
            {
                int colon = op.IndexOf(':');
                if (colon > 0)
                {
                    string seg = op.Substring(1, colon - 1);
                    string inner = op.Substring(colon + 1).Trim();
                    return seg.ToLowerInvariant() + ":" + ConvertMemAttToIntel(inner, indirectBranch);
                }
            }

            // memory d(b,i,s) / X(%rip)
            int lp = op.LastIndexOf('(');
            if (lp >= 0 && op.EndsWith(")"))
            {
                string outer = op.Substring(0, lp).Trim();
                string inner = op.Substring(lp + 1, op.Length - lp - 2).Trim();
                return ConvertMemAttToIntel2(outer, inner, indirectBranch);
            }

            if (op.StartsWith("%")) return op.Substring(1);          // 寄存器
            if (op.StartsWith("*")) return ConvertOperandAttToIntel(op.Substring(1), true);
            if (op.StartsWith("$")) return op.Substring(1);          // 立即数/符号 → Intel 直接写
            return op;
        }

        /// <summary>AT&T memory: outer=位移/符号, inner=base,index,scale → Intel [expr]</summary>
        private static string ConvertMemAttToIntel2(string outer, string inner, bool indirectBranch)
        {
            List<string> parts = new List<string>();
            // base
            string base64 = "";
            string idx = "";
            int scale = 1;
            if (inner.Length > 0)
            {
                // 段分类：% 开头 = base/index；纯数字 = scale（或退化 disp）
                string[] segs = inner.Split(',');
                if (segs.Length >= 1 && segs[0].Trim().StartsWith("%")) base64 = segs[0].Trim();
                if (segs.Length >= 2 && segs[1].Trim().StartsWith("%")) idx = segs[1].Trim();
                if (segs.Length >= 3 && segs[2].Trim().Length > 0) int.TryParse(segs[2].Trim(), out scale);
                else if (segs.Length == 2 && idx.Length == 0 && segs[1].Trim().Length > 0)
                    int.TryParse(segs[1].Trim(), out scale);   // (,1) 形式：第二段是 scale
            }

            // 单段纯数值位移（如 %fs:0x30 → inner="0x30"）：直接作 disp
            if (base64.Length == 0 && idx.Length == 0 && inner.Length > 0 &&
                !inner.Contains(",") && !inner.StartsWith("%"))
            {
                if (outer.Length == 0) return "[" + inner + "]";
            }
            StringBuilder expr = new StringBuilder();
            bool any = false;
            if (base64.Length > 0 && base64 != "rip")
            {
                expr.Append(StripPercent(base64));
                any = true;
            }
            if (idx.Length > 0)
            {
                if (any) expr.Append(" + ");
                expr.Append(StripPercent(idx));
                if (scale != 1) expr.Append('*').Append(scale);
                any = true;
            }
            if (outer.Length > 0)
            {
                string o = outer;
                if (base64 == "rip")
                {
                    if (any) expr.Append(" + ");
                    expr.Append("rip");
                    any = true;
                    if (o.Length > 0) { expr.Append(" + "); expr.Append(o); any = true; }
                }
                else
                {
                    if (any) { expr.Append(o.StartsWith("-") ? " - " : " + "); expr.Append(o.StartsWith("-") ? o.Substring(1) : o); }
                    else { expr.Append(o); }
                    any = true;
                }
            }
            string body = expr.ToString();
            if (body.Length == 0) body = "0";
            return "[" + body + "]";
        }

        /// <summary>是否为"data"寄存器（排除典型地址用寄存器 rsp/rbp）</summary>
        private static bool IsDataRegister(string op)
        {
            string t = op.Trim();
            if (t.StartsWith("%")) t = t.Substring(1);
            string r = t.ToLowerInvariant();
            if (r == "rsp" || r == "rbp" || r == "esp" || r == "ebp") return false;
            return IsRegisterOperand(r);
        }

        private static string ConvertMemAttToIntel(string inner, bool indirectBranch)
        {
            return ConvertMemAttToIntel2("", inner, indirectBranch);
        }

        /// <summary>段前缀内的位移（%fs:0x30）：数值/符号保持原样，不套括号不重排</summary>
        private static string ConvertMemIntelToAttNoBracket(string expr)
        {
            return expr;
        }

        private static string StripPercent(string reg)
        {
            return reg.StartsWith("%") ? reg.Substring(1) : reg;
        }

        // ================= 判定辅助 =================

        private static bool IsRegisterOperand(string op)
        {
            string t = op.Trim();
            if (t.StartsWith("%")) return true;
            switch (t.ToLowerInvariant())
            {
                case "rax": case "rbx": case "rcx": case "rdx": case "rsi": case "rdi":
                case "rbp": case "rsp": case "eax": case "ebx": case "ecx": case "edx":
                case "esi": case "edi": case "ebp": case "esp": case "ax": case "bx":
                case "cx": case "dx": case "si": case "di": case "bp": case "sp":
                case "al": case "bl": case "cl": case "dl": case "ah": case "bh":
                case "ch": case "dh": case "spl": case "bpl": case "sil": case "dil":
                    return true;
            }
            if (t.Length >= 3 && (t[0] == 'r' || t[0] == 'R') && char.IsDigit(t[1]))
            {
                string tail = t.Substring(1).TrimEnd('0');
                return true;   // r8..r15 及各种宽度后缀
            }
            return false;
        }

        private static bool IsSegmentReg(string s)
        {
            switch (s.ToLowerInvariant())
            {
                case "es": case "cs": case "ss": case "ds": case "fs": case "gs":
                    return true;
            }
            return false;
        }

        /// <summary>操作数中的寄存器宽度（字节），无寄存器返回 0</summary>
        private static int OperandRegWidth(string op)
        {
            string t = op.Trim();
            if (t.StartsWith("[") || t.StartsWith("$")) return 0;
            // 去掉 ptr 声明
            string lower = t.ToLowerInvariant();
            string[] ptrWords = { "byte ptr", "word ptr", "dword ptr", "qword ptr", "byte", "word", "dword", "qword" };
            foreach (string pw in ptrWords)
            {
                if (lower.StartsWith(pw))
                {
                    t = t.Substring(pw.Length).Trim();
                    if (t.StartsWith("ptr")) t = t.Substring(3).Trim();
                    break;
                }
            }
            if (t.StartsWith("[")) return 0;
            string first = t;
            int sp = first.IndexOf(' ');
            if (sp > 0) first = first.Substring(0, sp).Trim();
            if (!IsRegisterOperand(first)) return 0;
            return RegWidth(first);
        }

        /// <summary>byte/word/dword/qword ptr 声明宽度</summary>
        private static int OperandPtrWidth(string op)
        {
            string lower = op.Trim().ToLowerInvariant();
            if (lower.StartsWith("qword")) return 8;
            if (lower.StartsWith("dword")) return 4;
            if (lower.StartsWith("word")) return 2;
            if (lower.StartsWith("byte")) return 1;
            return 0;
        }

        /// <summary>源操作数尺寸（movzx/movsx 源）：寄存器宽度或 ptr 声明</summary>
        private static int OperandSrcSize(string op)
        {
            int w = OperandRegWidth(op);
            if (w > 0) return w;
            return OperandPtrWidth(op);
        }

        private static char SizeLetter(int width)
        {
            switch (width)
            {
                case 1: return 'b';
                case 2: return 'w';
                case 4: return 'l';
                case 8: return 'q';
            }
            return 'l';
        }

        private static string LetterToPtr(char l)
        {
            switch (l)
            {
                case 'b': return "byte ptr";
                case 'w': return "word ptr";
                case 'l': return "dword ptr";
                case 'q': return "qword ptr";
            }
            return "dword ptr";
        }

        private static bool NeedsSizeSuffix(string mnem)
        {
            switch (mnem)
            {
                case "lea": case "call": case "jmp": case "ret": case "leave":
                case "nop": case "pushf": case "popf": case "pushfq": case "popfq":
                case "push": case "pop": case "movsb": case "movsw": case "movsd":
                case "movsq": case "stosb": case "stosw": case "stosd": case "stosq":
                case "lodsb": case "lodsw": case "lodsd": case "lodsq":
                case "scasb": case "scasw": case "scasd": case "scasq":
                case "cmpsb": case "cmpsw": case "cmpsd": case "cmpsq":
                case "cdq": case "cdqe": case "cqo": case "cwde": case "endbr64":
                case "endbr32": case "syscall": case "int3": case "ud2":
                    return false;
            }
            if (mnem.StartsWith("j")) return false;   // jcc
            if (mnem.StartsWith("set") || mnem.StartsWith("cmov")) return true;
            if (mnem == "lock" || mnem == "rep") return false;
            return true;
        }

        private static int RegWidth(string reg)
        {
            string r = reg.Trim().ToLowerInvariant();
            switch (r)
            {
                case "rax": case "rbx": case "rcx": case "rdx": case "rsi": case "rdi":
                case "rbp": case "rsp": case "r8": case "r9": case "r10": case "r11":
                case "r12": case "r13": case "r14": case "r15":
                    return 8;
                case "eax": case "ebx": case "ecx": case "edx": case "esi": case "edi":
                case "ebp": case "esp": case "r8d": case "r9d": case "r10d": case "r11d":
                case "r12d": case "r13d": case "r14d": case "r15d":
                    return 4;
                case "ax": case "bx": case "cx": case "dx": case "si": case "di":
                case "bp": case "sp": case "r8w": case "r9w": case "r10w": case "r11w":
                case "r12w": case "r13w": case "r14w": case "r15w":
                    return 2;
                case "al": case "bl": case "cl": case "dl": case "ah": case "bh":
                case "ch": case "dh": case "spl": case "bpl": case "sil": case "dil":
                case "r8b": case "r9b": case "r10b": case "r11b": case "r12b":
                case "r13b": case "r14b": case "r15b":
                    return 1;
            }
            return 4;   // 未知按 32 位
        }
    }
}
