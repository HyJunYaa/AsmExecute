// AsmSyntax.cs - 汇编引擎公共类型定义
// x86/x64 + Intel/AT&T 双语法自研汇编引擎（零外部依赖）
using System;
using System.Collections.Generic;
using System.Text;

namespace AsmStudio.Engine
{
    /// <summary>汇编语法风格</summary>
    public enum SyntaxKind
    {
        Intel,   // mov eax, [ebx+esi*4+8] / mov dword ptr [x], 5
        Att      // movl 0x8(%esi,%ebx,4), %eax / movq $5, %rax
    }

    /// <summary>目标架构</summary>
    public enum ArchKind
    {
        X86,     // 32 位
        X64      // 64 位 (x86-64, MSVC ABI 调用约定场景)
    }

    /// <summary>段寄存器</summary>
    public enum SegKind : byte
    {
        None = 0, Es, Cs, Ss, Ds, Fs, Gs
    }

    /// <summary>一条诊断信息</summary>
    public sealed class AsmError
    {
        public int Line;        // 1 起始行号
        public int Column;      // 1 起始列（0=未知）
        public string Message;
        public AsmError(int line, int column, string message)
        {
            Line = line; Column = column; Message = message;
        }
        public override string ToString()
        {
            if (Column > 0)
                return string.Format("Row {0}, Col {1}: {2}", Line, Column, Message);
            return string.Format("Row {0}: {1}", Line, Message);
        }
    }

    /// <summary>
    /// 通用寄存器描述。Index 0..15 依次为 AX,CX,DX,BX,SP,BP,SI,DI,R8..R15。
    /// Size = 1/2/4/8 字节。High=true 表示 x86 下的 AH/CH/DH/BH。
    /// </summary>
    public struct Reg
    {
        public int Index;
        public int Size;
        public bool High;
        public bool IsValid { get { return Index >= 0 && Size > 0; } }
        public static Reg Invalid = new Reg { Index = -1, Size = 0 };
        /// <summary>ModRM/SIB/opcode 中的 3 位寄存器编码</summary>
        public int Code { get { return Index & 7; } }
        /// <summary>REX 扩展位（B/R/X 之一，取决于字段位置）</summary>
        public int RexBit { get { return (Index >> 3) & 1; } }
    }

    /// <summary>memory操作数: [base + index*scale + disp]</summary>
    public sealed class MemOperand
    {
        public int BaseReg = -1;        // Reg.Index，-1 = 无
        public int IndexReg = -1;       // Reg.Index，-1 = 无
        public int Scale = 1;           // 1/2/4/8
        public long Disp;               // 位移/绝对地址（数值时）
        public string DispSymbol;       // 位移为符号（标签或外部符号）时非空
        public bool RipRelative;        // x64 [rel label] / label(%rip)
        public bool ForceAbs;           // x64 [abs expr] 强制绝对寻址
        public SegKind Segment = SegKind.None;
        public int Size;                // 1/2/4/8，0 = 未声明尺寸
    }

    public enum OperandKind { Register, Immediate, Memory, Label }

    /// <summary>操作数</summary>
    public sealed class Operand
    {
        public OperandKind Kind;
        public Reg Reg;                 // Kind=Register
        public long Imm;                // Kind=Immediate
        public MemOperand Mem;          // Kind=Memory
        public string LabelName;        // 跳转/调用目标 或 symbol immediate
        public int Size;                // 已知操作数尺寸（字节），0=未知
        /// <summary>AT&T 的 '*' 间接跳转/调用标记</summary>
        public bool Indirect;
        /// <summary>db 字符串data</summary>
        public bool IsString;
        public string StrData;

        public static Operand RegOp(Reg r)
        {
            Operand o = new Operand();
            o.Kind = OperandKind.Register; o.Reg = r; o.Size = r.Size;
            return o;
        }
        public static Operand ImmOp(long v, int size)
        {
            Operand o = new Operand();
            o.Kind = OperandKind.Immediate; o.Imm = v; o.Size = size;
            return o;
        }
        public static Operand MemOp(MemOperand m)
        {
            Operand o = new Operand();
            o.Kind = OperandKind.Memory; o.Mem = m; o.Size = m.Size;
            return o;
        }
        public static Operand LabelOp(string name)
        {
            Operand o = new Operand();
            o.Kind = OperandKind.Label; o.LabelName = name;
            return o;
        }
        public static Operand SymbolImmOp(string name)
        {
            Operand o = new Operand();
            o.Kind = OperandKind.Immediate; o.LabelName = name; o.Size = 0;
            return o;
        }
    }

    /// <summary>一条已解析的语句（指令/data/标签）</summary>
    public sealed class Statement
    {
        public int Line;
        public int Column;
        public string Mnemonic;         // 小写规范化（去掉 AT&T 尺寸后缀）
        public int SizeHint;            // AT&T 后缀或 Intel ptr 声明得到的尺寸提示
        public int SrcSizeHint;         // AT&T movz/movs 双尺寸后缀的源尺寸
        public int DstSizeHint;         // AT&T movz/movs 双尺寸后缀的目标尺寸
        public List<Operand> Operands = new List<Operand>();
        public string LabelDef;         // 本行定义的标签（可为空）
        public bool Lock;
        public int RepKind;             // 0=无 1=rep/repe 2=repne
        public SegKind SegPrefix = SegKind.None;

        // ---- 两遍汇编的控制信息（内部使用） ----
        internal bool BranchForce32;    // 后向短跳溢出后强制 rel32
        internal int LabelDefIndex = -1;

        /// <summary>是否为data定义指令 db/dw/dd/dq</summary>
        public bool IsData
        {
            get
            {
                return Mnemonic == "db" || Mnemonic == "dw" || Mnemonic == "dd" || Mnemonic == "dq";
            }
        }
    }

    /// <summary>外部符号表（目标进程公开函数/data地址）</summary>
    public sealed class SymbolTable
    {
        private readonly Dictionary<string, ulong> _map =
            new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);

        public void Add(string name, ulong address) { _map[name] = address; }
        public void Remove(string name) { _map.Remove(name); }
        public void Clear() { _map.Clear(); }
        public bool Contains(string name) { return _map.ContainsKey(name); }
        public bool TryGet(string name, out ulong address) { return _map.TryGetValue(name, out address); }
        public bool TryGetValue(string name, out ulong address) { return _map.TryGetValue(name, out address); }
        public IEnumerable<KeyValuePair<string, ulong>> Items
        {
            get { return _map; }
        }
    }

    /// <summary>汇编选项</summary>
    public sealed class AssembleOptions
    {
        public SyntaxKind Syntax = SyntaxKind.Intel;
        public ArchKind Arch = ArchKind.X64;
        /// <summary>缓冲区基地址（影响 rel32 直调/直跳与绝对寻址校验），0 = 未知</summary>
        public ulong BaseAddress;
        public SymbolTable Symbols;
        /// <summary>
        /// ret 重定向网关地址（远程沙箱执行用，0 = 关闭）。
        /// 非零时所有无操作数 ret 编码为 "write that address into [rsp] then ret"，
        /// 避免 naive 内联代码的 ret 槽与其调用的 home space 冲突。
        /// </summary>
        public ulong RetTrapAddress;
    }

    /// <summary>汇编结果</summary>
    public sealed class AssembleResult
    {
        public bool Success;
        public byte[] Code;
        public List<AsmError> Errors = new List<AsmError>();
        /// <summary>内部标签 → 缓冲区内偏移</summary>
        public readonly Dictionary<string, ulong> Labels = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        public bool TryGetLabelOffset(string name, out ulong offset) { return Labels.TryGetValue(name, out offset); }

        public string Hex
        {
            get
            {
                if (Code == null) return string.Empty;
                StringBuilder sb = new StringBuilder(Code.Length * 3);
                for (int i = 0; i < Code.Length; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(Code[i].ToString("X2"));
                }
                return sb.ToString();
            }
        }
    }
}
