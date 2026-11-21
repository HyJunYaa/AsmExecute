// MainForm.cs - 主窗口逻辑：多文档汇编编辑 / 逐行校验 / 汇编 / 远程执行 / 方案运行
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using AsmStudio.Controls;
using AsmStudio.Engine;
using AsmStudio.Remote;
using AsmStudio.Schemes;

namespace AsmStudio
{
    public partial class MainForm : Form
    {
        private sealed class DocInfo
        {
            public TabPage Page;
            public AsmEditor Editor;
            public string FilePath;   // null = 未保存
            public SyntaxKind DocSyntax = SyntaxKind.Intel;   // 当前内容所属语法（切换时据此转换）
        }

        private readonly List<DocInfo> _docs = new List<DocInfo>();
        private readonly List<SchemeFile> _schemes = new List<SchemeFile>();
        private TargetSession _session;
        private SymbolTable _symbolTable = new SymbolTable();
        private int _newDocCounter = 1;

        public MainForm()
        {
            InitializeComponent();
            cboChannel.SelectedIndex = 0;
            cboChannel.SelectedIndexChanged += ChannelChanged;
            CreateDocument(null, null);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Log("AsmStudio 就绪。使用步骤:");
            Log("  1. 启动目标程序（bin\\Debug\\x64\\TargetCpp.exe 或 TargetSharp.exe）");
            Log("  2. 点击【扫描进程】并选择目标 → 连接");
            Log("  3. 编辑汇编 → 验证 → 执行 / 调用目标函数");
            Log("  4. 【方案】菜单可加载 Schemes\\*.asmscheme 顺序执行");
            Log("");
            Log("提示: 本进程模式需要把 AsmLibrary.dll 放到主程序目录。");
        }

        // ================= 文档管理 =================

        private DocInfo CurrentDoc
        {
            get
            {
                int idx = tabControlDocs.SelectedIndex;
                if (idx >= 0 && idx < _docs.Count) return _docs[idx];
                return null;
            }
        }

        private DocInfo CreateDocument(string filePath, string content)
        {
            DocInfo doc = new DocInfo();
            doc.Editor = new AsmEditor();
            doc.Editor.Dock = DockStyle.Fill;
            doc.Editor.TextChangedDelayed += EditorTextChangedDelayed;
            doc.Editor.CaretInfoChanged += delegate
            {
                DocInfo cur = CurrentDoc;
                if (cur != null && ReferenceEquals(doc, cur))
                {
                    lblStatus.Text = "行 " + doc.Editor.CaretLine + ", 列 " +
                        doc.Editor.CaretColumn + "  |  就绪";
                }
            };
            doc.FilePath = filePath;
            doc.DocSyntax = CurrentSyntax;

            TabPage page = new TabPage();
            page.Controls.Add(doc.Editor);
            page.Text = filePath != null ? Path.GetFileName(filePath)
                : ("汇编" + _newDocCounter + ".asm");
            if (filePath == null) _newDocCounter++;

            doc.Page = page;
            tabControlDocs.TabPages.Add(page);
            _docs.Add(doc);
            tabControlDocs.SelectedIndex = tabControlDocs.TabPages.Count - 1;

            if (content != null)
                doc.Editor.Text = content;
            else
                doc.Editor.Text = GetDefaultTemplate();
            return doc;
        }

        private static string GetDefaultTemplate()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; Intel 语法示例: 调用目标的 GameAddScore(5)");
            sb.AppendLine("; 远程执行时请用 mov+call 间接模板（跨进程安全）:");
            sb.AppendLine("mov rax, GameAddScore   ; 符号由主程序注入");
            sb.AppendLine("mov rcx, 5              ; x64 第一参数 → rcx");
            sb.AppendLine("call rax");
            sb.AppendLine("ret");
            return sb.ToString();
        }

        private DocInfo FindDoc(TabPage page)
        {
            foreach (DocInfo d in _docs)
                if (ReferenceEquals(d.Page, page)) return d;
            return null;
        }

        private void CloseDoc(int index)
        {
            if (_docs.Count <= 1) return;
            if (MessageBox.Show("关闭文档 " + _docs[index].Page.Text + "？", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            TabPage page = _docs[index].Page;
            tabControlDocs.TabPages.Remove(page);
            _docs.RemoveAt(index);
        }

        // ================= 菜单 =================

        private void MenuFileNewClick(object sender, EventArgs e)
        {
            CreateDocument(null, null);
        }

        private void MenuFileOpenClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "汇编文件 (*.asm;*.s;*.txt)|*.asm;*.s;*.txt|全部文件 (*.*)|*.*";
                dlg.Title = "打开汇编文件";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        string content = File.ReadAllText(dlg.FileName);
                        DocInfo doc = CreateDocument(dlg.FileName, content);
                        doc.Editor.Syntax = rbAtt.Checked
                            ? AsmEditor.SyntaxKindView.Att
                            : AsmEditor.SyntaxKindView.Intel;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "读取失败: " + ex.Message, "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void MenuFileSaveClick(object sender, EventArgs e)
        {
            SaveDocument(CurrentDoc);
        }

        private void MenuFileExitClick(object sender, EventArgs e)
        {
            Close();
        }

        // ---- 标签右键菜单：单独保存 / 关闭其他 / 全部关闭 ----

        private void MenuTabSaveClick(object sender, EventArgs e)
        {
            SaveDocument(CurrentDoc);
        }

        /// <summary>强制关闭文档（不弹确认；用于关闭其他/全部关闭的批量操作）</summary>
        private void ForceCloseDoc(DocInfo doc)
        {
            if (doc == null) return;
            tabControlDocs.TabPages.Remove(doc.Page);
            _docs.Remove(doc);
        }

        private void MenuTabCloseOthersClick(object sender, EventArgs e)
        {
            DocInfo cur = CurrentDoc;
            if (cur == null) return;
            int count = _docs.Count - 1;
            if (count <= 0) return;
            if (MessageBox.Show("关闭其他 " + count + " 个文档？", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            List<DocInfo> doomed = new List<DocInfo>();
            foreach (DocInfo d in _docs)
                if (!ReferenceEquals(d, cur)) doomed.Add(d);
            foreach (DocInfo d in doomed) ForceCloseDoc(d);
            if (_docs.Count > 0) tabControlDocs.SelectedIndex = 0;
        }

        private void MenuTabCloseAllClick(object sender, EventArgs e)
        {
            if (_docs.Count == 0) return;
            if (MessageBox.Show("关闭全部 " + _docs.Count + " 个文档？", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            foreach (DocInfo d in _docs) tabControlDocs.TabPages.Remove(d.Page);
            _docs.Clear();
        }

        private void SaveDocument(DocInfo doc)
        {
            if (doc == null) return;
            string path = doc.FilePath;
            if (path == null)
            {
                using (SaveFileDialog dlg = new SaveFileDialog())
                {
                    dlg.Filter = "汇编文件 (*.asm)|*.asm|全部文件 (*.*)|*.*";
                    dlg.FileName = doc.Page.Text;
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    path = dlg.FileName;
                }
            }
            try
            {
                File.WriteAllText(path, doc.Editor.Text, new UTF8Encoding(false));
                doc.FilePath = path;
                doc.Page.Text = Path.GetFileName(path);
                lblStatus.Text = "已保存: " + path;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TabControlDocsSelectedIndexChanged(object sender, EventArgs e)
        {
            DocInfo doc = CurrentDoc;
            if (doc == null) return;
            // 同步 radio 到该文档的语法（now == doc.DocSyntax 时 SyntaxChanged 不做转换）
            bool wantAtt = doc.DocSyntax == SyntaxKind.Att;
            if (rbAtt.Checked != wantAtt) rbAtt.Checked = wantAtt;
            else doc.Editor.Syntax = wantAtt
                ? AsmEditor.SyntaxKindView.Att : AsmEditor.SyntaxKindView.Intel;
            ValidateCurrent();
        }

        /// <summary>标签右键：命中某个标签时选中并弹出关闭菜单</summary>
        private void TabControlDocsMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            for (int i = 0; i < tabControlDocs.TabCount; i++)
            {
                Rectangle r = tabControlDocs.GetTabRect(i);
                if (r.Contains(e.Location))
                {
                    tabControlDocs.SelectedIndex = i;
                    tabCloseMenu.Show(tabControlDocs, e.Location);
                    break;
                }
            }
        }

        private void TabCloseMenuItemClick(object sender, EventArgs e)
        {
            CloseDoc(tabControlDocs.SelectedIndex);
        }

        // ================= 编辑（撤回/重做） =================

        private void MenuEditUndoClick(object sender, EventArgs e)
        {
            DocInfo doc = CurrentDoc;
            if (doc != null) doc.Editor.Undo();
        }

        private void MenuEditRedoClick(object sender, EventArgs e)
        {
            DocInfo doc = CurrentDoc;
            if (doc != null) doc.Editor.Redo();
        }

        private void MenuEditDropDownOpening(object sender, EventArgs e)
        {
            DocInfo doc = CurrentDoc;
            menuEditUndo.Enabled = doc != null && doc.Editor.CanUndo;
            menuEditRedo.Enabled = doc != null && doc.Editor.CanRedo;
        }

        private void MenuSchemeLoadClick(object sender, EventArgs e)
        {
            LoadSchemes();
        }

        private string _schemesDir;   // 会话内记住用户选择的方案文件夹

        private void LoadSchemes()
        {
            _schemes.Clear();
            string dir = _schemesDir ?? FindDefaultSchemesDir();
            if (!Directory.Exists(dir))
            {
                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "选择包含 *.asmscheme 方案文件的文件夹";
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                    {
                        Log("未选择方案目录。");
                        return;
                    }
                    dir = dlg.SelectedPath;
                }
            }
            _schemesDir = dir;
            _schemes.AddRange(SchemeFile.ScanDirectory(dir));
            Log("扫描方案目录: " + dir + " → " + _schemes.Count + " 个方案");
            foreach (SchemeFile s in _schemes)
                Log("  方案: " + s.Name + " (" + s.Steps.Count + " 步)");
        }

        private static string FindDefaultSchemesDir()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = {
                Path.Combine(exeDir, "Resources"),                     // 构建输出（csproj 自动复制）
                Path.Combine(exeDir, "..", "..", "Resources"),         // 源码目录（未构建时直接读源）
                Path.Combine(exeDir, "Schemes"),                       // 旧位置兼容
            };
            foreach (string c in candidates)
                if (Directory.Exists(c)) return Path.GetFullPath(c);
            return "Resources";
        }

        private void MenuSchemeRunClick(object sender, EventArgs e)
        {
            if (_session == null || !_sessionConnected)
            {
                MessageBox.Show(this, "请先连接目标。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_schemes.Count == 0) LoadSchemes();
            if (_schemes.Count == 0)
            {
                MessageBox.Show(this, "未找到方案文件（Schemes\\*.asmscheme）。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SchemePickForm pick = new SchemePickForm(_schemes))
            {
                if (pick.ShowDialog(this) != DialogResult.OK) return;
                SchemeFile scheme = pick.Selected;
                RunScheme(scheme);
            }
        }

        private void RunScheme(SchemeFile scheme)
        {
            Log("======== 运行方案: " + scheme.Name + " ========");
            SchemeRunner runner = new SchemeRunner(_session, Log);
            List<StepLog> logs = runner.Run(scheme);
            int ok = 0;
            foreach (StepLog l in logs)
                if (l.Success) ok++;
            Log("======== 方案完成: " + ok + "/" + logs.Count + " 步成功 ========");
            tabControlResults.SelectedTab = tabLog;
        }

        private void MenuToolsSelfTestClick(object sender, EventArgs e)
        {
            List<TestVectorResult> results = AsmEngine.RunSelfTest();
            int failed = 0;
            Log("======== 汇编引擎自检 ========");
            foreach (TestVectorResult r in results)
            {
                if (!r.Passed)
                {
                    failed++;
                    Log("FAIL " + r.Name);
                    Log("      期望: " + r.ExpectedHex);
                    Log("      实际: " + (r.ActualHex ?? "(无)"));
                    if (!string.IsNullOrEmpty(r.Error))
                        Log("      错误: " + r.Error);
                }
            }
            Log("自检完成: " + (results.Count - failed) + "/" + results.Count + " 通过");
            MessageBox.Show(this,
                string.Format("汇编引擎自检: {0}/{1} 个向量通过{2}",
                    results.Count - failed, results.Count,
                    failed == 0 ? "\n\n全部通过 ✓" : "\n\n存在失败项，见日志"),
                "自检结果", MessageBoxButtons.OK,
                failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            tabControlResults.SelectedTab = tabLog;
        }

        private void MenuToolsInjectClick(object sender, EventArgs e)
        {
            ProcessInfo target = SelectedProcess;
            if (target == null || !_sessionConnected)
            {
                MessageBox.Show(this, "请先连接目标（远程线程模式）。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string dllPath = FindNativeDll(target.Is64Bit);
            if (dllPath == null)
            {
                MessageBox.Show(this, "找不到 AsmLibrary.dll（对应架构目录）", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string err = DllInjector.Inject(target, dllPath);
            if (err == null)
                Log("[+] 注入完成: " + dllPath);
            else
                Log("[!] 注入失败: " + err);
        }

        /// <summary>按目标位数查找 AsmLibrary.dll（IDE 构建输出在各工程 bin\debug 下）</summary>
        private static string FindNativeDll(bool target64)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string dllName = "AsmLibrary.dll";
            string archDir = target64 ? "x64" : "x86";
            // 仓库根 = exe(AsmStudio\bin\debug) 上三级
            string root = Path.GetFullPath(Path.Combine(exeDir, "..", "..", ".."));
            string[] candidates = {
                Path.Combine(exeDir, dllName),                                    // 主程序运行目录
                Path.Combine(root, "AsmLibrary", "bin", "debug", archDir, dllName), // CppBinding 平台子目录
                Path.Combine(root, "AsmLibrary", "bin", "debug", dllName),          // CppBinding 输出目录
                Path.Combine(root, "bin", archDir, dllName),                       // 旧聚合布局（若存在）
            };
            foreach (string c in candidates)
                if (File.Exists(c)) return Path.GetFullPath(c);
            return null;
        }

        private void MenuHelpAboutClick(object sender, EventArgs e)
        {
            MessageBox.Show(this,
                "AsmStudio — 汇编编辑 / 校验 / 远程执行\n\n" +
                "C# 自研汇编引擎 (Intel/AT&T, x86/x64)\n" +
                "AsmLibrary.dll VEH 沙箱执行 + 命名管道远程通道\n\n" +
                "仅用于自有/已授权进程的开发调试与学习。",
                "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ================= 扫描 / 连接 =================

        private bool _sessionConnected;

        private ProcessInfo SelectedProcess
        {
            get
            {
                int idx = cboProcess.SelectedIndex;
                if (idx < 0 || idx >= _scanResult.Count)
                    idx = MatchProcessIndex(cboProcess.Text);
                return idx >= 0 && idx < _scanResult.Count ? _scanResult[idx] : null;
            }
        }

        /// <summary>手输名称 → 进程索引（未从下拉选中时）。依次尝试：
        /// 文本带 pid= 时按 pid 精确 → 整项文本精确 → 进程名精确 → 名称前缀 → 名称包含（均不区分大小写）</summary>
        private int MatchProcessIndex(string raw)
        {
            string text = raw != null ? raw.Trim() : "";
            if (text.Length == 0) return -1;

            // 文本里带 pid= 时按 pid 精确匹配（如从完整项文本复制/改写而来）
            int p = text.IndexOf("pid=", StringComparison.OrdinalIgnoreCase);
            if (p >= 0)
            {
                int digitStart = p + 4, digits = 0;
                while (digitStart + digits < text.Length && char.IsDigit(text[digitStart + digits])) digits++;
                if (digits > 0)
                {
                    int pid;
                    if (int.TryParse(text.Substring(digitStart, digits), out pid))
                    {
                        for (int i = 0; i < _scanResult.Count; i++)
                            if (_scanResult[i].Pid == pid) return i;
                    }
                }
            }

            // 整项文本精确匹配
            for (int i = 0; i < _scanResult.Count; i++)
                if (string.Equals(_scanResult[i].ToString(), text, StringComparison.OrdinalIgnoreCase))
                    return i;

            // 剥掉可能带的 "(pid=…)" 尾巴后按进程名匹配
            string name = text;
            int paren = name.IndexOf("(pid=", StringComparison.OrdinalIgnoreCase);
            if (paren > 0) name = name.Substring(0, paren).Trim();
            if (name.Length == 0) return -1;

            for (int i = 0; i < _scanResult.Count; i++)
                if (string.Equals(_scanResult[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            for (int i = 0; i < _scanResult.Count; i++)
                if (_scanResult[i].Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    return i;
            for (int i = 0; i < _scanResult.Count; i++)
                if (_scanResult[i].Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            return -1;
        }
        private List<ProcessInfo> _scanResult = new List<ProcessInfo>();

        private void BtnScanClick(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                List<ProcessInfo> all = ProcessScanner.ScanProcesses();
                _scanResult = new List<ProcessInfo>();
                cboProcess.Items.Clear();
                foreach (ProcessInfo p in all)
                {
                    if (p.Pid <= 4) continue;
                    _scanResult.Add(p);
                    cboProcess.Items.Add(p.ToString());
                }
                lblTargetArch.Text = "";
                Log("扫描完成: " + _scanResult.Count + " 个进程");
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void CboProcessEnsureScan(object sender, EventArgs e)
        {
            // 打开下拉 / 点输入框 / 输入首字符时若尚无扫描结果则自动扫描，
            // 保证任何路径下编辑补全都有进程名单可依据
            if (_scanResult.Count == 0)
                BtnScanClick(sender, e);
        }

        private ExecChannel SelectedChannel
        {
            get
            {
                switch (cboChannel.SelectedIndex)
                {
                    case 1: return ExecChannel.RemoteThreadVeh;
                    case 2: return ExecChannel.RemoteThread;
                    case 3: return ExecChannel.InProcess;
                    default: return ExecChannel.PipeDll;
                }
            }
        }

        private void ChannelChanged(object sender, EventArgs e)
        {
            cboProcess.Enabled = SelectedChannel != ExecChannel.InProcess;
        }

        private void BtnConnectClick(object sender, EventArgs e)
        {
            Disconnect();
            ExecChannel channel = SelectedChannel;

            if (channel == ExecChannel.InProcess)
            {
                _session = new TargetSession();
                if (_session.Connect(null, ExecChannel.InProcess))
                {
                    _session.LoadInProcessDemoSymbols();
                    FillSymbols();
                    _sessionConnected = true;
                    lblMode.Text = "已连接: 本进程 (VEH 沙箱)";
                    Log("[+] 本进程模式就绪（演示符号: DemoAdd/DemoEcho）");
                }
                else
                {
                    Log("[!] " + _session.LastError);
                    _session.Dispose();
                    _session = null;
                }
                return;
            }

            ProcessInfo target = SelectedProcess;
            if (target == null)
            {
                MessageBox.Show(this, "请先扫描并选择目标进程。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            lblTargetArch.Text = target.ArchText;
            cboProcess.Text = target.ToString();   // 手输名称规整为完整项文本

            _session = new TargetSession();
            bool ok;
            if (channel == ExecChannel.PipeDll)
            {
                ok = _session.Connect(target, ExecChannel.PipeDll);
                if (!ok && !TargetSession.HasPipe(target.Pid))
                {
                    // 管道不通 → 询问是否回退到远程线程+汇编VEH（无 DLL）
                    DialogResult dr = MessageBox.Show(this,
                        "目标未加载 AsmLibrary（管道不存在）。\n\n" +
                        "改用【远程线程+VEH】模式连接吗？\n" +
                        "(无 DLL 注入，汇编级崩溃保护)",
                        "回退模式", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (dr == DialogResult.Yes)
                    {
                        _session.Dispose();
                        _session = new TargetSession();
                        channel = ExecChannel.RemoteThreadVeh;
                        ok = _session.Connect(target, ExecChannel.RemoteThreadVeh);
                        if (!ok)
                        {
                            Log("[!] " + _session.LastError);
                            _session.Dispose();
                            _session = null;
                            return;
                        }
                        cboChannel.SelectedIndex = 1;
                    }
                }
            }
            else
            {
                ok = _session.Connect(target, channel);
            }

            if (ok)
            {
                _sessionConnected = true;
                FillSymbols();
                lblMode.Text = string.Format("已连接: {0} pid={1} {2} [{3}]",
                    target.Name, target.Pid, target.ArchText, channel);
                Log("[+] 已连接 " + target.Name + " (pid=" + target.Pid + ", " +
                    target.ArchText + ", 通道=" + channel + ")");
                Log("    符号 " + _session.Symbols.Count + " 个");
                // 自动切换架构选择
                if (channel == ExecChannel.PipeDll || channel == ExecChannel.RemoteThread)
                {
                    if (target.Is64Bit) rbX64.Checked = true;
                    else rbX86.Checked = true;
                }
            }
            else
            {
                Log("[!] 连接失败: " + _session.LastError);
                _session.Dispose();
                _session = null;
            }
        }

        private void BtnDisconnectClick(object sender, EventArgs e)
        {
            Disconnect();
            lblMode.Text = "未连接";
            Log("[-] 已断开");
        }

        private void Disconnect()
        {
            _sessionConnected = false;
            if (_session != null)
            {
                _session.Dispose();
                _session = null;
            }
            cboSymbol.Items.Clear();
        }

        private void BtnSymbolsClick(object sender, EventArgs e)
        {
            if (_session != null && _sessionConnected)
            {
                _session.RefreshSymbols();
                FillSymbols();
                Log("[+] 符号表已刷新: " + _session.Symbols.Count + " 个");
            }
            else if (_sessionConnected && _session != null && _session.Target != null)
            {
                // 无管道（RemoteThreadVeh/RemoteThread）：从目标内存解析模块导出表
                FillRemoteModules();
            }
            else
            {
                Log("[!] 请先连接目标。");
            }
        }

        // ---- 无管道符号源：模块列表 → 模块导出 API ----

        private List<RemoteSymbols.ModuleInfo> _remoteModules =
            new List<RemoteSymbols.ModuleInfo>();
        private List<SymbolInfo> _remoteExports = new List<SymbolInfo>();
        private int _symbolMode;   // 0=管道 1=模块列表 2=某模块导出

        private void FillRemoteModules()
        {
            _symbolTable.Clear();
            _remoteExports.Clear();
            _remoteModules = RemoteSymbols.EnumModules(_session.Target.Pid);
            cboSymbol.Items.Clear();
            foreach (RemoteSymbols.ModuleInfo m in _remoteModules)
                cboSymbol.Items.Add(m.Name);
            _symbolMode = 1;
            Log("[+] 模块列表已刷新: " + _remoteModules.Count + " 个（选择模块可查看其导出 API）");
            if (cboSymbol.Items.Count > 0) cboSymbol.SelectedIndex = 0;
        }

        private void LoadRemoteExports(RemoteSymbols.ModuleInfo m)
        {
            Log("[*] 正在解析 " + m.Name + " 导出表 ...");
            Cursor = Cursors.WaitCursor;
            try
            {
                _remoteExports = RemoteSymbols.ReadExports(
                    _session.Target.Pid, m, _session.Target.Is64Bit);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            _symbolTable.Clear();
            cboSymbol.Items.Clear();
            foreach (SymbolInfo s in _remoteExports)
            {
                if (s.Address != 0) _symbolTable.Add(s.Name, s.Address);
                cboSymbol.Items.Add(s.ToString());
            }
            _symbolMode = 2;
            Log("[+] " + m.Name + " 导出 API: " + _remoteExports.Count +
                " 个（选择 API 即填入调用目标）");
            if (cboSymbol.Items.Count > 0) cboSymbol.SelectedIndex = 0;
        }

        private void FillSymbols()
        {
            _symbolTable.Clear();
            cboSymbol.Items.Clear();
            _symbolMode = 0;
            if (_session == null) return;
            foreach (SymbolInfo s in _session.Symbols)
            {
                _symbolTable.Add(s.Name, s.Address);
                cboSymbol.Items.Add(s.ToString());
            }
            if (cboSymbol.Items.Count > 0) cboSymbol.SelectedIndex = 0;
            foreach (DocInfo d in _docs)
                d.Editor.SetErrorLines(new int[0]);
        }

        private void CboSymbolSelectedIndexChanged(object sender, EventArgs e)
        {
            if (_symbolMode == 1)
            {
                // 选中模块 → 载入其导出 API
                int idx = cboSymbol.SelectedIndex;
                if (idx >= 0 && idx < _remoteModules.Count)
                    LoadRemoteExports(_remoteModules[idx]);
                return;
            }
            if (_symbolMode == 2)
            {
                // 选中导出 API → 填入调用目标（十六进制地址）并自动勾选调用
                int idx = cboSymbol.SelectedIndex;
                if (idx >= 0 && idx < _remoteExports.Count)
                {
                    SymbolInfo s = _remoteExports[idx];
                    if (s.Address != 0)
                    {
                        txtCallTarget.Text = "0x" + s.Address.ToString("X");
                        chkModeCall.Checked = true;
                    }
                }
                return;
            }
            SymbolInfo ps = SelectedSymbol;
            if (ps != null)
                txtCallTarget.Text = ps.Name;
        }

        private SymbolInfo SelectedSymbol
        {
            get
            {
                if (_session == null || cboSymbol.SelectedIndex < 0 ||
                    cboSymbol.SelectedIndex >= _session.Symbols.Count)
                    return null;
                return _session.Symbols[cboSymbol.SelectedIndex];
            }
        }

        // ================= 语法 / 架构 =================

        private SyntaxKind CurrentSyntax
        {
            get { return rbAtt.Checked ? SyntaxKind.Att : SyntaxKind.Intel; }
        }

        private ArchKind CurrentArch
        {
            get { return rbX86.Checked ? ArchKind.X86 : ArchKind.X64; }
        }

        private void SyntaxChanged(object sender, EventArgs e)
        {
            DocInfo doc = CurrentDoc;
            SyntaxKind now = CurrentSyntax;
            if (doc != null)
            {
                // 切换语法时做源码级转换（内容跟随风格；无法识别的行原样保留）
                if (now != doc.DocSyntax && doc.Editor.Text.Length > 0)
                {
                    try
                    {
                        string converted = SyntaxConverter.Convert(
                            doc.Editor.Text, doc.DocSyntax, now);
                        doc.Editor.Text = converted;
                    }
                    catch { /* 转换失败保持原文 */ }
                }
                doc.DocSyntax = now;
                doc.Editor.Syntax = rbAtt.Checked
                    ? AsmEditor.SyntaxKindView.Att : AsmEditor.SyntaxKindView.Intel;
            }
            ValidateCurrent();
        }

        private void ArchChanged(object sender, EventArgs e)
        {
            ValidateCurrent();
        }

        // ================= 校验 / 汇编 / 执行 =================

        private void EditorTextChangedDelayed(object sender, EventArgs e)
        {
            ValidateCurrent();
        }

        private void ValidateCurrent()
        {
            DocInfo doc = CurrentDoc;
            if (doc == null) return;
            List<AsmError> errors = AsmEngine.Validate(
                doc.Editor.Text, CurrentSyntax, CurrentArch, _symbolTable);
            doc.Editor.SetErrorLines(GetErrorLines(errors));
            lstErrors.Items.Clear();
            foreach (AsmError err in errors)
                lstErrors.Items.Add(err.ToString());
            lblStatus.Text = "行 " + doc.Editor.CaretLine + ", 列 " + doc.Editor.CaretColumn +
                "  |  " + (errors.Count == 0 ? "校验通过" : (errors.Count + " 个错误"));
        }

        private static List<int> GetErrorLines(List<AsmError> errors)
        {
            List<int> lines = new List<int>();
            foreach (AsmError e in errors)
                if (e.Line > 0) lines.Add(e.Line);
            return lines;
        }

        private void LstErrorsDoubleClick(object sender, EventArgs e)
        {
            // 双击错误 → 定位（简单实现：选中行首）
            DocInfo doc = CurrentDoc;
            if (doc == null || lstErrors.SelectedIndex < 0) return;
            string text = lstErrors.SelectedItem as string;
            if (text == null) return;
            int li = text.IndexOf("行 ");
            int lineNo = 0;
            if (li >= 0)
            {
                StringBuilder num = new StringBuilder();
                for (int i = li + 2; i < text.Length && char.IsDigit(text[i]); i++)
                    num.Append(text[i]);
                int.TryParse(num.ToString(), out lineNo);
            }
            if (lineNo <= 0) return;
            int target = 0;
            int cur = 1;
            string full = doc.Editor.Text;
            int idx = 0;
            while (cur < lineNo && idx < full.Length)
            {
                int nl = full.IndexOf('\n', idx);
                if (nl < 0) break;
                target = nl + 1;
                idx = target;
                cur++;
            }
            doc.Editor.SelectAt(target, 0);
        }

        private void BtnAssembleClick(object sender, EventArgs e)
        {
            DocInfo doc = CurrentDoc;
            if (doc == null) return;
            AssembleOptions opt = new AssembleOptions();
            opt.Syntax = CurrentSyntax;
            opt.Arch = CurrentArch;
            opt.Symbols = _symbolTable;
            opt.BaseAddress = 0;
            if (_session != null && _session.Channel == ExecChannel.PipeDll)
                opt.RetTrapAddress = _session.GateAddress;
            AssembleResult result = AsmEngine.Assemble(doc.Editor.Text, opt);
            if (result.Success)
            {
                txtHex.Text = result.Hex;
                lblStatus.Text = "汇编成功: " + result.Code.Length + " 字节";
                Log("[+] 汇编成功: " + result.Code.Length + " 字节");
                foreach (KeyValuePair<string, ulong> kv in result.Labels)
                    Log("    标签 " + kv.Key + " = +0x" + kv.Value.ToString("x"));
                tabControlResults.SelectedTab = tabHex;
            }
            else
            {
                txtHex.Text = "";
                lstErrors.Items.Clear();
                foreach (AsmError err in result.Errors)
                    lstErrors.Items.Add(err.ToString());
                Log("[!] 汇编失败: " + result.Errors.Count + " 个错误");
                tabControlResults.SelectedTab = tabErrors;
            }
        }

        private void BtnExecuteClick(object sender, EventArgs e)
        {
            DocInfo doc = CurrentDoc;
            if (doc == null) return;
            if (!_sessionConnected || _session == null)
            {
                MessageBox.Show(this, "请先连接目标。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AssembleOptions opt = new AssembleOptions();
            opt.Syntax = CurrentSyntax;
            opt.Arch = CurrentArch;
            opt.Symbols = _symbolTable;
            opt.BaseAddress = 0;
            if (_session != null && _session.Channel == ExecChannel.PipeDll)
                opt.RetTrapAddress = _session.GateAddress;
            AssembleResult result = AsmEngine.Assemble(doc.Editor.Text, opt);
            if (!result.Success)
            {
                lstErrors.Items.Clear();
                foreach (AsmError err in result.Errors)
                    lstErrors.Items.Add(err.ToString());
                tabControlResults.SelectedTab = tabErrors;
                MessageBox.Show(this, "汇编失败，请先修正错误。", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            txtHex.Text = result.Hex;
            ulong[] args;
            int nargs;
            ParseArgs(out args, out nargs);
            ulong callTarget = ParseCallTarget();
            bool modeCall = chkModeCall.Checked && callTarget != 0;
            int timeout = ParseTimeout();

            Log("======== 执行 (" + result.Code.Length + " 字节, " +
                (modeCall ? "call=" + txtCallTarget.Text : "code 模式") + ") ========");
            ExecutionResult r;
            if (_session.Channel == ExecChannel.InProcess)
            {
                r = _session.ExecuteLocal(result.Code, args, nargs, callTarget, timeout);
            }
            else
            {
                r = _session.Execute(result.Code, args, nargs, callTarget, modeCall, timeout);
            }
            ShowResult(r);
        }

        private void ShowResult(ExecutionResult r)
        {
            if (r == null) return;
            txtRegs.Text = BuildRegsReport(r);
            switch (r.Status)
            {
                case ExecStatus.Ok:
                    Log("[OK] ret=0x" + r.RetValue.ToString("X") + "  " + r.Message);
                    tabControlResults.SelectedTab = tabRegs;
                    break;
                case ExecStatus.Exception:
                    Log("[VEH] 捕获异常 " + r.ExceptionName +
                        " @ 0x" + r.ExcAddr.ToString("X") +
                        (r.ExcWasAccessViolation
                            ? (r.ExcReadOrWrite == 0 ? " (读违例)"
                               : (r.ExcReadOrWrite == 1 ? " (写违例)" : ""))
                            : "") +
                        " — 目标进程存活");
                    tabControlResults.SelectedTab = tabRegs;
                    break;
                case ExecStatus.Timeout:
                    Log("[TIMEOUT] " + r.Message);
                    tabControlResults.SelectedTab = tabLog;
                    break;
                default:
                    Log("[ERR] " + r.Message);
                    tabControlResults.SelectedTab = tabLog;
                    break;
            }
        }

        private string BuildRegsReport(ExecutionResult r)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("状态: " + r.Status + (string.IsNullOrEmpty(r.Message) ? "" : ("  (" + r.Message + ")")));
            sb.AppendLine();
            if (r.Status == ExecStatus.Exception)
            {
                sb.AppendLine("== 异常现场 (VEH 快照) ==");
                sb.AppendLine("异常码: 0x" + r.ExcCode.ToString("X8") + " (" + r.ExceptionName + ")");
                sb.AppendLine("异常地址: 0x" + r.ExcAddr.ToString("X") +
                    (r.ExcWasAccessViolation
                        ? (r.ExcReadOrWrite == 0 ? "  [访问违例: 读]" : (r.ExcReadOrWrite == 1 ? "  [访问违例: 写]" : ""))
                        : ""));
                sb.AppendLine("异常 RIP: 0x" + r.ExcRip.ToString("X"));
                AppendRegs(sb, "异常时寄存器", r.RegsExc);
                sb.AppendLine("异常 EFLAGS: 0x" + r.FlagsExc.ToString("X"));
                sb.AppendLine();
            }
            sb.AppendLine("== 用户代码执行完 (call 前) ==");
            AppendRegs(sb, "regs_mid", r.RegsMid);
            sb.AppendLine("EFLAGS: 0x" + r.FlagsMid.ToString("X"));
            sb.AppendLine();
            sb.AppendLine("== 最终寄存器 ==");
            AppendRegs(sb, "regs_final", r.RegsFinal);
            sb.AppendLine("EFLAGS: 0x" + r.FlagsFinal.ToString("X"));
            sb.AppendLine();
            sb.AppendLine("返回值 RAX = 0x" + r.RetValue.ToString("X") +
                "  (" + ((long)r.RetValue).ToString() + ")");
            return sb.ToString();
        }

        private static void AppendRegs(StringBuilder sb, string title, ulong[] regs)
        {
            sb.AppendLine("[" + title + "]");
            for (int i = 0; i < RegIdx.Count; i += 2)
            {
                sb.Append("  ");
                sb.Append(RegIdx.Name(i).PadRight(4));
                sb.Append("= 0x");
                sb.Append(regs[i].ToString("X16"));
                sb.Append("    ");
                sb.Append(RegIdx.Name(i + 1).PadRight(4));
                sb.Append("= 0x");
                sb.AppendLine(regs[i + 1].ToString("X16"));
            }
        }

        private void ParseArgs(out ulong[] args, out int nargs)
        {
            string[] texts = { txtArg0.Text, txtArg1.Text, txtArg2.Text, txtArg3.Text };
            args = new ulong[4];
            nargs = 0;
            foreach (string t in texts)
            {
                string s = (t ?? "").Trim();
                ulong v = 0;
                if (s.Length == 0)
                {
                    args[nargs++] = 0;
                    continue;
                }
                if (_symbolTable.TryGet(s, out v)) { args[nargs++] = v; continue; }
                bool hex = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                if (hex)
                    ulong.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out v);
                else
                    ulong.TryParse(s, out v);
                args[nargs++] = v;
            }
        }

        private ulong ParseCallTarget()
        {
            string s = (txtCallTarget.Text ?? "").Trim();
            if (s.Length == 0) return 0;
            ulong v;
            if (_symbolTable.TryGet(s, out v)) return v;
            bool hex = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (hex)
                ulong.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out v);
            else
                ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out v);
            return v;
        }

        private int ParseTimeout()
        {
            int v;
            return int.TryParse((txtTimeout.Text ?? "").Trim(), out v) ? v : 3000;
        }

        private void BtnClearLogClick(object sender, EventArgs e)
        {
            txtLog.Clear();
        }

        private void Log(string line)
        {
            txtLog.AppendText(line + Environment.NewLine);
        }
    }
}
