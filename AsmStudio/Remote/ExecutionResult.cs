// ExecutionResult.cs - 执行结果模型与目标进程信息
using System;
using System.Collections.Generic;

namespace AsmStudio.Remote
{
    /// <summary>执行状态</summary>
    public enum ExecStatus
    {
        Ok,         // 成功
        Exception,  // VEH 捕获异常
        Timeout,    // 超时
        Busy,       // 嵌套执行
        BadArg,     // 参数无效
        Internal,   // 内部错误
        NoConnection // 未连接
    }

    /// <summary>寄存器槽索引（与 AsmLibrary ASMR_* 一致）</summary>
    public static class RegIdx
    {
        public const int Rax = 0, Rcx = 1, Rdx = 2, Rbx = 3;
        public const int Rsp = 4, Rbp = 5, Rsi = 6, Rdi = 7;
        public const int R8 = 8, R9 = 9, R10 = 10, R11 = 11;
        public const int R12 = 12, R13 = 13, R14 = 14, R15 = 15;
        public const int Count = 16;

        private static readonly string[] _names =
        {
            "rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi",
            "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15"
        };
        public static string Name(int i) { return _names[i]; }
    }

    /// <summary>一次沙箱执行的结果</summary>
    public sealed class ExecutionResult
    {
        public ExecStatus Status;
        public ulong RetValue;
        public ulong[] RegsMid = new ulong[RegIdx.Count];
        public ulong[] RegsFinal = new ulong[RegIdx.Count];
        public ulong FlagsMid;
        public ulong FlagsFinal;
        public ulong[] RegsExc = new ulong[RegIdx.Count];
        public ulong FlagsExc;
        public uint ExcCode;
        public ulong ExcAddr;
        public ulong ExcRip;
        public bool ExcWasAccessViolation;
        public int ExcReadOrWrite = -1;   // 0=读 1=写 8=DEP执行 -1=非访问违例
        public string Message;

        public bool Success
        {
            get { return Status == ExecStatus.Ok; }
        }

        public string ExceptionName
        {
            get
            {
                switch (ExcCode)
                {
                    case 0xC0000005: return "ACCESS_VIOLATION";
                    case 0xC000001D: return "ILLEGAL_INSTRUCTION";
                    case 0xC0000094: return "INT_DIVIDE_BY_ZERO";
                    case 0xC0000095: return "INT_OVERFLOW";
                    case 0xC00000FD: return "STACK_OVERFLOW";
                    case 0xC0000006: return "IN_PAGE_ERROR";
                    case 0x80000003: return "BREAKPOINT";
                    default: return string.Format("0x{0:X8}", ExcCode);
                }
            }
        }

        public string RegsToString(ulong[] regs)
        {
            if (regs == null) return string.Empty;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < RegIdx.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(RegIdx.Name(i));
                sb.Append('=');
                sb.Append(regs[i].ToString("X"));
            }
            return sb.ToString();
        }
    }

    /// <summary>目标进程信息</summary>
    public sealed class ProcessInfo
    {
        public int Pid;
        public string Name;
        public bool Is64Bit;
        public string ArchText
        {
            get { return Is64Bit ? "x64" : "x86"; }
        }
        public override string ToString()
        {
            return string.Format("{0} (pid={1}, {2})", Name, Pid, ArchText);
        }
    }

    /// <summary>目标符号</summary>
    public sealed class SymbolInfo
    {
        public string Name;
        public ulong Address;
        public override string ToString()
        {
            return string.Format("{0}  (0x{1:X})", Name, Address);
        }
    }

    /// <summary>执行通道</summary>
    public enum ExecChannel
    {
        PipeDll,        // 目标内 AsmLibrary.dll + 命名管道（VEH 全保护）
        RemoteThreadVeh,// 远程线程 + 汇编 VEH（无 DLL 注入，崩溃由 handler 重置上下文）
        RemoteThread,   // 远程线程直调（无 VEH 保护）
        InProcess       // 主程序本进程（直接 P/Invoke 自身架构的 AsmLibrary）
    }
}
