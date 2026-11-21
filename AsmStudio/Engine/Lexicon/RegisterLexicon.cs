// RegisterLexicon.cs - 寄存器/段/尺寸关键词词库（纯"名称"集合，不含编码事实）
//
// 职责边界：
//   本文件集中存放"寄存器与尺寸/段关键词"的名单，供 Intel/AT&T 解析器与编辑器高亮复用；
//   寄存器的"编码信息"（Index/Size/REX 位）在 InstructionTables.RegInfo，勿混入。
using System;
using System.Collections.Generic;

namespace AsmStudio.Engine
{
    internal static class RegisterLexicon
    {
        // ---- 寄存器关键词全集（编辑器高亮着色用；解析器用 InstructionTables 精确解析） ----
        private static readonly HashSet<string> _registerKeywords =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---- 段寄存器名 → SegKind（解析器与高亮共用） ----
        private static readonly Dictionary<string, SegKind> _segmentNames =
            new Dictionary<string, SegKind>(StringComparer.OrdinalIgnoreCase);

        // ---- 尺寸声明词 → 字节数（Intel: byte/word/dword/qword） ----
        private static readonly Dictionary<string, int> _sizeSpecifiers =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        static RegisterLexicon()
        {
            // 寄存器关键词（低 8 组 + r8..r15 各宽度）
            foreach (string r in new string[]
            {
                "al","cl","dl","bl","ah","ch","dh","bh",
                "spl","bpl","sil","dil",
                "ax","cx","dx","bx","sp","bp","si","di",
                "eax","ecx","edx","ebx","esp","ebp","esi","edi",
                "rax","rcx","rdx","rbx","rsp","rbp","rsi","rdi"
            })
            {
                _registerKeywords.Add(r);
            }
            for (int i = 8; i < 16; i++)
            {
                _registerKeywords.Add("r" + i);
                _registerKeywords.Add("r" + i + "d");
                _registerKeywords.Add("r" + i + "w");
                _registerKeywords.Add("r" + i + "b");
            }

            // 段寄存器
            _segmentNames["es"] = SegKind.Es;
            _segmentNames["cs"] = SegKind.Cs;
            _segmentNames["ss"] = SegKind.Ss;
            _segmentNames["ds"] = SegKind.Ds;
            _segmentNames["fs"] = SegKind.Fs;
            _segmentNames["gs"] = SegKind.Gs;

            // 尺寸声明词
            _sizeSpecifiers["byte"] = 1;
            _sizeSpecifiers["word"] = 2;
            _sizeSpecifiers["dword"] = 4;
            _sizeSpecifiers["qword"] = 8;
        }

        /// <summary>该词是否为寄存器关键词（编辑器高亮用；解析精确判定走 InstructionTables）</summary>
        public static bool IsRegisterKeyword(string word)
        {
            return _registerKeywords.Contains(word);
        }

        /// <summary>段名 → SegKind（es/cs/ss/ds/fs/gs）</summary>
        public static bool TryGetSegment(string name, out SegKind seg)
        {
            return _segmentNames.TryGetValue(name.ToLowerInvariant(), out seg);
        }

        /// <summary>尺寸声明词 → 字节数（byte/word/dword/qword）</summary>
        public static bool TryGetSizeSpecifier(string name, out int size)
        {
            return _sizeSpecifiers.TryGetValue(name.ToLowerInvariant(), out size);
        }

        /// <summary>该词是否为尺寸/指针声明关键词（byte/word/dword/qword/ptr；高亮用）</summary>
        public static bool IsSizeSpecifierWord(string word)
        {
            if (string.Equals(word, "ptr", StringComparison.OrdinalIgnoreCase)) return true;
            return _sizeSpecifiers.ContainsKey(word.ToLowerInvariant());
        }

        /// <summary>该词是否为段寄存器名（es/cs/ss/ds/fs/gs；高亮用）</summary>
        public static bool IsSegmentKeyword(string word)
        {
            return _segmentNames.ContainsKey(word.ToLowerInvariant());
        }
    }
}
