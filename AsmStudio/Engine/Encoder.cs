// Encoder.cs - x86/x64 指令编码核心
// 输入：已归一化的 Statement（Intel 顺序），输出：机器码字节。
// 支持前缀(lock/rep/seg/66)、REX、ModRM/SIB 全寻址、moffs、SIB 绝对寻址(x64 [abs]/数值/外部符号)、
// RIP 相对寻址、分支前向 rel32/后向 rel8 自动选择、symbol immediate、ret 网关改写等。
using System;
using System.Collections.Generic;

namespace AsmStudio.Engine
{
    /// <summary>编码期Error（由 Assembler 捕获转为 AsmError）</summary>
    internal sealed class EncodeException : Exception
    {
        public readonly int Line;
        public readonly int Column;
        public EncodeException(int line, int column, string message)
            : base(message)
        {
            Line = line; Column = column;
        }
    }

    internal sealed class Encoder
    {
        private readonly ArchKind _arch;
        private readonly ulong _base;
        private readonly SymbolTable _symbols;
        private readonly IDictionary<string, ulong> _labels;
        private readonly HashSet<string> _allLabelNames;     // 全部标签名（含前向），用于分支尺寸决策
        private readonly HashSet<string> _force32Branches;   // 需强制 rel32 的 "行号:目标" 记录
        private readonly ulong _retTrap;                     // ret 重定向网关地址（0=关闭）

        private List<byte> _buf;
        private int _stmtIndex;
        private Statement _cur;
        private int _startOffset;        // 本指令起始偏移（缓冲区内）
        private bool _needRex;           // 本语句含 spl/bpl/sil/dil（Index 4..7 的 8 位寄存器）
        private bool _highByteReg;       // 本语句含 ah/ch/dh/bh

        public Encoder(ArchKind arch, ulong baseAddress, SymbolTable symbols,
            IDictionary<string, ulong> labels, HashSet<string> allLabelNames,
            HashSet<string> force32Branches, ulong retTrap)
        {
            _arch = arch;
            _base = baseAddress;
            _symbols = symbols ?? new SymbolTable();
            _labels = labels;
            _allLabelNames = allLabelNames;
            _force32Branches = force32Branches;
            _retTrap = retTrap;
        }

        // ==================== 对外入口 ====================

        /// <summary>编码一条语句，返回生成的字节。startOffset 为该语句在缓冲区中的起始偏移。</summary>
        public byte[] Encode(Statement st, int stmtIndex, int startOffset)
        {
            _cur = st;
            _stmtIndex = stmtIndex;
            _startOffset = startOffset;
            _buf = new List<byte>();
            try
            {
                if (st.Mnemonic == null) return _buf.ToArray();
                ScanByteRegisters(st);
                EncodeCore(st);
            }
            finally
            {
                _cur = null;
            }
            return _buf.ToArray();
        }

        // ==================== 基础发射 ====================

        private void Emit(byte b) { _buf.Add(b); }
        private void Emit(params byte[] bs) { for (int i = 0; i < bs.Length; i++) _buf.Add(bs[i]); }
        /// <summary>发射低 8 位（按补码截断，不受 checked 溢出检查影响）</summary>
        private void EmitI8(long v)
        {
            unchecked { Emit((byte)v); }
        }

        private void EmitDword(long v)
        {
            unchecked
            {
                Emit((byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24));
            }
        }
        private void EmitQword(ulong v)
        {
            unchecked
            {
                Emit((byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24),
                     (byte)(v >> 32), (byte)(v >> 40), (byte)(v >> 48), (byte)(v >> 56));
            }
        }
        private void EmitWord(long v)
        {
            unchecked { Emit((byte)v, (byte)(v >> 8)); }
        }

        private bool IsX64 { get { return _arch == ArchKind.X64; } }

        private void Fail(string msg)
        {
            throw new EncodeException(_cur.Line, _cur.Column, msg);
        }

        /* 扫描本语句的 8 位寄存器：spl/bpl/sil/dil 与 ah/ch/dh/bh 共用同一套 ModRM 编码，
         * 前者必须靠 REX 前缀把 4..7 号编码重定向到低字节，后者则要求"没有 REX"。
         * 旧版只看 RexBit（只有 Index>=8 才置位），于是 spl/bpl/sil/dil 静默编成 ah/ch/dh/bh。 */
        private void ScanByteRegisters(Statement st)
        {
            _needRex = false;
            _highByteReg = false;
            foreach (Operand o in st.Operands)
            {
                if (o == null || o.Kind != OperandKind.Register) continue;
                Reg r = o.Reg;
                if (r.Size != 1) continue;
                if (r.High) _highByteReg = true;
                else if (r.Index >= 4) _needRex = true;
            }
            if (!_needRex) return;
            if (!IsX64) Fail("spl/bpl/sil/dil are only available on x64");
            if (_highByteReg) Fail("cannot mix AH/CH/DH/BH with SPL/BPL/SIL/DIL in one instruction");
        }

        // ==================== 操作数辅助 ====================

        private static bool IsReg(Operand o, out Reg r)
        {
            if (o != null && o.Kind == OperandKind.Register) { r = o.Reg; return true; }
            r = default(Reg);
            return false;
        }
        private static bool IsMem(Operand o)
        {
            return o != null && o.Kind == OperandKind.Memory;
        }
        private static bool IsImm(Operand o)
        {
            return o != null && o.Kind == OperandKind.Immediate;
        }

        private static int OperandSize(Operand o)
        {
            return o == null ? 0 : o.Size;
        }

        /// <summary>两操作数尺寸一致性检查，返回确定的操作尺寸</summary>
        private int CheckSize2(Operand a, Operand b, string mnem)
        {
            int sa = OperandSize(a), sb = OperandSize(b);
            if (sa != 0 && sb != 0 && sa != sb)
                Fail(string.Format("{0}: operand size mismatch ({1} bytes vs {2} bytes)", mnem, sa, sb));
            int s = sa != 0 ? sa : sb;
            if (s == 0) Fail(string.Format("{0}: cannot determine operand size (memory operand needs byte/word/dword/qword ptr or a size suffix)", mnem));
            return s;
        }

        /// <summary>符号/标签取值（立即数语境）</summary>
        private ulong ResolveSymbolValue(string name)
        {
            ulong v;
            if (_labels != null && _labels.TryGetValue(name, out v))
            {
                if (_base == 0)
                    Fail("Immediate uses internal label '" + name + "' requires a known BaseAddress");
                return _base + v;
            }
            if (_allLabelNames != null && _allLabelNames.Contains(name))
                return 0;   // 前向标签：第一遍占位，下一遍取真实值（尺寸不变）
            if (_symbols.TryGetValue(name, out v)) return v;
            Fail("Undefined symbol: '" + name + "'");
            return 0;
        }

        /// <summary>符号取值（位移语境）。返回 false 表示符号不存在。</summary>
        private bool TryResolveDisp(string name, out ulong value, out bool isInternal)
        {
            ulong v;
            if (_labels != null && _labels.TryGetValue(name, out v))
            {
                isInternal = true;
                value = _base + v;
                return true;
            }
            if (_allLabelNames != null && _allLabelNames.Contains(name))
            {
                // 前向内部标签：第 1 遍还没登记偏移，按 0 占位（尺寸固定，下一遍取真实值）。
                // 这里必须仍标记为 internal，否则 [rel fwd] 会被当成"外部符号"直接报错。
                isInternal = true;
                value = _base;
                return true;
            }
            isInternal = false;
            return _symbols.TryGetValue(name, out value);
        }

        /// <summary>立即数取值（可能是纯数字或符号）</summary>
        private long ImmValue(Operand o)
        {
            if (o.LabelName != null) return (long)ResolveSymbolValue(o.LabelName);
            return o.Imm;
        }

        private bool ImmFitsSigned(long v, int bytes)
        {
            switch (bytes)
            {
                case 1: return v >= -128 && v <= 127;
                case 2: return v >= -32768 && v <= 32767;
                case 4: return v >= int.MinValue && v <= int.MaxValue;
                default: return true;
            }
        }
        private bool ImmFitsUnsigned(long v, int bytes)
        {
            switch (bytes)
            {
                case 1: return v >= 0 && v <= 255;
                case 2: return v >= 0 && v <= 65535;
                case 4: return v >= 0 && v <= uint.MaxValue;
                default: return true;
            }
        }

        // ==================== 前缀与 REX ====================

        private void EmitLegacyAndRex(bool w64, int rexR, int rexX, int rexB,
            SegKind seg, bool need66, int repKind, bool useLock)
        {
            /* AH/CH/DH/BH 与 REX 前缀在同一指令里不可能共存：有 REX 时 8 位寄存器编码 4..7
             * 被解释成 spl/bpl/sil/dil，ah 会静默变成 spl（"mov ah,[r8]" 实测编出 41 8A 20）。
             * REX 可能来自另一操作数的 r8-r15，也可能来自内存基址/变址寄存器，
             * 所以这里必须按"本语句是否含高字节寄存器"统一拦，而不是只看寄存器操作数。 */
            if (_highByteReg && (w64 || _needRex || rexR != 0 || rexX != 0 || rexB != 0))
                Fail("AH/CH/DH/BH cannot be encoded in an instruction that needs a REX prefix "
                    + "(r8-r15 register, SIB base/index, spl/bpl/sil/dil or 64-bit form)");
            if (useLock) Emit(0xF0);
            if (repKind == 1) Emit(0xF3);
            else if (repKind == 2) Emit(0xF2);
            byte segPrefix = InstructionTables.SegPrefixByte(seg);
            if (segPrefix != 0) Emit(segPrefix);
            if (need66) Emit(0x66);
            if (IsX64)
            {
                int rex = 0x40;
                bool any = false;
                if (w64) { rex |= 8; any = true; }
                if (rexR != 0) { rex |= 4; any = true; }
                if (rexX != 0) { rex |= 2; any = true; }
                if (rexB != 0) { rex |= 1; any = true; }
                // 0x40（空 REX）不改变编码，但它是 spl/bpl/sil/dil 的唯一表达方式
                if (any || _needRex) Emit((byte)rex);
            }
        }

        private static SegKind FindMemSeg(Operand a, Operand b)
        {
            if (IsMem(a)) return a.Mem.Segment;
            if (IsMem(b)) return b.Mem.Segment;
            return SegKind.None;
        }

        // ==================== ModRM/SIB/位移 ====================

        /// <summary>发射寄存器形式的 ModRM（mod=11）。
        /// rmRexB 参数在此处不使用：REX.B 统一由调用方在 EmitLegacyAndRex 里发射
        /// （寄存器高位或内存基址），保留签名只为各调用点传参对称，勿在此重复发射。</summary>
        private void EmitModRmReg(int regField3, Reg rmReg, int rmRexB)
        {
            Emit((byte)(0xC0 | (regField3 << 3) | rmReg.Code));
        }

        /// <summary>发射memory操作数的 ModRM(+SIB)+位移。</summary>
        private void EmitMemModRm(MemOperand m, int regField3, int regRexR)
        {
            if (IsX64 && m.RipRelative)
            {
                // [rel label] → mod=00 rm=101 disp32（相对 RIP，基址无关）
                if (m.DispSymbol == null) Fail("[rel] requires a label symbol");
                ulong v;
                bool internalLabel;
                if (!TryResolveDisp(m.DispSymbol, out v, out internalLabel))
                    Fail("Undefined symbol: '" + m.DispSymbol + "'");
                if (!internalLabel)
                    Fail("[rel] is only valid for internal labels (for external symbols use mov+register indirection or [abs])");
                // 下一条指令地址 = 起始 + 已发射字节 + ModRM(1) + disp32(4)；
                // 这里 _buf.Count 只含 REX/opcode，漏掉 ModRM 会整体偏 1（原实现 +4）。
                long rel = (long)v - (long)_base - (long)(_startOffset + _buf.Count + 5);
                Emit((byte)(0x05 | (regField3 << 3)));
                EmitDword(rel);
                return;
            }

            bool hasBase = m.BaseReg >= 0;
            bool hasIndex = m.IndexReg >= 0;

            if (IsX64 && !hasBase && !hasIndex)
            {
                // x64 纯位移：数值地址 / 外部符号地址 ≤ uint32 → 自动 SIB 绝对形式；
                // 内部标签必须 [rel ...]；moffs 形式已由 mov 专用路径处理
                long disp;
                if (m.DispSymbol != null)
                {
                    ulong v;
                    bool internalLabel;
                    if (!TryResolveDisp(m.DispSymbol, out v, out internalLabel))
                        Fail("Undefined symbol: '" + m.DispSymbol + "'");
                    if (internalLabel && !m.ForceAbs)
                        Fail("on x64 use [rel label] for internal labels (RIP-relative)");
                    disp = (long)v;
                }
                else
                    disp = ResolveMemDisp(m);

                if (disp >= 0 && disp <= uint.MaxValue)
                {
                    Emit((byte)((0x00 << 6) | (regField3 << 3) | 0x04));
                    Emit(0x25);   // scale=0 index=none(100) base=101 → disp32 绝对
                    EmitDword(disp);
                    return;
                }
                if (!m.ForceAbs)
                    Fail("x64 absolute address exceeds 32 bits: mov al/eax/rax supports moffs ([abs symbol]), "
                        + "for other registers use mov rax, symbol + [rax] indirection");
                EmitAbsoluteX64(m, regField3);
                return;
            }

            if (!hasBase && !hasIndex)
            {
                // x86 纯位移: mod=00 rm=101 disp32 绝对
                long disp = ResolveMemDisp(m);
                Emit((byte)(0x05 | (regField3 << 3)));
                EmitDword(disp);
                return;
            }

            if (hasIndex && hasBase)
            {
                if (m.IndexReg % 8 == 4)
                    Fail(IsX64 ? "rsp/r12 cannot be an index register" : "esp cannot be an index register");
                long disp = ResolveMemDisp(m);
                int mod;
                if (m.BaseReg % 8 == 5 && (IsX64 || m.BaseReg == 5))
                {
                    // rbp/r13 作基址：mod=00 + SIB base=101 表示"no base"，必须用 disp
                    mod = (disp >= -128 && disp <= 127) ? 1 : 2;
                }
                else if (disp == 0 && m.DispSymbol == null)
                    mod = 0;
                else if (disp >= -128 && disp <= 127)
                    mod = 1;
                else
                    mod = 2;

                int sib = (ScaleBits(m.Scale) << 6) | ((m.IndexReg % 8) << 3) | (m.BaseReg % 8);
                Emit((byte)((mod << 6) | (regField3 << 3) | 0x04));
                Emit((byte)sib);
                EmitDispForMod(mod, disp);
                return;
            }

            // 仅 base（或仅 index 当作 base）
            int breg = hasBase ? m.BaseReg : m.IndexReg;
            if (!hasBase && hasIndex)
            {
                // 仅变址no base → SIB no base形式 (mod=0, base=101)
                if (breg % 8 == 4) Fail(IsX64 ? "rsp/r12 cannot be an index register" : "esp cannot be an index register");
                long disp = ResolveMemDisp(m);
                int sib = (ScaleBits(m.Scale) << 6) | ((breg % 8) << 3) | 0x05;
                Emit((byte)((0x00 << 6) | (regField3 << 3) | 0x04));
                Emit((byte)sib);
                EmitDword(disp);
                return;
            }

            // 仅 base
            if (breg % 8 == 4)
            {
                // rsp/r12 基址必须 SIB：rm=100 的含义就是"SIB 紧随其后"，
                // 旧写成 breg==4 只覆盖 rsp，[r12] 会漏掉 SIB 字节，后续字节被当 SIB 解释 → 整条流错位
                int mod;
                long disp = ResolveMemDisp(m);
                if (disp == 0 && m.DispSymbol == null) mod = 0;
                else if (disp >= -128 && disp <= 127) mod = 1;
                else mod = 2;
                Emit((byte)((mod << 6) | (regField3 << 3) | 0x04));
                Emit((byte)(0x00 << 6 | 0x04 << 3 | 0x04));   // scale=0, index=none(100), base=100
                EmitDispForMod(mod, disp);
                return;
            }
            if (breg % 8 == 5)
            {
                // rbp/r13 作基址：mod=00+rm101 会被解释为 disp32 绝对，必须 disp8=0
                long disp = ResolveMemDisp(m);
                int mod = (disp >= -128 && disp <= 127) ? 1 : 2;
                Emit((byte)((mod << 6) | (regField3 << 3) | 0x05));
                EmitDispForMod(mod, disp);
                return;
            }
            long d2 = ResolveMemDisp(m);
            int m2;
            if (d2 == 0 && m.DispSymbol == null) m2 = 0;
            else if (d2 >= -128 && d2 <= 127) m2 = 1;
            else m2 = 2;
            Emit((byte)((m2 << 6) | (regField3 << 3) | (breg % 8)));
            EmitDispForMod(m2, d2);
        }

        /// <summary>x64 [abs expr]：地址可编码 uint32 → SIB 绝对形式；否则报错（moffs 由 mov 专用路径处理）</summary>
        private void EmitAbsoluteX64(MemOperand m, int regField3)
        {
            long disp = ResolveMemDisp(m);
            if (disp >= 0 && disp <= uint.MaxValue)
            {
                Emit((byte)((0x00 << 6) | (regField3 << 3) | 0x04));
                Emit(0x25);   // scale=0 index=none(100) base=101 → disp32 绝对
                EmitDword(disp);
            }
            else
            {
                Fail("x64 absolute address exceeds 32 bits: only mov al/eax/rax supports the moffs form (mov rax,[abs symbol]), "
                    + "for others use mov rax, symbol + [rax] indirection");
            }
        }

        private void EmitDispForMod(int mod, long disp)
        {
            if (mod == 1) EmitI8(disp);
            else if (mod == 2) EmitDword(disp);
        }

        /// <summary>memory位移取值（数值 + 符号，可混合）</summary>
        private long ResolveMemDisp(MemOperand m)
        {
            long disp = m.Disp;
            if (m.DispSymbol != null)
                disp += SymbolDispValue(m);
            return disp;
        }

        private long SymbolDispValue(MemOperand m)
        {
            ulong v;
            bool internalLabel;
            if (!TryResolveDisp(m.DispSymbol, out v, out internalLabel))
                Fail("Undefined symbol: '" + m.DispSymbol + "'");
            if (internalLabel && _base == 0 && !m.RipRelative && !IsX64)
                Fail("Absolute addressing uses internal label '" + m.DispSymbol + "' requires a known BaseAddress");
            return (long)v;
        }

        private static int ScaleBits(int scale)
        {
            switch (scale)
            {
                case 1: return 0;
                case 2: return 1;
                case 4: return 2;
                case 8: return 3;
                default: return 0;
            }
        }

        // ==================== 指令分发 ====================

        private void EncodeCore(Statement st)
        {
            string m = st.Mnemonic;

            // data定义
            if (st.IsData) { EncodeData(st); return; }

            int n = st.Operands.Count;

            // ---- 无操作数 ----
            if (n == 0)
            {
                // 串操作（Intel 全名形式 与 AT&T 后缀剥离后的裸形式，尺寸来自 SizeHint）
                if (InstructionLexicon.IsStringOperation(m))
                {
                    EmitStringOp(st, m);
                    return;
                }
                switch (m)
                {
                    case "ret": EmitRet(); return;
                    case "leave": Emit(0xC9); return;
                    case "nop": Emit(0x90); return;
                    case "int3": Emit(0xCC); return;
                    case "stc": Emit(0xF9); return;
                    case "clc": Emit(0xF8); return;
                    case "cmc": Emit(0xF5); return;
                    case "cld": Emit(0xFC); return;
                    case "std": Emit(0xFD); return;
                    case "cbw": Emit(0x66, 0x98); return;
                    case "cwde": Emit(0x98); return;
                    case "cwd": Emit(0x66, 0x99); return;
                    case "cdq": Emit(0x99); return;
                    case "cdqe": if (!IsX64) Fail("cdqe only exists on x64"); Emit(0x48, 0x98); return;
                    case "cqo": if (!IsX64) Fail("cqo only exists on x64"); Emit(0x48, 0x99); return;
                    case "syscall":
                        if (!IsX64) Fail("syscall only exists on x64");
                        Emit(0x0F, 0x05); return;
                    case "sysenter": Emit(0x0F, 0x34); return;
                    default:
                        Fail("instruction '" + m + "' needs an operand or does not exist");
                        return;
                }
            }

            // ---- ret imm16 ----
            if (m == "ret" && n == 1 && IsImm(st.Operands[0]))
            {
                long v = ImmValue(st.Operands[0]);
                if (v < 0 || v > 65535) Fail("ret immediate exceeds 16-bit range");
                Emit(0xC2); EmitWord(v);
                return;
            }
            // ---- int imm8 ----
            if (m == "int" && n == 1 && IsImm(st.Operands[0]))
            {
                long v = ImmValue(st.Operands[0]);
                if (v < 0 || v > 255) Fail("int immediate exceeds 8-bit range");
                Emit(0xCD); EmitI8(v);
                return;
            }

            if (n == 1) EncodeOne(st, m, st.Operands[0]);
            else if (n == 2) EncodeTwo(st, m, st.Operands[0], st.Operands[1]);
            else if (n == 3) EncodeThree(st, m, st.Operands[0], st.Operands[1], st.Operands[2]);
        }

        private int StringSize(Statement st)
        {
            if (st.SizeHint != 0) return st.SizeHint;
            string m = st.Mnemonic;
            if (m.EndsWith("b")) return 1;
            if (m.EndsWith("w")) return 2;
            if (m.EndsWith("q")) return 8;
            if (m.EndsWith("d")) return 4;
            return 4;
        }

        /// <summary>串操作 opcode 映射（编码事实）：按家族基 opcode + 尺寸。
        /// movsd 在 x64 下与 SSE 助记符冲突，保持原判定提示改用 movsq。</summary>
        private void EmitStringOp(Statement st, string m)
        {
            int size = StringSize(st);
            if (m == "movsd" && IsX64) Fail("on x64 use movsq");
            if (m == "cmpsd" && IsX64) Fail("on x64 use cmpsq");

            byte opcode;
            switch (m)
            {
                case "movs": case "movsb": case "movsw": case "movsq": case "movsl":
                    opcode = 0xA4; break;
                case "stos": case "stosb": case "stosw": case "stosd": case "stosq":
                    opcode = size == 1 ? (byte)0xAA : (byte)0xAB; break;
                case "lods": case "lodsb": case "lodsw": case "lodsd": case "lodsq":
                    opcode = size == 1 ? (byte)0xAC : (byte)0xAD; break;
                case "scas": case "scasb": case "scasw": case "scasd": case "scasq":
                    opcode = size == 1 ? (byte)0xAE : (byte)0xAF; break;
                default:   /* cmps 系 */
                    opcode = size == 1 ? (byte)0xA6 : (byte)0xA7; break;
            }
            EmitString(st, opcode, size);
        }

        /// <summary>
        /// ret 编码：RetTrapAddress 非零时改写为 "write the gate address into [rsp] then ret"
        /// （不占用任何寄存器，解决 naive 内联代码的 ret 槽与 home space 冲突）。
        /// </summary>
        private void EmitRet()
        {
            if (_retTrap == 0)
            {
                Emit(0xC3);
                return;
            }
            unchecked
            {
                long addr = (long)_retTrap;
                // mov dword [rsp], low32
                Emit(0xC7); Emit(0x04); Emit(0x24);
                EmitDword(addr & 0xFFFFFFFF);
                if (IsX64)
                {
                    // mov dword [rsp+4], high32
                    Emit(0xC7); Emit(0x44); Emit(0x24); Emit(0x04);
                    EmitDword((addr >> 32) & 0xFFFFFFFF);
                }
                Emit(0xC3);
            }
        }

        // ==================== 单操作数 ====================

        private void EncodeOne(Statement st, string m, Operand o)
        {
            switch (m)
            {
                case "push":
                {
                    Reg r;
                    if (IsReg(o, out r))
                    {
                        if (r.Size == 1) Fail("push does not support 8-bit registers");
                        if (!IsX64 && r.Size == 8) Fail("invalid 64-bit register on x86");
                        // push r 在 x64 下默认 64 位，无需 REX.W
                        EmitLegacyAndRex(false, 0, 0, r.RexBit, SegKind.None, r.Size == 2, st.RepKind, false);
                        Emit((byte)(0x50 + r.Code));
                        return;
                    }
                    if (IsMem(o))
                    {
                        int size = o.Size;
                        if (size == 0) size = IsX64 ? 8 : 4;    // 栈槽尺寸无歧义，可缺省
                        if (!IsX64 && size == 8) Fail("invalid qword on x86");
                        EmitLegacyAndRex(false, 0, RexIndexOfMemIndex(o.Mem), RexIndexOfMemBase(o.Mem),
                            o.Mem.Segment, size == 2, st.RepKind, false);
                        Emit(0xFF);                 // FF /6 = push r/m（原缺此字节：push [r8] 被编成 41 30 = xor [r8], dh）
                        EmitMemModRm(o.Mem, 6, 0);
                        return;
                    }
                    if (IsImm(o))
                    {
                        long v = ImmValue(o);
                        if (v >= -128 && v <= 127) { Emit(0x6A); EmitI8(v); }
                        else if (ImmFitsSigned(v, 4) || (v >= 0 && v <= uint.MaxValue))
                        {
                            if (ImmFitsSigned(v, 2) && o.Size == 2) { Emit(0x66, 0x68); EmitWord(v); }
                            else { Emit(0x68); EmitDword(v); }
                        }
                        else Fail("push immediate exceeds 32-bit range");
                        return;
                    }
                    Fail("Invalid push operand");
                    return;
                }
                case "pop":
                {
                    Reg r;
                    if (IsReg(o, out r))
                    {
                        if (r.Size == 1) Fail("pop does not support 8-bit registers");
                        if (!IsX64 && r.Size == 8) Fail("invalid 64-bit register on x86");
                        EmitLegacyAndRex(false, 0, 0, r.RexBit, SegKind.None, r.Size == 2, st.RepKind, false);
                        Emit((byte)(0x58 + r.Code));
                        return;
                    }
                    if (IsMem(o))
                    {
                        int size = o.Size;
                        if (size == 0) size = IsX64 ? 8 : 4;
                        if (!IsX64 && size == 8) Fail("invalid qword on x86");
                        EmitLegacyAndRex(false, 0, RexIndexOfMemIndex(o.Mem), RexIndexOfMemBase(o.Mem),
                            o.Mem.Segment, size == 2, st.RepKind, false);
                        /* pop r/m 的 opcode 是 8F（/0），不是 FF：
                         * FF /0 是 inc r/m —— 上一轮补 opcode 时误写成 FF，
                         * 于是 pop qword ptr [rax] 静默编成 inc qword ptr [rax]
                         * （gcc/as 实测：popl (%eax) = 8F 00，16 位再加 66）。 */
                        Emit(0x8F);
                        EmitMemModRm(o.Mem, 0, 0);
                        return;
                    }
                    Fail("Invalid pop operand");
                    return;
                }
                case "jmp":
                case "call":
                {
                    Reg r;
                    bool isCall = m == "call";
                    if (IsReg(o, out r))
                    {
                        if (r.Size != 4 && r.Size != 8) Fail("Indirect jump register must be 32/64-bit");
                        if (!IsX64 && r.Size == 8) Fail("invalid 64-bit register on x86");
                        EmitLegacyAndRex(false, 0, 0, r.RexBit, SegKind.None, false, st.RepKind, false);
                        Emit(0xFF);
                        EmitModRmReg(isCall ? 2 : 4, r, r.RexBit);
                        return;
                    }
                    if (IsMem(o))
                    {
                        int size = o.Size;
                        if (size == 0) size = IsX64 ? 8 : 4;    // EIP/RIP 宽度无歧义，可缺省
                        if (!IsX64 && size == 8) Fail("invalid qword on x86");
                        EmitLegacyAndRex(false, 0, RexIndexOfMemIndex(o.Mem), RexIndexOfMemBase(o.Mem),
                            o.Mem.Segment, size == 2, st.RepKind, false);
                        Emit(0xFF);
                        EmitMemModRm(o.Mem, isCall ? 2 : 4, 0);
                        return;
                    }
                    // 直接分支（标签或数值）
                    EmitRelBranch(m, o, isCall);
                    return;
                }
                default: break;
            }

            // ---- jcc ----
            if (m.Length >= 2 && m[0] == 'j' && m != "jmp")
            {
                byte cc;
                if (InstructionTables.TryGetCC(m.Substring(1), out cc))
                {
                    EmitJcc(cc, o);
                    return;
                }
            }

            // ---- setcc ----
            if (m.Length >= 4 && m.StartsWith("set"))
            {
                byte ccSet;
                if (!InstructionTables.TryGetCC(m.Substring(3), out ccSet))
                    goto afterSetcc;
                byte cc = ccSet;
                Reg r;
                if (IsReg(o, out r))
                {
                    if (r.Size != 1) Fail("setcc destination must be an 8-bit register");
                    EmitLegacyAndRex(false, 0, 0, r.RexBit, SegKind.None, false, st.RepKind, false);
                    Emit(0x0F, (byte)(0x90 + cc));
                    EmitModRmReg(0, r, r.RexBit);
                    return;
                }
                if (IsMem(o))
                {
                    if (o.Size != 0 && o.Size != 1) Fail("setcc memory destination must be byte");
                    EmitLegacyAndRex(false, 0, RexIndexOfMemIndex(o.Mem), RexIndexOfMemBase(o.Mem),
                        o.Mem.Segment, false, st.RepKind, false);
                    Emit(0x0F, (byte)(0x90 + cc));
                    EmitMemModRm(o.Mem, 0, 0);
                    return;
                }
                Fail("setcc destination must be a register or memory");
                return;
            }
        afterSetcc:

            // ---- loop 系 ----
            if (m == "loop" || m == "loope" || m == "loopz" || m == "loopne" || m == "loopnz")
            {
                byte op;
                if (m == "loop") op = 0xE2;
                else if (m == "loope" || m == "loopz") op = 0xE1;
                else op = 0xE0;
                Reg loopRegCheck;
                if (IsReg(o, out loopRegCheck)) Fail("loop target can only be a label");
                EmitRel8Branch(op, o, "loop");
                return;
            }

            // ---- 移位（单操作数形式: 移位计数为 1）----
            int shiftDigit;
            if (InstructionTables.TryGetShift(m, out shiftDigit))
            {
                EncodeShift(st, m, o, Operand.ImmOp(1, 1));
                return;
            }

            // ---- 一元组 not/neg/mul/imul/div/idiv ----
            int unaryDigit;
            if (InstructionTables.TryGetUnary(m, out unaryDigit))
            {
                Reg r;
                if (IsReg(o, out r))
                {
                    if (!IsX64 && r.Size == 8) Fail("invalid 64-bit register on x86");
                    bool w = r.Size == 8;
                    EmitLegacyAndRex(w, 0, 0, r.RexBit, SegKind.None, r.Size == 2, st.RepKind, CheckLock(st, o));
                    Emit(r.Size == 1 ? (byte)0xF6 : (byte)0xF7);
                    EmitModRmReg(unaryDigit, r, r.RexBit);
                    return;
                }
                if (IsMem(o))
                {
                    int size = o.Size;
                    if (size == 0) Fail(m + ": memory operand needs a size declaration");
                    bool w = size == 8;
                    if (!IsX64 && w) Fail("invalid qword on x86");
                    EmitLegacyAndRex(w, 0, RexIndexOfMemIndex(o.Mem), RexIndexOfMemBase(o.Mem),
                        o.Mem.Segment, size == 2, st.RepKind, CheckLock(st, o));
                    Emit(size == 1 ? (byte)0xF6 : (byte)0xF7);
                    EmitMemModRm(o.Mem, unaryDigit, 0);
                    return;
                }
                Fail(m + ": operand must be a register or memory");
                return;
            }

            // ---- inc/dec 单寄存器或memory ----
            if (m == "inc" || m == "dec")
            {
                EncodeIncDec(st, m, o);
                return;
            }

            // ---- bswap ----
            if (m == "bswap")
            {
                Reg r;
                if (IsReg(o, out r))
                {
                    if (r.Size != 4 && r.Size != 8) Fail("bswap needs a 32/64-bit register");
                    if (!IsX64 && r.Size == 8) Fail("invalid 64-bit register on x86");
                    EmitLegacyAndRex(r.Size == 8, 0, 0, r.RexBit, SegKind.None, false, st.RepKind, false);
                    Emit(0x0F, (byte)(0xC8 + r.Code));
                    return;
                }
                Fail("bswap destination must be a register");
                return;
            }

            Fail("Cannot encode instruction '" + m + "' (unknown or unsupported operand form)");
        }

        private void EmitString(Statement st, byte opcode, int size)
        {
            bool w = size == 8;
            if (!IsX64 && w) Fail("invalid qword string operation on x86");
            EmitLegacyAndRex(w, 0, 0, 0, SegKind.None, size == 2, st.RepKind, false);
            Emit(opcode);
        }

        private void EncodeIncDec(Statement st, string m, Operand o)
        {
            int digit = m == "inc" ? 0 : 1;
            Reg r;
            if (IsReg(o, out r))
            {
                if (!IsX64 && r.Size == 8) Fail("invalid 64-bit register on x86");
                // x86 32 位寄存器用短形式 40+r / 48+r；x64 无此形式（40+ 即 REX）
                if (!IsX64 && r.Size == 4 && !r.High)
                {
                    Emit((byte)(0x40 + digit * 8 + r.Code));
                    return;
                }
                if (!IsX64 && r.Size == 2)
                {
                    Emit(0x66);
                    Emit((byte)(0x40 + digit * 8 + r.Code));
                    return;
                }
                bool w = r.Size == 8;
                EmitLegacyAndRex(w, 0, 0, r.RexBit, SegKind.None, r.Size == 2, st.RepKind, CheckLock(st, o));
                Emit(r.Size == 1 ? (byte)0xFE : (byte)0xFF);
                EmitModRmReg(digit, r, r.RexBit);
                return;
            }
            if (IsMem(o))
            {
                int size = o.Size;
                if (size == 0) Fail(m + ": memory operand needs a size declaration");
                bool w = size == 8;
                if (!IsX64 && w) Fail("invalid qword on x86");
                EmitLegacyAndRex(w, 0, RexIndexOfMemIndex(o.Mem), RexIndexOfMemBase(o.Mem),
                    o.Mem.Segment, size == 2, st.RepKind, CheckLock(st, o));
                Emit(size == 1 ? (byte)0xFE : (byte)0xFF);
                EmitMemModRm(o.Mem, digit, 0);
                return;
            }
            Fail(m + ": operand must be a register or memory");
        }

        // ==================== 双操作数 ====================

        private void EncodeTwo(Statement st, string m, Operand dst, Operand src)
        {
            // mov 家族
            switch (m)
            {
                case "mov": EncodeMov(st, dst, src); return;
                case "movabs":
                case "movabsq": EncodeMovAbs(st, dst, src); return;
                case "lea": EncodeLea(st, dst, src); return;
                case "movzx": case "movzb": EncodeMovx(st, "movzx", dst, src, false); return;
                case "movsx": EncodeMovx(st, "movsx", dst, src, false); return;
                case "movsxd": EncodeMovx(st, "movsxd", dst, src, true); return;
                case "xchg": EncodeXchg(st, dst, src); return;
                default: break;
            }

            // Group1: add/or/adc/sbb/and/sub/xor/cmp
            int digit; byte op8; byte op32;
            if (InstructionTables.TryGetGroup1(m, out digit, out op8, out op32))
            {
                EncodeGroup1(st, m, dst, src, digit, op8, op32);
                return;
            }

            // test
            if (m == "test") { EncodeTest(st, dst, src); return; }

            // 移位 r/m, count
            int shiftDigit;
            if (InstructionTables.TryGetShift(m, out shiftDigit))
            {
                EncodeShift(st, m, dst, src);
                return;
            }

            // imul r, r/m（双操作数形式）
            if (m == "imul") { EncodeImul2(st, dst, src); return; }

            // bt 组寄存器形式
            int btDigit;
            if (InstructionTables.TryGetBitTestImm(m, out btDigit))
            {
                EncodeBtReg(st, m, dst, src, btDigit);
                return;
            }

            // bsf/bsr
            if (m == "bsf" || m == "bsr")
            {
                Reg rd, rs;
                int ds = CheckSize2(dst, src, m);
                if (ds == 1) Fail(m + " does not support 8-bit operands");
                if (!IsReg(dst, out rd)) Fail(m + ": first operand must be a register");
                if (!IsX64 && ds == 8) Fail("invalid 64-bit register on x86");
                EmitLegacyAndRex(ds == 8, rd.RexBit, RexIdxOf(src), RexBaseOf(src),
                    FindMemSeg(dst, src), ds == 2, st.RepKind, false);
                Emit(0x0F, m == "bsf" ? (byte)0xBC : (byte)0xBD);
                if (IsReg(src, out rs)) EmitModRmReg(rd.Code, rs, rs.RexBit);
                else EmitMemModRm(src.Mem, rd.Code, rd.RexBit);
                return;
            }

            // xadd
            if (m == "xadd")
            {
                int ds = CheckSize2(dst, src, m);
                Reg rd, rs;
                bool dstIsReg = IsReg(dst, out rd);
                bool srcIsReg = IsReg(src, out rs);
                if (!srcIsReg) Fail("xadd: second operand must be a register");
                if (!IsX64 && ds == 8) Fail("invalid 64-bit register on x86");
                EmitLegacyAndRex(ds == 8, rs.RexBit,
                    dstIsReg ? 0 : RexIndexOfMemIndex(dst.Mem),
                    dstIsReg ? (dst.Reg.RexBit) : RexIndexOfMemBase(dst.Mem),
                    FindMemSeg(dst, src), ds == 2, st.RepKind, CheckLock(st, dst));
                Emit(0x0F, 0xC1);
                if (dstIsReg) EmitModRmReg(rs.Code, rd, rd.RexBit);
                else EmitMemModRm(dst.Mem, rs.Code, rs.RexBit);
                return;
            }

            Fail("Cannot encode instruction '" + m + "' (unknown or unsupported operand form)");
        }

        // ---- mov ----
        private void EncodeMov(Statement st, Operand dst, Operand src)
        {
            Reg rd, rs;
            bool dstReg = IsReg(dst, out rd);
            bool srcReg = IsReg(src, out rs);

            // moffs 形式（AL/EAX/RAX 与纯绝对地址）
            if (dstReg && IsMem(src) && TryEmitMoffs(st, rd, src, true)) return;
            if (srcReg && IsMem(dst) && TryEmitMoffs(st, rs, dst, false)) return;

            if (dstReg && srcReg)
            {
                int size = CheckSize2(dst, src, "mov");
                if (rd.High || rs.High)
                {
                    if (size != 1) Fail("AH/CH/DH/BH are only valid for 8-bit operations");
                    if (IsX64 && (rd.Index >= 8 || rs.Index >= 8)) Fail("on x64 AH/BH/CH/DH cannot be used together with R8-R15");
                    EmitLegacyAndRex(false, 0, 0, 0, SegKind.None, false, st.RepKind, false);
                    Emit(0x88);
                    EmitModRmReg(rs.Code, rd, 0);
                    return;
                }
                if (!IsX64 && (rd.Size == 8 || rs.Size == 8)) Fail("invalid 64-bit register on x86");
                bool w = size == 8;
                EmitLegacyAndRex(w, rs.RexBit, 0, rd.RexBit, SegKind.None, size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0x88 : (byte)0x89);
                EmitModRmReg(rs.Code, rd, rd.RexBit);
                return;
            }
            if (dstReg && IsMem(src))
            {
                int size = CheckSize2(dst, src, "mov");
                if (!IsX64 && size == 8) Fail("invalid 64-bit register on x86");
                bool w = size == 8;
                EmitLegacyAndRex(w, rd.RexBit, RexIndexOfMemIndex(src.Mem), RexIndexOfMemBase(src.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0x8A : (byte)0x8B);
                EmitMemModRm(src.Mem, rd.Code, rd.RexBit);
                return;
            }
            if (IsMem(dst) && srcReg)
            {
                int size = CheckSize2(dst, src, "mov");
                if (!IsX64 && size == 8) Fail("invalid 64-bit register on x86");
                bool w = size == 8;
                EmitLegacyAndRex(w, rs.RexBit, RexIndexOfMemIndex(dst.Mem), RexIndexOfMemBase(dst.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0x88 : (byte)0x89);
                EmitMemModRm(dst.Mem, rs.Code, rs.RexBit);
                return;
            }
            if (dstReg && IsImm(src))
            {
                if (!IsX64 && rd.Size == 8) Fail("invalid 64-bit register on x86");
                long v = ImmValue(src);
                if (rd.High)
                {
                    if (!ImmFitsSigned(v, 1) && !ImmFitsUnsigned(v, 1)) Fail("8-bit immediate overflow");
                    Emit((byte)(0xB0 + rd.Code));
                    EmitI8(v);
                    return;
                }
                switch (rd.Size)
                {
                    case 1:
                        if (!ImmFitsSigned(v, 1) && !ImmFitsUnsigned(v, 1)) Fail("8-bit immediate overflow");
                        EmitLegacyAndRex(false, 0, 0, rd.RexBit, SegKind.None, false, st.RepKind, false);
                        Emit((byte)(0xB0 + rd.Code));
                        EmitI8(v);
                        return;
                    case 2:
                        if (!ImmFitsSigned(v, 2) && !ImmFitsUnsigned(v, 2)) Fail("16-bit immediate overflow");
                        EmitLegacyAndRex(false, 0, 0, rd.RexBit, SegKind.None, true, st.RepKind, false);
                        Emit((byte)(0xB8 + rd.Code));
                        EmitWord(v);
                        return;
                    case 4:
                        if (!ImmFitsSigned(v, 4) && !ImmFitsUnsigned(v, 4))
                        {
                            if (IsX64) Fail("32-bit register immediate overflow (use a 64-bit register)");
                            Fail("32-bit immediate overflow");
                        }
                        EmitLegacyAndRex(false, 0, 0, rd.RexBit, SegKind.None, false, st.RepKind, false);
                        Emit((byte)(0xB8 + rd.Code));
                        EmitDword(v);
                        return;
                    case 8:
                        if (!IsX64) Fail("invalid 64-bit register on x86");
                        if (src.LabelName != null || !ImmFitsSigned(v, 4))
                        {
                            EmitLegacyAndRex(true, 0, 0, rd.RexBit, SegKind.None, false, st.RepKind, false);
                            Emit((byte)(0xB8 + rd.Code));
                            EmitQword((ulong)v);
                        }
                        else
                        {
                            // mov r64, imm32（符号扩展）较短的 C7 形式
                            EmitLegacyAndRex(true, 0, 0, rd.RexBit, SegKind.None, false, st.RepKind, false);
                            Emit(0xC7);
                            EmitModRmReg(0, rd, rd.RexBit);
                            EmitDword(v);
                        }
                        return;
                    default:
                        Fail("Invalid register size");
                        return;
                }
            }
            if (IsMem(dst) && IsImm(src))
            {
                int size = dst.Size;
                if (size == 0) Fail("mov: writing an immediate to memory needs a size declaration (byte/word/dword/qword ptr)");
                long v = ImmValue(src);
                if (!IsX64 && size == 8) Fail("invalid qword on x86");
                bool w = size == 8;
                if (size == 8 && !ImmFitsSigned(v, 4)) Fail("qword memory immediate must fit in a signed 32-bit value");
                if (size == 4 && !ImmFitsSigned(v, 4) && !ImmFitsUnsigned(v, 4)) Fail("32-bit immediate overflow");
                if (size == 2 && !ImmFitsSigned(v, 2) && !ImmFitsUnsigned(v, 2)) Fail("16-bit immediate overflow");
                if (size == 1 && !ImmFitsSigned(v, 1) && !ImmFitsUnsigned(v, 1)) Fail("8-bit immediate overflow");
                EmitLegacyAndRex(w, 0, RexIndexOfMemIndex(dst.Mem), RexIndexOfMemBase(dst.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0xC6 : (byte)0xC7);
                EmitMemModRm(dst.Mem, 0, 0);
                if (size == 1) EmitI8(v);
                else if (size == 2) EmitWord(v);
                else EmitDword(v);
                return;
            }
            Fail("mov: unsupported operand combination");
        }

        /// <summary>
        /// 尝试 moffs 形式: mov al/eax/rax, [abs] 或 mov [abs], al/eax/rax。
        /// 返回 false 表示该组合不适用 moffs（调用方继续常规编码）。
        /// </summary>
        private bool TryEmitMoffs(Statement st, Reg r, Operand memOp, bool regIsDst)
        {
            MemOperand mo = memOp.Mem;
            if (mo.BaseReg >= 0 || mo.IndexReg >= 0 || mo.RipRelative) return false;
            if (!mo.ForceAbs && !IsPureDisp(mo)) return false;

            int size = r.Size;
            if (size == 2) return false;
            if (!IsX64 && size == 8) return false;
            if (r.Index != 0 || r.High) return false;   // moffs 形式只允许 AL/EAX/RAX，其余寄存器走通用路径

            // x86: AL/EAX 用 moffs 更短；x64: 地址超 32 位时必须 moffs（仅 AL/EAX/RAX）
            bool needMoffs = false, preferMoffs = false;
            if (!IsX64)
            {
                if (size == 1 || size == 4) preferMoffs = true;
            }
            else
            {
                long probeDisp = ResolveMemDisp(mo);
                if (probeDisp < 0 || probeDisp > uint.MaxValue) needMoffs = true;
            }
            if (!needMoffs && !preferMoffs) return false;

            // memory侧尺寸检查（寄存器已给出操作尺寸，缺省声明是允许的）
            if (memOp.Size != 0 && memOp.Size != size) Fail("mov: operand size mismatch");

            byte opcode;
            if (regIsDst)
            {
                // 装载: reg ← [moffs]  (A0/A1)
                opcode = size == 1 ? (byte)0xA0 : (byte)0xA1;
            }
            else
            {
                // 存储: [moffs] ← reg  (A2/A3)
                opcode = size == 1 ? (byte)0xA2 : (byte)0xA3;
            }

            bool w = size == 8;
            EmitLegacyAndRex(w, 0, 0, 0, mo.Segment, false, st.RepKind, false);
            Emit(opcode);
            long disp = ResolveMemDisp(mo);
            if (IsX64) EmitQword((ulong)disp);
            else EmitDword(disp);
            return true;
        }

        private static bool IsPureDisp(MemOperand m)
        {
            return m.BaseReg < 0 && m.IndexReg < 0 && !m.RipRelative;
        }

        // ---- movabs ----
        private void EncodeMovAbs(Statement st, Operand dst, Operand src)
        {
            if (!IsX64) Fail("movabs only exists on x64");
            Reg rd;
            if (!IsReg(dst, out rd) || rd.Size != 8) Fail("movabs: destination must be a 64-bit register");
            if (!IsImm(src)) Fail("movabs: source must be an immediate");
            long v = ImmValue(src);
            EmitLegacyAndRex(true, 0, 0, rd.RexBit, SegKind.None, false, st.RepKind, false);
            Emit((byte)(0xB8 + rd.Code));
            EmitQword((ulong)v);
        }

        // ---- lea ----
        private void EncodeLea(Statement st, Operand dst, Operand src)
        {
            Reg rd;
            if (!IsReg(dst, out rd)) Fail("lea: first operand must be a register");
            if (!IsMem(src)) Fail("lea: second operand must be memory");
            int size = rd.Size;
            if (!IsX64 && size == 8) Fail("invalid 64-bit register on x86");
            if (src.Size != 0 && src.Size != rd.Size && !(IsX64 && src.Size == 8 && rd.Size == 4))
                Fail("lea: operand size mismatch");
            EmitLegacyAndRex(size == 8, rd.RexBit, RexIndexOfMemIndex(src.Mem), RexIndexOfMemBase(src.Mem),
                src.Mem.Segment, size == 2, st.RepKind, false);
            Emit(0x8D);
            EmitMemModRm(src.Mem, rd.Code, rd.RexBit);
        }

        // ---- movzx / movsx / movsxd ----
        private void EncodeMovx(Statement st, string m, Operand dst, Operand src, bool isSxd)
        {
            Reg rd, rs;
            if (!IsReg(dst, out rd)) Fail(m + ": destination must be a register");
            int srcSize = 0;
            bool srcIsReg = IsReg(src, out rs);
            if (isSxd)
            {
                if (!IsX64) Fail("movsxd only exists on x64");
                if (rd.Size != 8) Fail("movsxd: destination must be a 64-bit register");
                srcSize = 4;
                if (srcIsReg && rs.Size != 4) Fail("movsxd: source must be a 32-bit register");
                if (IsMem(src) && src.Size != 0 && src.Size != 4) Fail("movsxd: source must be dword");
            }
            else if (st.SrcSizeHint != 0)
            {
                srcSize = st.SrcSizeHint;
                if (st.DstSizeHint != 0 && st.DstSizeHint != rd.Size)
                    Fail(m + ": destination size does not match register");
            }
            else
            {
                if (srcIsReg) srcSize = rs.Size;
                else if (IsMem(src) && src.Size != 0) srcSize = src.Size;
                else Fail(m + ": cannot determine source operand size");
                if (srcSize == rd.Size) Fail(m + ": source and destination sizes are the same (no extension needed)");
            }
            if (!isSxd && srcSize != 1 && srcSize != 2) Fail(m + ": source operand must be 8/16-bit");
            if (rd.Size != 2 && rd.Size != 4 && rd.Size != 8) Fail(m + ": destination must be a 16/32/64-bit register");
            if (!IsX64 && rd.Size == 8) Fail("invalid 64-bit register on x86");

            bool w = rd.Size == 8;
            bool need66 = rd.Size == 2;
            byte op;
            if (isSxd) op = 0x63;
            else if (m == "movzx") op = srcSize == 1 ? (byte)0xB6 : (byte)0xB7;
            else op = srcSize == 1 ? (byte)0xBE : (byte)0xBF;

            EmitLegacyAndRex(w || isSxd, rd.RexBit,
                srcIsReg ? 0 : RexIndexOfMemIndex(src.Mem),
                srcIsReg ? RegLowBit(src) : RexIndexOfMemBase(src.Mem),
                FindMemSeg(dst, src), need66, st.RepKind, false);
            Emit(0x0F, op);
            if (srcIsReg) EmitModRmReg(rd.Code, rs, rs.RexBit);
            else EmitMemModRm(src.Mem, rd.Code, rd.RexBit);
        }

        // ---- xchg ----
        private void EncodeXchg(Statement st, Operand a, Operand b)
        {
            int size = CheckSize2(a, b, "xchg");
            Reg ra, rb;
            bool aReg = IsReg(a, out ra);
            bool bReg = IsReg(b, out rb);

            if (aReg && bReg)
            {
                if (!IsX64 && (ra.Size == 8 || rb.Size == 8)) Fail("invalid 64-bit register on x86");
                // 短形式: 与 AX/EAX/RAX 交换
                if ((ra.Index == 0 && !ra.High && rb.Index != 0 && !rb.High) ||
                    (rb.Index == 0 && !rb.High && ra.Index != 0 && !ra.High))
                {
                    Reg other = ra.Index == 0 ? rb : ra;
                    bool w = size == 8;
                    EmitLegacyAndRex(w, 0, 0, other.RexBit, SegKind.None, size == 2, st.RepKind, false);
                    Emit((byte)(0x90 + other.Code));
                    return;
                }
                if (ra.High || rb.High)
                {
                    EmitLegacyAndRex(false, 0, 0, 0, SegKind.None, false, st.RepKind, false);
                    Emit(0x86);
                    EmitModRmReg(ra.Code, rb, 0);
                    return;
                }
                bool w2 = size == 8;
                EmitLegacyAndRex(w2, ra.RexBit, 0, rb.RexBit, SegKind.None, size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0x86 : (byte)0x87);
                EmitModRmReg(ra.Code, rb, rb.RexBit);
                return;
            }
            if (aReg && IsMem(b))
            {
                EmitXchgMem(st, ra, b, size);
                return;
            }
            if (bReg && IsMem(a))
            {
                EmitXchgMem(st, rb, a, size);
                return;
            }
            Fail("xchg: unsupported operand combination");
        }

        private void EmitXchgMem(Statement st, Reg r, Operand memOp, int size)
        {
            if (!IsX64 && size == 8) Fail("invalid 64-bit register on x86");
            EmitLegacyAndRex(size == 8, r.RexBit,
                RexIndexOfMemIndex(memOp.Mem), RexIndexOfMemBase(memOp.Mem),
                FindMemSeg(memOp, null), size == 2, st.RepKind, CheckLock(st, memOp));
            Emit(size == 1 ? (byte)0x86 : (byte)0x87);
            EmitMemModRm(memOp.Mem, r.Code, r.RexBit);
        }

        // ---- Group1 ----
        private void EncodeGroup1(Statement st, string m, Operand dst, Operand src,
            int digit, byte op8, byte op32)
        {
            Reg rd, rs;
            bool dstReg = IsReg(dst, out rd);
            bool srcReg = IsReg(src, out rs);
            bool lockable = CheckLock(st, dst);

            if (dstReg && srcReg)
            {
                int size = CheckSize2(dst, src, m);
                if (!IsX64 && (rd.Size == 8 || rs.Size == 8)) Fail("invalid 64-bit register on x86");
                bool w = size == 8;
                EmitLegacyAndRex(w, rs.RexBit, 0, rd.RexBit, SegKind.None, size == 2, st.RepKind, false);
                Emit(size == 1 ? op8 : op32);
                EmitModRmReg(rs.Code, rd, rd.RexBit);
                return;
            }
            if (dstReg && IsMem(src))
            {
                int size = CheckSize2(dst, src, m);
                if (!IsX64 && size == 8) Fail("invalid 64-bit register on x86");
                bool w = size == 8;
                EmitLegacyAndRex(w, rd.RexBit, RexIndexOfMemIndex(src.Mem), RexIndexOfMemBase(src.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)(op8 + 2) : (byte)(op32 + 2));
                EmitMemModRm(src.Mem, rd.Code, rd.RexBit);
                return;
            }
            if (IsMem(dst) && srcReg)
            {
                int size = CheckSize2(dst, src, m);
                if (!IsX64 && size == 8) Fail("invalid 64-bit register on x86");
                bool w = size == 8;
                EmitLegacyAndRex(w, rs.RexBit, RexIndexOfMemIndex(dst.Mem), RexIndexOfMemBase(dst.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, lockable);
                Emit(size == 1 ? op8 : op32);
                EmitMemModRm(dst.Mem, rs.Code, rs.RexBit);
                return;
            }
            // AL/EAX/RAX 短形式: 仅在它更短时使用
            // - 8 位(AL): 04 ib 恒最短 → 直接用
            // - 16/32/64: imm 可放入 int8 时用 83 (r/m,imm8) 更短 → 落到下方通用路径;
            //             否则 05 id 短形式 (比 81 少 1 bytes)
            if (dstReg && rd.Index == 0 && !rd.High && IsImm(src))
            {
                int size = OperandSize(dst) != 0 ? OperandSize(dst) : OperandSize(src);
                if (size == 0) Fail(m + ": cannot determine operand size");
                if (!IsX64 && size == 8) Fail("invalid 64-bit operation on x86");
                long v = ImmValue(src);
                if (size == 1)
                {
                    if (!ImmFitsSigned(v, 1) && !ImmFitsUnsigned(v, 1)) Fail("8-bit immediate overflow");
                    Emit((byte)(op8 + 4)); EmitI8(v);
                    return;
                }
                if (!ImmFitsSigned(v, 1))
                {
                    bool w = size == 8;
                    if (size == 2)
                    {
                        if (!ImmFitsSigned(v, 2) && !ImmFitsUnsigned(v, 2)) Fail("16-bit immediate overflow");
                        Emit(0x66, (byte)(op32 + 4));
                        EmitWord(v);
                        return;
                    }
                    if (!ImmFitsSigned(v, 4)) Fail(m + ": immediate exceeds signed 32-bit range");
                    EmitLegacyAndRex(w, 0, 0, 0, SegKind.None, false, st.RepKind, false);
                    Emit((byte)(op32 + 4));
                    EmitDword(v);
                    return;
                }
                // imm8 可放下 → 继续走下方 r/m,imm8 (83) 路径
            }
            // r/m, imm
            if ((dstReg || IsMem(dst)) && IsImm(src))
            {
                int size = OperandSize(dst);
                if (size == 0) Fail(m + ": cannot determine operand size (memory needs a size declaration)");
                if (!IsX64 && size == 8) Fail("invalid 64-bit operation on x86");
                long v = ImmValue(src);
                bool w = size == 8;
                if (size == 1)
                {
                    if (!ImmFitsSigned(v, 1) && !ImmFitsUnsigned(v, 1)) Fail("8-bit immediate overflow");
                    EmitLegacyAndRex(false, 0, RexIdxOf(dst), RexBaseOf(dst),
                        FindMemSeg(dst, src), false, st.RepKind, lockable);
                    Emit(0x80);
                    EmitRm(dst, digit);
                    EmitI8(v);
                    return;
                }
                if (size == 2 && (!ImmFitsSigned(v, 2) && !ImmFitsUnsigned(v, 2))) Fail("16-bit immediate overflow");
                if (size >= 4 && !ImmFitsSigned(v, 4)) Fail(m + ": immediate exceeds signed 32-bit range");
                bool use83 = ImmFitsSigned(v, 1) && (size == 2 || size >= 4);
                EmitLegacyAndRex(w, 0, RexIdxOf(dst), RexBaseOf(dst),
                    FindMemSeg(dst, src), size == 2, st.RepKind, lockable);
                Emit(use83 ? (byte)0x83 : (byte)0x81);
                EmitRm(dst, digit);
                if (use83) EmitI8(v);
                else if (size == 2) EmitWord(v);
                else EmitDword(v);
                return;
            }
            Fail(m + ": unsupported operand combination");
        }

        // ---- test ----
        private void EncodeTest(Statement st, Operand dst, Operand src)
        {
            Reg rd, rs;
            bool dstReg = IsReg(dst, out rd);
            bool srcReg = IsReg(src, out rs);

            if (dstReg && srcReg)
            {
                int size = CheckSize2(dst, src, "test");
                if (!IsX64 && (rd.Size == 8 || rs.Size == 8)) Fail("invalid 64-bit register on x86");
                bool w = size == 8;
                EmitLegacyAndRex(w, rs.RexBit, 0, rd.RexBit, SegKind.None, size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0x84 : (byte)0x85);
                EmitModRmReg(rs.Code, rd, rd.RexBit);
                return;
            }
            if (dstReg && rd.Index == 0 && !rd.High && IsImm(src))
            {
                int size = OperandSize(dst);
                long v = ImmValue(src);
                if (size == 1)
                {
                    if (!ImmFitsSigned(v, 1) && !ImmFitsUnsigned(v, 1)) Fail("8-bit immediate overflow");
                    Emit(0xA8); EmitI8(v);
                    return;
                }
                if (!IsX64 && size == 8) Fail("invalid 64-bit operation on x86");
                if (!ImmFitsSigned(v, 4) && !ImmFitsUnsigned(v, 4)) Fail("Immediate overflow");
                bool w = size == 8;
                EmitLegacyAndRex(w, 0, 0, 0, SegKind.None, size == 2, st.RepKind, false);
                Emit(0xA9);
                if (size == 2) EmitWord(v);
                else EmitDword(v);
                return;
            }
            if ((dstReg || IsMem(dst)) && IsImm(src))
            {
                int size = OperandSize(dst);
                if (size == 0) Fail("test: cannot determine operand size");
                if (!IsX64 && size == 8) Fail("invalid 64-bit operation on x86");
                long v = ImmValue(src);
                bool w = size == 8;
                if (size == 1 && (!ImmFitsSigned(v, 1) && !ImmFitsUnsigned(v, 1))) Fail("8-bit immediate overflow");
                if (size == 2 && (!ImmFitsSigned(v, 2) && !ImmFitsUnsigned(v, 2))) Fail("16-bit immediate overflow");
                if (size >= 4 && !ImmFitsSigned(v, 4) && !ImmFitsUnsigned(v, 4)) Fail("Immediate overflow");
                EmitLegacyAndRex(w, 0, RexIdxOf(dst), RexBaseOf(dst),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0xF6 : (byte)0xF7);
                EmitRm(dst, 0);
                if (size == 1) EmitI8(v);
                else if (size == 2) EmitWord(v);
                else EmitDword(v);
                return;
            }
            if (IsMem(dst) && srcReg)
            {
                int size = CheckSize2(dst, src, "test");
                if (!IsX64 && size == 8) Fail("invalid 64-bit register on x86");
                bool w = size == 8;
                EmitLegacyAndRex(w, rs.RexBit, RexIndexOfMemIndex(dst.Mem), RexIndexOfMemBase(dst.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0x84 : (byte)0x85);
                EmitMemModRm(dst.Mem, rs.Code, rs.RexBit);
                return;
            }
            if (dstReg && IsMem(src))
            {
                int size = CheckSize2(dst, src, "test");
                if (!IsX64 && size == 8) Fail("invalid 64-bit register on x86");
                bool w = size == 8;
                EmitLegacyAndRex(w, rd.RexBit, RexIndexOfMemIndex(src.Mem), RexIndexOfMemBase(src.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0x84 : (byte)0x85);
                EmitMemModRm(src.Mem, rd.Code, rd.RexBit);
                return;
            }
            Fail("test: unsupported operand combination");
        }

        // ---- 移位 ----
        private void EncodeShift(Statement st, string m, Operand dst, Operand src)
        {
            int digit;
            InstructionTables.TryGetShift(m, out digit);
            Reg rd;
            bool dstReg = IsReg(dst, out rd);
            if (!dstReg && !IsMem(dst)) Fail(m + ": destination must be a register or memory");
            int size = OperandSize(dst);
            if (size == 0) Fail(m + ": cannot determine operand size (memory needs a size declaration)");
            if (!IsX64 && size == 8) Fail("invalid 64-bit operation on x86");

            Reg rs;
            if (IsReg(src, out rs))
            {
                if (rs.Size != 1 || rs.Index != 1 || rs.High)
                    Fail(m + ": count operand can only be cl");
                bool w = size == 8;
                EmitLegacyAndRex(w, 0, RexIdxOf(dst), RexBaseOf(dst),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0xD2 : (byte)0xD3);
                EmitRm(dst, digit);
                return;
            }
            if (IsImm(src))
            {
                long v = ImmValue(src);
                if (v < 0 || v > 255) Fail(m + ": shift count exceeds 0-255");
                bool w = size == 8;
                if (v == 1)
                {
                    EmitLegacyAndRex(w, 0, RexIdxOf(dst), RexBaseOf(dst),
                        FindMemSeg(dst, src), size == 2, st.RepKind, false);
                    Emit(size == 1 ? (byte)0xD0 : (byte)0xD1);
                    EmitRm(dst, digit);
                    return;
                }
                EmitLegacyAndRex(w, 0, RexIdxOf(dst), RexBaseOf(dst),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0xC0 : (byte)0xC1);
                EmitRm(dst, digit);
                EmitI8(v);
                return;
            }
            Fail(m + ": count operand must be an immediate or cl");
        }

        // ---- imul r, r/m ----
        private void EncodeImul2(Statement st, Operand dst, Operand src)
        {
            Reg rd, rs;
            if (!IsReg(dst, out rd)) Fail("imul: first operand must be a register");
            int size = rd.Size;
            if (IsReg(src, out rs)) { if (rs.Size != size) Fail("imul: operand size mismatch"); }
            else if (IsMem(src)) { if (src.Size != 0 && src.Size != size) Fail("imul: operand size mismatch"); }
            else Fail("imul: second operand must be a register or memory");
            if (!IsX64 && size == 8) Fail("invalid 64-bit register on x86");
            EmitLegacyAndRex(size == 8, rd.RexBit,
                IsReg(src, out rs) ? 0 : RexIndexOfMemIndex(src.Mem),
                IsReg(src, out rs) ? RegLowBit(src) : RexIndexOfMemBase(src.Mem),
                FindMemSeg(dst, src), size == 2, st.RepKind, false);
            Emit(0x0F, 0xAF);
            if (IsReg(src, out rs)) EmitModRmReg(rd.Code, rs, rs.RexBit);
            else EmitMemModRm(src.Mem, rd.Code, rd.RexBit);
        }

        // ---- bt 组（寄存器源形式）----
        private void EncodeBtReg(Statement st, string m, Operand dst, Operand src, int digitImm)
        {
            Reg rd, rs;
            int size;
            if (IsReg(src, out rs))
            {
                size = CheckSize2(dst, src, m);
                if (!IsX64 && size == 8) Fail("invalid 64-bit operation on x86");
                bool dstIsReg = IsReg(dst, out rd);
                EmitLegacyAndRex(size == 8, rs.RexBit,
                    dstIsReg ? 0 : RexIndexOfMemIndex(dst.Mem),
                    dstIsReg ? dst.Reg.RexBit : RexIndexOfMemBase(dst.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                byte op = m == "bt" ? (byte)0xA3 : m == "bts" ? (byte)0xAB : m == "btr" ? (byte)0xB3 : (byte)0xBB;
                Emit(0x0F, op);
                if (dstIsReg) EmitModRmReg(rs.Code, rd, rd.RexBit);
                else EmitMemModRm(dst.Mem, rs.Code, rs.RexBit);
                return;
            }
            if (IsImm(src))
            {
                size = OperandSize(dst);
                if (size == 0) Fail(m + ": cannot determine operand size");
                if (!IsX64 && size == 8) Fail("invalid 64-bit operation on x86");
                long v = ImmValue(src);
                if (v < 0 || v > 255) Fail(m + ": bit index exceeds 0-255");
                bool w = size == 8;
                EmitLegacyAndRex(w, 0, RexIdxOf(dst), RexBaseOf(dst),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(0x0F, 0xBA);
                EmitRm(dst, digitImm);
                EmitI8(v);
                return;
            }
            Fail(m + ": second operand must be a register or immediate");
        }

        // ==================== 三操作数 ====================

        private void EncodeThree(Statement st, string m, Operand o1, Operand o2, Operand o3)
        {
            if (m == "imul")
            {
                Reg rd, rs;
                if (!IsReg(o1, out rd)) Fail("imul: first operand must be a register");
                int size = rd.Size;
                if (!IsReg(o2, out rs) && !IsMem(o2)) Fail("imul: second operand must be a register or memory");
                if (!IsImm(o3)) Fail("imul: third operand must be an immediate");
                if (!IsX64 && size == 8) Fail("invalid 64-bit register on x86");
                long v = ImmValue(o3);
                bool shortForm = ImmFitsSigned(v, 1);
                EmitLegacyAndRex(size == 8, rd.RexBit,
                    IsReg(o2, out rs) ? 0 : RexIndexOfMemIndex(o2.Mem),
                    IsReg(o2, out rs) ? RegLowBit(o2) : RexIndexOfMemBase(o2.Mem),
                    FindMemSeg(o1, o2), size == 2, st.RepKind, false);
                Emit(shortForm ? (byte)0x6B : (byte)0x69);
                if (IsReg(o2, out rs)) EmitModRmReg(rd.Code, rs, rs.RexBit);
                else EmitMemModRm(o2.Mem, rd.Code, rd.RexBit);
                if (shortForm) EmitI8(v);
                else EmitDword(v);
                return;
            }
            Fail("Cannot encode three-operand instruction '" + m + "'");
        }

        // ==================== 分支 ====================

        /// <summary>jcc 编码（前向 rel32 / 后向 rel8 优先）</summary>
        private void EmitJcc(byte cc, Operand target)
        {
            long rel;
            int size = BranchRelSize("jcc", target);
            if (size == 1)
            {
                rel = ComputeBranchRel(target, 2);
                Emit((byte)(0x70 + cc));
                EmitI8(rel);
            }
            else
            {
                rel = ComputeBranchRel(target, 6);
                Emit(0x0F, (byte)(0x80 + cc));
                EmitDword(rel);
            }
        }

        /// <summary>jmp/call 直接分支</summary>
        private void EmitRelBranch(string m, Operand target, bool isCall)
        {
            if (isCall)
            {
                // call 只有 rel32 形式；BranchRelSize 仅为设置前向标签的粘性标记
                BranchRelSize("call", target);
                long rel = ComputeBranchRel(target, 5);
                Emit(0xE8);
                EmitDword(rel);
                return;
            }
            int size = BranchRelSize("jmp", target);
            if (size == 1)
            {
                long rel = ComputeBranchRel(target, 2);
                Emit(0xEB);
                EmitI8(rel);
            }
            else
            {
                long rel = ComputeBranchRel(target, 5);
                Emit(0xE9);
                EmitDword(rel);
            }
        }

        private void EmitRel8Branch(byte opcode, Operand target, string what)
        {
            long rel = ComputeBranchRel(target, 2);
            if (rel < -128 || rel > 127)
                Fail(what + ": short jump exceeds ±128 bytes");
            Emit(opcode);
            EmitI8(rel);
        }

        /// <summary>决定分支编码尺寸：前向引用 → rel32（粘性）；后向引用 → rel8（除非强制 rel32）。
        /// kind 参数仅为语义（"jmp"/"call"/"jcc"）。</summary>
        private int BranchRelSize(string kind, Operand target)
        {
            if (_cur.BranchForce32) return 4;
            if (target.LabelName != null)
            {
                string key = _cur.Line + ":" + target.LabelName;
                if (_force32Branches != null && _force32Branches.Contains(key))
                {
                    _cur.BranchForce32 = true;
                    return 4;
                }
                ulong v;
                if (_labels != null && _labels.TryGetValue(target.LabelName, out v))
                {
                    // already defined → 后向
                    return 1;
                }
                if (_allLabelNames != null && _allLabelNames.Contains(target.LabelName))
                {
                    // 前向引用 → 粘性 rel32
                    _cur.BranchForce32 = true;
                    return 4;
                }
                // 未在标签集合中 → 外部符号，rel32
                return 4;
            }
            return 4;
        }

        /// <summary>计算相对位移。instrSize 为完整指令长度（含位移字段）。
        /// 注意：解析器把裸符号产出为"symbol immediate"（Kind=Immediate + LabelName），
        /// 分支语境下一律视为标签/符号目标。</summary>
        private long ComputeBranchRel(Operand target, int instrSize)
        {
            long targetAddr;
            bool isLabelTarget = target.Kind == OperandKind.Label ||
                (target.Kind == OperandKind.Immediate && target.LabelName != null);
            if (isLabelTarget)
            {
                ulong off;
                ulong symAddr;
                if (_labels != null && _labels.TryGetValue(target.LabelName, out off))
                {
                    targetAddr = (long)(_base + off);
                }
                else if (_cur.BranchForce32 && _allLabelNames != null &&
                         _allLabelNames.Contains(target.LabelName))
                {
                    // 前向标签：第一遍占位 0，下一遍取真实值（尺寸不变 → 偏移稳定）
                    return 0;
                }
                else if (_symbols.TryGetValue(target.LabelName, out symAddr))
                {
                    // 外部符号作为直接分支目标：需要已知 BaseAddress 才能算 rel32
                    if (_base == 0)
                        Fail("for external symbol '" + target.LabelName + "' requires a known BaseAddress for a direct jump; "
                            + "for cross-process calls use the indirect form: mov rax, symbol + call rax");
                    targetAddr = (long)symAddr;
                }
                else
                {
                    Fail("Undefined label or symbol: '" + target.LabelName + "'");
                    return 0;
                }
            }
            else if (target.Kind == OperandKind.Immediate)
            {
                targetAddr = ImmValue(target);
            }
            else
            {
                Fail("Branch target must be a label or immediate");
                return 0;
            }
            long nextIp = (long)_base + _startOffset + instrSize;
            long rel = targetAddr - nextIp;
            if (instrSize == 2 && (rel < -128 || rel > 127))
            {
                // 后向短跳溢出 → 记录强制 rel32，触发重汇编
                if (target.LabelName != null && _force32Branches != null)
                    _force32Branches.Add(_cur.Line + ":" + target.LabelName);
                Fail("__FORCE32__");
            }
            if ((instrSize == 5 || instrSize == 6) && (rel < int.MinValue || rel > int.MaxValue))
                Fail("Branch distance exceeds ±2GB");
            return rel;
        }

        // ==================== data定义 ====================

        private void EncodeData(Statement st)
        {
            int size;
            switch (st.Mnemonic)
            {
                case "db": size = 1; break;
                case "dw": size = 2; break;
                case "dd": size = 4; break;
                case "dq": size = 8; break;
                default: size = 1; break;
            }
            if (st.Operands.Count == 0) Fail("Data definition needs at least one value");
            foreach (Operand o in st.Operands)
            {
                if (o.IsString)
                {
                    if (size != 1) Fail("Strings are only valid with db");
                    for (int i = 0; i < o.StrData.Length; i++) Emit((byte)o.StrData[i]);
                    continue;
                }
                long v = o.LabelName != null ? (long)ResolveSymbolValue(o.LabelName) : o.Imm;
                if (size == 1) EmitI8(v);
                else if (size == 2) EmitWord(v);
                else if (size == 4) EmitDword(v);
                else EmitQword((ulong)v);
            }
        }

        // ==================== REX 辅助 ====================

        private static int RegLowBit(Operand o)
        {
            Reg r;
            if (IsReg(o, out r)) return r.RexBit;
            return 0;
        }
        private static int RexIndexOfMemBase(MemOperand m)
        {
            if (m == null || m.BaseReg < 0) return 0;
            return (m.BaseReg >> 3) & 1;
        }
        private static int RexIndexOfMemIndex(MemOperand m)
        {
            if (m == null || m.IndexReg < 0) return 0;
            return (m.IndexReg >> 3) & 1;
        }

        /* 下面两个用于"操作数在 r/m 字段"的指令（/digit 组、移位、test、bt 等）。
         * 这类指令的 reg 字段被 /digit 占用，操作数只能落在 r/m，因此 r8-r15 只能靠 REX.B
         * 表达。若像旧代码那样直接传 dst.Mem（寄存器时为 null），RexIndexOfMemBase 返回 0，
         * REX.B 永远不发射 → add r8,1 被静默编成 add rax,1。 */

        /// <summary>r/m 操作数的 REX.B：寄存器取高位，内存取基址寄存器</summary>
        private static int RexBaseOf(Operand o)
        {
            Reg r;
            if (IsReg(o, out r)) return r.RexBit;
            return RexIndexOfMemBase(o.Mem);
        }

        /// <summary>r/m 操作数的 REX.X：寄存器形式恒 0，内存形式取索引寄存器</summary>
        private static int RexIdxOf(Operand o)
        {
            Reg r;
            if (IsReg(o, out r)) return 0;
            return RexIndexOfMemIndex(o.Mem);
        }

        /// <summary>发射 rm 字段（寄存器或memory），reg 字段为 /digit</summary>
        private void EmitRm(Operand o, int digit)
        {
            Reg r;
            if (IsReg(o, out r)) EmitModRmReg(digit, r, r.RexBit);
            else EmitMemModRm(o.Mem, digit, 0);
        }

        /// <summary>lock 前缀合法性检查并返回是否需要（允许名单见 InstructionLexicon）</summary>
        private bool CheckLock(Statement st, Operand rmOp)
        {
            if (!st.Lock) return false;
            string m = st.Mnemonic;
            if (!InstructionLexicon.IsLockAllowed(m))
                Fail("lock prefix is not valid for '" + m + "'");
            if (rmOp == null || rmOp.Kind != OperandKind.Memory)
                Fail("lock requires a memory destination");
            return true;
        }
    }
}
