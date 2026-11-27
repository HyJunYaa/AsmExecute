// Lexer.cs - 行级词法工具（Intel/AT&T 解析器共用）
using System;
using System.Collections.Generic;
using System.Text;

namespace AsmStudio.Engine
{
    /// <summary>行文本解析辅助：按逗号切分操作数（跳过 [] () 内部与引号内）、
    /// 识别标签/注释、数值解析等。</summary>
    internal static class Lexer
    {
        /// <summary>去掉行注释（Intel: ';' 与 '//'；AT&T: '#' 与 ';' 与 '//'）。</summary>
        public static string StripComment(string line, SyntaxKind syntax, out int commentCol)
        {
            commentCol = 0;
            // '#' 注释仅 AT&T（GAS）语义；Intel 侧不识别（该字符在合法 Intel 语法里也不出现）
            bool hashComment = syntax == SyntaxKind.Att;
            bool inQuote = false, inBracket = false, inParen = false;
            char quote = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuote)
                {
                    if (c == quote) inQuote = false;
                    continue;
                }
                if (c == '"' || c == '\'') { inQuote = true; quote = c; continue; }
                if (c == '[') { inBracket = true; continue; }
                if (c == ']') { inBracket = false; continue; }
                if (c == '(') { inParen = true; continue; }
                if (c == ')') { inParen = false; continue; }
                if (inBracket || inParen) continue;

                bool isComment = false;
                if (c == ';' || (hashComment && c == '#')) isComment = true;
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') isComment = true;

                if (isComment)
                {
                    commentCol = i + 1;
                    return line.Substring(0, i);
                }
            }
            return line;
        }

        /// <summary>把一行切分为多个逗号分隔的字段（顶层逗号），
        /// 忽略 [] () "" 内部的逗号。括号/引号字符本身保留在字段文本中。</summary>
        public static List<KeyValuePair<string, int>> SplitFields(string text)
        {
            List<KeyValuePair<string, int>> fields = new List<KeyValuePair<string, int>>();
            int start = -1;
            bool inQuote = false, inBracket = false, inParen = false;
            char quote = '\0';
            for (int i = 0; i <= text.Length; i++)
            {
                bool last = i == text.Length;
                char c = last ? ',' : text[i];
                if (!last)
                {
                    if (inQuote)
                    {
                        if (c == quote) inQuote = false;
                    }
                    else if (c == '"' || c == '\'')
                    {
                        inQuote = true; quote = c;
                    }
                    else if (c == '[') inBracket = true;
                    else if (c == ']') inBracket = false;
                    else if (c == '(') inParen = true;
                    else if (c == ')') inParen = false;
                }
                if (c == ',' && !inBracket && !inParen && !inQuote)
                {
                    if (start >= 0)
                    {
                        fields.Add(new KeyValuePair<string, int>(text.Substring(start, i - start), start + 1));
                        start = -1;
                    }
                    else if (!last)
                    {
                        // 空字段（连续逗号），记录占位
                        fields.Add(new KeyValuePair<string, int>(string.Empty, i + 1));
                    }
                }
                else if (start < 0 && !char.IsWhiteSpace(c))
                {
                    start = i;
                }
            }
            return fields;
        }

        /// <summary>解析整数立即数：支持 0x 十六进制 / 0b 二进制 / 十进制 / 负数 / 单字符 'A'。</summary>
        public static bool TryParseNumber(string s, out long value)
        {
            value = 0;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            // 字符常量
            if (s.Length >= 3 && s[0] == '\'' && s[s.Length - 1] == '\'')
            {
                string inner = s.Substring(1, s.Length - 2);
                if (inner.Length == 1) { value = inner[0]; return true; }
                return false;
            }
            bool neg = false;
            int i = 0;
            if (s[0] == '+' || s[0] == '-')
            {
                neg = s[0] == '-';
                i = 1;
                while (i < s.Length && s[i] == ' ') i++;
            }
            if (i >= s.Length) return false;
            string body = s.Substring(i);
            long v;
            if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!long.TryParse(body.Substring(2).Trim(), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out v)) return false;
            }
            else if (body.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                string bin = body.Substring(2).Trim();
                v = 0;
                foreach (char ch in bin)
                {
                    if (ch != '0' && ch != '1') return false;
                    v = v * 2 + (ch == '0' ? 0L : 1L);
                }
            }
            else
            {
                if (!long.TryParse(body.Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out v)) return false;
            }
            value = neg ? -v : v;
            return true;
        }

        /// <summary>合法符号/标签字符判断</summary>
        public static bool IsIdentStart(char c)
        {
            return char.IsLetter(c) || c == '_' || c == '.' || c == '$';
        }
        public static bool IsIdentChar(char c)
        {
            // '!' 用于 模块!API 形态的外部符号（kernel32.dll!Beep），汇编里经
            // mov+call 间接调用；不能加 '-'——会吞掉负立即数（mov rax, -5）
            return char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '$' || c == '@' || c == '!';
        }

        /// <summary>提取行首的标识符（助记符/前缀/标签名）</summary>
        public static string ReadIdent(string text, int pos)
        {
            if (pos >= text.Length || !IsIdentStart(text[pos])) return null;
            int i = pos;
            while (i < text.Length && IsIdentChar(text[i])) i++;
            return text.Substring(pos, i - pos);
        }
    }
}
