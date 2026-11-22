// InstructionTables.cs - 寄存器表 / 条件码 / 指令分组表
using System;
using System.Collections.Generic;

namespace AsmStudio.Engine
{
    /// <summary>静态编码用表</summary>
    internal static class InstructionTables
    {
        internal sealed class RegInfo
        {
            public readonly int Index;
            public readonly int Size;
            public readonly bool High;
            public RegInfo(int index, int size, bool high)
            {
                Index = index; Size = size; High = high;
            }
        }

        private static readonly Dictionary<string, RegInfo> _regs =
            new Dictionary<string, RegInfo>(StringComparer.OrdinalIgnoreCase);

        // jcc/setcc 条件码
        private static readonly Dictionary<string, byte> _cc =
            new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        // Group1 (add/or/adc/sbb/and/sub/xor/cmp)
        private static readonly Dictionary<string, int> _grp1Digit = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, byte> _grp1Op8 = new Dictionary<string, byte>();    // r/m,r 方向 8 位
        private static readonly Dictionary<string, byte> _grp1Op32 = new Dictionary<string, byte>();   // r/m,r 方向 32/64 位

        // 移位组 /digit
        private static readonly Dictionary<string, int> _shift = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // 一元组 F6/F7 /digit
        private static readonly Dictionary<string, int> _unary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // bt 组 imm8 形式 /digit
        private static readonly Dictionary<string, int> _bitTestImm = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        static InstructionTables()
        {
            // ---- 寄存器表 ----
            string[] lo8 = { "al", "cl", "dl", "bl", "ah", "ch", "dh", "bh" };
            string[] lo8x = { "spl", "bpl", "sil", "dil" };
            string[] lo16 = { "ax", "cx", "dx", "bx", "sp", "bp", "si", "di" };
            string[] lo32 = { "eax", "ecx", "edx", "ebx", "esp", "ebp", "esi", "edi" };
            string[] lo64 = { "rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi" };
            for (int i = 0; i < 8; i++)
            {
                _regs[lo8[i]] = new RegInfo(i, 1, i >= 4);      // ah/ch/dh/bh 为高字节寄存器
                // spl/bpl/sil/dil 的编码是 4..7（不是 0..3！0..3 是 al/cl/dl/bl），
                // 旧表写成 i 会让 "mov spl,1" 静默编成 "mov al,1"。
                if (i < 4) _regs[lo8x[i]] = new RegInfo(i + 4, 1, false);
                _regs[lo16[i]] = new RegInfo(i, 2, false);
                _regs[lo32[i]] = new RegInfo(i, 4, false);
                _regs[lo64[i]] = new RegInfo(i, 8, false);
            }
            for (int i = 0; i < 8; i++)
            {
                int idx = 8 + i;
                _regs["r" + (8 + i) + "b"] = new RegInfo(idx, 1, false);
                _regs["r" + (8 + i) + "w"] = new RegInfo(idx, 2, false);
                _regs["r" + (8 + i) + "d"] = new RegInfo(idx, 4, false);
                _regs["r" + (8 + i)] = new RegInfo(idx, 8, false);
            }

            // ---- 条件码 ----
            _cc["o"] = 0; _cc["no"] = 1;
            _cc["b"] = 2; _cc["c"] = 2; _cc["nae"] = 2;
            _cc["ae"] = 3; _cc["nb"] = 3; _cc["nc"] = 3;
            _cc["e"] = 4; _cc["z"] = 4;
            _cc["ne"] = 5; _cc["nz"] = 5;
            _cc["be"] = 6; _cc["na"] = 6;
            _cc["a"] = 7; _cc["nbe"] = 7;
            _cc["s"] = 8; _cc["ns"] = 9;
            _cc["p"] = 10; _cc["pe"] = 10;
            _cc["np"] = 11; _cc["po"] = 11;
            _cc["l"] = 12; _cc["nge"] = 12;
            _cc["ge"] = 13; _cc["nl"] = 13;
            _cc["le"] = 14; _cc["ng"] = 14;
            _cc["g"] = 15; _cc["nle"] = 15;

            // ---- Group1 /digit 与基础 opcode ----
            byte[] op8 = { 0x00, 0x08, 0x10, 0x18, 0x20, 0x28, 0x30, 0x38 };
            byte[] op32 = { 0x01, 0x09, 0x11, 0x19, 0x21, 0x29, 0x31, 0x39 };
            string[] g1 = { "add", "or", "adc", "sbb", "and", "sub", "xor", "cmp" };
            for (int i = 0; i < 8; i++)
            {
                _grp1Digit[g1[i]] = i;
                _grp1Op8[g1[i]] = op8[i];
                _grp1Op32[g1[i]] = op32[i];
            }

            // ---- 移位 ----
            _shift["rol"] = 0; _shift["ror"] = 1; _shift["rcl"] = 2; _shift["rcr"] = 3;
            _shift["shl"] = 4; _shift["sal"] = 4; _shift["shr"] = 5; _shift["sar"] = 7;

            // ---- 一元组 ----
            _unary["not"] = 2; _unary["neg"] = 3; _unary["mul"] = 4;
            _unary["imul"] = 5; _unary["div"] = 6; _unary["idiv"] = 7;

            // ---- bt 组 imm8 形式 ----
            _bitTestImm["bt"] = 4; _bitTestImm["bts"] = 5; _bitTestImm["btr"] = 6; _bitTestImm["btc"] = 7;
        }

        /// <summary>尝试解析寄存器名（Intel 形式无 %；AT&T 调用前先去掉 %）</summary>
        internal static bool TryGetReg(string name, out RegInfo info)
        {
            return _regs.TryGetValue(name.ToLowerInvariant(), out info);
        }

        /// <summary>AT&T 形式寄存器（带 % 前缀）</summary>
        internal static bool TryGetRegAtt(string name, out RegInfo info)
        {
            if (name.Length > 1 && name[0] == '%')
                return TryGetReg(name.Substring(1), out info);
            info = null;
            return false;
        }

        internal static bool TryGetCC(string ccName, out byte cc)
        {
            return _cc.TryGetValue(ccName, out cc);
        }

        internal static bool TryGetGroup1(string m, out int digit, out byte op8, out byte op32)
        {
            if (_grp1Digit.TryGetValue(m, out digit))
            {
                op8 = _grp1Op8[m]; op32 = _grp1Op32[m];
                return true;
            }
            op8 = op32 = 0;
            return false;
        }

        internal static bool TryGetShift(string m, out int digit) { return _shift.TryGetValue(m, out digit); }
        internal static bool TryGetUnary(string m, out int digit) { return _unary.TryGetValue(m, out digit); }
        internal static bool TryGetBitTestImm(string m, out int digit) { return _bitTestImm.TryGetValue(m, out digit); }

        internal static byte SegPrefixByte(SegKind seg)
        {
            switch (seg)
            {
                case SegKind.Es: return 0x26;
                case SegKind.Cs: return 0x2E;
                case SegKind.Ss: return 0x36;
                case SegKind.Ds: return 0x3E;
                case SegKind.Fs: return 0x64;
                case SegKind.Gs: return 0x65;
                default: return 0;
            }
        }
    }
}
