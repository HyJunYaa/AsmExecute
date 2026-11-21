// MainForm.Designer.cs - 主窗口设计器文件（WinForms Designer 格式，可被 SharpDevelop/VS 往返编辑）
namespace AsmStudio
{
    public partial class MainForm
    {
        /// <summary>必需的设计器变量。</summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>清理所有正在使用的资源。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.menuStrip = new System.Windows.Forms.MenuStrip();
            this.menuFile = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileNew = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileOpen = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileSave = new System.Windows.Forms.ToolStripMenuItem();
            this.menuFileExit = new System.Windows.Forms.ToolStripMenuItem();
            this.menuEdit = new System.Windows.Forms.ToolStripMenuItem();
            this.menuEditUndo = new System.Windows.Forms.ToolStripMenuItem();
            this.menuEditRedo = new System.Windows.Forms.ToolStripMenuItem();
            this.tabCloseMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.tabCloseMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.tabCloseSaveItem = new System.Windows.Forms.ToolStripMenuItem();
            this.tabCloseOthersItem = new System.Windows.Forms.ToolStripMenuItem();
            this.tabCloseAllItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuScheme = new System.Windows.Forms.ToolStripMenuItem();
            this.menuSchemeLoad = new System.Windows.Forms.ToolStripMenuItem();
            this.menuSchemeRun = new System.Windows.Forms.ToolStripMenuItem();
            this.menuTools = new System.Windows.Forms.ToolStripMenuItem();
            this.menuToolsSelfTest = new System.Windows.Forms.ToolStripMenuItem();
            this.menuToolsInject = new System.Windows.Forms.ToolStripMenuItem();
            this.menuHelp = new System.Windows.Forms.ToolStripMenuItem();
            this.menuHelpAbout = new System.Windows.Forms.ToolStripMenuItem();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblMode = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolbar = new System.Windows.Forms.ToolStrip();
            this.btnScan = new System.Windows.Forms.ToolStripButton();
            this.cboProcess = new System.Windows.Forms.ToolStripComboBox();
            this.lblTargetArch = new System.Windows.Forms.ToolStripLabel();
            this.cboChannel = new System.Windows.Forms.ToolStripComboBox();
            this.btnConnect = new System.Windows.Forms.ToolStripButton();
            this.btnDisconnect = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.btnSymbols = new System.Windows.Forms.ToolStripButton();
            this.cboSymbol = new System.Windows.Forms.ToolStripComboBox();
            this.splitContainerMain = new System.Windows.Forms.SplitContainer();
            this.splitContainerLeft = new System.Windows.Forms.SplitContainer();
            this.tabControlDocs = new System.Windows.Forms.TabControl();
            this.tabControlResults = new System.Windows.Forms.TabControl();
            this.tabHex = new System.Windows.Forms.TabPage();
            this.txtHex = new System.Windows.Forms.TextBox();
            this.tabErrors = new System.Windows.Forms.TabPage();
            this.lstErrors = new System.Windows.Forms.ListBox();
            this.tabLog = new System.Windows.Forms.TabPage();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.tabRegs = new System.Windows.Forms.TabPage();
            this.txtRegs = new System.Windows.Forms.TextBox();
            this.panelParams = new System.Windows.Forms.Panel();
            this.grpSyntax = new System.Windows.Forms.GroupBox();
            this.rbIntel = new System.Windows.Forms.RadioButton();
            this.rbAtt = new System.Windows.Forms.RadioButton();
            this.grpArch = new System.Windows.Forms.GroupBox();
            this.rbX64 = new System.Windows.Forms.RadioButton();
            this.rbX86 = new System.Windows.Forms.RadioButton();
            this.grpCall = new System.Windows.Forms.GroupBox();
            this.chkModeCall = new System.Windows.Forms.CheckBox();
            this.lblCallTarget = new System.Windows.Forms.Label();
            this.txtCallTarget = new System.Windows.Forms.TextBox();
            this.lblArgs = new System.Windows.Forms.Label();
            this.txtArg0 = new System.Windows.Forms.TextBox();
            this.txtArg1 = new System.Windows.Forms.TextBox();
            this.txtArg2 = new System.Windows.Forms.TextBox();
            this.txtArg3 = new System.Windows.Forms.TextBox();
            this.lblTimeout = new System.Windows.Forms.Label();
            this.txtTimeout = new System.Windows.Forms.TextBox();
            this.btnAssemble = new System.Windows.Forms.Button();
            this.btnExecute = new System.Windows.Forms.Button();
            this.btnClearLog = new System.Windows.Forms.Button();
            this.menuStrip.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.toolbar.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerMain)).BeginInit();
            this.splitContainerMain.Panel1.SuspendLayout();
            this.splitContainerMain.Panel2.SuspendLayout();
            this.splitContainerMain.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerLeft)).BeginInit();
            this.splitContainerLeft.Panel1.SuspendLayout();
            this.splitContainerLeft.Panel2.SuspendLayout();
            this.splitContainerLeft.SuspendLayout();
            this.tabControlDocs.SuspendLayout();
            this.tabControlResults.SuspendLayout();
            this.tabHex.SuspendLayout();
            this.tabErrors.SuspendLayout();
            this.tabLog.SuspendLayout();
            this.tabRegs.SuspendLayout();
            this.panelParams.SuspendLayout();
            this.grpSyntax.SuspendLayout();
            this.grpArch.SuspendLayout();
            this.grpCall.SuspendLayout();
            this.SuspendLayout();
            //
            // menuStrip
            //
            this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.menuFile,
                this.menuEdit,
                this.menuScheme,
                this.menuTools,
                this.menuHelp});
            this.menuStrip.Location = new System.Drawing.Point(0, 0);
            this.menuStrip.Name = "menuStrip";
            this.menuStrip.Size = new System.Drawing.Size(1184, 25);
            this.menuStrip.TabIndex = 0;
            //
            // menuFile
            //
            this.menuFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.menuFileNew,
                this.menuFileOpen,
                this.menuFileSave,
                this.menuFileExit});
            this.menuFile.Name = "menuFile";
            this.menuFile.Size = new System.Drawing.Size(44, 21);
            this.menuFile.Text = "文件(&F)";
            //
            // menuFileExit
            //
            this.menuFileExit.Name = "menuFileExit";
            this.menuFileExit.Size = new System.Drawing.Size(148, 22);
            this.menuFileExit.Text = "退出(&X)";
            this.menuFileExit.Click += new System.EventHandler(this.MenuFileExitClick);
            //
            // menuEdit
            //
            this.menuEdit.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.menuEditUndo,
                this.menuEditRedo});
            this.menuEdit.Name = "menuEdit";
            this.menuEdit.Size = new System.Drawing.Size(44, 21);
            this.menuEdit.Text = "编辑(&E)";
            this.menuEdit.DropDownOpening += new System.EventHandler(this.MenuEditDropDownOpening);
            //
            // menuEditUndo
            //
            this.menuEditUndo.Name = "menuEditUndo";
            this.menuEditUndo.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Z)));
            this.menuEditUndo.Size = new System.Drawing.Size(148, 22);
            this.menuEditUndo.Text = "撤回(&U)";
            this.menuEditUndo.Click += new System.EventHandler(this.MenuEditUndoClick);
            //
            // menuEditRedo
            //
            this.menuEditRedo.Name = "menuEditRedo";
            this.menuEditRedo.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Y)));
            this.menuEditRedo.Size = new System.Drawing.Size(148, 22);
            this.menuEditRedo.Text = "重做(&R)";
            this.menuEditRedo.Click += new System.EventHandler(this.MenuEditRedoClick);
            //
            // menuFileNew
            //
            this.menuFileNew.Name = "menuFileNew";
            this.menuFileNew.Size = new System.Drawing.Size(148, 22);
            this.menuFileNew.Text = "新建(&N)";
            this.menuFileNew.Click += new System.EventHandler(this.MenuFileNewClick);
            //
            // menuFileOpen
            //
            this.menuFileOpen.Name = "menuFileOpen";
            this.menuFileOpen.Size = new System.Drawing.Size(148, 22);
            this.menuFileOpen.Text = "打开(&O)...";
            this.menuFileOpen.Click += new System.EventHandler(this.MenuFileOpenClick);
            //
            // menuFileSave
            //
            this.menuFileSave.Name = "menuFileSave";
            this.menuFileSave.Size = new System.Drawing.Size(148, 22);
            this.menuFileSave.Text = "保存(&S)";
            this.menuFileSave.Click += new System.EventHandler(this.MenuFileSaveClick);
            //
            // tabCloseMenu
            //
            this.tabCloseMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.tabCloseMenuItem,
                this.tabCloseSaveItem,
                this.tabCloseOthersItem,
                this.tabCloseAllItem});
            this.tabCloseMenu.Name = "tabCloseMenu";
            this.tabCloseMenu.Size = new System.Drawing.Size(132, 26);
            //
            // tabCloseMenuItem
            //
            this.tabCloseMenuItem.Name = "tabCloseMenuItem";
            this.tabCloseMenuItem.Size = new System.Drawing.Size(131, 22);
            this.tabCloseMenuItem.Text = "关闭(&C)";
            this.tabCloseMenuItem.Click += new System.EventHandler(this.TabCloseMenuItemClick);
            //
            // tabCloseSaveItem
            //
            this.tabCloseSaveItem.Name = "tabCloseSaveItem";
            this.tabCloseSaveItem.Size = new System.Drawing.Size(131, 22);
            this.tabCloseSaveItem.Text = "单独保存(&S)";
            this.tabCloseSaveItem.Click += new System.EventHandler(this.MenuTabSaveClick);
            //
            // tabCloseOthersItem
            //
            this.tabCloseOthersItem.Name = "tabCloseOthersItem";
            this.tabCloseOthersItem.Size = new System.Drawing.Size(131, 22);
            this.tabCloseOthersItem.Text = "关闭其他(&O)";
            this.tabCloseOthersItem.Click += new System.EventHandler(this.MenuTabCloseOthersClick);
            //
            // tabCloseAllItem
            //
            this.tabCloseAllItem.Name = "tabCloseAllItem";
            this.tabCloseAllItem.Size = new System.Drawing.Size(131, 22);
            this.tabCloseAllItem.Text = "全部关闭(&A)";
            this.tabCloseAllItem.Click += new System.EventHandler(this.MenuTabCloseAllClick);
            //
            // menuScheme
            //
            this.menuScheme.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.menuSchemeLoad,
                this.menuSchemeRun});
            this.menuScheme.Name = "menuScheme";
            this.menuScheme.Size = new System.Drawing.Size(44, 21);
            this.menuScheme.Text = "方案(&P)";
            //
            // menuSchemeLoad
            //
            this.menuSchemeLoad.Name = "menuSchemeLoad";
            this.menuSchemeLoad.Size = new System.Drawing.Size(136, 22);
            this.menuSchemeLoad.Text = "扫描方案目录(&L)";
            this.menuSchemeLoad.Click += new System.EventHandler(this.MenuSchemeLoadClick);
            //
            // menuSchemeRun
            //
            this.menuSchemeRun.Name = "menuSchemeRun";
            this.menuSchemeRun.Size = new System.Drawing.Size(136, 22);
            this.menuSchemeRun.Text = "运行选定方案(&R)...";
            this.menuSchemeRun.Click += new System.EventHandler(this.MenuSchemeRunClick);
            //
            // menuTools
            //
            this.menuTools.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.menuToolsSelfTest,
                this.menuToolsInject});
            this.menuTools.Name = "menuTools";
            this.menuTools.Size = new System.Drawing.Size(44, 21);
            this.menuTools.Text = "工具(&T)";
            //
            // menuToolsSelfTest
            //
            this.menuToolsSelfTest.Name = "menuToolsSelfTest";
            this.menuToolsSelfTest.Size = new System.Drawing.Size(180, 22);
            this.menuToolsSelfTest.Text = "汇编引擎自检(&S)";
            this.menuToolsSelfTest.Click += new System.EventHandler(this.MenuToolsSelfTestClick);
            //
            // menuToolsInject
            //
            this.menuToolsInject.Name = "menuToolsInject";
            this.menuToolsInject.Size = new System.Drawing.Size(180, 22);
            this.menuToolsInject.Text = "注入 AsmLibrary.dll(&I)...";
            this.menuToolsInject.Click += new System.EventHandler(this.MenuToolsInjectClick);
            //
            // menuHelp
            //
            this.menuHelp.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.menuHelpAbout});
            this.menuHelp.Name = "menuHelp";
            this.menuHelp.Size = new System.Drawing.Size(44, 21);
            this.menuHelp.Text = "帮助(&H)";
            //
            // menuHelpAbout
            //
            this.menuHelpAbout.Name = "menuHelpAbout";
            this.menuHelpAbout.Size = new System.Drawing.Size(132, 22);
            this.menuHelpAbout.Text = "关于(&A)...";
            this.menuHelpAbout.Click += new System.EventHandler(this.MenuHelpAboutClick);
            //
            // statusStrip
            //
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.lblStatus,
                this.lblMode});
            this.statusStrip.Location = new System.Drawing.Point(0, 673);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(1184, 22);
            this.statusStrip.TabIndex = 1;
            //
            // lblStatus
            //
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(937, 17);
            this.lblStatus.Spring = true;
            this.lblStatus.Text = "就绪";
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // lblMode
            //
            this.lblMode.Name = "lblMode";
            this.lblMode.Size = new System.Drawing.Size(232, 17);
            this.lblMode.Text = "未连接";
            //
            // toolbar
            //
            this.toolbar.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolbar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.btnScan,
                this.cboProcess,
                this.lblTargetArch,
                this.cboChannel,
                this.btnConnect,
                this.btnDisconnect,
                this.toolStripSeparator1,
                this.btnSymbols,
                this.cboSymbol});
            this.toolbar.Location = new System.Drawing.Point(0, 25);
            this.toolbar.Name = "toolbar";
            this.toolbar.Size = new System.Drawing.Size(1184, 25);
            this.toolbar.TabIndex = 2;
            //
            // btnScan
            //
            this.btnScan.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnScan.Name = "btnScan";
            this.btnScan.Size = new System.Drawing.Size(60, 22);
            this.btnScan.Text = "扫描进程";
            this.btnScan.Click += new System.EventHandler(this.BtnScanClick);
            //
            // cboProcess
            //
            // 可编辑 + 输入自动补全程序名；打开下拉时若尚无扫描结果则自动扫描
            // （扫描进程按钮仍是手动刷新入口；手输名称在 SelectedProcess 里按文本解析）
            this.cboProcess.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            this.cboProcess.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
            this.cboProcess.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDown;
            this.cboProcess.Name = "cboProcess";
            this.cboProcess.Size = new System.Drawing.Size(240, 23);
            this.cboProcess.DropDown += new System.EventHandler(this.CboProcessEnsureScan);
            this.cboProcess.Click += new System.EventHandler(this.CboProcessEnsureScan);
            this.cboProcess.TextChanged += new System.EventHandler(this.CboProcessEnsureScan);
            //
            // lblTargetArch
            //
            this.lblTargetArch.Name = "lblTargetArch";
            this.lblTargetArch.Size = new System.Drawing.Size(0, 20);
            //
            // cboChannel
            //
            this.cboChannel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboChannel.Items.AddRange(new object[] {
                "管道+DLL (VEH保护)",
                "远程线程+VEH (无DLL)",
                "远程线程 (无保护)",
                "本进程 (快速试验)"});
            this.cboChannel.Name = "cboChannel";
            this.cboChannel.Size = new System.Drawing.Size(140, 23);
            //
            // btnConnect
            //
            this.btnConnect.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(60, 22);
            this.btnConnect.Text = "连接";
            this.btnConnect.Click += new System.EventHandler(this.BtnConnectClick);
            //
            // btnDisconnect
            //
            this.btnDisconnect.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnDisconnect.Name = "btnDisconnect";
            this.btnDisconnect.Size = new System.Drawing.Size(60, 22);
            this.btnDisconnect.Text = "断开";
            this.btnDisconnect.Click += new System.EventHandler(this.BtnDisconnectClick);
            //
            // toolStripSeparator1
            //
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 25);
            //
            // btnSymbols
            //
            this.btnSymbols.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnSymbols.Name = "btnSymbols";
            this.btnSymbols.Size = new System.Drawing.Size(84, 22);
            this.btnSymbols.Text = "刷新符号表";
            this.btnSymbols.Click += new System.EventHandler(this.BtnSymbolsClick);
            //
            // cboSymbol
            //
            this.cboSymbol.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboSymbol.Name = "cboSymbol";
            this.cboSymbol.Size = new System.Drawing.Size(220, 23);
            this.cboSymbol.SelectedIndexChanged += new System.EventHandler(this.CboSymbolSelectedIndexChanged);
            //
            // splitContainerMain
            //
            this.splitContainerMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainerMain.Location = new System.Drawing.Point(0, 50);
            this.splitContainerMain.Name = "splitContainerMain";
            this.splitContainerMain.Panel1.Controls.Add(this.splitContainerLeft);
            this.splitContainerMain.Panel2.Controls.Add(this.panelParams);
            this.splitContainerMain.Size = new System.Drawing.Size(1184, 623);
            this.splitContainerMain.SplitterDistance = 820;
            this.splitContainerMain.TabIndex = 3;
            //
            // splitContainerLeft
            //
            this.splitContainerLeft.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainerLeft.Location = new System.Drawing.Point(0, 0);
            this.splitContainerLeft.Name = "splitContainerLeft";
            this.splitContainerLeft.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.splitContainerLeft.Panel1.Controls.Add(this.tabControlDocs);
            this.splitContainerLeft.Panel2.Controls.Add(this.tabControlResults);
            this.splitContainerLeft.Size = new System.Drawing.Size(820, 623);
            this.splitContainerLeft.SplitterDistance = 340;
            this.splitContainerLeft.TabIndex = 0;
            //
            // tabControlDocs
            //
            // 文档标签页全部由运行时 CreateDocument 动态创建（设计器不放占位页）
            this.tabControlDocs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControlDocs.Location = new System.Drawing.Point(0, 0);
            this.tabControlDocs.Name = "tabControlDocs";
            this.tabControlDocs.Size = new System.Drawing.Size(820, 340);
            this.tabControlDocs.TabIndex = 0;
            this.tabControlDocs.SelectedIndexChanged += new System.EventHandler(this.TabControlDocsSelectedIndexChanged);
            this.tabControlDocs.MouseUp += new System.Windows.Forms.MouseEventHandler(this.TabControlDocsMouseUp);
            //
            // tabControlResults
            //
            this.tabControlResults.Controls.Add(this.tabHex);
            this.tabControlResults.Controls.Add(this.tabErrors);
            this.tabControlResults.Controls.Add(this.tabLog);
            this.tabControlResults.Controls.Add(this.tabRegs);
            this.tabControlResults.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControlResults.Location = new System.Drawing.Point(0, 0);
            this.tabControlResults.Name = "tabControlResults";
            this.tabControlResults.SelectedIndex = 0;
            this.tabControlResults.Size = new System.Drawing.Size(820, 279);
            this.tabControlResults.TabIndex = 0;
            //
            // tabHex
            //
            this.tabHex.Controls.Add(this.txtHex);
            this.tabHex.Location = new System.Drawing.Point(4, 22);
            this.tabHex.Name = "tabHex";
            this.tabHex.Padding = new System.Windows.Forms.Padding(3);
            this.tabHex.Size = new System.Drawing.Size(812, 253);
            this.tabHex.TabIndex = 0;
            this.tabHex.Text = "机器码 Hex";
            this.tabHex.UseVisualStyleBackColor = true;
            //
            // txtHex
            //
            this.txtHex.BackColor = System.Drawing.Color.White;
            this.txtHex.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtHex.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtHex.Location = new System.Drawing.Point(3, 3);
            this.txtHex.Multiline = true;
            this.txtHex.Name = "txtHex";
            this.txtHex.ReadOnly = true;
            this.txtHex.Size = new System.Drawing.Size(806, 247);
            this.txtHex.TabIndex = 0;
            //
            // tabErrors
            //
            this.tabErrors.Controls.Add(this.lstErrors);
            this.tabErrors.Location = new System.Drawing.Point(4, 22);
            this.tabErrors.Name = "tabErrors";
            this.tabErrors.Padding = new System.Windows.Forms.Padding(3);
            this.tabErrors.Size = new System.Drawing.Size(812, 253);
            this.tabErrors.TabIndex = 1;
            this.tabErrors.Text = "校验错误";
            this.tabErrors.UseVisualStyleBackColor = true;
            //
            // lstErrors
            //
            this.lstErrors.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lstErrors.Font = new System.Drawing.Font("Consolas", 9F);
            this.lstErrors.ItemHeight = 15;
            this.lstErrors.Location = new System.Drawing.Point(3, 3);
            this.lstErrors.Name = "lstErrors";
            this.lstErrors.Size = new System.Drawing.Size(806, 247);
            this.lstErrors.TabIndex = 0;
            this.lstErrors.DoubleClick += new System.EventHandler(this.LstErrorsDoubleClick);
            //
            // tabLog
            //
            this.tabLog.Controls.Add(this.txtLog);
            this.tabLog.Location = new System.Drawing.Point(4, 22);
            this.tabLog.Name = "tabLog";
            this.tabLog.Padding = new System.Windows.Forms.Padding(3);
            this.tabLog.Size = new System.Drawing.Size(812, 253);
            this.tabLog.TabIndex = 2;
            this.tabLog.Text = "执行日志";
            this.tabLog.UseVisualStyleBackColor = true;
            //
            // txtLog
            //
            this.txtLog.BackColor = System.Drawing.Color.FromArgb(30, 30, 34);
            this.txtLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtLog.ForeColor = System.Drawing.Color.FromArgb(200, 205, 210);
            this.txtLog.Location = new System.Drawing.Point(3, 3);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtLog.Size = new System.Drawing.Size(806, 247);
            this.txtLog.TabIndex = 0;
            this.txtLog.WordWrap = false;
            //
            // tabRegs
            //
            this.tabRegs.Controls.Add(this.txtRegs);
            this.tabRegs.Location = new System.Drawing.Point(4, 22);
            this.tabRegs.Name = "tabRegs";
            this.tabRegs.Padding = new System.Windows.Forms.Padding(3);
            this.tabRegs.Size = new System.Drawing.Size(812, 253);
            this.tabRegs.TabIndex = 3;
            this.tabRegs.Text = "寄存器结果";
            this.tabRegs.UseVisualStyleBackColor = true;
            //
            // txtRegs
            //
            this.txtRegs.BackColor = System.Drawing.Color.White;
            this.txtRegs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtRegs.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtRegs.Location = new System.Drawing.Point(3, 3);
            this.txtRegs.Multiline = true;
            this.txtRegs.Name = "txtRegs";
            this.txtRegs.ReadOnly = true;
            this.txtRegs.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtRegs.Size = new System.Drawing.Size(806, 247);
            this.txtRegs.TabIndex = 0;
            this.txtRegs.WordWrap = false;
            //
            // panelParams
            //
            this.panelParams.AutoScroll = true;
            this.panelParams.Controls.Add(this.grpSyntax);
            this.panelParams.Controls.Add(this.grpArch);
            this.panelParams.Controls.Add(this.grpCall);
            this.panelParams.Controls.Add(this.btnAssemble);
            this.panelParams.Controls.Add(this.btnExecute);
            this.panelParams.Controls.Add(this.btnClearLog);
            this.panelParams.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelParams.Location = new System.Drawing.Point(0, 0);
            this.panelParams.Name = "panelParams";
            this.panelParams.Padding = new System.Windows.Forms.Padding(8);
            this.panelParams.Size = new System.Drawing.Size(360, 623);
            this.panelParams.TabIndex = 0;
            //
            // grpSyntax
            //
            this.grpSyntax.Controls.Add(this.rbIntel);
            this.grpSyntax.Controls.Add(this.rbAtt);
            this.grpSyntax.Location = new System.Drawing.Point(11, 8);
            this.grpSyntax.Name = "grpSyntax";
            this.grpSyntax.Size = new System.Drawing.Size(330, 60);
            this.grpSyntax.TabIndex = 0;
            this.grpSyntax.TabStop = false;
            this.grpSyntax.Text = "语法风格";
            //
            // rbIntel
            //
            this.rbIntel.AutoSize = true;
            this.rbIntel.Checked = true;
            this.rbIntel.Location = new System.Drawing.Point(20, 25);
            this.rbIntel.Name = "rbIntel";
            this.rbIntel.Size = new System.Drawing.Size(92, 19);
            this.rbIntel.TabIndex = 0;
            this.rbIntel.TabStop = true;
            this.rbIntel.Text = "Intel (NASM)";
            this.rbIntel.UseVisualStyleBackColor = true;
            this.rbIntel.CheckedChanged += new System.EventHandler(this.SyntaxChanged);
            //
            // rbAtt
            //
            this.rbAtt.AutoSize = true;
            this.rbAtt.Location = new System.Drawing.Point(170, 25);
            this.rbAtt.Name = "rbAtt";
            this.rbAtt.Size = new System.Drawing.Size(120, 19);
            this.rbAtt.TabIndex = 1;
            this.rbAtt.Text = "AT&T (GAS)";
            this.rbAtt.UseVisualStyleBackColor = true;
            this.rbAtt.CheckedChanged += new System.EventHandler(this.SyntaxChanged);
            //
            // grpArch
            //
            this.grpArch.Controls.Add(this.rbX64);
            this.grpArch.Controls.Add(this.rbX86);
            this.grpArch.Location = new System.Drawing.Point(11, 74);
            this.grpArch.Name = "grpArch";
            this.grpArch.Size = new System.Drawing.Size(330, 60);
            this.grpArch.TabIndex = 1;
            this.grpArch.TabStop = false;
            this.grpArch.Text = "目标架构";
            //
            // rbX64
            //
            this.rbX64.AutoSize = true;
            this.rbX64.Checked = true;
            this.rbX64.Location = new System.Drawing.Point(20, 25);
            this.rbX64.Name = "rbX64";
            this.rbX64.Size = new System.Drawing.Size(60, 19);
            this.rbX64.TabIndex = 0;
            this.rbX64.TabStop = true;
            this.rbX64.Text = "x64";
            this.rbX64.UseVisualStyleBackColor = true;
            this.rbX64.CheckedChanged += new System.EventHandler(this.ArchChanged);
            //
            // rbX86
            //
            this.rbX86.AutoSize = true;
            this.rbX86.Location = new System.Drawing.Point(170, 25);
            this.rbX86.Name = "rbX86";
            this.rbX86.Size = new System.Drawing.Size(60, 19);
            this.rbX86.TabIndex = 1;
            this.rbX86.Text = "x86";
            this.rbX86.UseVisualStyleBackColor = true;
            this.rbX86.CheckedChanged += new System.EventHandler(this.ArchChanged);
            //
            // grpCall
            //
            this.grpCall.Controls.Add(this.chkModeCall);
            this.grpCall.Controls.Add(this.lblCallTarget);
            this.grpCall.Controls.Add(this.txtCallTarget);
            this.grpCall.Controls.Add(this.lblArgs);
            this.grpCall.Controls.Add(this.txtArg0);
            this.grpCall.Controls.Add(this.txtArg1);
            this.grpCall.Controls.Add(this.txtArg2);
            this.grpCall.Controls.Add(this.txtArg3);
            this.grpCall.Controls.Add(this.lblTimeout);
            this.grpCall.Controls.Add(this.txtTimeout);
            this.grpCall.Location = new System.Drawing.Point(11, 140);
            this.grpCall.Name = "grpCall";
            this.grpCall.Size = new System.Drawing.Size(330, 220);
            this.grpCall.TabIndex = 2;
            this.grpCall.TabStop = false;
            this.grpCall.Text = "调用设置";
            //
            // chkModeCall
            //
            this.chkModeCall.AutoSize = true;
            this.chkModeCall.Location = new System.Drawing.Point(20, 25);
            this.chkModeCall.Name = "chkModeCall";
            this.chkModeCall.Size = new System.Drawing.Size(260, 19);
            this.chkModeCall.TabIndex = 0;
            this.chkModeCall.Text = "执行后调用目标函数 (按目标 ABI 封送参数)";
            this.chkModeCall.UseVisualStyleBackColor = true;
            //
            // lblCallTarget
            //
            this.lblCallTarget.AutoSize = true;
            this.lblCallTarget.Location = new System.Drawing.Point(20, 52);
            this.lblCallTarget.Name = "lblCallTarget";
            this.lblCallTarget.Size = new System.Drawing.Size(155, 13);
            this.lblCallTarget.TabIndex = 1;
            this.lblCallTarget.Text = "调用目标 (符号名或十六进制地址):";
            //
            // txtCallTarget
            //
            this.txtCallTarget.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtCallTarget.Location = new System.Drawing.Point(22, 70);
            this.txtCallTarget.Name = "txtCallTarget";
            this.txtCallTarget.Size = new System.Drawing.Size(280, 22);
            this.txtCallTarget.TabIndex = 2;
            //
            // lblArgs
            //
            this.lblArgs.AutoSize = true;
            this.lblArgs.Location = new System.Drawing.Point(20, 100);
            this.lblArgs.Name = "lblArgs";
            this.lblArgs.Size = new System.Drawing.Size(197, 13);
            this.lblArgs.TabIndex = 3;
            this.lblArgs.Text = "参数 (十进制 / 0x十六进制 / 符号名):";
            //
            // txtArg0
            //
            this.txtArg0.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtArg0.Location = new System.Drawing.Point(22, 118);
            this.txtArg0.Name = "txtArg0";
            this.txtArg0.Size = new System.Drawing.Size(133, 22);
            this.txtArg0.TabIndex = 4;
            this.txtArg0.Text = "0";
            //
            // txtArg1
            //
            this.txtArg1.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtArg1.Location = new System.Drawing.Point(169, 118);
            this.txtArg1.Name = "txtArg1";
            this.txtArg1.Size = new System.Drawing.Size(133, 22);
            this.txtArg1.TabIndex = 5;
            this.txtArg1.Text = "0";
            //
            // txtArg2
            //
            this.txtArg2.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtArg2.Location = new System.Drawing.Point(22, 146);
            this.txtArg2.Name = "txtArg2";
            this.txtArg2.Size = new System.Drawing.Size(133, 22);
            this.txtArg2.TabIndex = 6;
            this.txtArg2.Text = "0";
            //
            // txtArg3
            //
            this.txtArg3.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtArg3.Location = new System.Drawing.Point(169, 146);
            this.txtArg3.Name = "txtArg3";
            this.txtArg3.Size = new System.Drawing.Size(133, 22);
            this.txtArg3.TabIndex = 7;
            this.txtArg3.Text = "0";
            //
            // lblTimeout
            //
            this.lblTimeout.AutoSize = true;
            this.lblTimeout.Location = new System.Drawing.Point(20, 178);
            this.lblTimeout.Name = "lblTimeout";
            this.lblTimeout.Size = new System.Drawing.Size(67, 13);
            this.lblTimeout.TabIndex = 8;
            this.lblTimeout.Text = "超时 (毫秒):";
            //
            // txtTimeout
            //
            this.txtTimeout.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtTimeout.Location = new System.Drawing.Point(110, 174);
            this.txtTimeout.Name = "txtTimeout";
            this.txtTimeout.Size = new System.Drawing.Size(100, 22);
            this.txtTimeout.TabIndex = 9;
            this.txtTimeout.Text = "3000";
            //
            // btnAssemble
            //
            this.btnAssemble.Location = new System.Drawing.Point(11, 372);
            this.btnAssemble.Name = "btnAssemble";
            this.btnAssemble.Size = new System.Drawing.Size(155, 34);
            this.btnAssemble.TabIndex = 3;
            this.btnAssemble.Text = "验证 + 生成机器码";
            this.btnAssemble.UseVisualStyleBackColor = true;
            this.btnAssemble.Click += new System.EventHandler(this.BtnAssembleClick);
            //
            // btnExecute
            //
            this.btnExecute.Location = new System.Drawing.Point(186, 372);
            this.btnExecute.Name = "btnExecute";
            this.btnExecute.Size = new System.Drawing.Size(155, 34);
            this.btnExecute.TabIndex = 4;
            this.btnExecute.Text = "执行 (发送到目标)";
            this.btnExecute.UseVisualStyleBackColor = true;
            this.btnExecute.Click += new System.EventHandler(this.BtnExecuteClick);
            //
            // btnClearLog
            //
            this.btnClearLog.Location = new System.Drawing.Point(11, 414);
            this.btnClearLog.Name = "btnClearLog";
            this.btnClearLog.Size = new System.Drawing.Size(100, 26);
            this.btnClearLog.TabIndex = 5;
            this.btnClearLog.Text = "清空日志";
            this.btnClearLog.UseVisualStyleBackColor = true;
            this.btnClearLog.Click += new System.EventHandler(this.BtnClearLogClick);
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1184, 695);
            this.Controls.Add(this.splitContainerMain);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.toolbar);
            this.Controls.Add(this.menuStrip);
            this.MainMenuStrip = this.menuStrip;
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "AsmStudio — 汇编编辑 / 校验 / 远程执行";
            this.menuStrip.ResumeLayout(false);
            this.menuStrip.PerformLayout();
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.toolbar.ResumeLayout(false);
            this.toolbar.PerformLayout();
            this.splitContainerMain.Panel1.ResumeLayout(false);
            this.splitContainerMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerMain)).EndInit();
            this.splitContainerMain.ResumeLayout(false);
            this.splitContainerLeft.Panel1.ResumeLayout(false);
            this.splitContainerLeft.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerLeft)).EndInit();
            this.splitContainerLeft.ResumeLayout(false);
            this.tabControlDocs.ResumeLayout(false);
            this.tabControlResults.ResumeLayout(false);
            this.tabHex.ResumeLayout(false);
            this.tabHex.PerformLayout();
            this.tabErrors.ResumeLayout(false);
            this.tabLog.ResumeLayout(false);
            this.tabLog.PerformLayout();
            this.tabRegs.ResumeLayout(false);
            this.tabRegs.PerformLayout();
            this.panelParams.ResumeLayout(false);
            this.grpSyntax.ResumeLayout(false);
            this.grpSyntax.PerformLayout();
            this.grpArch.ResumeLayout(false);
            this.grpArch.PerformLayout();
            this.grpCall.ResumeLayout(false);
            this.grpCall.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.ToolStripMenuItem menuFile;
        private System.Windows.Forms.ToolStripMenuItem menuFileNew;
        private System.Windows.Forms.ToolStripMenuItem menuFileOpen;
        private System.Windows.Forms.ToolStripMenuItem menuFileSave;
        private System.Windows.Forms.ToolStripMenuItem menuFileExit;
        private System.Windows.Forms.ToolStripMenuItem menuEdit;
        private System.Windows.Forms.ToolStripMenuItem menuEditUndo;
        private System.Windows.Forms.ToolStripMenuItem menuEditRedo;
        private System.Windows.Forms.ContextMenuStrip tabCloseMenu;
        private System.Windows.Forms.ToolStripMenuItem tabCloseMenuItem;
        private System.Windows.Forms.ToolStripMenuItem tabCloseSaveItem;
        private System.Windows.Forms.ToolStripMenuItem tabCloseOthersItem;
        private System.Windows.Forms.ToolStripMenuItem tabCloseAllItem;
        private System.Windows.Forms.ToolStripMenuItem menuScheme;
        private System.Windows.Forms.ToolStripMenuItem menuSchemeLoad;
        private System.Windows.Forms.ToolStripMenuItem menuSchemeRun;
        private System.Windows.Forms.ToolStripMenuItem menuTools;
        private System.Windows.Forms.ToolStripMenuItem menuToolsSelfTest;
        private System.Windows.Forms.ToolStripMenuItem menuToolsInject;
        private System.Windows.Forms.ToolStripMenuItem menuHelp;
        private System.Windows.Forms.ToolStripMenuItem menuHelpAbout;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
        private System.Windows.Forms.ToolStripStatusLabel lblMode;
        private System.Windows.Forms.ToolStrip toolbar;
        private System.Windows.Forms.ToolStripButton btnScan;
        private System.Windows.Forms.ToolStripComboBox cboProcess;
        private System.Windows.Forms.ToolStripLabel lblTargetArch;
        private System.Windows.Forms.ToolStripComboBox cboChannel;
        private System.Windows.Forms.ToolStripButton btnConnect;
        private System.Windows.Forms.ToolStripButton btnDisconnect;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripButton btnSymbols;
        private System.Windows.Forms.ToolStripComboBox cboSymbol;
        private System.Windows.Forms.SplitContainer splitContainerMain;
        private System.Windows.Forms.SplitContainer splitContainerLeft;
        private System.Windows.Forms.TabControl tabControlDocs;
        private System.Windows.Forms.TabControl tabControlResults;
        private System.Windows.Forms.TabPage tabHex;
        private System.Windows.Forms.TextBox txtHex;
        private System.Windows.Forms.TabPage tabErrors;
        private System.Windows.Forms.ListBox lstErrors;
        private System.Windows.Forms.TabPage tabLog;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.TabPage tabRegs;
        private System.Windows.Forms.TextBox txtRegs;
        private System.Windows.Forms.Panel panelParams;
        private System.Windows.Forms.GroupBox grpSyntax;
        private System.Windows.Forms.RadioButton rbIntel;
        private System.Windows.Forms.RadioButton rbAtt;
        private System.Windows.Forms.GroupBox grpArch;
        private System.Windows.Forms.RadioButton rbX64;
        private System.Windows.Forms.RadioButton rbX86;
        private System.Windows.Forms.GroupBox grpCall;
        private System.Windows.Forms.CheckBox chkModeCall;
        private System.Windows.Forms.Label lblCallTarget;
        private System.Windows.Forms.TextBox txtCallTarget;
        private System.Windows.Forms.Label lblArgs;
        private System.Windows.Forms.TextBox txtArg0;
        private System.Windows.Forms.TextBox txtArg1;
        private System.Windows.Forms.TextBox txtArg2;
        private System.Windows.Forms.TextBox txtArg3;
        private System.Windows.Forms.Label lblTimeout;
        private System.Windows.Forms.TextBox txtTimeout;
        private System.Windows.Forms.Button btnAssemble;
        private System.Windows.Forms.Button btnExecute;
        private System.Windows.Forms.Button btnClearLog;
    }
}
