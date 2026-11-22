// InstructionLexicon.cs - 指令关键词词库（纯"名称"集合，不含编码事实）
//
// 职责边界：
//   本文件只集中存放"指令相关关键词"的名单，供解析器（前缀剥离/lock 判定/串操作识别）
//   与编辑器高亮（助记符着色）复用；
//   指令的"编码事实"（opcode/ModRM/digit 等）在 InstructionTables.cs 与 Encoder.cs，勿混入。
using System;
using System.Collections.Generic;

namespace AsmStudio.Engine
{
    internal static class InstructionLexicon
    {
        // ---- 助记符关键词全集（编辑器高亮用，宽松收录） ----
        // 含引擎支持的指令、数据指示 db/dw/dd/dq、前缀与系统指令。
        private static readonly string[] MnemonicList =
        {
            "mov","movzx","movsx","movsxd","movabs","lea","push","pop","xchg","xadd",
            "add","or","adc","sbb","and","sub","xor","cmp","test",
            "not","neg","mul","imul","div","idiv",
            "shl","sal","shr","sar","rol","ror","rcl","rcr",
            "jmp","call","ret","leave","loop","loope","loopz","loopne","loopnz",
            "je","jne","jz","jnz","ja","jae","jb","jbe","jg","jge","jl","jle",
            "js","jns","jc","jnc","jo","jno","jp","jnp","jpe","jpo",
            "sete","setne","setz","setnz","seta","setae","setb","setbe",
            "setg","setge","setl","setle","sets","setns","setc","setnc",
            "nop","int3","int","stc","clc","cmc","cld","std",
            "cbw","cwde","cdqe","cwd","cdq","cqo",
            "bsf","bsr","bt","bts","btr","btc","bswap",
            "movsb","movsw","movsd","movsq","movsl","stosb","stosw","stosd","stosq",
            "lodsb","lodsw","lodsd","lodsq","scasb","scasw","scasd","scasq",
            "cmpsb","cmpsw","cmpsd","cmpsq","movs","stos","lods","scas","cmps",
            "db","dw","dd","dq",
            "lock","rep","repe","repz","repne","repnz",
            "syscall","sysenter"
        };

        private static readonly HashSet<string> _mnemonicKeywords =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>助记符关键词全集（编辑器高亮着色用）</summary>
        public static ICollection<string> MnemonicKeywords
        {
            get { return _mnemonicKeywords; }
        }

        public static bool IsMnemonicKeyword(string word)
        {
            return _mnemonicKeywords.Contains(word);
        }

        // ---- lock 前缀允许名单（Encoder.CheckLock 判定） ----
        private static readonly HashSet<string> _lockAllowed =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>AT&T 尺寸后缀可剥离的助记符白名单（AtAndTParser 判定）。
        /// 防止误伤：call→cal、sal→sa、rol→ro 等同形词不被剥离。</summary>
        private static readonly HashSet<string> _suffixStrippable =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>串操作家族名（含全部尺寸变体）。Encoder 识别"这是串操作"用。</summary>
        private static readonly HashSet<string> _stringOperations =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static InstructionLexicon()
        {
            // lock 允许名单：可加锁的读-改-写指令
            foreach (string m in new string[]
            {
                "add", "or", "adc", "sbb", "and", "sub", "xor", "cmp",
                "not", "neg", "inc", "dec",
                "xadd", "xchg", "bt", "bts", "btr", "btc"
            })
            {
                _lockAllowed.Add(m);
            }

            // AT&T 剥后缀白名单：剥掉 b/w/l/q 后仍是合法助记符的词
            foreach (string m in new string[]
            {
                "mov", "lea", "push", "pop", "xchg",
                "add", "or", "adc", "sbb", "and", "sub", "xor", "cmp", "test",
                "not", "neg", "mul", "imul", "div", "idiv",
                "shl", "sal", "shr", "sar",
                "rol", "ror", "rcl", "rcr",
                "inc", "dec",
                "bt", "bts", "btr", "btc", "bsf", "bsr", "xadd", "bswap",
                "movs", "stos", "lods", "scas", "cmps",
                "movabs"
            })
            {
                _suffixStrippable.Add(m);
            }

            // 串操作家族（全变体：裸名 + Intel 全名形式）
            foreach (string m in new string[]
            {
                "movs", "movsb", "movsw", "movsd", "movsq", "movsl",
                "stos", "stosb", "stosw", "stosd", "stosq",
                "lods", "lodsb", "lodsw", "lodsd", "lodsq",
                "scas", "scasb", "scasw", "scasd", "scasq",
                "cmps", "cmpsb", "cmpsw", "cmpsd", "cmpsq"
            })
            {
                _stringOperations.Add(m);
            }

            // 高亮全集 = 名单数据填充
            foreach (string m in MnemonicList)
                _mnemonicKeywords.Add(m);
        }

        /// <summary>lock 前缀是否允许用于该指令</summary>
        public static bool IsLockAllowed(string mnemonic)
        {
            return _lockAllowed.Contains(mnemonic);
        }

        /// <summary>该助记符是否允许剥离 AT&T 尺寸后缀</summary>
        public static bool IsSuffixStrippable(string mnemonic)
        {
            return _suffixStrippable.Contains(mnemonic);
        }

        /// <summary>该助记符是否为串操作（movs/stos/lods/scas/cmps 及其变体）</summary>
        public static bool IsStringOperation(string mnemonic)
        {
            return _stringOperations.Contains(mnemonic);
        }
    }
}
