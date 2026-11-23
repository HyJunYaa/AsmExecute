// MainForm.cs - 主窗口逻辑：多文档汇编编辑 / 逐行校验 / 汇编 / 远程执行 / 方案运行
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
        private bool _executing;          // 后台执行中（重入保护，同时禁用执行按钮）
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
            SetupFileDrop();   // 汇编文件拖到窗体/编辑器即打开
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
            Log(T("  4. [Schemes] menu loads Resources\\Schemes\\*.asmscheme and runs them"));
            Log("");
        }

        // ================= 多区域语言 =================

        private readonly Dictionary<object, string> _origTexts =
            new Dictionary<object, string>();   // 控件/菜单项 → 英文原始键
        private string[] _origChannelItems;     // 通道下拉的英文原始项
        private string[] _memColOrig;           // 内存页 ListView 列头的英文原始键

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
            // 内存页 ListView 列头（ColumnHeader 不是 Control，须单独快照）
            _memColOrig = new string[lstMem.Columns.Count];
            for (int i = 0; i < lstMem.Columns.Count; i++)
                _memColOrig[i] = lstMem.Columns[i].Text;
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
            // 内存页 ListView 列头按快照重翻
            if (_memColOrig != null && _memColOrig.Length == lstMem.Columns.Count)
                for (int i = 0; i < lstMem.Columns.Count; i++)
                    lstMem.Columns[i].Text = T(_memColOrig[i]);
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
            // 启动时也要按已保存的语言打勾（此前只在点击时设置，重开程序勾选丢失）
            UpdateLanguageChecks(LanguageService.CurrentCulture);
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
                // 窗口/任务栏图标按进程位数取角标版（32/64），与 exe 的 Win32 图标一致
                string icoName = IntPtr.Size == 4
                    ? "AsmStudio.Icons.app_32.ico" : "AsmStudio.Icons.app_64.ico";
                using (Stream s = typeof(MainForm).Assembly.GetManifestResourceStream(icoName))
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
            doc.Editor.EnableFileDrop(FileDropDragEnter, FileDropDrop);
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
            sb.AppendLine(T("; Demo: beep once, then pop a MessageBoxW (needs a connected target)"));
            sb.AppendLine(T("; module!API symbols (kernel32.dll!Beep) resolve automatically after connect"));
            sb.AppendLine(T("; x64 args: 1st-4th -> rcx / rdx / r8 / r9"));
            sb.AppendLine(T("; NOTE: the stub enters user code with rsp%16==8 - do sub rsp,0x28 before calls"));
            sb.AppendLine(T(";    (0x20 shadow space + 8 alignment); add rsp,0x28 back before the final ret"));
            sb.AppendLine("");
            sb.AppendLine(T("; 1) Beep(750, 300) - beep first"));
            sb.AppendLine("sub rsp, 0x28");
            sb.AppendLine("mov rcx, 750");
            sb.AppendLine("mov rdx, 300");
            sb.AppendLine("mov rax, kernel32.dll!Beep");
            sb.AppendLine("call rax");
            sb.AppendLine("");
            sb.AppendLine(T("; 2) MessageBoxW(0, text, title, 0) - then show a message box"));
            sb.AppendLine(T(";    allocate the strings on the Memory tab (type=Text, name msgText/msgTitle),"));
            sb.AppendLine(T(";    then reference them here by symbol:"));
            sb.AppendLine("xor ecx, ecx");
            sb.AppendLine("mov rdx, msgText");
            sb.AppendLine("mov r8, msgTitle");
            sb.AppendLine("xor r9d, r9d");
            sb.AppendLine("mov rax, user32.dll!MessageBoxW");
            sb.AppendLine("call rax");
            sb.AppendLine("");
            sb.AppendLine("add rsp, 0x28");
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
                    OpenAssemblyFile(dlg.FileName);
            }
        }

        /// <summary>打开汇编文件为新标签文档（菜单打开与拖放共用）。
        /// 打开即成为当前文档——执行/校验取的正是当前文档的汇编代码。</summary>
        private void OpenAssemblyFile(string path)
        {
            try
            {
                string content = File.ReadAllText(path);
                DocInfo doc = CreateDocument(path, content);
                doc.Editor.Syntax = rbAtt.Checked
                    ? AsmEditor.SyntaxKindView.Att
                    : AsmEditor.SyntaxKindView.Intel;
                Log("[+] " + T("Opened: ") + path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("Failed to read: ") + ex.Message, T("Error"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---- 文件拖放 ----

        private static readonly string[] _dropExts = { ".asm", ".s", ".inc", ".txt" };

        /// <summary>窗体级拖放兜底（编辑器/标签页已在 CreateDocument/这里各自注册）。</summary>
        private void SetupFileDrop()
        {
            EnableDropTarget(this);
            EnableDropTarget(tabControlDocs);
        }

        private void EnableDropTarget(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += FileDropDragEnter;
            c.DragDrop += FileDropDrop;
        }

        private void FileDropDragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (!DropHasSupportedFile(e.Data.GetData(DataFormats.FileDrop) as string[])) return;
            e.Effect = DragDropEffects.Copy;
        }

        private void FileDropDrop(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            foreach (string f in files)
            {
                if (!HasSupportedExt(f)) continue;
                OpenAssemblyFile(f);   // 打开即当前文档，执行即汇编其内容
            }
        }

        private static bool DropHasSupportedFile(string[] files)
        {
            if (files == null) return false;
            foreach (string f in files)
                if (HasSupportedExt(f)) return true;
            return false;
        }

        private static bool HasSupportedExt(string path)
        {
            string ext = Path.GetExtension(path ?? "");
            foreach (string ok in _dropExts)
                if (string.Equals(ext, ok, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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
                Path.Combine(exeDir, "..", "..", "..", "AsmStudio", "Resources", "Schemes"),  // 源码：工程内 Schemes 目录
                Path.Combine(exeDir, "Schemes"),                       // 工程内（IDE 构建布局）
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
                MessageBox.Show(this, "No scheme files found (Resources\\\\Schemes\\\\*.asmscheme).", T("Info"),
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
                "Remote channels: RemoteThread+VEH Protection / RemoteThread (no injection)\n\n" +
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

                // 下拉展开状态下重建列表会在原生下拉窗里触发 comctl32 AV（闪退根因，
                // comctl32!+0x141ec3 两连崩实测）：AutoComplete 已整体移除（见 Designer
                // 注释），此处仍先收起下拉；扫描由 EnsureScan 延后到独立消息边界执行，
                // 绝不嵌在按键/点击消息的处理过程中
                System.Windows.Forms.ComboBox inner = cboProcess.ComboBox;
                if (inner != null && inner.DroppedDown)
                    inner.DroppedDown = false;

                _scanResult = new List<ProcessInfo>();
                cboProcess.Items.Clear();
                foreach (ProcessInfo p in all)
                {
                    if (p.Pid <= 4) continue;
                    _scanResult.Add(p);
                    cboProcess.Items.Add(p.ToString());
                }
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

        private bool _ensureScanQueued;   // 按键连击时只排一个延迟扫描

        private void CboProcessEnsureScan(object sender, EventArgs e)
        {
            // 打开下拉 / 点输入框 / 输入首字符时若尚无扫描结果则自动扫描。
            // 扫描必须延后到独立消息边界（BeginInvoke）——此前在按键/点击消息处理的
            // 半路同步重建列表，native combo 在消息中途遭遇下拉窗销毁重建，
            // 后续按键落上去就 comctl32 AV 闪退（用户实测：展开下拉疯狂敲进程名
            // 首字母必崩，事件日志 comctl32!+0x141ec3 两连崩）。
            if (_scanResult.Count == 0 && !_ensureScanQueued && !_scanning)
            {
                _ensureScanQueued = true;
                BeginInvoke((Action)(delegate
                {
                    _ensureScanQueued = false;
                    if (_scanResult.Count == 0 && !_scanning)
                        BtnScanClick(this, EventArgs.Empty);
                }));
            }
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
                ValidateCurrent();   // 连接后重校验：清掉未连接时的占位标记，刷新状态栏
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
            // 内存页：断开后地址全部失效——清空列表与符号（目标可能已退出，不做远程释放）
            _memAllocs.Clear();
            lstMem.Items.Clear();
            cboModule.Items.Clear();
            cboApis.Items.Clear();
        }

        // ---- 远程符号源：模块下拉（cboModule）→ 模块导出 API 下拉（cboApis）----

        private List<RemoteSymbols.ModuleInfo> _remoteModules =
            new List<RemoteSymbols.ModuleInfo>();
        private List<SymbolInfo> _remoteExports = new List<SymbolInfo>();

        /// <summary>连接后自动填充模块下拉。不自动钻进第一个模块——
        /// 多数程序自身导出为 0，钻进去只会得到空下拉（实测教训）。</summary>
        private void FillRemoteModules()
        {
            _symbolTable.Clear();
            _remoteExports.Clear();
            if (_session != null) _session.Symbols.Clear();
            _remoteModules = RemoteSymbols.EnumModules(_session.Target.Pid);
            cboApis.Items.Clear();
            cboModule.Items.Clear();
            foreach (RemoteSymbols.ModuleInfo m in _remoteModules)
                cboModule.Items.Add(m.Name);
            cboModule.SelectedIndex = -1;
            Log("[+] Module list refreshed: " + _remoteModules.Count + T(" (select a module to list its exports)"));
        }

        private void CboModuleSelectedIndexChanged(object sender, EventArgs e)
        {
            // 选中模块 → 载入其导出 API（模块下拉保持选中，可随时切换别的模块）
            int idx = cboModule.SelectedIndex;
            if (idx >= 0 && idx < _remoteModules.Count)
                LoadRemoteExports(_remoteModules[idx]);
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
            // 方案运行器（SchemeRunner）只认 _session.Symbols，UI 侧 _symbolTable 它看不到，
            // 因此这里必须双写：漏写会导致 {GetCurrentProcessId} 之类的占位符永远解析失败。
            _symbolTable.Clear();
            _session.Symbols.Clear();
            cboApis.Items.Clear();
            foreach (SymbolInfo s in _remoteExports)
            {
                if (s.Address != 0)
                {
                    _symbolTable.Add(s.Name, s.Address);
                    _session.Symbols.Add(new SymbolInfo { Name = s.Name, Address = s.Address });
                }
                cboApis.Items.Add(s.ToString());
            }
            Log("[+] " + m.Name + T(" exports: ") + _remoteExports.Count +
                T(" (select an API to fill the call target)"));
        }

        private void CboApisSelectedIndexChanged(object sender, EventArgs e)
        {
            // 选中导出 API → 填入调用目标（十六进制地址）并自动勾选调用
            int idx = cboApis.SelectedIndex;
            if (idx >= 0 && idx < _remoteExports.Count)
            {
                SymbolInfo s = _remoteExports[idx];
                if (s.Address != 0)
                {
                    // 符号名,十六进制地址 一并填入（逗号分隔，ParseCallTarget 以末尾地址为准）；
                    // 剥掉 "模块!" 前缀，裸名即可解析（后缀回退）
                    string name = s.Name;
                    int bang = name.IndexOf('!');
                    if (bang >= 0) name = name.Substring(bang + 1);
                    txtCallTarget.Text = name + ",0x" + s.Address.ToString("X");
                    chkModeCall.Checked = true;
                }
            }
        }

        private void TxtCallTargetTextChanged(object sender, EventArgs e)
        {
            // 手动输入调用目标 = 明确想调用（与下拉选中自动勾选同一语义）。
            // 免得忘勾复选框出现"目标/参数都填了却走 code 模式"的困惑（用户实测踩坑）
            if ((txtCallTarget.Text ?? "").Trim().Length > 0 && !chkModeCall.Checked)
                chkModeCall.Checked = true;
        }

        // ================= 内存页：目标进程内申请缓冲区（文本/数值/十六进制） =================

        private sealed class MemAlloc
        {
            public string Name;
            public string Content;   // 数据列显示用（原始输入）
            public ulong Addr;
            public int Size;
        }
        private readonly List<MemAlloc> _memAllocs = new List<MemAlloc>();
        private int _memCounter;

        private void BtnMemAllocClick(object sender, EventArgs e)
        {
            if (!_sessionConnected || _session == null)
            {
                MessageBox.Show(this, T("Please connect to a target first."), T("Info"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string name = (txtMemName.Text ?? "").Trim();
            if (name.Length == 0)
            {
                _memCounter++;
                name = "mem" + _memCounter;
                txtMemName.Text = name;
            }
            if (_symbolTable.Contains(name))
            {
                MessageBox.Show(this, T("Symbol name already exists: ") + name, T("Error"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string content = txtMemValue.Text ?? "";
            byte[] data;
            string typeDesc;
            if (rbMemText.Checked)
            {
                // 文本 = UTF-16LE + 终止符（W 系 API 直接当指针用）
                data = System.Text.Encoding.Unicode.GetBytes(content + "\0");
                typeDesc = "utf16";
            }
            else if (rbMemQword.Checked)
            {
                long v;
                string t = content.Trim();
                bool ok = long.TryParse(t, out v);
                if (!ok && t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    ok = long.TryParse(t.Substring(2),
                        System.Globalization.NumberStyles.HexNumber, null, out v);
                if (!ok)
                {
                    MessageBox.Show(this, T("Cannot parse number: ") + t, T("Error"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                data = System.BitConverter.GetBytes(v);   // 8 字节小端
                typeDesc = "qword";
            }
            else
            {
                // 十六进制字节串：原样按字节落库（"DE AD BE EF" / "DEADBEEF" 均可）
                string hex = content.Replace(" ", "").Replace(",", "");
                if (hex.Length == 0 || hex.Length % 2 != 0)
                {
                    MessageBox.Show(this, T("Hex bytes must be even-length"), T("Error"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                data = new byte[hex.Length / 2];
                for (int i = 0; i < data.Length; i++)
                {
                    if (!byte.TryParse(hex.Substring(i * 2, 2),
                        System.Globalization.NumberStyles.HexNumber, null, out data[i]))
                    {
                        MessageBox.Show(this, T("Cannot parse hex bytes: ") + content, T("Error"),
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                typeDesc = "hex";
            }

            ulong addr = _session.AllocBytes(data);
            if (addr == 0)
            {
                MessageBox.Show(this, T("Failed to allocate memory in target"), T("Error"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            // 地址注册成符号：汇编里 mov rdx, msgText / push xxx 直接引用
            _symbolTable.Add(name, addr);
            _session.Symbols.Add(new SymbolInfo { Name = name, Address = addr });
            MemAlloc a = new MemAlloc();
            a.Name = name;
            a.Content = content;
            a.Addr = addr;
            a.Size = data.Length;
            _memAllocs.Add(a);
            ListViewItem row = new ListViewItem(name);
            row.SubItems.Add(content);
            row.SubItems.Add("0x" + addr.ToString("X"));
            lstMem.Items.Add(row);
            Log("[+] " + T("Memory allocated: ") + name + " = 0x" + addr.ToString("X") +
                " (" + data.Length + " bytes)");
            ValidateCurrent();   // 新符号入表 → 刷新编辑器错误标记（msgText 等不再标红）
        }

        private void BtnMemFreeClick(object sender, EventArgs e)
        {
            if (lstMem.SelectedIndices.Count == 0) return;
            int idx = lstMem.SelectedIndices[0];
            if (idx < 0 || idx >= _memAllocs.Count) return;
            MemAlloc a = _memAllocs[idx];
            if (_session != null) _session.FreeRemote(a.Addr);
            _symbolTable.Remove(a.Name);
            if (_session != null && _session.Symbols != null)
                _session.Symbols.RemoveAll(delegate(SymbolInfo s)
                {
                    return string.Equals(s.Name, a.Name, StringComparison.OrdinalIgnoreCase);
                });
            _memAllocs.RemoveAt(idx);
            lstMem.Items.RemoveAt(idx);
            Log("[+] " + T("Memory freed: ") + a.Name + " (0x" + a.Addr.ToString("X") + ")");
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

        // ================= 校验 / 汇编 / 执行 =================

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
            ResolveModuleApiSymbols(doc.Editor.Text);
            List<AsmError> errors = AsmEngine.Validate(
                doc.Editor.Text, CurrentSyntax, CurrentArch, BuildValidationTable(doc.Editor.Text));
            doc.Editor.SetErrorLines(GetErrorLines(errors));
            lstErrors.Items.Clear();
            foreach (AsmError err in errors)
                lstErrors.Items.Add(err.ToString());
            lblStatus.Text = T("Row ") + doc.Editor.CaretLine + T(", Col ") + doc.Editor.CaretColumn +
                "  |  " + (errors.Count == 0 ? T("Validation passed") : (errors.Count + T(" errors")));
        }

        private static readonly System.Text.RegularExpressions.Regex _moduleApiRefRegex =
            new System.Text.RegularExpressions.Regex(
                @"(?i)\b([a-z0-9_][a-z0-9_.\-]*![a-z_][a-z0-9_]*)");

        /// <summary>扫描源码里的 模块!API 外部符号引用（去重、统一小写）。</summary>
        private static List<string> CollectModuleApiRefs(string source)
        {
            var list = new List<string>();
            if (source == null) return list;
            foreach (System.Text.RegularExpressions.Match m in _moduleApiRefRegex.Matches(source))
            {
                string sym = m.Groups[1].Value.ToLowerInvariant();
                if (!list.Contains(sym)) list.Add(sym);
            }
            return list;
        }

        /// <summary>汇编/校验前把源码里的 模块!API 符号按需解析进符号表（读目标导出表，
        /// 需已连接；结果符号表缓存 + RemoteSymbols 内部缓存，重复校验零开销）。</summary>
        private void ResolveModuleApiSymbols(string source)
        {
            if (_session == null || !_sessionConnected) return;
            foreach (string sym in CollectModuleApiRefs(source))
            {
                ulong addr;
                if (_symbolTable.Contains(sym)) continue;
                if (TryResolveTargetSymbol(sym, out addr))
                    Log("[+] " + T("Symbol resolved: ") + sym + " = 0x" + addr.ToString("X"));
            }
        }

        /// <summary>校验专用符号表：真实符号 + 未连接时给 模块!API 占位值 1。
        /// 校验只查语法/结构——真实地址在连接后的汇编/执行阶段按需解析，
        /// 避免默认示例未连接时一直显示校验错误（地址不回填，生成机器码仍是真实汇编）。</summary>
        private SymbolTable BuildValidationTable(string source)
        {
            SymbolTable t = new SymbolTable();
            foreach (KeyValuePair<string, ulong> kv in _symbolTable.Items)
                t.Add(kv.Key, kv.Value);
            if (_session == null || !_sessionConnected)
                foreach (string sym in CollectModuleApiRefs(source))
                    if (!t.Contains(sym)) t.Add(sym, 1);
            return t;
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
            // 前缀是本地化字符串（"Row " / "行 "），长度不能写死
            int li = text.IndexOf(T("Row "));
            int lineNo = 0;
            if (li >= 0)
            {
                StringBuilder num = new StringBuilder();
                for (int i = li + T("Row ").Length; i < text.Length && char.IsDigit(text[i]); i++)
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
            ResolveModuleApiSymbols(doc.Editor.Text);
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

            ResolveModuleApiSymbols(doc.Editor.Text);
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
            string[] argStrings;
            int nargs;
            string argsErr;
            if (!ParseArgsText(out args, out argStrings, out nargs, out argsErr))
            {
                MessageBox.Show(this, argsErr, T("Error"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            ulong callTarget = ParseCallTarget();
            bool modeCall = chkModeCall.Checked && callTarget != 0;
            // 等待远程线程的时长：300ms 内完成 → 捕获返回值（快速 API 不受影响）；
            // 阻塞 API（MessageBoxW/Sleep…）等不满 → 立即交还控制（线程继续跑、
            // 执行页按泄漏策略保留、无返回值），不再拖 3 秒（用户反馈"执行后有延迟"）
            const int timeout = 300;

            Log("======== Execute (" + result.Code.Length + T(" bytes, ") +
                (modeCall ? "call=" + txtCallTarget.Text : T("code mode")) +
                (nargs > 0 ? ", args=" + nargs : "") + ") ========");

            /* 远程执行要等目标线程（默认 3s 超时，死循环就等满）——放到线程池跑，
             * 期间界面保持响应；_executing 兼作重入保护（防止连点重复注入）。 */
            if (_executing)
            {
                Log("[!] " + T("Previous execution still running — request ignored"));
                return;
            }
            TargetSession sess = _session;
            byte[] code = result.Code;
            _executing = true;
            btnExecute.Enabled = false;
            lblStatus.Text = T("Executing...");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                ExecutionResult rr;
                try
                {
                    // 引号字符串参数：在目标进程分配 UTF-16 缓冲区、传指针。
                    // 干净结束即释放；超时/异常时泄漏（目标线程可能仍在引用，
                    // 与执行页的泄漏策略一致——释放正在执行的页会崩目标）。
                    List<ulong> allocs = new List<ulong>();
                    bool allocFailed = false;
                    for (int i = 0; i < nargs && !allocFailed; i++)
                    {
                        if (argStrings[i] == null) continue;
                        ulong p = sess.AllocUtf16(argStrings[i]);
                        if (p == 0) { allocFailed = true; break; }
                        args[i] = p;
                        allocs.Add(p);
                    }
                    if (allocFailed)
                    {
                        foreach (ulong p in allocs) sess.FreeRemote(p);
                        rr = new ExecutionResult();
                        rr.Status = ExecStatus.Internal;
                        rr.Message = T("Failed to allocate string buffer in target");
                    }
                    else
                    {
                        rr = sess.Execute(code, args, nargs, callTarget, modeCall, timeout);
                        if (rr.Status == ExecStatus.Ok)
                            foreach (ulong p in allocs) sess.FreeRemote(p);
                    }
                }
                catch (Exception ex)
                {
                    rr = new ExecutionResult();
                    rr.Status = ExecStatus.Internal;
                    rr.Message = "execute threw: " + ex.Message;
                }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        _executing = false;
                        btnExecute.Enabled = true;
                        lblStatus.Text = T("Ready");
                        ShowResult(rr);
                    });
                }
                catch (InvalidOperationException)
                {
                    // 窗口已关闭，结果无处可送
                }
            });
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
            if (r.Status == ExecStatus.Ok)
            {
                // 快照由桩在"用户码执行后、call target 前"写入；异常/超时路径没有这份现场
                sb.AppendLine("== After user code executed ==");
                sb.AppendLine("   (rbx/rcx/rdi = tool internals here: data block / VEH block / result slot)");
                AppendRegs(sb, "regs_mid", r.RegsMid);
                sb.AppendLine();
            }
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

        /// <summary>符号解析：先精确匹配，再按 "模块!名" / "模块.名" / "模块\名" 后缀回退。
        /// 符号表键带模块前缀（如 kernel32.dll!Sleep），UI 输入通常只写裸名——
        /// 与 SchemeRunner 的 TryGetBySuffix 同一语义。</summary>
        private bool TryResolveSymbol(string name, out ulong addr)
        {
            if (_symbolTable.TryGet(name, out addr)) return true;
            if (string.IsNullOrEmpty(name)) return false;
            foreach (KeyValuePair<string, ulong> kv in _symbolTable.Items)
            {
                string k = kv.Key;
                int sep = k.Length - name.Length - 1;
                if (sep < 0) continue;
                char c = k[sep];
                if ((c == '!' || c == '.' || c == '\\') &&
                    k.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    addr = kv.Value;
                    return true;
                }
            }
            addr = 0;
            return false;
        }

        /// <summary>参数文本框 → 参数序列。两种写法：
        /// ① 顺序式 ("MessageBox",0,"Title",0x0)：数组顺序即参数顺序，外层括号可省；
        /// ② 槽位式 0:"MessageBox" 2:0x10：冒号前为参数槽位（0 起），未指定的槽位补 0，
        ///    空格/逗号分隔，可与顺序式思想独立使用（一行内不能混用两种）。
        /// 值类型：十进制 / 0x 十六进制 / 符号名（带或不带模块前缀均可）/ "引号字符串"。
        /// 字符串在调用时于目标进程分配 UTF-16 缓冲区、传指针（见 BtnExecuteClick）。
        /// 最多 4 个参数——VEH 通道后奏只支持寄存器赋参 4 个（x64 ABI 寄存器传参上限）。</summary>
        private bool ParseArgsText(out ulong[] args, out string[] argStrings,
            out int nargs, out string err)
        {
            args = new ulong[4];
            argStrings = new string[4];
            nargs = 0;
            err = null;
            string text = (txtArgs.Text ?? "").Trim();
            if (text.StartsWith("(") && text.EndsWith(")") && text.Length >= 2)
                text = text.Substring(1, text.Length - 2).Trim();
            if (text.Length == 0) return true;

            // 槽位模式探测：把引号段遮掉再判断，避免字符串内容（如 "0:abc"）误判
            var masked = new System.Text.StringBuilder(text);
            bool inQuote = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"') inQuote = !inQuote;
                else if (inQuote) masked[i] = ' ';
            }
            bool slotMode = System.Text.RegularExpressions.Regex.IsMatch(
                masked.ToString(), "(^|[\\s,])[0-9]+\\s*:");

            // 引号感知分词：引号段并成一个 token；槽位模式空格/逗号都切分，顺序模式仅逗号。
            // sawQuote 记录"该 token 含引号"——闭合引号会清 quoted，但标记必须保留到 emit，
            // 否则 "hi" 的字符串标记丢失会被当裸符号解析（实测报"无法解析参数: hi"）。
            var parts = new List<KeyValuePair<bool, string>>();   // bool = 引号字符串
            var cur = new System.Text.StringBuilder();
            bool quoted = false, sawQuote = false, hasToken = false;
            Action emit = delegate
            {
                if (!hasToken) return;
                parts.Add(new KeyValuePair<bool, string>(sawQuote, cur.ToString().Trim()));
                cur.Length = 0;
                quoted = false;
                sawQuote = false;
                hasToken = false;
            };
            foreach (char c in text)
            {
                if (quoted)
                {
                    if (c == '"') quoted = false;
                    else cur.Append(c);
                    continue;
                }
                if (c == '"') { quoted = true; sawQuote = true; hasToken = true; continue; }
                if (c == ',' || (slotMode && char.IsWhiteSpace(c))) { emit(); continue; }
                cur.Append(c);
                hasToken = true;
            }
            emit();
            if (quoted) { err = T("Unterminated quoted string in args"); return false; }

            foreach (KeyValuePair<bool, string> p in parts)
            {
                string tok = p.Value;
                int slot;
                string val = tok;
                if (slotMode)
                {
                    int colon = tok.IndexOf(':');
                    if (colon <= 0 || !int.TryParse(tok.Substring(0, colon).Trim(), out slot))
                    {
                        err = T("Bad arg slot syntax: ") + tok;
                        return false;
                    }
                    if (colon + 1 >= tok.Length)
                    {
                        err = T("Missing value after slot: ") + tok;
                        return false;
                    }
                    val = tok.Substring(colon + 1).Trim();
                }
                else
                {
                    slot = parts.IndexOf(p);
                }
                if (slot < 0 || slot > 3)
                {
                    err = T("Arg slot out of range (0-3): ") + tok;
                    return false;
                }
                nargs = Math.Max(nargs, slot + 1);

                if (p.Key)
                {
                    argStrings[slot] = val;   // 引号字符串 → 执行时分配远程缓冲区传指针
                    args[slot] = 0;
                    continue;
                }
                ulong v;
                if (!TryResolveTargetSymbol(val, out v))
                {
                    bool hex = val.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                    bool ok = hex
                        ? ulong.TryParse(val.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out v)
                        : ulong.TryParse(val, out v);
                    if (!ok)
                    {
                        err = T("Cannot parse arg: ") + val;
                        return false;
                    }
                }
                args[slot] = v;
            }
            return true;
        }

        /// <summary>目标符号解析（调用目标/参数共用）：
        /// ①已载入符号表精确/后缀匹配；②带 "模块!API" 的名称按需远程解析目标导出表
        /// （未浏览模块也能直填 user32.dll!MessageBoxW），成功后回写符号表/会话符号表复用。</summary>
        private bool TryResolveTargetSymbol(string name, out ulong addr)
        {
            if (TryResolveSymbol(name, out addr)) return true;
            int bang = name == null ? -1 : name.IndexOf('!');
            if (bang <= 0 || bang == name.Length - 1) return false;
            string module = name.Substring(0, bang).Trim();
            string api = name.Substring(bang + 1).Trim();
            if (module.Length == 0 || api.Length == 0 || api.IndexOf(' ') >= 0) return false;
            TargetSession sess = _session;
            if (sess == null || sess.Target == null) return false;
            addr = RemoteSymbols.ResolveTargetApi(sess.Target.Pid, sess.Target.Is64Bit, module, api);
            if (addr == 0) return false;
            string key = module.ToLowerInvariant() + "!" + api;
            _symbolTable.Add(key, addr);
            sess.Symbols.Add(new SymbolInfo { Name = key, Address = addr });
            return true;
        }

        private ulong ParseCallTarget()
        {
            string s = (txtCallTarget.Text ?? "").Trim();
            if (s.Length == 0) return 0;
            ulong v;
            // 组合形式 "符号名,0x地址"（API 下拉自动填入；空格分隔也兼容）：
            // 以末尾十六进制地址为准，符号名作对照——微调地址改数字即可，删掉地址只留符号名也行；
            // 不选模块/API 时也可手填 "模块!API"（如 user32.dll!MessageBoxW，按需远程解析）
            int comma = s.LastIndexOf(',');
            if (comma > 0)
            {
                string tail = s.Substring(comma + 1).Trim();
                if (tail.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                    ulong.TryParse(tail.Substring(2),
                        System.Globalization.NumberStyles.HexNumber, null, out v))
                    return v;
            }
            int hx = s.LastIndexOf(" 0x", StringComparison.OrdinalIgnoreCase);
            if (hx > 0 &&
                ulong.TryParse(s.Substring(hx + 3).Trim(),
                    System.Globalization.NumberStyles.HexNumber, null, out v))
                return v;
            if (TryResolveTargetSymbol(s, out v)) return v;
            bool hex = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            bool ok = hex
                ? ulong.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out v)
                : ulong.TryParse(s, out v);
            if (!ok)
                Log("[!] " + T("Call target not found in symbols (use module-prefixed name or hex): ") + s);
            return ok ? v : 0;
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
