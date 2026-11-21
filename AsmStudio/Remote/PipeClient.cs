// PipeClient.cs - 命名管道客户端（与目标内 AsmLibrary.dll 通信）
// 协议见 docs/protocol.md：文本行协议，服务端连接后先发 HELLO 行（含 gate 网关地址）。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace AsmStudio.Remote
{
    internal sealed class PipeClient : IDisposable
    {
        private NamedPipeClientStream _pipe;
        private StreamReader _reader;
        private StreamWriter _writer;
        public int TargetArch;      // 4/8
        public int TargetPid;
        public int ProtoVersion;
        /// <summary>ret 重定向网关地址（目标沙箱页内固定桩）</summary>
        public ulong GateAddress;

        public bool Connected
        {
            get { return _pipe != null && _pipe.IsConnected; }
        }

        /// <summary>连接 \\.\pipe\asmexec_<pid> 并完成握手</summary>
        public bool Connect(int pid, int timeoutMs)
        {
            try
            {
                _pipe = new NamedPipeClientStream(".", "asmexec_" + pid,
                    PipeDirection.InOut, PipeOptions.None);
                _pipe.Connect(timeoutMs);
                if (!_pipe.IsConnected)
                    return false;

                // 无 BOM 的 UTF-8（避免首行混入 EF BB BF 前导）
                Encoding enc = new UTF8Encoding(false);
                _reader = new StreamReader(_pipe, enc);
                _writer = new StreamWriter(_pipe, enc);
                _writer.AutoFlush = true;

                // 服务端欢迎行: OK proto=1 arch=8 pid=1234 version=256 gate=...
                string hello = _reader.ReadLine();
                if (hello == null || !hello.StartsWith("OK", StringComparison.Ordinal))
                    return false;
                foreach (string kv in hello.Split(' '))
                {
                    string[] pair = kv.Split('=');
                    if (pair.Length != 2) continue;
                    int v;
                    if (pair[0] == "proto" && int.TryParse(pair[1], out v)) ProtoVersion = v;
                    if (pair[0] == "arch" && int.TryParse(pair[1], out v)) TargetArch = v;
                    if (pair[0] == "pid" && int.TryParse(pair[1], out v)) TargetPid = v;
                    if (pair[0] == "gate") GateAddress = ParseHex(pair[1]);
                }
                return TargetArch == 4 || TargetArch == 8;
            }
            catch (Exception)
            {
                Dispose();
                return false;
            }
        }

        /// <summary>拉取目标符号表</summary>
        public List<SymbolInfo> GetSymbols()
        {
            List<SymbolInfo> list = new List<SymbolInfo>();
            _writer.WriteLine("SYMBOLS");
            string line = _reader.ReadLine();   // BEGIN
            if (line == null || !line.StartsWith("BEGIN", StringComparison.Ordinal))
                return list;
            while (true)
            {
                line = _reader.ReadLine();
                if (line == null) break;
                if (line.StartsWith("END", StringComparison.Ordinal)) break;
                // SYM <name> <hexaddr>
                if (line.StartsWith("SYM ", StringComparison.Ordinal))
                {
                    string body = line.Substring(4);
                    int sp = body.LastIndexOf(' ');
                    if (sp > 0)
                    {
                        SymbolInfo s = new SymbolInfo();
                        s.Name = body.Substring(0, sp);
                        ulong addr;
                        string addrText = body.Substring(sp + 1).Trim();
                        if (addrText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                            addrText = addrText.Substring(2);
                        if (ulong.TryParse(addrText, System.Globalization.NumberStyles.HexNumber,
                            null, out addr))
                        {
                            s.Address = addr;
                            list.Add(s);
                        }
                    }
                }
            }
            return list;
        }

        /// <summary>执行请求。code 可为 null（纯 call）。</summary>
        public ExecutionResult Execute(byte[] code, ulong[] args, int nargs,
            ulong callTarget, bool modeCall, int timeoutMs)
        {
            ExecutionResult r = new ExecutionResult();
            StringBuilder sb = new StringBuilder();
            sb.Append("EXEC mode=");
            sb.Append(modeCall ? "call" : "code");
            if (modeCall && callTarget != 0)
            {
                sb.Append(" call=0x");
                sb.Append(callTarget.ToString("x"));
            }
            if (args != null && nargs > 0)
            {
                sb.Append(" args=");
                for (int i = 0; i < nargs; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(args[i].ToString("x"));
                }
            }
            sb.Append(" timeout=");
            sb.Append(timeoutMs);
            sb.Append(" code_hex=");
            if (code != null)
            {
                // 紧凑 hex（协议值不允许空格）
                for (int i = 0; i < code.Length; i++)
                    sb.Append(code[i].ToString("X2"));
            }
            _writer.WriteLine(sb.ToString());

            string resp = _reader.ReadLine();
            if (resp == null)
            {
                r.Status = ExecStatus.NoConnection;
                r.Message = "管道连接中断";
                return r;
            }
            ParseResponse(resp, r);
            return r;
        }

        private static void ParseResponse(string resp, ExecutionResult r)
        {
            string[] parts = resp.Split(' ');
            if (parts[0] == "OK")
            {
                r.Status = ExecStatus.Ok;
                foreach (string kv in parts)
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = kv.Substring(0, eq);
                    string val = kv.Substring(eq + 1);
                    if (key == "ret") r.RetValue = ParseHex(val);
                    else if (key == "flags") r.FlagsFinal = ParseHex(val);
                    else if (key == "regs") ParseRegs(val, r.RegsFinal);
                }
            }
            else if (parts[0] == "ERR")
            {
                foreach (string kv in parts)
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = kv.Substring(0, eq);
                    string val = kv.Substring(eq + 1);
                    switch (key)
                    {
                        case "status":
                            if (val == "exc") r.Status = ExecStatus.Exception;
                            else if (val == "timeout") r.Status = ExecStatus.Timeout;
                            else if (val == "busy") r.Status = ExecStatus.Busy;
                            else if (val == "badarg") r.Status = ExecStatus.BadArg;
                            else r.Status = ExecStatus.Internal;
                            break;
                        case "code": r.ExcCode = (uint)ParseHex(val); break;
                        case "addr": r.ExcAddr = ParseHex(val); break;
                        case "av": r.ExcWasAccessViolation = val == "1"; break;
                        case "rw":
                            {
                                int v;
                                if (int.TryParse(val, out v)) r.ExcReadOrWrite = v;
                            }
                            break;
                        case "flags": r.FlagsExc = ParseHex(val); break;
                        case "regs": ParseRegs(val, r.RegsExc); break;
                    }
                }
            }
            else
            {
                r.Status = ExecStatus.Internal;
                r.Message = resp;
            }
        }

        private static void ParseRegs(string text, ulong[] regs)
        {
            if (text == null) return;
            foreach (string pair in text.Split(','))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                string name = pair.Substring(0, eq);
                ulong val = ParseHex(pair.Substring(eq + 1));
                for (int i = 0; i < RegIdx.Count; i++)
                {
                    if (string.Equals(RegIdx.Name(i), name, StringComparison.OrdinalIgnoreCase))
                    {
                        regs[i] = val;
                        break;
                    }
                }
            }
        }

        private static ulong ParseHex(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(2);
            ulong v;
            return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out v) ? v : 0;
        }

        public void Dispose()
        {
            try
            {
                if (_writer != null) _writer.Dispose();
            }
            catch (Exception) { }
            try
            {
                if (_reader != null) _reader.Dispose();
            }
            catch (Exception) { }
            try
            {
                if (_pipe != null) _pipe.Dispose();
            }
            catch (Exception) { }
            _writer = null;
            _reader = null;
            _pipe = null;
        }
    }
}
