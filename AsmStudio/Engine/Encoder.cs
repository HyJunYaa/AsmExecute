// Encoder.cs - x86/x64 指令编码核心
// 输入：已归一化的 Statement（Intel 顺序），输出：机器码字节。
// 支持前缀(lock/rep/seg/66)、REX、ModRM/SIB 全寻址、moffs、SIB 绝对寻址(x64 [abs]/数值/外部符号)、
// RIP 相对寻址、分支前向 rel32/后向 rel8 自动选择、符号立即数、ret 网关改写等。
using System;
using System.Collections.Generic;

namespace AsmStudio.Engine
{
    /// <summary>编码期错误（由 Assembler 捕获转为 AsmError）</summary>
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
                Fail(string.Format("{0}: 操作数尺寸不匹配 ({1} 字节 vs {2} 字节)", mnem, sa, sb));
            int s = sa != 0 ? sa : sb;
            if (s == 0) Fail(string.Format("{0}: 无法确定操作数尺寸（内存操作数需要 byte/word/dword/qword ptr 或尺寸后缀）", mnem));
            return s;
        }

        /// <summary>符号/标签取值（立即数语境）</summary>
        private ulong ResolveSymbolValue(string name)
        {
            ulong v;
            if (_labels != null && _labels.TryGetValue(name, out v))
            {
                if (_base == 0)
                    Fail("立即数使用内部标签 '" + name + "' 需要已知 BaseAddress");
                return _base + v;
            }
            if (_allLabelNames != null && _allLabelNames.Contains(name))
                return 0;   // 前向标签：第一遍占位，下一遍取真实值（尺寸不变）
            if (_symbols.TryGetValue(name, out v)) return v;
            Fail("未定义的符号: '" + name + "'");
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
                isInternal = false;     // 前向占位（base 检查延后到有真实值的一遍）
                value = 0;
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
                if (any) Emit((byte)rex);
            }
        }

        private static SegKind FindMemSeg(Operand a, Operand b)
        {
            if (IsMem(a)) return a.Mem.Segment;
            if (IsMem(b)) return b.Mem.Segment;
            return SegKind.None;
        }

        // ==================== ModRM/SIB/位移 ====================

        /// <summary>发射寄存器形式的 ModRM（mod=11）。</summary>
        private void EmitModRmReg(int regField3, Reg rmReg, int rmRexB)
        {
            Emit((byte)(0xC0 | (regField3 << 3) | rmReg.Code));
        }

        /// <summary>发射内存操作数的 ModRM(+SIB)+位移。</summary>
        private void EmitMemModRm(MemOperand m, int regField3, int regRexR)
        {
            if (IsX64 && m.RipRelative)
            {
                // [rel label] → mod=00 rm=101 disp32（相对 RIP，基址无关）
                if (m.DispSymbol == null) Fail("[rel] 需要标签符号");
                ulong v;
                bool internalLabel;
                if (!TryResolveDisp(m.DispSymbol, out v, out internalLabel))
                    Fail("未定义的符号: '" + m.DispSymbol + "'");
                if (!internalLabel)
                    Fail("[rel] 只能用于内部标签（外部符号请用 mov+寄存器间接或 [abs]）");
                long rel = (long)v - (long)_base - (long)(_startOffset + _buf.Count + 4);
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
                        Fail("未定义的符号: '" + m.DispSymbol + "'");
                    if (internalLabel && !m.ForceAbs)
                        Fail("x64 下内部标签请用 [rel 标签]（RIP 相对寻址）");
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
                    Fail("x64 绝对地址超出 32 位范围：mov al/eax/rax 支持 moffs（[abs 符号]），"
                        + "其余寄存器请用 mov rax, 符号 + [rax] 间接寻址");
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
                if (m.IndexReg == 4 && IsX64) Fail("rsp 不能作为变址寄存器");
                if (!IsX64 && m.IndexReg == 4) Fail("esp 不能作为变址寄存器");
                long disp = ResolveMemDisp(m);
                int mod;
                if (m.BaseReg % 8 == 5 && (IsX64 || m.BaseReg == 5))
                {
                    // rbp/r13 作基址：mod=00 + SIB base=101 表示"无基址"，必须用 disp
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
                // 仅变址无基址 → SIB 无基址形式 (mod=0, base=101)
                if (breg == 4) Fail("rsp/esp 不能作为变址寄存器");
                long disp = ResolveMemDisp(m);
                int sib = (ScaleBits(m.Scale) << 6) | ((breg % 8) << 3) | 0x05;
                Emit((byte)((0x00 << 6) | (regField3 << 3) | 0x04));
                Emit((byte)sib);
                EmitDword(disp);
                return;
            }

            // 仅 base
            if (breg == 4)
            {
                // esp/rsp 基址必须 SIB
                int mod;
                long disp = ResolveMemDisp(m);
                if (disp == 0 && m.DispSymbol == null) mod = 0;
                else if (disp >= -128 && disp <= 127) mod = 1;
                else mod = 2;
                Emit((byte)((mod << 6) | (regField3 << 3) | 0x04));
                Emit((byte)(0x00 << 6 | 0x04 << 3 | 0x04));   // scale=0, index=none(100), base=rsp(100)
                EmitDispForMod(mod, disp);
                return;
            }
            if (breg == 5 || breg == 13)
            {
                if (!IsX64 && breg == 13) Fail("x86 下无效寄存器");
                // rbp/ebp 作基址：mod=00+rm101 会被解释为 disp32 绝对，必须 disp8=0
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
                Fail("x64 绝对地址超出 32 位范围：仅 mov al/eax/rax 支持 moffs 形式（mov rax,[abs 符号]），"
                    + "其余请用 mov rax, 符号 + [rax] 间接寻址");
            }
        }

        private void EmitDispForMod(int mod, long disp)
        {
            if (mod == 1) EmitI8(disp);
            else if (mod == 2) EmitDword(disp);
        }

        /// <summary>内存位移取值（数值 + 符号，可混合）</summary>
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
                Fail("未定义的符号: '" + m.DispSymbol + "'");
            if (internalLabel && _base == 0 && !m.RipRelative && !IsX64)
                Fail("绝对寻址使用内部标签 '" + m.DispSymbol + "' 需要已知 BaseAddress");
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

            // 数据定义
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
                    case "cdqe": if (!IsX64) Fail("cdqe 仅存在于 x64"); Emit(0x48, 0x98); return;
                    case "cqo": if (!IsX64) Fail("cqo 仅存在于 x64"); Emit(0x48, 0x99); return;
                    case "syscall":
                        if (!IsX64) Fail("syscall 仅存在于 x64");
                        Emit(0x0F, 0x05); return;
                    case "sysenter": Emit(0x0F, 0x34); return;
                    default:
                        Fail("指令 '" + m + "' 需要操作数或不存在");
                        return;
                }
            }

            // ---- ret imm16 ----
            if (m == "ret" && n == 1 && IsImm(st.Operands[0]))
            {
                long v = ImmValue(st.Operands[0]);
                if (v < 0 || v > 65535) Fail("ret 立即数超出 16 位范围");
                Emit(0xC2); EmitWord(v);
                return;
            }
            // ---- int imm8 ----
            if (m == "int" && n == 1 && IsImm(st.Operands[0]))
            {
                long v = ImmValue(st.Operands[0]);
                if (v < 0 || v > 255) Fail("int 立即数超出 8 位范围");
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
            if (m == "movsd" && IsX64) Fail("x64 下请使用 movsq");
            if (m == "cmpsd" && IsX64) Fail("x64 下请使用 cmpsq");

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
        /// ret 编码：RetTrapAddress 非零时改写为 "把网关地址写入 [rsp] 再 ret"
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
                        if (r.Size == 1) Fail("push 不支持 8 位寄存器");
                        if (!IsX64 && r.Size == 8) Fail("x86 下无效的 64 位寄存器");
                        // push r 在 x64 下默认 64 位，无需 REX.W
                        EmitLegacyAndRex(false, 0, 0, r.RexBit, SegKind.None, r.Size == 2, st.RepKind, false);
                        Emit((byte)(0x50 + r.Code));
                        return;
                    }
                    if (IsMem(o))
                    {
                        int size = o.Size;
                        if (size == 0) size = IsX64 ? 8 : 4;    // 栈槽尺寸无歧义，可缺省
                        if (!IsX64 && size == 8) Fail("x86 下无效的 qword");
                        EmitLegacyAndRex(false, 0, RexIndexOfMemIndex(o.Mem), RexIndexOfMemBase(o.Mem),
                            o.Mem.Segment, size == 2, st.RepKind, false);
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
                        else Fail("push 立即数超出 32 位范围");
                        return;
                    }
                    Fail("无效的 push 操作数");
                    return;
                }
                case "pop":
                {
                    Reg r;
                    if (IsReg(o, out r))
                    {
                        if (r.Size == 1) Fail("pop 不支持 8 位寄存器");
                        if (!IsX64 && r.Size == 8) Fail("x86 下无效的 64 位寄存器");
                        EmitLegacyAndRex(false, 0, 0, r.RexBit, SegKind.None, r.Size == 2, st.RepKind, false);
                        Emit((byte)(0x58 + r.Code));
                        return;
                    }
                    if (IsMem(o))
                    {
                        int size = o.Size;
                        if (size == 0) size = IsX64 ? 8 : 4;
                        if (!IsX64 && size == 8) Fail("x86 下无效的 qword");
                        EmitLegacyAndRex(false, 0, RexIndexOfMemIndex(o.Mem), RexIndexOfMemBase(o.Mem),
                            o.Mem.Segment, size == 2, st.RepKind, false);
                        EmitMemModRm(o.Mem, 0, 0);
                        return;
                    }
                    Fail("无效的 pop 操作数");
                    return;
                }
                case "jmp":
                case "call":
                {
                    Reg r;
                    bool isCall = m == "call";
                    if (IsReg(o, out r))
                    {
                        if (r.Size != 4 && r.Size != 8) Fail("间接跳转寄存器必须是 32/64 位");
                        if (!IsX64 && r.Size == 8) Fail("x86 下无效的 64 位寄存器");
                        EmitLegacyAndRex(false, 0, 0, r.RexBit, SegKind.None, false, st.RepKind, false);
                        Emit(0xFF);
                        EmitModRmReg(isCall ? 2 : 4, r, r.RexBit);
                        return;
                    }
                    if (IsMem(o))
                    {
                        int size = o.Size;
                        if (size == 0) size = IsX64 ? 8 : 4;    // EIP/RIP 宽度无歧义，可缺省
                        if (!IsX64 && size == 8) Fail("x86 下无效的 qword");
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
                    if (r.Size != 1) Fail("setcc 目标必须是 8 位寄存器");
                    EmitLegacyAndRex(false, 0, 0, r.RexBit, SegKind.None, false, st.RepKind, false);
                    Emit(0x0F, (byte)(0x90 + cc));
                    EmitModRmReg(0, r, r.RexBit);
                    return;
                }
                if (IsMem(o))
                {
                    if (o.Size != 0 && o.Size != 1) Fail("setcc 内存目标必须是 byte");
                    EmitLegacyAndRex(false, 0, RexIndexOfMemIndex(o.Mem), RexIndexOfMemBase(o.Mem),
                        o.Mem.Segment, false, st.RepKind, false);
                    Emit(0x0F, (byte)(0x90 + cc));
                    EmitMemModRm(o.Mem, 0, 0);
                    return;
                }
                Fail("setcc 目标必须是寄存器或内存");
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
                if (IsReg(o, out loopRegCheck)) Fail("loop 目标只能是标签");
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
                    if (!IsX64 && r.Size == 8) Fail("x86 下无效的 64 位寄存器");
                    bool w = r.Size == 8;
                    EmitLegacyAndRex(w, 0, 0, r.RexBit, SegKind.None, r.Size == 2, st.RepKind, CheckLock(st, o));
                    Emit(r.Size == 1 ? (byte)0xF6 : (byte)0xF7);
                    EmitModRmReg(unaryDigit, r, r.RexBit);
                    return;
                }
                if (IsMem(o))
                {
                    int size = o.Size;
                    if (size == 0) Fail(m + ": 内存操作数需要尺寸声明");
                    bool w = size == 8;
                    if (!IsX64 && w) Fail("x86 下无效的 qword");
                    EmitLegacyAndRex(w, 0, RexIndexOfMemIndex(o.Mem), RexIndexOfMemBase(o.Mem),
                        o.Mem.Segment, size == 2, st.RepKind, CheckLock(st, o));
                    Emit(size == 1 ? (byte)0xF6 : (byte)0xF7);
                    EmitMemModRm(o.Mem, unaryDigit, 0);
                    return;
                }
                Fail(m + ": 操作数必须是寄存器或内存");
                return;
            }

            // ---- inc/dec 单寄存器或内存 ----
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
                    if (r.Size != 4 && r.Size != 8) Fail("bswap 需要 32/64 位寄存器");
                    if (!IsX64 && r.Size == 8) Fail("x86 下无效的 64 位寄存器");
                    EmitLegacyAndRex(r.Size == 8, 0, 0, r.RexBit, SegKind.None, false, st.RepKind, false);
                    Emit(0x0F, (byte)(0xC8 + r.Code));
                    return;
                }
                Fail("bswap 目标必须是寄存器");
                return;
            }

            Fail("无法编码指令 '" + m + "'（未知或操作数形式不支持）");
        }

        private void EmitString(Statement st, byte opcode, int size)
        {
            bool w = size == 8;
            if (!IsX64 && w) Fail("x86 下无效的 qword 串操作");
            EmitLegacyAndRex(w, 0, 0, 0, SegKind.None, size == 2, st.RepKind, false);
            Emit(opcode);
        }

        private void EncodeIncDec(Statement st, string m, Operand o)
        {
            int digit = m == "inc" ? 0 : 1;
            Reg r;
            if (IsReg(o, out r))
            {
                if (!IsX64 && r.Size == 8) Fail("x86 下无效的 64 位寄存器");
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
                if (size == 0) Fail(m + ": 内存操作数需要尺寸声明");
                bool w = size == 8;
                if (!IsX64 && w) Fail("x86 下无效的 qword");
                EmitLegacyAndRex(w, 0, RexIndexOfMemIndex(o.Mem), RexIndexOfMemBase(o.Mem),
                    o.Mem.Segment, size == 2, st.RepKind, CheckLock(st, o));
                Emit(size == 1 ? (byte)0xFE : (byte)0xFF);
                EmitMemModRm(o.Mem, digit, 0);
                return;
            }
            Fail(m + ": 操作数必须是寄存器或内存");
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
                if (ds == 1) Fail(m + " 不支持 8 位操作数");
                if (!IsReg(dst, out rd)) Fail(m + ": 第一操作数必须是寄存器");
                if (!IsX64 && ds == 8) Fail("x86 下无效的 64 位寄存器");
                EmitLegacyAndRex(ds == 8, rd.RexBit,
                    IsMem(src) ? RexIndexOfMemIndex(src.Mem) : RegHighBit(src),
                    IsMem(src) ? RexIndexOfMemBase(src.Mem) : RegLowBit(src),
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
                if (!srcIsReg) Fail("xadd: 第二操作数必须是寄存器");
                if (!IsX64 && ds == 8) Fail("x86 下无效的 64 位寄存器");
                EmitLegacyAndRex(ds == 8, rs.RexBit,
                    dstIsReg ? 0 : RexIndexOfMemIndex(dst.Mem),
                    dstIsReg ? (dst.Reg.RexBit) : RexIndexOfMemBase(dst.Mem),
                    FindMemSeg(dst, src), ds == 2, st.RepKind, CheckLock(st, dst));
                Emit(0x0F, 0xC1);
                if (dstIsReg) EmitModRmReg(rs.Code, rd, rd.RexBit);
                else EmitMemModRm(dst.Mem, rs.Code, rs.RexBit);
                return;
            }

            Fail("无法编码指令 '" + m + "'（未知或操作数形式不支持）");
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
                    if (size != 1) Fail("AH/CH/DH/BH 只能用于 8 位操作");
                    if (IsX64 && (rd.Index >= 8 || rs.Index >= 8)) Fail("x64 下 AH 等高字节寄存器不能与 R8-R15 同用");
                    EmitLegacyAndRex(false, 0, 0, 0, SegKind.None, false, st.RepKind, false);
                    Emit(0x88);
                    EmitModRmReg(rs.Code, rd, 0);
                    return;
                }
                if (!IsX64 && (rd.Size == 8 || rs.Size == 8)) Fail("x86 下无效的 64 位寄存器");
                bool w = size == 8;
                EmitLegacyAndRex(w, rs.RexBit, 0, rd.RexBit, SegKind.None, size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0x88 : (byte)0x89);
                EmitModRmReg(rs.Code, rd, rd.RexBit);
                return;
            }
            if (dstReg && IsMem(src))
            {
                int size = CheckSize2(dst, src, "mov");
                if (!IsX64 && size == 8) Fail("x86 下无效的 64 位寄存器");
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
                if (!IsX64 && size == 8) Fail("x86 下无效的 64 位寄存器");
                bool w = size == 8;
                EmitLegacyAndRex(w, rs.RexBit, RexIndexOfMemIndex(dst.Mem), RexIndexOfMemBase(dst.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0x88 : (byte)0x89);
                EmitMemModRm(dst.Mem, rs.Code, rs.RexBit);
                return;
            }
            if (dstReg && IsImm(src))
            {
                if (!IsX64 && rd.Size == 8) Fail("x86 下无效的 64 位寄存器");
                long v = ImmValue(src);
                if (rd.High)
                {
                    if (!ImmFitsSigned(v, 1) && !ImmFitsUnsigned(v, 1)) Fail("8 位立即数溢出");
                    Emit((byte)(0xB0 + rd.Code));
                    EmitI8(v);
                    return;
                }
                switch (rd.Size)
                {
                    case 1:
                        if (!ImmFitsSigned(v, 1) && !ImmFitsUnsigned(v, 1)) Fail("8 位立即数溢出");
                        EmitLegacyAndRex(false, 0, 0, rd.RexBit, SegKind.None, false, st.RepKind, false);
                        Emit((byte)(0xB0 + rd.Code));
                        EmitI8(v);
                        return;
                    case 2:
                        if (!ImmFitsSigned(v, 2) && !ImmFitsUnsigned(v, 2)) Fail("16 位立即数溢出");
                        EmitLegacyAndRex(false, 0, 0, rd.RexBit, SegKind.None, true, st.RepKind, false);
                        Emit((byte)(0xB8 + rd.Code));
                        EmitWord(v);
                        return;
                    case 4:
                        if (!ImmFitsSigned(v, 4) && !ImmFitsUnsigned(v, 4))
                        {
                            if (IsX64) Fail("32 位寄存器立即数溢出（请用 64 位寄存器）");
                            Fail("32 位立即数溢出");
                        }
                        EmitLegacyAndRex(false, 0, 0, rd.RexBit, SegKind.None, false, st.RepKind, false);
                        Emit((byte)(0xB8 + rd.Code));
                        EmitDword(v);
                        return;
                    case 8:
                        if (!IsX64) Fail("x86 下无效的 64 位寄存器");
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
                        Fail("无效的寄存器尺寸");
                        return;
                }
            }
            if (IsMem(dst) && IsImm(src))
            {
                int size = dst.Size;
                if (size == 0) Fail("mov: 写内存立即数需要尺寸声明（byte/word/dword/qword ptr）");
                long v = ImmValue(src);
                if (!IsX64 && size == 8) Fail("x86 下无效的 qword");
                bool w = size == 8;
                if (size == 8 && !ImmFitsSigned(v, 4)) Fail("qword 内存立即数必须能放入 32 位有符号数");
                if (size == 4 && !ImmFitsSigned(v, 4) && !ImmFitsUnsigned(v, 4)) Fail("32 位立即数溢出");
                if (size == 2 && !ImmFitsSigned(v, 2) && !ImmFitsUnsigned(v, 2)) Fail("16 位立即数溢出");
                if (size == 1 && !ImmFitsSigned(v, 1) && !ImmFitsUnsigned(v, 1)) Fail("8 位立即数溢出");
                EmitLegacyAndRex(w, 0, RexIndexOfMemIndex(dst.Mem), RexIndexOfMemBase(dst.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0xC6 : (byte)0xC7);
                EmitMemModRm(dst.Mem, 0, 0);
                if (size == 1) EmitI8(v);
                else if (size == 2) EmitWord(v);
                else EmitDword(v);
                return;
            }
            Fail("mov: 不支持的操作数组合");
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

            // 内存侧尺寸检查（寄存器已给出操作尺寸，缺省声明是允许的）
            if (memOp.Size != 0 && memOp.Size != size) Fail("mov: 操作数尺寸不匹配");

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
            if (!IsX64) Fail("movabs 仅存在于 x64");
            Reg rd;
            if (!IsReg(dst, out rd) || rd.Size != 8) Fail("movabs: 目标必须是 64 位寄存器");
            if (!IsImm(src)) Fail("movabs: 源必须是立即数");
            long v = ImmValue(src);
            EmitLegacyAndRex(true, 0, 0, rd.RexBit, SegKind.None, false, st.RepKind, false);
            Emit((byte)(0xB8 + rd.Code));
            EmitQword((ulong)v);
        }

        // ---- lea ----
        private void EncodeLea(Statement st, Operand dst, Operand src)
        {
            Reg rd;
            if (!IsReg(dst, out rd)) Fail("lea: 第一操作数必须是寄存器");
            if (!IsMem(src)) Fail("lea: 第二操作数必须是内存");
            int size = rd.Size;
            if (!IsX64 && size == 8) Fail("x86 下无效的 64 位寄存器");
            if (src.Size != 0 && src.Size != rd.Size && !(IsX64 && src.Size == 8 && rd.Size == 4))
                Fail("lea: 操作数尺寸不匹配");
            EmitLegacyAndRex(size == 8, rd.RexBit, RexIndexOfMemIndex(src.Mem), RexIndexOfMemBase(src.Mem),
                src.Mem.Segment, size == 2, st.RepKind, false);
            Emit(0x8D);
            EmitMemModRm(src.Mem, rd.Code, rd.RexBit);
        }

        // ---- movzx / movsx / movsxd ----
        private void EncodeMovx(Statement st, string m, Operand dst, Operand src, bool isSxd)
        {
            Reg rd, rs;
            if (!IsReg(dst, out rd)) Fail(m + ": 目标必须是寄存器");
            int srcSize = 0;
            bool srcIsReg = IsReg(src, out rs);
            if (isSxd)
            {
                if (!IsX64) Fail("movsxd 仅存在于 x64");
                if (rd.Size != 8) Fail("movsxd: 目标必须是 64 位寄存器");
                srcSize = 4;
                if (srcIsReg && rs.Size != 4) Fail("movsxd: 源必须是 32 位寄存器");
                if (IsMem(src) && src.Size != 0 && src.Size != 4) Fail("movsxd: 源必须是 dword");
            }
            else if (st.SrcSizeHint != 0)
            {
                srcSize = st.SrcSizeHint;
                if (st.DstSizeHint != 0 && st.DstSizeHint != rd.Size)
                    Fail(m + ": 目标尺寸与寄存器不匹配");
            }
            else
            {
                if (srcIsReg) srcSize = rs.Size;
                else if (IsMem(src) && src.Size != 0) srcSize = src.Size;
                else Fail(m + ": 无法确定源操作数尺寸");
                if (srcSize == rd.Size) Fail(m + ": 源与目标尺寸相同（不需要扩展）");
            }
            if (!isSxd && srcSize != 1 && srcSize != 2) Fail(m + ": 源操作数必须是 8/16 位");
            if (rd.Size != 2 && rd.Size != 4 && rd.Size != 8) Fail(m + ": 目标必须是 16/32/64 位寄存器");
            if (!IsX64 && rd.Size == 8) Fail("x86 下无效的 64 位寄存器");

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
                if (!IsX64 && (ra.Size == 8 || rb.Size == 8)) Fail("x86 下无效的 64 位寄存器");
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
            Fail("xchg: 不支持的操作数组合");
        }

        private void EmitXchgMem(Statement st, Reg r, Operand memOp, int size)
        {
            if (!IsX64 && size == 8) Fail("x86 下无效的 64 位寄存器");
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
                if (!IsX64 && (rd.Size == 8 || rs.Size == 8)) Fail("x86 下无效的 64 位寄存器");
                bool w = size == 8;
                EmitLegacyAndRex(w, rs.RexBit, 0, rd.RexBit, SegKind.None, size == 2, st.RepKind, false);
                Emit(size == 1 ? op8 : op32);
                EmitModRmReg(rs.Code, rd, rd.RexBit);
                return;
            }
            if (dstReg && IsMem(src))
            {
                int size = CheckSize2(dst, src, m);
                if (!IsX64 && size == 8) Fail("x86 下无效的 64 位寄存器");
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
                if (!IsX64 && size == 8) Fail("x86 下无效的 64 位寄存器");
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
            //             否则 05 id 短形式 (比 81 少 1 字节)
            if (dstReg && rd.Index == 0 && !rd.High && IsImm(src))
            {
                int size = OperandSize(dst) != 0 ? OperandSize(dst) : OperandSize(src);
                if (size == 0) Fail(m + ": 无法确定操作数尺寸");
                if (!IsX64 && size == 8) Fail("x86 下无效的 64 位操作");
                long v = ImmValue(src);
                if (size == 1)
                {
                    if (!ImmFitsSigned(v, 1) && !ImmFitsUnsigned(v, 1)) Fail("8 位立即数溢出");
                    Emit((byte)(op8 + 4)); EmitI8(v);
                    return;
                }
                if (!ImmFitsSigned(v, 1))
                {
                    bool w = size == 8;
                    if (size == 2)
                    {
                        if (!ImmFitsSigned(v, 2) && !ImmFitsUnsigned(v, 2)) Fail("16 位立即数溢出");
                        Emit(0x66, (byte)(op32 + 4));
                        EmitWord(v);
                        return;
                    }
                    if (!ImmFitsSigned(v, 4)) Fail(m + ": 立即数超出 32 位有符号范围");
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
                if (size == 0) Fail(m + ": 无法确定操作数尺寸（内存需要尺寸声明）");
                if (!IsX64 && size == 8) Fail("x86 下无效的 64 位操作");
                long v = ImmValue(src);
                bool w = size == 8;
                if (size == 1)
                {
                    if (!ImmFitsSigned(v, 1) && !ImmFitsUnsigned(v, 1)) Fail("8 位立即数溢出");
                    EmitLegacyAndRex(false, 0, RexIndexOfMemIndex(dst.Mem), RexIndexOfMemBase(dst.Mem),
                        FindMemSeg(dst, src), false, st.RepKind, lockable);
                    Emit(0x80);
                    EmitRm(dst, digit);
                    EmitI8(v);
                    return;
                }
                if (size == 2 && (!ImmFitsSigned(v, 2) && !ImmFitsUnsigned(v, 2))) Fail("16 位立即数溢出");
                if (size >= 4 && !ImmFitsSigned(v, 4)) Fail(m + ": 立即数超出 32 位有符号范围");
                bool use83 = ImmFitsSigned(v, 1) && (size == 2 || size >= 4);
                EmitLegacyAndRex(w, 0, RexIndexOfMemIndex(dst.Mem), RexIndexOfMemBase(dst.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, lockable);
                Emit(use83 ? (byte)0x83 : (byte)0x81);
                EmitRm(dst, digit);
                if (use83) EmitI8(v);
                else if (size == 2) EmitWord(v);
                else EmitDword(v);
                return;
            }
            Fail(m + ": 不支持的操作数组合");
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
                if (!IsX64 && (rd.Size == 8 || rs.Size == 8)) Fail("x86 下无效的 64 位寄存器");
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
                    if (!ImmFitsSigned(v, 1) && !ImmFitsUnsigned(v, 1)) Fail("8 位立即数溢出");
                    Emit(0xA8); EmitI8(v);
                    return;
                }
                if (!IsX64 && size == 8) Fail("x86 下无效的 64 位操作");
                if (!ImmFitsSigned(v, 4) && !ImmFitsUnsigned(v, 4)) Fail("立即数溢出");
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
                if (size == 0) Fail("test: 无法确定操作数尺寸");
                if (!IsX64 && size == 8) Fail("x86 下无效的 64 位操作");
                long v = ImmValue(src);
                bool w = size == 8;
                if (size == 1 && (!ImmFitsSigned(v, 1) && !ImmFitsUnsigned(v, 1))) Fail("8 位立即数溢出");
                if (size == 2 && (!ImmFitsSigned(v, 2) && !ImmFitsUnsigned(v, 2))) Fail("16 位立即数溢出");
                if (size >= 4 && !ImmFitsSigned(v, 4) && !ImmFitsUnsigned(v, 4)) Fail("立即数溢出");
                EmitLegacyAndRex(w, 0, RexIndexOfMemIndex(dst.Mem), RexIndexOfMemBase(dst.Mem),
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
                if (!IsX64 && size == 8) Fail("x86 下无效的 64 位寄存器");
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
                if (!IsX64 && size == 8) Fail("x86 下无效的 64 位寄存器");
                bool w = size == 8;
                EmitLegacyAndRex(w, rd.RexBit, RexIndexOfMemIndex(src.Mem), RexIndexOfMemBase(src.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0x84 : (byte)0x85);
                EmitMemModRm(src.Mem, rd.Code, rd.RexBit);
                return;
            }
            Fail("test: 不支持的操作数组合");
        }

        // ---- 移位 ----
        private void EncodeShift(Statement st, string m, Operand dst, Operand src)
        {
            int digit;
            InstructionTables.TryGetShift(m, out digit);
            Reg rd;
            bool dstReg = IsReg(dst, out rd);
            if (!dstReg && !IsMem(dst)) Fail(m + ": 目标必须是寄存器或内存");
            int size = OperandSize(dst);
            if (size == 0) Fail(m + ": 无法确定操作数尺寸（内存需要尺寸声明）");
            if (!IsX64 && size == 8) Fail("x86 下无效的 64 位操作");

            Reg rs;
            if (IsReg(src, out rs))
            {
                if (rs.Size != 1 || rs.Index != 1 || rs.High)
                    Fail(m + ": 计数操作数只能是 cl");
                bool w = size == 8;
                EmitLegacyAndRex(w, 0, RexIndexOfMemIndex(dst.Mem), RexIndexOfMemBase(dst.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0xD2 : (byte)0xD3);
                EmitRm(dst, digit);
                return;
            }
            if (IsImm(src))
            {
                long v = ImmValue(src);
                if (v < 0 || v > 255) Fail(m + ": 移位计数超出 0-255");
                bool w = size == 8;
                if (v == 1)
                {
                    EmitLegacyAndRex(w, 0, RexIndexOfMemIndex(dst.Mem), RexIndexOfMemBase(dst.Mem),
                        FindMemSeg(dst, src), size == 2, st.RepKind, false);
                    Emit(size == 1 ? (byte)0xD0 : (byte)0xD1);
                    EmitRm(dst, digit);
                    return;
                }
                EmitLegacyAndRex(w, 0, RexIndexOfMemIndex(dst.Mem), RexIndexOfMemBase(dst.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(size == 1 ? (byte)0xC0 : (byte)0xC1);
                EmitRm(dst, digit);
                EmitI8(v);
                return;
            }
            Fail(m + ": 计数操作数必须是立即数或 cl");
        }

        // ---- imul r, r/m ----
        private void EncodeImul2(Statement st, Operand dst, Operand src)
        {
            Reg rd, rs;
            if (!IsReg(dst, out rd)) Fail("imul: 第一操作数必须是寄存器");
            int size = rd.Size;
            if (IsReg(src, out rs)) { if (rs.Size != size) Fail("imul: 操作数尺寸不匹配"); }
            else if (IsMem(src)) { if (src.Size != 0 && src.Size != size) Fail("imul: 操作数尺寸不匹配"); }
            else Fail("imul: 第二操作数必须是寄存器或内存");
            if (!IsX64 && size == 8) Fail("x86 下无效的 64 位寄存器");
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
                if (!IsX64 && size == 8) Fail("x86 下无效的 64 位操作");
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
                if (size == 0) Fail(m + ": 无法确定操作数尺寸");
                if (!IsX64 && size == 8) Fail("x86 下无效的 64 位操作");
                long v = ImmValue(src);
                if (v < 0 || v > 255) Fail(m + ": 位号超出 0-255");
                bool w = size == 8;
                EmitLegacyAndRex(w, 0, RexIndexOfMemIndex(dst.Mem), RexIndexOfMemBase(dst.Mem),
                    FindMemSeg(dst, src), size == 2, st.RepKind, false);
                Emit(0x0F, 0xBA);
                EmitRm(dst, digitImm);
                EmitI8(v);
                return;
            }
            Fail(m + ": 第二操作数必须是寄存器或立即数");
        }

        // ==================== 三操作数 ====================

        private void EncodeThree(Statement st, string m, Operand o1, Operand o2, Operand o3)
        {
            if (m == "imul")
            {
                Reg rd, rs;
                if (!IsReg(o1, out rd)) Fail("imul: 第一操作数必须是寄存器");
                int size = rd.Size;
                if (!IsReg(o2, out rs) && !IsMem(o2)) Fail("imul: 第二操作数必须是寄存器或内存");
                if (!IsImm(o3)) Fail("imul: 第三操作数必须是立即数");
                if (!IsX64 && size == 8) Fail("x86 下无效的 64 位寄存器");
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
            Fail("无法编码三操作数指令 '" + m + "'");
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
                Fail(what + ": 短跳转超出 ±128 字节范围");
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
                    // 已定义 → 后向
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
        /// 注意：解析器把裸符号产出为"符号立即数"（Kind=Immediate + LabelName），
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
                        Fail("对外部符号 '" + target.LabelName + "' 的直接跳转需要已知 BaseAddress；"
                            + "跨进程调用请使用间接形式：mov rax, 符号 + call rax");
                    targetAddr = (long)symAddr;
                }
                else
                {
                    Fail("未定义的标签或符号: '" + target.LabelName + "'");
                    return 0;
                }
            }
            else if (target.Kind == OperandKind.Immediate)
            {
                targetAddr = ImmValue(target);
            }
            else
            {
                Fail("分支目标必须是标签或立即数");
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
                Fail("分支距离超出 ±2GB");
            return rel;
        }

        // ==================== 数据定义 ====================

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
            if (st.Operands.Count == 0) Fail("数据定义至少需要一个值");
            foreach (Operand o in st.Operands)
            {
                if (o.IsString)
                {
                    if (size != 1) Fail("字符串只能用于 db");
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

        private static int RegHighBit(Operand o)
        {
            Reg r;
            if (IsReg(o, out r)) return r.RexBit;
            return 0;
        }
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

        /// <summary>发射 rm 字段（寄存器或内存），reg 字段为 /digit</summary>
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
                Fail("lock 前缀不能用于 '" + m + "'");
            if (rmOp == null || rmOp.Kind != OperandKind.Memory)
                Fail("lock 要求目标为内存操作数");
            return true;
        }
    }
}
