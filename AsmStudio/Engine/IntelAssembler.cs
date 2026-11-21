// IntelAssembler.cs - Intel 语法行解析器（NASM 风格）
// 产出与 AT&T 解析器相同的 Statement IR，编码由 Encoder 统一完成。
using System;
using System.Collections.Generic;
using System.Text;

namespace AsmStudio.Engine
{
    internal static class IntelParser
    {
        /// <summary>
        /// 解析一行 Intel 汇编。成功返回 true 并填充 stmt。
        /// stmt.LabelDef 允许为空；空行/纯注释/纯标签行时 Mnemonic 可为 null。
        /// </summary>
        public static bool ParseLine(string line, int lineNo, Statement stmt, List<AsmError> errors)
        {
            int col;
            string text = Lexer.StripComment(line, SyntaxKind.Intel, out col).Trim();
            if (text.Length == 0) return true;

            // 标签: name:  （允许一行多个）
            while (true)
            {
                string ident = Lexer.ReadIdent(text, 0);
                if (ident != null && ident.Length < text.Length && text[ident.Length] == ':')
                {
                    if (stmt.LabelDef != null)
                    {
                        errors.Add(new AsmError(lineNo, 1, "一行内只能定义一个标签"));
                        return false;
                    }
                    stmt.LabelDef = ident;
                    stmt.Column = 1;
                    text = text.Substring(ident.Length + 1).Trim();
                    if (text.Length == 0) return true;
                    continue;
                }
                break;
            }

            // 前缀
            while (true)
            {
                string ident = Lexer.ReadIdent(text, 0);
                if (ident == null) break;
                string low = ident.ToLowerInvariant();
                if (low == "lock")
                {
                    stmt.Lock = true;
                }
                else if (low == "rep" || low == "repe" || low == "repz")
                {
                    stmt.RepKind = 1;
                }
                else if (low == "repne" || low == "repnz")
                {
                    stmt.RepKind = 2;
                }
                else if (low == "notrack" || low == "data16" || low == "addr16")
                {
                    errors.Add(new AsmError(lineNo, 1, "不支持的前缀: " + ident));
                    return false;
                }
                else break;
                text = text.Substring(ident.Length).Trim();
                if (text.Length == 0)
                {
                    errors.Add(new AsmError(lineNo, 1, "前缀后缺少指令"));
                    return false;
                }
            }

            // 助记符
            string mnem = Lexer.ReadIdent(text, 0);
            if (mnem == null)
            {
                errors.Add(new AsmError(lineNo, 1, "无法识别的指令"));
                return false;
            }
            stmt.Mnemonic = mnem.ToLowerInvariant();
            stmt.Column = 1;
            text = text.Substring(mnem.Length).Trim();

            // 操作数
            if (text.Length > 0)
            {
                List<KeyValuePair<string, int>> fields = Lexer.SplitFields(text);
                foreach (KeyValuePair<string, int> f in fields)
                {
                    string field = f.Key.Trim();
                    if (field.Length == 0)
                    {
                        errors.Add(new AsmError(lineNo, f.Value, "空操作数"));
                        return false;
                    }
                    // 字符串字面量（db 用）
                    if (field.Length >= 2 && (field[0] == '"' || field[0] == '\''))
                    {
                        Operand sop = new Operand();
                        sop.Kind = OperandKind.Immediate;
                        sop.IsString = true;
                        sop.StrData = UnescapeString(field);
                        stmt.Operands.Add(sop);
                        continue;
                    }
                    Operand op;
                    int errCol;
                    if (!ParseOperand(field, f.Value, lineNo, out op, out errCol, errors))
                        return false;
                    stmt.Operands.Add(op);
                }
            }
            bool isData = stmt.Mnemonic == "db" || stmt.Mnemonic == "dw" ||
                          stmt.Mnemonic == "dd" || stmt.Mnemonic == "dq";
            if (stmt.Operands.Count > 3 && !isData)
            {
                errors.Add(new AsmError(lineNo, stmt.Column, "操作数过多"));
                return false;
            }
            return true;
        }

        /// <summary>去除引号并处理简单转义（\" \' \\ \n \r \t \0）</summary>
        private static string UnescapeString(string s)
        {
            if (s.Length < 2) return string.Empty;
            char q = s[0];
            string body = s.Substring(1);
            if (body.EndsWith(q.ToString())) body = body.Substring(0, body.Length - 1);
            StringBuilder sb = new StringBuilder(body.Length);
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c == '\\' && i + 1 < body.Length)
                {
                    i++;
                    switch (body[i])
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '0': sb.Append('\0'); break;
                        default: sb.Append(body[i]); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>解析单个操作数表达式</summary>
        private static bool ParseOperand(string text, int baseCol, int lineNo,
            out Operand op, out int errCol, List<AsmError> errors)
        {
            op = null;
            errCol = baseCol;
            string s = text;
            int size = 0;

            // 尺寸声明: byte/word/dword/qword (ptr)?（词库: RegisterLexicon.TryGetSizeSpecifier）
            string[] sizeWords = { "byte", "word", "dword", "qword" };
            bool matchedSize = false;
            for (int i = 0; i < sizeWords.Length; i++)
            {
                if (StartsWithWord(s, sizeWords[i]))
                {
                    RegisterLexicon.TryGetSizeSpecifier(sizeWords[i], out size);
                    string rest = s.Substring(sizeWords[i].Length).TrimStart();
                    if (StartsWithWord(rest, "ptr"))
                        rest = rest.Substring(3).TrimStart();
                    s = rest;
                    matchedSize = true;
                    break;
                }
            }
            if (matchedSize && s.Length == 0)
            {
                errCol = baseCol;
                errors.Add(new AsmError(lineNo, baseCol, "尺寸声明后缺少内存表达式"));
                return false;
            }

            // 段前缀: fs:[...] / fs [..]（词库: RegisterLexicon.TryGetSegment）
            SegKind seg = SegKind.None;
            string afterSeg;
            if (TrySplitSegmentPrefix(s, out seg, out afterSeg))
                s = afterSeg;

            if (s.Length > 0 && s[0] == '[')
            {
                int close = FindCloseBracket(s);
                if (close < 0)
                {
                    errCol = baseCol;
                    errors.Add(new AsmError(lineNo, baseCol, "缺少 ']'"));
                    return false;
                }
                MemOperand mem = new MemOperand();
                mem.Size = size;
                mem.Segment = seg;
                if (!ParseMemExpr(s.Substring(1, close - 1), baseCol + 1, lineNo, mem, errors, out errCol))
                    return false;
                op = Operand.MemOp(mem);
                return true;
            }

            if (seg != SegKind.None)
            {
                errCol = baseCol;
                errors.Add(new AsmError(lineNo, baseCol, "段前缀只能用于内存操作数"));
                return false;
            }

            // 寄存器
            InstructionTables.RegInfo ri;
            if (Lexer.ReadIdent(s, 0) == s && InstructionTables.TryGetReg(s, out ri))
            {
                if (size != 0 && size != ri.Size)
                {
                    errCol = baseCol;
                    errors.Add(new AsmError(lineNo, baseCol, "尺寸声明与寄存器尺寸不匹配"));
                    return false;
                }
                op = Operand.RegOp(new Reg { Index = ri.Index, Size = ri.Size, High = ri.High });
                return true;
            }

            // 立即数 / 符号
            long num;
            if (Lexer.TryParseNumber(s, out num))
            {
                if (size != 0) op = Operand.ImmOp(num, size);
                else op = Operand.ImmOp(num, 0);
                return true;
            }
            if (Lexer.IsIdentStart(s[0]) && Lexer.ReadIdent(s, 0) == s)
            {
                // 裸符号 = 符号地址立即数（NASM 风格）
                op = Operand.SymbolImmOp(s);
                return true;
            }

            errCol = baseCol;
            errors.Add(new AsmError(lineNo, baseCol, "无法识别的操作数: " + text));
            return false;
        }

        /// <summary>解析 [] 内的内存表达式: base + index*scale + disp (+ rel/abs 关键字)</summary>
        private static bool ParseMemExpr(string expr, int baseCol, int lineNo,
            MemOperand mem, List<AsmError> errors, out int errCol)
        {
            errCol = baseCol;
            string s = expr.Trim();

            // rel / abs 关键字（x64 专用语义）
            if (StartsWithWord(s, "rel"))
            {
                mem.RipRelative = true;
                s = s.Substring(3).TrimStart();
            }
            else if (StartsWithWord(s, "abs"))
            {
                mem.ForceAbs = true;
                s = s.Substring(3).TrimStart();
            }
            // 段前缀出现在括号内: [fs:0x30]（词库: RegisterLexicon.TryGetSegment）
            SegKind innerSeg;
            string afterInnerSeg;
            if (TrySplitSegmentPrefix(s, out innerSeg, out afterInnerSeg))
            {
                mem.Segment = innerSeg;
                s = afterInnerSeg;
            }

            if (s.Length == 0)
            {
                errCol = baseCol;
                errors.Add(new AsmError(lineNo, baseCol, "空的内存表达式"));
                return false;
            }

            // 按 +/- 拆分项
            List<KeyValuePair<string, int>> terms = SplitAdditive(s, baseCol);
            int regCount = 0, symCount = 0;
            long disp = 0;
            bool firstTerm = true;

            foreach (KeyValuePair<string, int> term in terms)
            {
                string t = term.Key.Trim();
                bool negative = false;
                if (t.StartsWith("+"))
                {
                    t = t.Substring(1).Trim();
                }
                else if (t.StartsWith("-"))
                {
                    negative = true;
                    t = t.Substring(1).Trim();
                }
                if (t.Length == 0)
                {
                    errCol = term.Value;
                    errors.Add(new AsmError(lineNo, term.Value, "空的地址项"));
                    return false;
                }

                // reg * scale 或 scale * reg 或 reg
                string[] parts = t.Split('*');
                if (parts.Length == 2)
                {
                    string a = parts[0].Trim();
                    string b = parts[1].Trim();
                    InstructionTables.RegInfo ri;
                    long scaleNum = 0;
                    int regIdx = -1, scale = 0;
                    if (Lexer.ReadIdent(a, 0) == a && InstructionTables.TryGetReg(a, out ri))
                    {
                        regIdx = ri.Index;
                        if (ri.Size != 4 && ri.Size != 8)
                        {
                            errCol = term.Value;
                            errors.Add(new AsmError(lineNo, term.Value, "变址寄存器必须是 32/64 位"));
                            return false;
                        }
                        if (!Lexer.TryParseNumber(b, out scaleNum))
                        {
                            errCol = term.Value;
                            errors.Add(new AsmError(lineNo, term.Value, "比例系数必须是数字"));
                            return false;
                        }
                        scale = (int)scaleNum;
                    }
                    else if (Lexer.ReadIdent(b, 0) == b && InstructionTables.TryGetReg(b, out ri))
                    {
                        regIdx = ri.Index;
                        if (ri.Size != 4 && ri.Size != 8)
                        {
                            errCol = term.Value;
                            errors.Add(new AsmError(lineNo, term.Value, "变址寄存器必须是 32/64 位"));
                            return false;
                        }
                        if (!Lexer.TryParseNumber(a, out scaleNum))
                        {
                            errCol = term.Value;
                            errors.Add(new AsmError(lineNo, term.Value, "比例系数必须是数字"));
                            return false;
                        }
                        scale = (int)scaleNum;
                    }
                    else
                    {
                        errCol = term.Value;
                        errors.Add(new AsmError(lineNo, term.Value, "乘法项必须包含一个寄存器"));
                        return false;
                    }
                    if (scale != 1 && scale != 2 && scale != 4 && scale != 8)
                    {
                        errCol = term.Value;
                        errors.Add(new AsmError(lineNo, term.Value, "比例系数只能是 1/2/4/8"));
                        return false;
                    }
                    if (negative)
                    {
                        errCol = term.Value;
                        errors.Add(new AsmError(lineNo, term.Value, "变址项不能取负"));
                        return false;
                    }
                    regCount++;
                    if (regCount == 1 && firstTerm && scale == 1)
                        mem.BaseReg = regIdx;
                    else if (regCount == 1)
                        mem.IndexReg = regIdx;
                    else
                    {
                        if (mem.IndexReg >= 0)
                        {
                            errCol = term.Value;
                            errors.Add(new AsmError(lineNo, term.Value, "变址寄存器过多"));
                            return false;
                        }
                        mem.IndexReg = regIdx;
                    }
                    mem.Scale = scale;
                }
                else
                {
                    // 单项: 寄存器 / 数字 / 符号
                    InstructionTables.RegInfo ri;
                    long num;
                    if (Lexer.ReadIdent(t, 0) == t && InstructionTables.TryGetReg(t, out ri))
                    {
                        if (negative)
                        {
                            errCol = term.Value;
                            errors.Add(new AsmError(lineNo, term.Value, "寄存器项不能取负"));
                            return false;
                        }
                        if (ri.Size != 4 && ri.Size != 8)
                        {
                            errCol = term.Value;
                            errors.Add(new AsmError(lineNo, term.Value, "基址/变址寄存器必须是 32/64 位"));
                            return false;
                        }
                        regCount++;
                        if (regCount == 1 && firstTerm)
                            mem.BaseReg = ri.Index;
                        else if (regCount == 1)
                            mem.IndexReg = ri.Index;
                        else
                        {
                            if (mem.IndexReg >= 0)
                            {
                                errCol = term.Value;
                                errors.Add(new AsmError(lineNo, term.Value, "变址寄存器过多"));
                                return false;
                            }
                            mem.IndexReg = ri.Index;
                        }
                    }
                    else if (Lexer.TryParseNumber(t, out num))
                    {
                        disp += negative ? -num : num;
                    }
                    else if (Lexer.IsIdentStart(t[0]) && Lexer.ReadIdent(t, 0) == t)
                    {
                        symCount++;
                        if (symCount > 1)
                        {
                            errCol = term.Value;
                            errors.Add(new AsmError(lineNo, term.Value, "地址表达式中符号过多"));
                            return false;
                        }
                        if (negative)
                        {
                            errCol = term.Value;
                            errors.Add(new AsmError(lineNo, term.Value, "符号地址不能取负"));
                            return false;
                        }
                        mem.DispSymbol = t;
                    }
                    else
                    {
                        errCol = term.Value;
                        errors.Add(new AsmError(lineNo, term.Value, "无法识别的地址项: " + t));
                        return false;
                    }
                }
                firstTerm = false;
            }

            if (regCount == 0 && symCount == 0 && disp == 0)
            {
                errCol = baseCol;
                errors.Add(new AsmError(lineNo, baseCol, "内存表达式不能为空"));
                return false;
            }
            mem.Disp = disp;
            return true;
        }

        /// <summary>按顶层 +/- 拆分（第一个项可带符号）</summary>
        private static List<KeyValuePair<string, int>> SplitAdditive(string s, int baseCol)
        {
            List<KeyValuePair<string, int>> terms = new List<KeyValuePair<string, int>>();
            int depthSq = 0, depthPar = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '[') depthSq++;
                else if (c == ']') depthSq--;
                else if (c == '(') depthPar++;
                else if (c == ')') depthPar--;
                else if ((c == '+' || c == '-') && depthSq == 0 && depthPar == 0)
                {
                    if (i == 0) continue;
                    string piece = s.Substring(start, i - start);
                    if (piece.Trim().Length > 0)
                        terms.Add(new KeyValuePair<string, int>(piece, baseCol + start));
                    start = i;
                }
            }
            terms.Add(new KeyValuePair<string, int>(s.Substring(start), baseCol + start));
            return terms;
        }

        private static bool StartsWithWord(string s, string word)
        {
            if (s.Length < word.Length) return false;
            if (string.Compare(s, 0, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;
            if (s.Length == word.Length) return true;
            char next = s[word.Length];
            return next == ' ' || next == '\t' || next == ':' || next == '[';
        }

        /// <summary>尝试在 s 起始处匹配 "段名:"；成功则经词库给出 SegKind 与冒号后的剩余文本</summary>
        private static bool TrySplitSegmentPrefix(string s, out SegKind seg, out string rest)
        {
            seg = SegKind.None;
            rest = null;
            int n = 0;
            while (n < s.Length && char.IsLetter(s[n])) n++;
            if (n == 0 || n >= s.Length || s[n] != ':')
                return false;
            if (!RegisterLexicon.TryGetSegment(s.Substring(0, n), out seg))
                return false;
            rest = s.Substring(n + 1).TrimStart();
            return true;
        }

        private static int FindCloseBracket(string s)
        {
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '[') depth++;
                else if (s[i] == ']')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }
}
