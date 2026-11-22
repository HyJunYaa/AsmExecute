// AtAndTAssembler.cs - AT&T 语法行解析器（GAS 风格）
// %reg / $imm / disp(base,index,scale) / '*' 间接 / 尺寸后缀 b|w|l|q
// 产出 Intel 顺序（dst, src）的 Statement IR。
using System;
using System.Collections.Generic;
using System.Text;

namespace AsmStudio.Engine
{
    internal static class AtAndTParser
    {
        public static bool ParseLine(string line, int lineNo, Statement stmt, List<AsmError> errors)
        {
            int col;
            string text = Lexer.StripComment(line, SyntaxKind.Att, out col).Trim();
            if (text.Length == 0) return true;

            // 标签
            while (true)
            {
                string ident = Lexer.ReadIdent(text, 0);
                if (ident != null && ident.Length < text.Length && text[ident.Length] == ':')
                {
                    if (stmt.LabelDef != null)
                    {
                        errors.Add(new AsmError(lineNo, 1, "Only one label can be defined per line"));
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
                if (low == "lock") stmt.Lock = true;
                else if (low == "rep" || low == "repe" || low == "repz") stmt.RepKind = 1;
                else if (low == "repne" || low == "repnz") stmt.RepKind = 2;
                else break;
                text = text.Substring(ident.Length).Trim();
                if (text.Length == 0)
                {
                    errors.Add(new AsmError(lineNo, 1, "Missing instruction after prefix"));
                    return false;
                }
            }

            // 助记符 + 尺寸后缀
            string mnem = Lexer.ReadIdent(text, 0);
            if (mnem == null)
            {
                errors.Add(new AsmError(lineNo, 1, "Unknown instruction"));
                return false;
            }
            stmt.Column = 1;
            text = text.Substring(mnem.Length).Trim();
            string m = mnem.ToLowerInvariant();
            int sizeHint = 0;
            int srcSize = 0, dstSize = 0;

            if (m.Length >= 6 && (m[3] == 'z' || m[3] == 's') && m.StartsWith("mov"))
            {
                char s1 = m[4], d1 = m[5];
                if (IsSuffix(s1) && IsSuffix(d1))
                {
                    int ss = SuffixSize(s1), ds = SuffixSize(d1);
                    if (m[3] == 'z')
                    {
                        stmt.Mnemonic = "movzx";
                        srcSize = ss; dstSize = ds;
                        m = "movzx";
                    }
                    else if (ss == 4 && ds == 8)
                    {
                        stmt.Mnemonic = "movsxd";
                        srcSize = 4; dstSize = 8;
                        m = "movsxd";
                    }
                    else
                    {
                        stmt.Mnemonic = "movsx";
                        srcSize = ss; dstSize = ds;
                        m = "movsx";
                    }
                    stmt.SrcSizeHint = srcSize;
                    stmt.DstSizeHint = dstSize;
                    goto operands;
                }
            }
            if (m.Length >= 4 && IsSuffix(m[m.Length - 1]))
            {
                // 仅当剥离后缀后是已知助记符时才剥离（避免误伤 call→cal、sal→sa 等）
                char sfx = m[m.Length - 1];
                string base_m = m.Substring(0, m.Length - 1);
                if (InstructionLexicon.IsSuffixStrippable(base_m))
                {
                    sizeHint = SuffixSize(sfx);
                    stmt.SizeHint = sizeHint;
                    m = base_m;
                }
            }
            stmt.Mnemonic = m;

        operands:
            if (text.Length > 0)
            {
                List<KeyValuePair<string, int>> fields = Lexer.SplitFields(text);
                foreach (KeyValuePair<string, int> f in fields)
                {
                    string field = f.Key.Trim();
                    if (field.Length == 0)
                    {
                        errors.Add(new AsmError(lineNo, f.Value, "Empty operand"));
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
                errors.Add(new AsmError(lineNo, stmt.Column, "Too many operands"));
                return false;
            }

            // AT&T 源在前 → 归一化为 Intel 顺序（dst, src）
            if (stmt.Operands.Count == 2)
            {
                Operand tmp = stmt.Operands[0];
                stmt.Operands[0] = stmt.Operands[1];
                stmt.Operands[1] = tmp;
            }

            // 尺寸后缀 → 未声明尺寸的memory操作数
            if (stmt.SizeHint != 0)
            {
                foreach (Operand o in stmt.Operands)
                {
                    if (o.Kind == OperandKind.Memory && o.Mem.Size == 0)
                    {
                        o.Mem.Size = stmt.SizeHint;
                        o.Size = stmt.SizeHint;
                    }
                }
            }

            // 分支助记符的裸符号操作数 = 跳转目标标签（GAS 里 je ok / jmp done 的前向引用
            // 是纯标签，不是 "disp=symbol" 的内存）。解析器默认把裸符号当内存，这里按助记符
            // 纠正为 LabelOp；带 '*' 间接（call *foo）仍保留内存语义走间接调用。
            if (stmt.Mnemonic != null && stmt.Operands.Count == 1)
            {
                bool isBranch = stmt.Mnemonic == "jmp" || stmt.Mnemonic == "call" ||
                    stmt.Mnemonic.StartsWith("j") || stmt.Mnemonic.StartsWith("loop");
                Operand bop = stmt.Operands[0];
                if (isBranch && bop.Kind == OperandKind.Memory && bop.Mem != null &&
                    bop.Mem.DispSymbol != null && bop.Mem.BaseReg < 0 &&
                    bop.Mem.IndexReg < 0 && !bop.Indirect)
                {
                    stmt.Operands[0] = Operand.LabelOp(bop.Mem.DispSymbol);
                }
            }
            return true;
        }

        private static bool IsSuffix(char c)
        {
            return c == 'b' || c == 'w' || c == 'l' || c == 'q';
        }

        private static int SuffixSize(char c)
        {
            switch (c)
            {
                case 'b': return 1;
                case 'w': return 2;
                case 'l': return 4;
                case 'q': return 8;
                default: return 0;
            }
        }

        /// <summary>去除引号并处理简单转义</summary>
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

        private static bool ParseOperand(string text, int baseCol, int lineNo,
            out Operand op, out int errCol, List<AsmError> errors)
        {
            op = null;
            errCol = baseCol;
            string s = text;

            // '*' 间接（jmp/call）
            bool indirect = false;
            if (s.Length > 0 && s[0] == '*')
            {
                indirect = true;
                s = s.Substring(1).Trim();
            }

            // 段前缀 %fs: / %gs: ...
            SegKind seg = SegKind.None;
            if (s.Length > 1 && s[0] == '%')
            {
                int ci = s.IndexOf(':');
                if (ci > 1)
                {
                    string segName = s.Substring(1, ci - 1).ToLowerInvariant();
                    switch (segName)
                    {
                        case "es": seg = SegKind.Es; break;
                        case "cs": seg = SegKind.Cs; break;
                        case "ss": seg = SegKind.Ss; break;
                        case "ds": seg = SegKind.Ds; break;
                        case "fs": seg = SegKind.Fs; break;
                        case "gs": seg = SegKind.Gs; break;
                        default:
                            errCol = baseCol;
                            errors.Add(new AsmError(lineNo, baseCol, "Unknown segment register: %" + segName));
                            return false;
                    }
                    s = s.Substring(ci + 1).Trim();
                }
            }

            // 寄存器
            InstructionTables.RegInfo ri;
            if (seg == SegKind.None && InstructionTables.TryGetRegAtt(s, out ri))
            {
                op = Operand.RegOp(new Reg { Index = ri.Index, Size = ri.Size, High = ri.High });
                op.Indirect = indirect;
                return true;
            }

            // 立即数 $imm / $sym
            if (s.Length > 0 && s[0] == '$')
            {
                string body = s.Substring(1).Trim();
                long num;
                if (Lexer.TryParseNumber(body, out num))
                {
                    op = Operand.ImmOp(num, 0);
                }
                else if (body.Length > 0 && Lexer.IsIdentStart(body[0]) && Lexer.ReadIdent(body, 0) == body)
                {
                    op = Operand.SymbolImmOp(body);
                }
                else
                {
                    errCol = baseCol;
                    errors.Add(new AsmError(lineNo, baseCol, "Unknown immediate: " + s));
                    return false;
                }
                op.Indirect = indirect;
                return true;
            }

            // memory disp(base,index,scale) / label(%rip)
            int pi = s.IndexOf('(');
            if (pi >= 0)
            {
                int close = FindCloseParen(s, pi);
                if (close < 0)
                {
                    errCol = baseCol;
                    errors.Add(new AsmError(lineNo, baseCol, "Missing ')'"));
                    return false;
                }
                MemOperand mem = new MemOperand();
                mem.Segment = seg;
                if (!ParseMemBase(s.Substring(pi + 1, close - pi - 1), lineNo, mem, errors, out errCol))
                    return false;

                string dispPart = s.Substring(0, pi).Trim();
                if (dispPart.Length > 0)
                {
                    long num;
                    if (Lexer.TryParseNumber(dispPart, out num))
                    {
                        mem.Disp = num;
                    }
                    else if (Lexer.IsIdentStart(dispPart[0]) && Lexer.ReadIdent(dispPart, 0) == dispPart)
                    {
                        if (mem.DispSymbol != null)
                        {
                            errCol = baseCol;
                            errors.Add(new AsmError(lineNo, baseCol, "Too many symbols in displacement"));
                            return false;
                        }
                        mem.DispSymbol = dispPart;
                    }
                    else
                    {
                        errCol = baseCol;
                        errors.Add(new AsmError(lineNo, baseCol, "Unknown displacement: " + dispPart));
                        return false;
                    }
                }
                op = Operand.MemOp(mem);
                op.Indirect = indirect;
                return true;
            }

            if (seg != SegKind.None)
            {
                // %fs:0x18 / %fs:symbol → 段前缀 + 绝对地址memory
                long segNum;
                if (Lexer.TryParseNumber(s, out segNum))
                {
                    MemOperand mem = new MemOperand();
                    mem.Segment = seg;
                    mem.Disp = segNum;
                    mem.ForceAbs = true;
                    op = Operand.MemOp(mem);
                    op.Indirect = indirect;
                    return true;
                }
                if (s.Length > 0 && Lexer.IsIdentStart(s[0]) && Lexer.ReadIdent(s, 0) == s)
                {
                    MemOperand mem = new MemOperand();
                    mem.Segment = seg;
                    mem.DispSymbol = s;
                    op = Operand.MemOp(mem);
                    op.Indirect = indirect;
                    return true;
                }
                errCol = baseCol;
                errors.Add(new AsmError(lineNo, baseCol, "Segment prefix must be followed by a memory expression"));
                return false;
            }

            // 裸符号 = 符号处memory（disp=symbol, 无 base）
            long n2;
            if (Lexer.TryParseNumber(s, out n2))
            {
                errCol = baseCol;
                errors.Add(new AsmError(lineNo, baseCol, "Bare number operand invalid (immediates need the $ prefix)"));
                return false;
            }
            if (s.Length > 0 && Lexer.IsIdentStart(s[0]) && Lexer.ReadIdent(s, 0) == s)
            {
                MemOperand mem = new MemOperand();
                mem.DispSymbol = s;
                op = Operand.MemOp(mem);
                op.Indirect = indirect;
                return true;
            }

            errCol = baseCol;
            errors.Add(new AsmError(lineNo, baseCol, "Unknown operand: " + text));
            return false;
        }

        /// <summary>解析 (base,index,scale) 内部</summary>
        private static bool ParseMemBase(string inner, int lineNo,
            MemOperand mem, List<AsmError> errors, out int errCol)
        {
            errCol = 1;
            string[] parts = inner.Split(',');
            if (parts.Length > 3)
            {
                errors.Add(new AsmError(lineNo, 1, "Memory expression has at most three parts (base,index,scale)"));
                return false;
            }
            string basePart = parts[0].Trim();
            if (basePart.Length > 0)
            {
                InstructionTables.RegInfo ri;
                if (InstructionTables.TryGetRegAtt(basePart, out ri))
                {
                    if (ri.Size == 8 && string.Equals(basePart, "%rip", StringComparison.OrdinalIgnoreCase))
                    {
                        mem.RipRelative = true;
                        mem.BaseReg = -1;
                    }
                    else
                    {
                        if (ri.Size != 4 && ri.Size != 8)
                        {
                            errors.Add(new AsmError(lineNo, 1, "Base register must be 32/64-bit"));
                            return false;
                        }
                        mem.BaseReg = ri.Index;
                    }
                }
                else
                {
                    errors.Add(new AsmError(lineNo, 1, "Invalid base part: " + basePart));
                    return false;
                }
            }
            if (parts.Length >= 2)
            {
                string idxPart = parts[1].Trim();
                if (idxPart.Length > 0)
                {
                    InstructionTables.RegInfo ri;
                    if (!InstructionTables.TryGetRegAtt(idxPart, out ri))
                    {
                        errors.Add(new AsmError(lineNo, 1, "Invalid index part: " + idxPart));
                        return false;
                    }
                    if (ri.Size != 4 && ri.Size != 8)
                    {
                        errors.Add(new AsmError(lineNo, 1, "Index register must be 32/64-bit"));
                        return false;
                    }
                    mem.IndexReg = ri.Index;
                }
            }
            if (parts.Length >= 3)
            {
                string scPart = parts[2].Trim();
                if (scPart.Length > 0)
                {
                    long sc;
                    if (!Lexer.TryParseNumber(scPart, out sc) || (sc != 1 && sc != 2 && sc != 4 && sc != 8))
                    {
                        errors.Add(new AsmError(lineNo, 1, "Scale must be 1/2/4/8"));
                        return false;
                    }
                    mem.Scale = (int)sc;
                }
            }
            return true;
        }

        private static int FindCloseParen(string s, int open)
        {
            int depth = 0;
            for (int i = open; i < s.Length; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }
}
