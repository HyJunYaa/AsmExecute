// AsmEngine.cs - 汇编引擎门面
using System;
using System.Collections.Generic;
using System.Text;

namespace AsmStudio.Engine
{
    /// <summary>汇编引擎统一入口</summary>
    public static class AsmEngine
    {
        /// <summary>汇编整段文本</summary>
        public static AssembleResult Assemble(string text, AssembleOptions options)
        {
            if (options == null) options = new AssembleOptions();
            return Assembler.Assemble(text, options);
        }

        /// <summary>逐行校验（不产出代码，只收集错误）</summary>
        public static List<AsmError> Validate(string text, SyntaxKind syntax, ArchKind arch, SymbolTable symbols)
        {
            AssembleOptions opt = new AssembleOptions();
            opt.Syntax = syntax;
            opt.Arch = arch;
            opt.Symbols = symbols;
            opt.BaseAddress = 0;
            return Assembler.Validate(text, opt);
        }

        /// <summary>运行内置自检向量</summary>
        public static List<TestVectorResult> RunSelfTest()
        {
            return TestVectors.RunAll();
        }
    }

    /// <summary>自检向量运行结果</summary>
    public sealed class TestVectorResult
    {
        public string Name;
        public bool Passed;
        public string ExpectedHex;
        public string ActualHex;
        public string Error;
    }
}
