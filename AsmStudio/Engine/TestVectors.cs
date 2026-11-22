// TestVectors.cs - 编码器自检向量（已知正确编码对照）
// 每个向量: 语法 / 架构 / 源码 / 期望机器码。全部来自公开的 x86 编码事实。
using System;
using System.Collections.Generic;

namespace AsmStudio.Engine
{
    internal static class TestVectors
    {
        private sealed class Vector
        {
            public readonly SyntaxKind Syntax;
            public readonly ArchKind Arch;
            public readonly string Source;
            public readonly string ExpectedHex;
            public Vector(SyntaxKind s, ArchKind a, string src, string hex)
            {
                Syntax = s; Arch = a; Source = src; ExpectedHex = hex;
            }
        }

        private static readonly List<Vector> _vectors = BuildVectors();

        private static List<Vector> BuildVectors()
        {
            List<Vector> v = new List<Vector>();
            const SyntaxKind I = SyntaxKind.Intel;
            const SyntaxKind A = SyntaxKind.Att;
            const ArchKind X86 = ArchKind.X86;
            const ArchKind X64 = ArchKind.X64;

            // ============ Intel x86 ============
            v.Add(new Vector(I, X86, "mov eax, 5", "B8 05 00 00 00"));
            v.Add(new Vector(I, X86, "mov ecx, -1", "B9 FF FF FF FF"));
            v.Add(new Vector(I, X86, "mov al, 7", "B0 07"));
            v.Add(new Vector(I, X86, "mov bx, 0x1234", "66 BB 34 12"));
            v.Add(new Vector(I, X86, "mov eax, ebx", "89 D8"));
            v.Add(new Vector(I, X86, "mov ebx, eax", "89 C3"));
            v.Add(new Vector(I, X86, "mov [eax], ecx", "89 08"));
            v.Add(new Vector(I, X86, "mov ecx, [eax]", "8B 08"));
            v.Add(new Vector(I, X86, "mov ecx, [eax+4]", "8B 48 04"));
            v.Add(new Vector(I, X86, "mov ecx, [eax+ebx*4+0x10]", "8B 4C 98 10"));
            v.Add(new Vector(I, X86, "mov dword ptr [0x12345678], eax", "A3 78 56 34 12"));
            v.Add(new Vector(I, X86, "mov eax, [0x12345678]", "A1 78 56 34 12"));
            v.Add(new Vector(I, X86, "mov dword ptr [0x12345678], ecx", "89 0D 78 56 34 12"));
            v.Add(new Vector(I, X86, "mov ecx, [0x12345678]", "8B 0D 78 56 34 12"));
            v.Add(new Vector(I, X86, "mov dword ptr [eax], 0x12345678", "C7 00 78 56 34 12"));
            v.Add(new Vector(I, X86, "push eax", "50"));
            v.Add(new Vector(I, X86, "pop ebx", "5B"));
            v.Add(new Vector(I, X86, "push 5", "6A 05"));
            v.Add(new Vector(I, X86, "push -5", "6A FB"));
            v.Add(new Vector(I, X86, "push 0x12345678", "68 78 56 34 12"));
            v.Add(new Vector(I, X86, "add eax, 5", "83 C0 05"));
            v.Add(new Vector(I, X86, "add eax, 0x12345678", "05 78 56 34 12"));
            v.Add(new Vector(I, X86, "add eax, ebx", "01 D8"));
            v.Add(new Vector(I, X86, "add ebx, eax", "01 C3"));
            v.Add(new Vector(I, X86, "sub esp, 8", "83 EC 08"));
            v.Add(new Vector(I, X86, "xor eax, eax", "31 C0"));
            v.Add(new Vector(I, X86, "cmp eax, 10", "83 F8 0A"));
            v.Add(new Vector(I, X86, "test eax, eax", "85 C0"));
            v.Add(new Vector(I, X86, "inc eax", "40"));
            v.Add(new Vector(I, X86, "dec ecx", "49"));
            v.Add(new Vector(I, X86, "nop", "90"));
            v.Add(new Vector(I, X86, "ret", "C3"));
            v.Add(new Vector(I, X86, "ret 8", "C2 08 00"));
            v.Add(new Vector(I, X86, "call eax", "FF D0"));
            v.Add(new Vector(I, X86, "call [eax]", "FF 10"));
            v.Add(new Vector(I, X86, "call ebx", "FF D3"));
            v.Add(new Vector(I, X86, "jmp eax", "FF E0"));
            v.Add(new Vector(I, X86, "leave", "C9"));
            v.Add(new Vector(I, X86, "int3", "CC"));
            v.Add(new Vector(I, X86, "int 0x2d", "CD 2D"));
            v.Add(new Vector(I, X86, "stc", "F9"));
            v.Add(new Vector(I, X86, "cld", "FC"));
            v.Add(new Vector(I, X86, "xchg eax, ebx", "93"));
            v.Add(new Vector(I, X86, "lea ebx, [eax+8]", "8D 58 08"));
            v.Add(new Vector(I, X86, "movzx ecx, al", "0F B6 C8"));
            v.Add(new Vector(I, X86, "movzx ecx, byte ptr [eax]", "0F B6 08"));
            v.Add(new Vector(I, X86, "movsx ecx, al", "0F BE C8"));
            v.Add(new Vector(I, X86, "shl eax, 4", "C1 E0 04"));
            v.Add(new Vector(I, X86, "shl eax, cl", "D3 E0"));
            v.Add(new Vector(I, X86, "shl eax, 1", "D1 E0"));
            v.Add(new Vector(I, X86, "shr ebx, 2", "C1 EB 02"));
            v.Add(new Vector(I, X86, "not eax", "F7 D0"));
            v.Add(new Vector(I, X86, "neg eax", "F7 D8"));
            v.Add(new Vector(I, X86, "mul ebx", "F7 E3"));
            v.Add(new Vector(I, X86, "div ecx", "F7 F1"));
            v.Add(new Vector(I, X86, "imul ebx, eax", "0F AF D8"));
            v.Add(new Vector(I, X86, "imul ebx, [eax], 5", "6B 18 05"));
            v.Add(new Vector(I, X86, "sete al", "0F 94 C0"));
            v.Add(new Vector(I, X86, "bt eax, ebx", "0F A3 D8"));
            v.Add(new Vector(I, X86, "bswap eax", "0F C8"));
            v.Add(new Vector(I, X86, "mov eax, fs:[0x18]", "64 A1 18 00 00 00"));
            v.Add(new Vector(I, X86, "mov eax, fs:[ebx]", "64 8B 03"));
            v.Add(new Vector(I, X86, "lock inc dword ptr [eax]", "F0 FF 00"));
            v.Add(new Vector(I, X86, "cdq", "99"));
            v.Add(new Vector(I, X86, "cwde", "98"));
            v.Add(new Vector(I, X86, "movsb", "A4"));
            v.Add(new Vector(I, X86, "rep movsb", "F3 A4"));
            v.Add(new Vector(I, X86, "stosd", "AB"));
            v.Add(new Vector(I, X86, "db 0x90, 1, 255", "90 01 FF"));
            v.Add(new Vector(I, X86, "db \"AB\"", "41 42"));

            // ============ Intel x64 ============
            v.Add(new Vector(I, X64, "mov rax, 0x1122334455667788", "48 B8 88 77 66 55 44 33 22 11"));
            v.Add(new Vector(I, X64, "mov rax, 5", "48 C7 C0 05 00 00 00"));
            v.Add(new Vector(I, X64, "mov eax, 5", "B8 05 00 00 00"));
            v.Add(new Vector(I, X64, "mov rcx, rdx", "48 89 D1"));
            v.Add(new Vector(I, X64, "mov rax, r8", "4C 89 C0"));
            v.Add(new Vector(I, X64, "mov r8, rax", "49 89 C0"));
            v.Add(new Vector(I, X64, "mov r8, 5", "49 C7 C0 05 00 00 00"));
            v.Add(new Vector(I, X64, "mov al, r8b", "44 88 C0"));
            v.Add(new Vector(I, X64, "mov r8b, al", "41 88 C0"));
            v.Add(new Vector(I, X64, "mov rax, [rax]", "48 8B 00"));
            v.Add(new Vector(I, X64, "mov [rax], rbx", "48 89 18"));
            v.Add(new Vector(I, X64, "mov qword ptr [rax], 5", "48 C7 00 05 00 00 00"));
            v.Add(new Vector(I, X64, "mov dword ptr [rax], 5", "C7 00 05 00 00 00"));
            v.Add(new Vector(I, X64, "movzx eax, byte ptr [rax]", "0F B6 00"));
            v.Add(new Vector(I, X64, "movzx rax, byte ptr [rax]", "48 0F B6 00"));
            v.Add(new Vector(I, X64, "movsxd rax, dword ptr [rax]", "48 0F 63 00"));
            v.Add(new Vector(I, X64, "push rbp", "55"));
            v.Add(new Vector(I, X64, "push r8", "41 50"));
            v.Add(new Vector(I, X64, "pop r8", "41 58"));
            v.Add(new Vector(I, X64, "mov edi, esi", "89 F7"));
            v.Add(new Vector(I, X64, "sub rsp, 0x28", "48 83 EC 28"));
            v.Add(new Vector(I, X64, "call rax", "FF D0"));
            v.Add(new Vector(I, X64, "inc eax", "FF C0"));
            v.Add(new Vector(I, X64, "cdqe", "48 98"));
            v.Add(new Vector(I, X64, "cqo", "48 99"));
            v.Add(new Vector(I, X64, "mov rax, [abs 0x30]", "48 8B 04 25 30 00 00 00"));
            v.Add(new Vector(I, X64, "mov rax, gs:[abs 0x30]", "65 48 8B 04 25 30 00 00 00"));
            v.Add(new Vector(I, X64, "mov rax, [abs 0x1122334455667788]", "48 A1 88 77 66 55 44 33 22 11"));

            // ============ AT&T x86 ============
            v.Add(new Vector(A, X86, "movl $5, %eax", "B8 05 00 00 00"));
            v.Add(new Vector(A, X86, "movl %ebx, %eax", "89 D8"));
            v.Add(new Vector(A, X86, "movl %eax, %ebx", "89 C3"));
            v.Add(new Vector(A, X86, "movl %eax, (%ebx)", "89 03"));
            v.Add(new Vector(A, X86, "movl (%ebx), %eax", "8B 03"));
            v.Add(new Vector(A, X86, "movl $5, -8(%ebp)", "C7 45 F8 05 00 00 00"));
            v.Add(new Vector(A, X86, "movb $7, %al", "B0 07"));
            v.Add(new Vector(A, X86, "movw $0x1234, %bx", "66 BB 34 12"));
            v.Add(new Vector(A, X86, "pushl %eax", "50"));
            v.Add(new Vector(A, X86, "popl %ebx", "5B"));
            v.Add(new Vector(A, X86, "call *%eax", "FF D0"));
            v.Add(new Vector(A, X86, "call *(%eax)", "FF 10"));
            v.Add(new Vector(A, X86, "jmp *%ebx", "FF E3"));
            v.Add(new Vector(A, X86, "movzbl (%eax), %ecx", "0F B6 08"));
            v.Add(new Vector(A, X86, "movswl %bx, %eax", "0F BF C3"));
            v.Add(new Vector(A, X86, "addl $5, %eax", "83 C0 05"));
            v.Add(new Vector(A, X86, "addl %ebx, %eax", "01 D8"));
            v.Add(new Vector(A, X86, "subl $8, %esp", "83 EC 08"));
            v.Add(new Vector(A, X86, "xchgl %eax, %ebx", "93"));
            v.Add(new Vector(A, X86, "leal 8(%eax), %ebx", "8D 58 08"));
            v.Add(new Vector(A, X86, "incl %eax", "40"));
            v.Add(new Vector(A, X64, "cmpq $0x28, %rsp", "48 83 FC 28"));
            v.Add(new Vector(A, X86, "testl %eax, %eax", "85 C0"));
            v.Add(new Vector(A, X86, "shrl $4, %eax", "C1 E8 04"));
            v.Add(new Vector(A, X86, "movl %fs:0x18, %eax", "64 A1 18 00 00 00"));
            v.Add(new Vector(A, X86, "movl 0x10(%esi,%ebx,4), %ecx", "8B 4C 9E 10"));
            v.Add(new Vector(A, X86, "ret", "C3"));
            v.Add(new Vector(A, X86, "nop", "90"));

            // ============ AT&T x64 ============
            v.Add(new Vector(A, X64, "movq %rax, %rbx", "48 89 C3"));
            v.Add(new Vector(A, X64, "movq $0x1122334455667788, %rax", "48 B8 88 77 66 55 44 33 22 11"));
            v.Add(new Vector(A, X64, "movabsq $0x1122334455667788, %rax", "48 B8 88 77 66 55 44 33 22 11"));
            v.Add(new Vector(A, X64, "movslq %eax, %rax", "48 0F 63 C0"));
            v.Add(new Vector(A, X64, "pushq %rbp", "55"));
            v.Add(new Vector(A, X64, "movq %r8, %rax", "4C 89 C0"));
            v.Add(new Vector(A, X64, "movq %rax, %r8", "49 89 C0"));
            v.Add(new Vector(A, X64, "subq $0x28, %rsp", "48 83 EC 28"));
            v.Add(new Vector(A, X64, "call *%rax", "FF D0"));

            // ============ 标签与分支 ============
            v.Add(new Vector(I, X86, "lab:\n  jmp lab", "EB FE"));
            v.Add(new Vector(I, X86, "  jmp end\nlab:\nend: nop", "E9 00 00 00 00 90"));
            v.Add(new Vector(I, X64, "lab:\n  je lab", "74 FE"));
            v.Add(new Vector(I, X64, "  call GameAdd\nGameAdd: ret", "E8 00 00 00 00 C3"));

            return v;
        }

        public static List<TestVectorResult> RunAll()
        {
            List<TestVectorResult> results = new List<TestVectorResult>();
            foreach (Vector v in _vectors)
            {
                TestVectorResult r = new TestVectorResult();
                r.Name = (v.Syntax == SyntaxKind.Intel ? "Intel" : "AT&T") + "/" +
                         (v.Arch == ArchKind.X64 ? "x64" : "x86") + ": " +
                         v.Source.Replace("\n", " ; ");
                try
                {
                    AssembleOptions opt = new AssembleOptions();
                    opt.Syntax = v.Syntax;
                    opt.Arch = v.Arch;
                    opt.BaseAddress = 0;
                    AssembleResult res = Assembler.Assemble(v.Source, opt);
                    if (!res.Success)
                    {
                        r.Passed = false;
                        r.Error = res.Errors.Count > 0 ? res.Errors[0].ToString() : "unknown error";
                    }
                    else
                    {
                        string actual = res.Hex;
                        r.ActualHex = actual;
                        r.ExpectedHex = v.ExpectedHex;
                        r.Passed = string.Equals(
                            NormalizeHex(actual), NormalizeHex(v.ExpectedHex), StringComparison.Ordinal);
                        if (!r.Passed) r.Error = "encoding mismatch";
                    }
                }
                catch (Exception ex)
                {
                    r.Passed = false;
                    r.Error = ex.Message;
                }
                results.Add(r);
            }
            return results;
        }

        private static string NormalizeHex(string hex)
        {
            return hex.Replace(" ", string.Empty).ToUpperInvariant();
        }
    }
}
