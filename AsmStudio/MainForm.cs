// MainForm.cs - 主窗口逻辑：多文档汇编编辑 / 逐行校验 / 汇编 / 远程执Row / 方案运行
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using AsmStudio.Controls;
using AsmStudio.Engine;
using AsmStudio.Localization;
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
        public string FilePath;   // null = unsaved
        public SyntaxKind DocSyntax = SyntaxKind.Intel;   // 当前内容所属语法（切换时据此转换）
        public bool UntouchedDefault;      // 仍是默认模板（未被编辑），语言切换后可重翻
        }

        private readonly List<DocInfo> _docs = new List<DocInfo>();
        private readonly List<SchemeFile> _schemes = new List<SchemeFile>();
        private TargetSession _session;
        private SymbolTable _symbolTable = new SymbolTable();
        private int _newDocCounter = 1;
        private string _initialTemplate;   // 构造时（语言加载前）的默认模板，用于启动后重翻

        public MainForm()
        {
            InitializeComponent();
            cboChannel.SelectedIndex = 0;
            cboChannel.SelectedIndexChanged += ChannelChanged;
            _initialTemplate = GetDefaultTemplate();
            CreateDocument(null, null);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LoadAppIcons();
            SnapshotOriginalTexts();   // 语言键快照必须在首次翻译前建立
            LanguageService.SetCulture(LanguageService.LoadChoice());
            ApplyLanguage();
            BuildLanguageMenu();
            // 默认文档若未被编辑过，按当前所选语言重翻模板
            foreach (DocInfo d in _docs)
                if (d.UntouchedDefault)
                {
                    d.Editor.Text = GetDefaultTemplate();
                    d.UntouchedDefault = true;
                }
            Log(T("AsmStudio ready. Usage:"));
            Log(T("  1. Start a target (bin\\Debug\\x64\\GameSharp.exe, or your own process)"));
            Log(T("  2. Click [Scan Processes], pick a target, then Connect"));
            Log(T("  3. Edit assembly → Validate → Execute / call target function"));
            Log(T("  4. [Schemes] menu loads Resources\\Scheme\\*.asmscheme and runs them"));
            Log("");
        }

        // ================= 多区域语言 =================

        private readonly Dictionary<object, string> _origTexts =
            new Dictionary<object, string>();   // 控件/菜单项 → 英文原始键
        private string[] _origChannelItems;     // 通道下拉的英文原始项

        /// <summary>语言键快照：首次翻译前记录每个控件/菜单项的英文原文。
        /// 切换语言时始终从快照重翻 —— 否则 T(当前文本) 在第二次切换后 miss（键已不是英文）。</summary>
        private void SnapshotOriginalTexts()
        {
            _origTexts.Clear();
            _origTexts[this] = Text;
            SnapshotMenu(menuStrip);
            SnapshotMenu(tabCloseMenu);     // 标签右键菜单（ContextMenuStrip 不在 Controls 树里，须显式覆盖）
            SnapshotMenu(logClearMenu);     // 执行日志右键菜单
            foreach (ToolStripItem it in statusStrip.Items)
                _origTexts[it] = it.Text;
            foreach (ToolStripItem it in toolbar.Items)
                _origTexts[it] = it.Text;
            var cbo = cboChannel.ComboBox;
            if (cbo != null)
            {
                _origChannelItems = new string[cbo.Items.Count];
                for (int i = 0; i < cbo.Items.Count; i++)
                    _origChannelItems[i] = cbo.Items[i] as string;
            }
            SnapshotControls(this);
        }

        private void SnapshotMenu(ToolStrip strip)
        {
            foreach (ToolStripItem it in strip.Items)
                SnapshotMenuItem(it as ToolStripMenuItem);
        }

        private void SnapshotMenuItem(ToolStripMenuItem mi)
        {
            if (mi == null) return;
            _origTexts[mi] = mi.Text;
            foreach (ToolStripItem child in mi.DropDownItems)
                SnapshotMenuItem(child as ToolStripMenuItem);
        }

        private void SnapshotControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is GroupBox || c is Button || c is Label ||
                    c is CheckBox || c is RadioButton || c is TabPage)
                    _origTexts[c] = c.Text;
                SnapshotControls(c);
            }
        }

        private string OrigText(object key, string current)
        {
            string v;
            return _origTexts.TryGetValue(key, out v) ? v : current;
        }

        /// <summary>从英文键快照重刷全部菜单/控件文本</summary>
        private void ApplyLanguage()
        {
            Text = T(OrigText(this, Text));
            ApplyMenuItems(menuStrip);
            ApplyMenuItems(tabCloseMenu);   // 标签右键菜单
            ApplyMenuItems(logClearMenu);   // 执行日志右键菜单
            foreach (ToolStripItem it in statusStrip.Items)
                it.Text = T(OrigText(it, it.Text));
            foreach (ToolStripItem it in toolbar.Items)
                if (it is ToolStripButton || it is ToolStripLabel)
                    it.Text = T(OrigText(it, it.Text));
            // 通道下拉项（保留选中索引，从快照重翻）
            var cbo = cboChannel.ComboBox;
            if (cbo != null && _origChannelItems != null &&
                cbo.Items.Count == _origChannelItems.Length)
            {
                int sel = cbo.SelectedIndex;
                for (int i = 0; i < cbo.Items.Count; i++)
                    cbo.Items[i] = T(_origChannelItems[i]);
                cbo.SelectedIndex = sel;
            }
            ApplyControls(this);
            // 未编辑过的默认文档按当前语言重翻模板
            foreach (DocInfo d in _docs)
                if (d.UntouchedDefault)
                    d.Editor.Text = GetDefaultTemplate();
        }

        private void ApplyMenuItems(ToolStrip strip)
        {
            foreach (ToolStripItem it in strip.Items)
                ApplyMenuItem(it as ToolStripMenuItem);
        }

        private void ApplyMenuItem(ToolStripMenuItem mi)
        {
            if (mi == null) return;
            mi.Text = T(OrigText(mi, mi.Text));
            if (mi == menuLanguage) return;   // 下拉项是语言名，不翻译
            foreach (ToolStripItem child in mi.DropDownItems)
                ApplyMenuItem(child as ToolStripMenuItem);
        }

        private void ApplyControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is GroupBox || c is Button || c is Label ||
                    c is CheckBox || c is RadioButton || c is TabPage)
                    c.Text = T(OrigText(c, c.Text));
                ApplyControls(c);
            }
        }

        /// <summary>按 LanguageService.Languages 重建 语言 菜单下拉项</summary>
        private void BuildLanguageMenu()
        {
            menuLanguage.DropDownItems.Clear();
            foreach (Localization.LanguageService.LanguageDef lang in LanguageService.Languages)
            {
                string culture = lang.Culture;
                ToolStripMenuItem item = new ToolStripMenuItem(lang.DisplayName);
                item.Tag = culture;
                item.Click += delegate
                {
                    LanguageService.SaveChoice(culture);
                    LanguageService.SetCulture(culture);
                    ApplyLanguage();
                    UpdateLanguageChecks(culture);
                    Log(T("UI language switched: ") + lang.DisplayName);
                };
                menuLanguage.DropDownItems.Add(item);
            }
        }

        /// <summary>切换后同步语言菜单的勾选状态</summary>
        private void UpdateLanguageChecks(string culture)
        {
            foreach (ToolStripItem it in menuLanguage.DropDownItems)
            {
                ToolStripMenuItem mi = it as ToolStripMenuItem;
                if (mi != null) mi.Checked = (string)mi.Tag == culture;
            }
        }

        // ================= 应用图标 =================

        private static readonly System.Resources.ResourceManager _appRes =
            new System.Resources.ResourceManager("AsmStudio.AppResources", typeof(MainForm).Assembly);

        /// <summary>从 AppResources 资源取图标位图并挂到工具栏/按钮/窗口</summary>
        private void LoadAppIcons()
        {
            try
            {
                using (Stream s = typeof(MainForm).Assembly.GetManifestResourceStream(
                    "AsmStudio.Icons.app.ico"))
                {
                    if (s != null) this.Icon = new Icon(s);
                }
                btnScan.Image = (Image)_appRes.GetObject("Scan");
                btnConnect.Image = (Image)_appRes.GetObject("Connect");
                btnDisconnect.Image = (Image)_appRes.GetObject("Disconnect");
                btnAssemble.Image = (Image)_appRes.GetObject("Validate");
                btnAssemble.TextImageRelation = TextImageRelation.ImageBeforeText;
                btnExecute.Image = (Image)_appRes.GetObject("Run");
                btnExecute.TextImageRelation = TextImageRelation.ImageBeforeText;
                foreach (ToolStripItem it in toolbar.Items)
                    if (it.Image != null)
                        it.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            }
            catch (Exception ex)
            {
                Log("[!] icon load failed: " + ex.Message);
            }
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
                    lblStatus.Text = T("Row ") + doc.Editor.CaretLine + T(", Col ") +
                        doc.Editor.CaretColumn + "  |  " + T("Ready");
                }
            };
            doc.FilePath = filePath;
            doc.DocSyntax = CurrentSyntax;

            TabPage page = new TabPage();
            page.Controls.Add(doc.Editor);
            page.Text = filePath != null ? Path.GetFileName(filePath)
                : ("Assembly" + _newDocCounter + ".asm");
            if (filePath == null) _newDocCounter++;

            doc.Page = page;
            tabControlDocs.TabPages.Add(page);
            _docs.Add(doc);
            tabControlDocs.SelectedIndex = tabControlDocs.TabPages.Count - 1;

            if (content != null)
                doc.Editor.Text = content;
            else
            {
                doc.Editor.Text = GetDefaultTemplate();
                doc.UntouchedDefault = true;   // 启动后按所选语言重翻
            }
            return doc;
        }

        private static string GetDefaultTemplate()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(T("; Intel syntax example: call target GameAddScore(5)"));
            sb.AppendLine(T("; For remote execution use the mov+call indirection template (cross-process safe):"));
            sb.AppendLine(T("mov rax, GameAddScore   ; symbol injected by the main app"));
            sb.AppendLine(T("mov rcx, 5              ; x64 first argument -> rcx"));
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
            if (MessageBox.Show(T("Close document ") + _docs[index].Page.Text + "？", T("Confirm"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            TabPage page = _docs[index].Page;
            tabControlDocs.TabPages.Remove(page);
            _docs.RemoveAt(index);
        }

        // ================= 菜单 =================

        private void MenuFileNewClick(object sender, EventArgs e)
        {
            _initialTemplate = GetDefaultTemplate();
            CreateDocument(null, null);
        }

        private void MenuFileOpenClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = T("Assembly files (*.asm;*.s;*.txt)|*.asm;*.s;*.txt|All files (*.*)|*.*");
                dlg.Title = T("Open Assembly File");
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
                        MessageBox.Show(this, T("Failed to read: ") + ex.Message, T("Error"),
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

        // ---- 标签右键菜单：单独保存 / Close other / 全部关闭 ----

        private void MenuTabSaveClick(object sender, EventArgs e)
        {
            SaveDocument(CurrentDoc);
        }

        /// <summary>强制关闭文档（不弹Confirm；用于关闭其他/全部关闭的批量操作）</summary>
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
            if (MessageBox.Show(T("Close other ") + count + " document(s)?", T("Confirm"),
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
            if (MessageBox.Show(T("Close all ") + _docs.Count + " document(s)?", T("Confirm"),
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
                    dlg.Filter = T("Assembly files (*.asm)|*.asm|All files (*.*)|*.*");
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
                lblStatus.Text = T("Saved: ") + path;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("Save failed: ") + ex.Message, T("Error"),
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
                    dlg.Description = T("Select the folder containing *.asmscheme scheme files");
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                    {
                        Log(T("No scheme folder selected."));
                        return;
                    }
                    dir = dlg.SelectedPath;
                }
            }
            _schemesDir = dir;
            _schemes.AddRange(SchemeFile.ScanDirectory(dir));
            Log(T("Scanned scheme folder: ") + dir + " -> " + _schemes.Count + " scheme(s)");
            foreach (SchemeFile s in _schemes)
                Log("  scheme: " + s.Name + " (" + s.Steps.Count + T(" steps)"));
        }

        private static string FindDefaultSchemesDir()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = {
                Path.Combine(exeDir, "Resources"),                     // 构建输出（csproj 自动复制，扁平）
                Path.Combine(exeDir, "..", "..", "..", "Resources", "Scheme"),  // 仓库根目录源码（用户整理后的 Scheme 子目录）
                Path.Combine(exeDir, "..", "..", "..", "Resources"),   // 仓库根目录（兼容直放）
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
                MessageBox.Show(this, T("Please connect to a target first."), T("Info"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_schemes.Count == 0) LoadSchemes();
            if (_schemes.Count == 0)
            {
                MessageBox.Show(this, "No scheme files found (Resources\\\\Scheme\\\\*.asmscheme).", T("Info"),
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
            Log("======== Running scheme: " + scheme.Name + " ========");
            SchemeRunner runner = new SchemeRunner(_session, Log);
            List<StepLog> logs = runner.Run(scheme);
            int ok = 0;
            foreach (StepLog l in logs)
                if (l.Success) ok++;
            Log("======== Scheme finished: " + ok + "/" + logs.Count + " steps OK ========");
        }

        private void MenuToolsSelfTestClick(object sender, EventArgs e)
        {
            List<TestVectorResult> results = AsmEngine.RunSelfTest();
            int failed = 0;
            Log("======== Assembler self-test ========");
            foreach (TestVectorResult r in results)
            {
                if (!r.Passed)
                {
                    failed++;
                    Log("FAIL " + r.Name);
                    Log("      expected: " + r.ExpectedHex);
                    Log("      actual: " + (r.ActualHex ?? T("(none)")));
                    if (!string.IsNullOrEmpty(r.Error))
                        Log("      error: " + r.Error);
                }
            }
            Log("Self-test finished: " + (results.Count - failed) + "/" + results.Count + " passed");
            MessageBox.Show(this,
                string.Format(T("Assembler self-test: {0}/{1} vectors passed{2}"),
                    results.Count - failed, results.Count,
                    failed == 0 ? "\n\nAll passed ✓" : "\n\nFailures present, see log"),
                T("Self-Test Result"), MessageBoxButtons.OK,
                failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void MenuHelpAboutClick(object sender, EventArgs e)
        {
            MessageBox.Show(this,
                "AsmStudio — Assemble / Validate / Remote Execute\n\n" +
                "C# self-built assembly engine (Intel/AT&T, x86/x64)\n" +
                "Remote channels: RemoteThread+VEH / RemoteThread (no DLL, no injection)\n\n" +
                T("For development/debugging of your own or authorized processes only."),
                T("About"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ================= 扫描 / Connect =================

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

        private bool _scanning;   // 扫描重入保护（Click 每次点击都触发，连点会嵌套重建下拉列表）

        private void BtnScanClick(object sender, EventArgs e)
        {
            if (_scanning) return;
            _scanning = true;
            Cursor = Cursors.WaitCursor;
            try
            {
                List<ProcessInfo> all = ProcessScanner.ScanProcesses();

                // 下拉展开状态下重建列表会在原生自动补全/下拉窗里触发 AV（闪退根因）：
                // 先收起下拉、停用 AutoComplete，重建完成后再恢复
                System.Windows.Forms.ComboBox inner = cboProcess.ComboBox;
                if (inner != null && inner.DroppedDown)
                    inner.DroppedDown = false;
                AutoCompleteMode oldMode = cboProcess.AutoCompleteMode;
                cboProcess.AutoCompleteMode = AutoCompleteMode.None;

                _scanResult = new List<ProcessInfo>();
                cboProcess.Items.Clear();
                foreach (ProcessInfo p in all)
                {
                    if (p.Pid <= 4) continue;
                    _scanResult.Add(p);
                    cboProcess.Items.Add(p.ToString());
                }
                cboProcess.AutoCompleteMode = oldMode;
                lblTargetArch.Text = "";
                Log(T("Scan finished: ") + _scanResult.Count + T(" processes"));
            }
            catch (Exception ex)
            {
                Log("[!] Scan FAILED: " + ex.Message);
            }
            finally
            {
                _scanning = false;
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
                    case 1: return ExecChannel.RemoteThread;
                    default: return ExecChannel.RemoteThreadVeh;
                }
            }
        }

        private void ChannelChanged(object sender, EventArgs e)
        {
            cboProcess.Enabled = true;
        }

        private void BtnConnectClick(object sender, EventArgs e)
        {
            Disconnect();
            ExecChannel channel = SelectedChannel;

            ProcessInfo target = SelectedProcess;
            if (target == null)
            {
                MessageBox.Show(this, T("Please scan and select a target process first."), T("Info"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            lblTargetArch.Text = target.ArchText;
            cboProcess.Text = target.ToString();   // 手输名称规整为完整项文本

            _session = new TargetSession();
            bool ok = _session.Connect(target, channel);

            if (ok)
            {
                _sessionConnected = true;
                FillRemoteModules();
                lblMode.Text = string.Format(T("Connected: {0} pid={1} {2} [{3}]"),
                    target.Name, target.Pid, target.ArchText, channel);
                Log(T("[+] Connected ") + target.Name + " (pid=" + target.Pid + ", " +
                    target.ArchText + ", channel=" + channel + ")");
                // 远程线程通道自动切换架构选择
                if (channel == ExecChannel.RemoteThread)
                {
                    if (target.Is64Bit) rbX64.Checked = true;
                    else rbX86.Checked = true;
                }
            }
            else
            {
                Log("[!] ConnectFAILED: " + _session.LastError);
                _session.Dispose();
                _session = null;
            }
        }

        private void BtnDisconnectClick(object sender, EventArgs e)
        {
            Disconnect();
            lblMode.Text = T("Not Connected");
            Log(T("[-] Disconnected"));
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

        // ---- 远程符号源：模块列表 → 模块导出 API ----

        private List<RemoteSymbols.ModuleInfo> _remoteModules =
            new List<RemoteSymbols.ModuleInfo>();
        private List<SymbolInfo> _remoteExports = new List<SymbolInfo>();
        private int _symbolMode;   // 1=模块列表 2=某模块导出

        /// <summary>连接后自动填充模块列表。不自动钻进第一个模块——
        /// 多数程序自身导出为 0，钻进去只会得到空下拉（实测教训）。</summary>
        private void FillRemoteModules()
        {
            _symbolTable.Clear();
            _remoteExports.Clear();
            _remoteModules = RemoteSymbols.EnumModules(_session.Target.Pid);
            cboSymbol.Items.Clear();
            foreach (RemoteSymbols.ModuleInfo m in _remoteModules)
                cboSymbol.Items.Add(m.Name);
            _symbolMode = 1;
            cboSymbol.SelectedIndex = -1;   // 保持模块列表视图，不触发钻取
            Log("[+] Module list refreshed: " + _remoteModules.Count + T(" (select a module to list its exports)"));
        }

        private void LoadRemoteExports(RemoteSymbols.ModuleInfo m)
        {
            Log("[*] Parsing exports of " + m.Name + " ...");
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
            if (_remoteExports.Count == 0)
            {
                // 该模块无导出 → 记录并回退模块列表视图（避免空下拉）
                Log("[+] " + m.Name + T(" exports: 0 (module has no exported APIs)"));
                _symbolTable.Clear();
                _remoteExports.Clear();
                cboSymbol.Items.Clear();
                foreach (RemoteSymbols.ModuleInfo mm in _remoteModules)
                    cboSymbol.Items.Add(mm.Name);
                _symbolMode = 1;
                cboSymbol.SelectedIndex = -1;
                return;
            }
            _symbolTable.Clear();
            cboSymbol.Items.Clear();
            foreach (SymbolInfo s in _remoteExports)
            {
                if (s.Address != 0) _symbolTable.Add(s.Name, s.Address);
                cboSymbol.Items.Add(s.ToString());
            }
            _symbolMode = 2;
            Log("[+] " + m.Name + T(" exports: ") + _remoteExports.Count +
                T(" (select an API to fill the call target)"));
            if (cboSymbol.Items.Count > 0) cboSymbol.SelectedIndex = 0;
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
                    catch { /* 转换FAILED保持原文 */ }
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

        // ================= 校验 / 汇编 / 执Row =================

        private void EditorTextChangedDelayed(object sender, EventArgs e)
        {
            DocInfo doc = CurrentDoc;
            if (doc != null && ReferenceEquals(sender, doc.Editor))
                doc.UntouchedDefault = false;   // 用户已编辑，不再自动重翻
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
            lblStatus.Text = T("Row ") + doc.Editor.CaretLine + T(", Col ") + doc.Editor.CaretColumn +
                "  |  " + (errors.Count == 0 ? T("Validation passed") : (errors.Count + T(" errors")));
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
            // 双击Error → 定位（简单实现：选中行首）
            DocInfo doc = CurrentDoc;
            if (doc == null || lstErrors.SelectedIndex < 0) return;
            string text = lstErrors.SelectedItem as string;
            if (text == null) return;
            int li = text.IndexOf(T("Row "));
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
            AssembleResult result = AsmEngine.Assemble(doc.Editor.Text, opt);
            if (result.Success)
            {
                txtHex.Text = result.Hex;
                lblStatus.Text = T("Assembled OK: ") + result.Code.Length + " bytes";
                Log("[+] Assembled OK: " + result.Code.Length + " bytes");
                foreach (KeyValuePair<string, ulong> kv in result.Labels)
                    Log(T("    label ") + kv.Key + " = +0x" + kv.Value.ToString("x"));
                tabControlResults.SelectedTab = tabHex;
            }
            else
            {
                txtHex.Text = "";
                lstErrors.Items.Clear();
                foreach (AsmError err in result.Errors)
                    lstErrors.Items.Add(err.ToString());
                Log("[!] Assembly failed: " + result.Errors.Count + T(" errors"));
                tabControlResults.SelectedTab = tabErrors;
            }
        }

        private void BtnExecuteClick(object sender, EventArgs e)
        {
            DocInfo doc = CurrentDoc;
            if (doc == null) return;
            if (!_sessionConnected || _session == null)
            {
                MessageBox.Show(this, T("Please connect to a target first."), T("Info"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AssembleOptions opt = new AssembleOptions();
            opt.Syntax = CurrentSyntax;
            opt.Arch = CurrentArch;
            opt.Symbols = _symbolTable;
            opt.BaseAddress = 0;
            AssembleResult result = AsmEngine.Assemble(doc.Editor.Text, opt);
            if (!result.Success)
            {
                lstErrors.Items.Clear();
                foreach (AsmError err in result.Errors)
                    lstErrors.Items.Add(err.ToString());
                tabControlResults.SelectedTab = tabErrors;
                MessageBox.Show(this, "Assembly failed — fix the errors first.", T("Error"),
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

            Log("======== Execute (" + result.Code.Length + T(" bytes, ") +
                (modeCall ? "call=" + txtCallTarget.Text : T("code mode")) + ") ========");
            ExecutionResult r = _session.Execute(result.Code, args, nargs, callTarget, modeCall, timeout);
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
                    Log(T("[VEH] caught exception ") + r.ExceptionName +
                        " @ 0x" + r.ExcAddr.ToString("X") +
                        (r.ExcWasAccessViolation
                            ? (r.ExcReadOrWrite == 0 ? T(" (read)")
                               : (r.ExcReadOrWrite == 1 ? T(" (write)") : ""))
                            : "") +
                        T(" — target survived"));
                    tabControlResults.SelectedTab = tabRegs;
                    break;
                case ExecStatus.Timeout:
                    Log("[TIMEOUT] " + r.Message);
                    break;
                default:
                    Log("[ERR] " + r.Message);
                    break;
            }
        }

        private string BuildRegsReport(ExecutionResult r)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Status: " + r.Status + (string.IsNullOrEmpty(r.Message) ? "" : ("  (" + r.Message + ")")));
            sb.AppendLine();
            if (r.Status == ExecStatus.Exception)
            {
                sb.AppendLine("== Exception state (VEH snapshot) ==");
                sb.AppendLine("Exception code: 0x" + r.ExcCode.ToString("X8") + " (" + r.ExceptionName + ")");
                sb.AppendLine("Exception address: 0x" + r.ExcAddr.ToString("X") +
                    (r.ExcWasAccessViolation
                        ? (r.ExcReadOrWrite == 0 ? "  [access violation: read]" : (r.ExcReadOrWrite == 1 ? "  [access violation: write]" : ""))
                        : ""));
                sb.AppendLine("Exception RIP: 0x" + r.ExcRip.ToString("X"));
                AppendRegs(sb, "Registers at exception", r.RegsExc);
                sb.AppendLine("Exception EFLAGS: 0x" + r.FlagsExc.ToString("X"));
                sb.AppendLine();
            }
            sb.AppendLine("== After user code executed (pre-call) ==");
            AppendRegs(sb, "regs_mid", r.RegsMid);
            sb.AppendLine("EFLAGS: 0x" + r.FlagsMid.ToString("X"));
            sb.AppendLine();
            sb.AppendLine("== Final registers ==");
            AppendRegs(sb, "regs_final", r.RegsFinal);
            sb.AppendLine("EFLAGS: 0x" + r.FlagsFinal.ToString("X"));
            sb.AppendLine();
            sb.AppendLine("Return value RAX = 0x" + r.RetValue.ToString("X") +
                "  (" + ((long)r.RetValue).ToString() + ")");
            return sb.ToString();
        }

        private static readonly string[] _regNames32 =
            { "eax", "ecx", "edx", "ebx", "esp", "ebp", "esi", "edi" };

        private bool TargetIs32Bit
        {
            get
            {
                TargetSession s = _session;
                return s != null && s.Target != null && !s.Target.Is64Bit;
            }
        }

        private void AppendRegs(StringBuilder sb, string title, ulong[] regs)
        {
            bool is32 = TargetIs32Bit;
            sb.AppendLine("[" + title + "]");
            int count = is32 ? 8 : RegIdx.Count;   // x86 只显示 8 个 32 位寄存器
            for (int i = 0; i < count; i += 2)
            {
                sb.Append("  ");
                sb.Append((is32 ? _regNames32[i] : RegIdx.Name(i)).PadRight(4));
                sb.Append("= 0x");
                sb.Append(regs[i].ToString(is32 ? "X8" : "X16"));
                sb.Append("    ");
                sb.Append((is32 ? _regNames32[i + 1] : RegIdx.Name(i + 1)).PadRight(4));
                sb.Append("= 0x");
                sb.AppendLine(regs[i + 1].ToString(is32 ? "X8" : "X16"));
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

        private void MenuLogClearClick(object sender, EventArgs e)
        {
            txtLog.Clear();
        }

        /// <summary>多语言翻译快捷入口（以中文原文为键查语言表）</summary>
        private static string T(string text)
        {
            return LanguageService.T(text);
        }

        private void Log(string line)
        {
            txtLog.AppendText(line + Environment.NewLine);
        }
    }
}
