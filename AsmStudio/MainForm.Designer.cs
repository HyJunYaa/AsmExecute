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
        	this.menuScheme = new System.Windows.Forms.ToolStripMenuItem();
        	this.menuSchemeLoad = new System.Windows.Forms.ToolStripMenuItem();
        	this.menuSchemeRun = new System.Windows.Forms.ToolStripMenuItem();
        	this.menuTools = new System.Windows.Forms.ToolStripMenuItem();
        	this.menuToolsSelfTest = new System.Windows.Forms.ToolStripMenuItem();
        	this.menuLanguage = new System.Windows.Forms.ToolStripMenuItem();
        	this.menuHelp = new System.Windows.Forms.ToolStripMenuItem();
        	this.menuHelpAbout = new System.Windows.Forms.ToolStripMenuItem();
        	this.tabCloseMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
        	this.tabCloseMenuItem = new System.Windows.Forms.ToolStripMenuItem();
        	this.tabCloseSaveItem = new System.Windows.Forms.ToolStripMenuItem();
        	this.tabCloseOthersItem = new System.Windows.Forms.ToolStripMenuItem();
        	this.tabCloseAllItem = new System.Windows.Forms.ToolStripMenuItem();
        	this.logClearMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
        	this.logClearMenuItem = new System.Windows.Forms.ToolStripMenuItem();
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
        	this.splitContainerMain = new System.Windows.Forms.SplitContainer();
        	this.splitLeft = new System.Windows.Forms.SplitContainer();
        	this.tabControlDocs = new System.Windows.Forms.TabControl();
        	this.tabControlResults = new System.Windows.Forms.TabControl();
        	this.tabHex = new System.Windows.Forms.TabPage();
        	this.txtHex = new System.Windows.Forms.TextBox();
        	this.tabErrors = new System.Windows.Forms.TabPage();
        	this.lstErrors = new System.Windows.Forms.ListBox();
        	this.tabRegs = new System.Windows.Forms.TabPage();
        	this.txtRegs = new System.Windows.Forms.TextBox();
        	this.tabMem = new System.Windows.Forms.TabPage();
        	this.lblMemName = new System.Windows.Forms.Label();
        	this.txtMemName = new System.Windows.Forms.TextBox();
        	this.rbMemText = new System.Windows.Forms.RadioButton();
        	this.rbMemQword = new System.Windows.Forms.RadioButton();
        	this.rbMemHex = new System.Windows.Forms.RadioButton();
        	this.lblMemValue = new System.Windows.Forms.Label();
        	this.txtMemValue = new System.Windows.Forms.TextBox();
        	this.btnMemAlloc = new System.Windows.Forms.Button();
        	this.btnMemFree = new System.Windows.Forms.Button();
        	this.lstMem = new System.Windows.Forms.ListView();
        	this.chName = new System.Windows.Forms.ColumnHeader();
        	this.chData = new System.Windows.Forms.ColumnHeader();
        	this.chAddr = new System.Windows.Forms.ColumnHeader();
        	this.splitRight = new System.Windows.Forms.SplitContainer();
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
        	this.lblModule = new System.Windows.Forms.Label();
        	this.cboModule = new System.Windows.Forms.ComboBox();
        	this.lblApis = new System.Windows.Forms.Label();
        	this.cboApis = new System.Windows.Forms.ComboBox();
        	this.lblArgs = new System.Windows.Forms.Label();
        	this.txtArgs = new System.Windows.Forms.TextBox();
        	this.btnAssemble = new System.Windows.Forms.Button();
        	this.btnExecute = new System.Windows.Forms.Button();
        	this.btnClearLog = new System.Windows.Forms.Button();
        	this.txtLog = new System.Windows.Forms.TextBox();
        	this.menuStrip.SuspendLayout();
        	this.tabCloseMenu.SuspendLayout();
        	this.logClearMenu.SuspendLayout();
        	this.statusStrip.SuspendLayout();
        	this.toolbar.SuspendLayout();
        	((System.ComponentModel.ISupportInitialize)(this.splitContainerMain)).BeginInit();
        	this.splitContainerMain.Panel1.SuspendLayout();
        	this.splitContainerMain.Panel2.SuspendLayout();
        	this.splitContainerMain.SuspendLayout();
        	((System.ComponentModel.ISupportInitialize)(this.splitLeft)).BeginInit();
        	this.splitLeft.Panel1.SuspendLayout();
        	this.splitLeft.Panel2.SuspendLayout();
        	this.splitLeft.SuspendLayout();
        	this.tabControlResults.SuspendLayout();
        	this.tabHex.SuspendLayout();
        	this.tabErrors.SuspendLayout();
        	this.tabRegs.SuspendLayout();
        	this.tabMem.SuspendLayout();
        	((System.ComponentModel.ISupportInitialize)(this.splitRight)).BeginInit();
        	this.splitRight.Panel1.SuspendLayout();
        	this.splitRight.Panel2.SuspendLayout();
        	this.splitRight.SuspendLayout();
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
			this.menuLanguage,
			this.menuHelp});
        	this.menuStrip.Location = new System.Drawing.Point(0, 0);
        	this.menuStrip.Name = "menuStrip";
        	this.menuStrip.Size = new System.Drawing.Size(1184, 24);
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
        	this.menuFile.Size = new System.Drawing.Size(47, 20);
        	this.menuFile.Text = "&File";
        	// 
        	// menuFileNew
        	// 
        	this.menuFileNew.Name = "menuFileNew";
        	this.menuFileNew.Size = new System.Drawing.Size(123, 22);
        	this.menuFileNew.Text = "&New";
        	this.menuFileNew.Click += new System.EventHandler(this.MenuFileNewClick);
        	// 
        	// menuFileOpen
        	// 
        	this.menuFileOpen.Name = "menuFileOpen";
        	this.menuFileOpen.Size = new System.Drawing.Size(123, 22);
        	this.menuFileOpen.Text = "&Open...";
        	this.menuFileOpen.Click += new System.EventHandler(this.MenuFileOpenClick);
        	// 
        	// menuFileSave
        	// 
        	this.menuFileSave.Name = "menuFileSave";
        	this.menuFileSave.Size = new System.Drawing.Size(123, 22);
        	this.menuFileSave.Text = "&Save";
        	this.menuFileSave.Click += new System.EventHandler(this.MenuFileSaveClick);
        	// 
        	// menuFileExit
        	// 
        	this.menuFileExit.Name = "menuFileExit";
        	this.menuFileExit.Size = new System.Drawing.Size(123, 22);
        	this.menuFileExit.Text = "E&xit";
        	this.menuFileExit.Click += new System.EventHandler(this.MenuFileExitClick);
        	// 
        	// menuEdit
        	// 
        	this.menuEdit.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this.menuEditUndo,
			this.menuEditRedo});
        	this.menuEdit.Name = "menuEdit";
        	this.menuEdit.Size = new System.Drawing.Size(47, 20);
        	this.menuEdit.Text = "&Edit";
        	this.menuEdit.DropDownOpening += new System.EventHandler(this.MenuEditDropDownOpening);
        	// 
        	// menuEditUndo
        	// 
        	this.menuEditUndo.Name = "menuEditUndo";
        	this.menuEditUndo.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Z)));
        	this.menuEditUndo.Size = new System.Drawing.Size(151, 22);
        	this.menuEditUndo.Text = "&Undo";
        	this.menuEditUndo.Click += new System.EventHandler(this.MenuEditUndoClick);
        	// 
        	// menuEditRedo
        	// 
        	this.menuEditRedo.Name = "menuEditRedo";
        	this.menuEditRedo.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Y)));
        	this.menuEditRedo.Size = new System.Drawing.Size(151, 22);
        	this.menuEditRedo.Text = "&Redo";
        	this.menuEditRedo.Click += new System.EventHandler(this.MenuEditRedoClick);
        	// 
        	// menuScheme
        	// 
        	this.menuScheme.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this.menuSchemeLoad,
			this.menuSchemeRun});
        	this.menuScheme.Name = "menuScheme";
        	this.menuScheme.Size = new System.Drawing.Size(68, 20);
        	this.menuScheme.Text = "&Schemes";
        	// 
        	// menuSchemeLoad
        	// 
        	this.menuSchemeLoad.Name = "menuSchemeLoad";
        	this.menuSchemeLoad.Size = new System.Drawing.Size(228, 22);
        	this.menuSchemeLoad.Text = "&Scan Scheme Folder";
        	this.menuSchemeLoad.Click += new System.EventHandler(this.MenuSchemeLoadClick);
        	// 
        	// menuSchemeRun
        	// 
        	this.menuSchemeRun.Name = "menuSchemeRun";
        	this.menuSchemeRun.Size = new System.Drawing.Size(228, 22);
        	this.menuSchemeRun.Text = "&Run Selected Scheme...";
        	this.menuSchemeRun.Click += new System.EventHandler(this.MenuSchemeRunClick);
        	// 
        	// menuTools
        	// 
        	this.menuTools.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this.menuToolsSelfTest});
        	this.menuTools.Name = "menuTools";
        	this.menuTools.Size = new System.Drawing.Size(54, 20);
        	this.menuTools.Text = "&Tools";
        	// 
        	// menuToolsSelfTest
        	// 
        	this.menuToolsSelfTest.Name = "menuToolsSelfTest";
        	this.menuToolsSelfTest.Size = new System.Drawing.Size(207, 22);
        	this.menuToolsSelfTest.Text = "Assembler &Self-Test";
        	this.menuToolsSelfTest.Click += new System.EventHandler(this.MenuToolsSelfTestClick);
        	// 
        	// menuLanguage
        	// 
        	this.menuLanguage.Name = "menuLanguage";
        	this.menuLanguage.Size = new System.Drawing.Size(75, 20);
        	this.menuLanguage.Text = "&Language";
        	// 
        	// menuHelp
        	// 
        	this.menuHelp.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this.menuHelpAbout});
        	this.menuHelp.Name = "menuHelp";
        	this.menuHelp.Size = new System.Drawing.Size(47, 20);
        	this.menuHelp.Text = "&Help";
        	// 
        	// menuHelpAbout
        	// 
        	this.menuHelpAbout.Name = "menuHelpAbout";
        	this.menuHelpAbout.Size = new System.Drawing.Size(130, 22);
        	this.menuHelpAbout.Text = "&About...";
        	this.menuHelpAbout.Click += new System.EventHandler(this.MenuHelpAboutClick);
        	// 
        	// tabCloseMenu
        	// 
        	this.tabCloseMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this.tabCloseMenuItem,
			this.tabCloseSaveItem,
			this.tabCloseOthersItem,
			this.tabCloseAllItem});
        	this.tabCloseMenu.Name = "tabCloseMenu";
        	this.tabCloseMenu.Size = new System.Drawing.Size(159, 92);
        	// 
        	// tabCloseMenuItem
        	// 
        	this.tabCloseMenuItem.Name = "tabCloseMenuItem";
        	this.tabCloseMenuItem.Size = new System.Drawing.Size(158, 22);
        	this.tabCloseMenuItem.Text = "&Close";
        	this.tabCloseMenuItem.Click += new System.EventHandler(this.TabCloseMenuItemClick);
        	// 
        	// tabCloseSaveItem
        	// 
        	this.tabCloseSaveItem.Name = "tabCloseSaveItem";
        	this.tabCloseSaveItem.Size = new System.Drawing.Size(158, 22);
        	this.tabCloseSaveItem.Text = "&Save This";
        	this.tabCloseSaveItem.Click += new System.EventHandler(this.MenuTabSaveClick);
        	// 
        	// tabCloseOthersItem
        	// 
        	this.tabCloseOthersItem.Name = "tabCloseOthersItem";
        	this.tabCloseOthersItem.Size = new System.Drawing.Size(158, 22);
        	this.tabCloseOthersItem.Text = "Close &Others";
        	this.tabCloseOthersItem.Click += new System.EventHandler(this.MenuTabCloseOthersClick);
        	// 
        	// tabCloseAllItem
        	// 
        	this.tabCloseAllItem.Name = "tabCloseAllItem";
        	this.tabCloseAllItem.Size = new System.Drawing.Size(158, 22);
        	this.tabCloseAllItem.Text = "Close &All";
        	this.tabCloseAllItem.Click += new System.EventHandler(this.MenuTabCloseAllClick);
        	// 
        	// logClearMenu
        	// 
        	this.logClearMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this.logClearMenuItem});
        	this.logClearMenu.Name = "logClearMenu";
        	this.logClearMenu.Size = new System.Drawing.Size(138, 26);
        	// 
        	// logClearMenuItem
        	// 
        	this.logClearMenuItem.Name = "logClearMenuItem";
        	this.logClearMenuItem.Size = new System.Drawing.Size(137, 22);
        	this.logClearMenuItem.Text = "&Clear Log";
        	this.logClearMenuItem.Click += new System.EventHandler(this.MenuLogClearClick);
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
        	this.lblStatus.Size = new System.Drawing.Size(1071, 17);
        	this.lblStatus.Spring = true;
        	this.lblStatus.Text = "Ready";
        	this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        	// 
        	// lblMode
        	// 
        	this.lblMode.Name = "lblMode";
        	this.lblMode.Size = new System.Drawing.Size(98, 17);
        	this.lblMode.Text = "Not Connected";
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
			this.toolStripSeparator1});
        	this.toolbar.Location = new System.Drawing.Point(0, 24);
        	this.toolbar.Name = "toolbar";
        	this.toolbar.Size = new System.Drawing.Size(1184, 25);
        	this.toolbar.TabIndex = 2;
        	// 
        	// btnScan
        	// 
        	this.btnScan.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
        	this.btnScan.Name = "btnScan";
        	this.btnScan.Size = new System.Drawing.Size(109, 22);
        	this.btnScan.Text = "Scan Processes";
        	this.btnScan.Click += new System.EventHandler(this.BtnScanClick);
        	// 
        	// cboProcess
        	// 
        	this.cboProcess.Name = "cboProcess";
        	this.cboProcess.Size = new System.Drawing.Size(240, 25);
        	this.cboProcess.DropDown += new System.EventHandler(this.CboProcessEnsureScan);
        	this.cboProcess.Click += new System.EventHandler(this.CboProcessEnsureScan);
        	this.cboProcess.TextChanged += new System.EventHandler(this.CboProcessEnsureScan);
        	// 
        	// lblTargetArch
        	// 
        	this.lblTargetArch.Name = "lblTargetArch";
        	this.lblTargetArch.Size = new System.Drawing.Size(0, 22);
        	// 
        	// cboChannel
        	// 
        	this.cboChannel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        	this.cboChannel.Items.AddRange(new object[] {
			"RemoteThread+VEH Protection",
			"Remote Thread (unprotected)"});
        	this.cboChannel.Name = "cboChannel";
        	this.cboChannel.Size = new System.Drawing.Size(140, 25);
        	// 
        	// btnConnect
        	// 
        	this.btnConnect.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
        	this.btnConnect.Name = "btnConnect";
        	this.btnConnect.Size = new System.Drawing.Size(60, 22);
        	this.btnConnect.Text = "Connect";
        	this.btnConnect.Click += new System.EventHandler(this.BtnConnectClick);
        	// 
        	// btnDisconnect
        	// 
        	this.btnDisconnect.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
        	this.btnDisconnect.Name = "btnDisconnect";
        	this.btnDisconnect.Size = new System.Drawing.Size(81, 22);
        	this.btnDisconnect.Text = "Disconnect";
        	this.btnDisconnect.Click += new System.EventHandler(this.BtnDisconnectClick);
        	// 
        	// toolStripSeparator1
        	// 
        	this.toolStripSeparator1.Name = "toolStripSeparator1";
        	this.toolStripSeparator1.Size = new System.Drawing.Size(6, 25);
        	// 
        	// splitContainerMain
        	// 
        	this.splitContainerMain.Dock = System.Windows.Forms.DockStyle.Fill;
        	this.splitContainerMain.Location = new System.Drawing.Point(0, 49);
        	this.splitContainerMain.Name = "splitContainerMain";
        	// 
        	// splitContainerMain.Panel1
        	// 
        	this.splitContainerMain.Panel1.Controls.Add(this.splitLeft);
        	// 
        	// splitContainerMain.Panel2
        	// 
        	this.splitContainerMain.Panel2.Controls.Add(this.splitRight);
        	this.splitContainerMain.Size = new System.Drawing.Size(1184, 624);
        	this.splitContainerMain.SplitterDistance = 640;
        	this.splitContainerMain.TabIndex = 3;
        	// 
        	// splitLeft
        	// 
        	this.splitLeft.Dock = System.Windows.Forms.DockStyle.Fill;
        	this.splitLeft.Location = new System.Drawing.Point(0, 0);
        	this.splitLeft.Name = "splitLeft";
        	this.splitLeft.Orientation = System.Windows.Forms.Orientation.Horizontal;
        	// 
        	// splitLeft.Panel1
        	// 
        	this.splitLeft.Panel1.Controls.Add(this.tabControlDocs);
        	// 
        	// splitLeft.Panel2
        	// 
        	this.splitLeft.Panel2.Controls.Add(this.tabControlResults);
        	this.splitLeft.Size = new System.Drawing.Size(640, 624);
        	this.splitLeft.SplitterDistance = 340;
        	this.splitLeft.TabIndex = 0;
        	// 
        	// tabControlDocs
        	// 
        	this.tabControlDocs.Dock = System.Windows.Forms.DockStyle.Fill;
        	this.tabControlDocs.Location = new System.Drawing.Point(0, 0);
        	this.tabControlDocs.Name = "tabControlDocs";
        	this.tabControlDocs.SelectedIndex = 0;
        	this.tabControlDocs.Size = new System.Drawing.Size(640, 340);
        	this.tabControlDocs.TabIndex = 0;
        	this.tabControlDocs.SelectedIndexChanged += new System.EventHandler(this.TabControlDocsSelectedIndexChanged);
        	this.tabControlDocs.MouseUp += new System.Windows.Forms.MouseEventHandler(this.TabControlDocsMouseUp);
        	// 
        	// tabControlResults
        	// 
        	this.tabControlResults.Controls.Add(this.tabHex);
        	this.tabControlResults.Controls.Add(this.tabErrors);
        	this.tabControlResults.Controls.Add(this.tabRegs);
        	this.tabControlResults.Controls.Add(this.tabMem);
        	this.tabControlResults.Dock = System.Windows.Forms.DockStyle.Fill;
        	this.tabControlResults.Location = new System.Drawing.Point(0, 0);
        	this.tabControlResults.Name = "tabControlResults";
        	this.tabControlResults.SelectedIndex = 0;
        	this.tabControlResults.Size = new System.Drawing.Size(640, 280);
        	this.tabControlResults.TabIndex = 0;
        	// 
        	// tabHex
        	// 
        	this.tabHex.Controls.Add(this.txtHex);
        	this.tabHex.Location = new System.Drawing.Point(4, 22);
        	this.tabHex.Name = "tabHex";
        	this.tabHex.Padding = new System.Windows.Forms.Padding(3);
        	this.tabHex.Size = new System.Drawing.Size(632, 254);
        	this.tabHex.TabIndex = 0;
        	this.tabHex.Text = "Machine Code Hex";
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
        	this.txtHex.Size = new System.Drawing.Size(626, 248);
        	this.txtHex.TabIndex = 0;
        	// 
        	// tabErrors
        	// 
        	this.tabErrors.Controls.Add(this.lstErrors);
        	this.tabErrors.Location = new System.Drawing.Point(4, 22);
        	this.tabErrors.Name = "tabErrors";
        	this.tabErrors.Padding = new System.Windows.Forms.Padding(3);
        	this.tabErrors.Size = new System.Drawing.Size(632, 254);
        	this.tabErrors.TabIndex = 1;
        	this.tabErrors.Text = "Validation Errors";
        	this.tabErrors.UseVisualStyleBackColor = true;
        	// 
        	// lstErrors
        	// 
        	this.lstErrors.Dock = System.Windows.Forms.DockStyle.Fill;
        	this.lstErrors.Font = new System.Drawing.Font("Consolas", 9F);
        	this.lstErrors.ItemHeight = 14;
        	this.lstErrors.Location = new System.Drawing.Point(3, 3);
        	this.lstErrors.Name = "lstErrors";
        	this.lstErrors.Size = new System.Drawing.Size(626, 248);
        	this.lstErrors.TabIndex = 0;
        	this.lstErrors.DoubleClick += new System.EventHandler(this.LstErrorsDoubleClick);
        	// 
        	// tabRegs
        	// 
        	this.tabRegs.Controls.Add(this.txtRegs);
        	this.tabRegs.Location = new System.Drawing.Point(4, 22);
        	this.tabRegs.Name = "tabRegs";
        	this.tabRegs.Padding = new System.Windows.Forms.Padding(3);
        	this.tabRegs.Size = new System.Drawing.Size(632, 254);
        	this.tabRegs.TabIndex = 3;
        	this.tabRegs.Text = "Register Results";
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
        	this.txtRegs.Size = new System.Drawing.Size(626, 248);
        	this.txtRegs.TabIndex = 0;
        	this.txtRegs.WordWrap = false;
        	// 
        	// tabMem
        	// 
        	this.tabMem.Controls.Add(this.lblMemName);
        	this.tabMem.Controls.Add(this.txtMemName);
        	this.tabMem.Controls.Add(this.rbMemText);
        	this.tabMem.Controls.Add(this.rbMemQword);
        	this.tabMem.Controls.Add(this.rbMemHex);
        	this.tabMem.Controls.Add(this.lblMemValue);
        	this.tabMem.Controls.Add(this.txtMemValue);
        	this.tabMem.Controls.Add(this.btnMemAlloc);
        	this.tabMem.Controls.Add(this.btnMemFree);
        	this.tabMem.Controls.Add(this.lstMem);
        	this.tabMem.Location = new System.Drawing.Point(4, 22);
        	this.tabMem.Name = "tabMem";
        	this.tabMem.Padding = new System.Windows.Forms.Padding(3);
        	this.tabMem.Size = new System.Drawing.Size(632, 254);
        	this.tabMem.TabIndex = 4;
        	this.tabMem.Text = "Memory Alloc";
        	this.tabMem.UseVisualStyleBackColor = true;
        	// 
        	// lblMemName
        	// 
        	this.lblMemName.AutoSize = true;
        	this.lblMemName.Location = new System.Drawing.Point(11, 15);
        	this.lblMemName.Name = "lblMemName";
        	this.lblMemName.Size = new System.Drawing.Size(79, 13);
        	this.lblMemName.TabIndex = 0;
        	this.lblMemName.Text = "Name (symbol):";
        	// 
        	// txtMemName
        	// 
        	this.txtMemName.Font = new System.Drawing.Font("Consolas", 9F);
        	this.txtMemName.Location = new System.Drawing.Point(95, 9);
        	this.txtMemName.Name = "txtMemName";
        	this.txtMemName.Size = new System.Drawing.Size(288, 22);
        	this.txtMemName.TabIndex = 1;
        	this.txtMemName.Text = "msgText";
        	// 
        	// rbMemText
        	// 
        	this.rbMemText.AutoSize = true;
        	this.rbMemText.Location = new System.Drawing.Point(531, 42);
        	this.rbMemText.Name = "rbMemText";
        	this.rbMemText.Size = new System.Drawing.Size(91, 17);
        	this.rbMemText.TabIndex = 2;
        	this.rbMemText.Text = "Text (UTF-16)";
        	this.rbMemText.UseVisualStyleBackColor = true;
        	// 
        	// rbMemQword
        	// 
        	this.rbMemQword.AutoSize = true;
        	this.rbMemQword.Checked = true;
        	this.rbMemQword.Location = new System.Drawing.Point(389, 42);
        	this.rbMemQword.Name = "rbMemQword";
        	this.rbMemQword.Size = new System.Drawing.Size(58, 17);
        	this.rbMemQword.TabIndex = 3;
        	this.rbMemQword.TabStop = true;
        	this.rbMemQword.Text = "Integer";
        	this.rbMemQword.UseVisualStyleBackColor = true;
        	// 
        	// rbMemHex
        	// 
        	this.rbMemHex.AutoSize = true;
        	this.rbMemHex.Location = new System.Drawing.Point(453, 42);
        	this.rbMemHex.Name = "rbMemHex";
        	this.rbMemHex.Size = new System.Drawing.Size(72, 17);
        	this.rbMemHex.TabIndex = 4;
        	this.rbMemHex.Text = "Hex bytes";
        	this.rbMemHex.UseVisualStyleBackColor = true;
        	// 
        	// lblMemValue
        	// 
        	this.lblMemValue.AutoSize = true;
        	this.lblMemValue.Location = new System.Drawing.Point(11, 45);
        	this.lblMemValue.Name = "lblMemValue";
        	this.lblMemValue.Size = new System.Drawing.Size(47, 13);
        	this.lblMemValue.TabIndex = 5;
        	this.lblMemValue.Text = "Content:";
        	// 
        	// txtMemValue
        	// 
        	this.txtMemValue.Font = new System.Drawing.Font("Consolas", 9F);
        	this.txtMemValue.Location = new System.Drawing.Point(95, 41);
        	this.txtMemValue.Name = "txtMemValue";
        	this.txtMemValue.Size = new System.Drawing.Size(288, 22);
        	this.txtMemValue.TabIndex = 6;
        	this.txtMemValue.Text = "hi";
        	// 
        	// btnMemAlloc
        	// 
        	this.btnMemAlloc.Location = new System.Drawing.Point(390, 7);
        	this.btnMemAlloc.Name = "btnMemAlloc";
        	this.btnMemAlloc.Size = new System.Drawing.Size(115, 28);
        	this.btnMemAlloc.TabIndex = 7;
        	this.btnMemAlloc.Text = "Allocate";
        	this.btnMemAlloc.UseVisualStyleBackColor = true;
        	this.btnMemAlloc.Click += new System.EventHandler(this.BtnMemAllocClick);
        	// 
        	// btnMemFree
        	// 
        	this.btnMemFree.Location = new System.Drawing.Point(511, 7);
        	this.btnMemFree.Name = "btnMemFree";
        	this.btnMemFree.Size = new System.Drawing.Size(115, 28);
        	this.btnMemFree.TabIndex = 8;
        	this.btnMemFree.Text = "Free selected";
        	this.btnMemFree.UseVisualStyleBackColor = true;
        	this.btnMemFree.Click += new System.EventHandler(this.BtnMemFreeClick);
        	// 
        	// lstMem
        	// 
        	this.lstMem.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
			this.chName,
			this.chData,
			this.chAddr});
        	this.lstMem.Dock = System.Windows.Forms.DockStyle.Bottom;
        	this.lstMem.Font = new System.Drawing.Font("Consolas", 9F);
        	this.lstMem.FullRowSelect = true;
        	this.lstMem.GridLines = true;
        	this.lstMem.HideSelection = false;
        	this.lstMem.Location = new System.Drawing.Point(3, 71);
        	this.lstMem.Name = "lstMem";
        	this.lstMem.Size = new System.Drawing.Size(626, 180);
        	this.lstMem.TabIndex = 9;
        	this.lstMem.UseCompatibleStateImageBehavior = false;
        	this.lstMem.View = System.Windows.Forms.View.Details;
        	// 
        	// chName
        	// 
        	this.chName.Text = "Name (symbol)";
        	this.chName.Width = 150;
        	// 
        	// chData
        	// 
        	this.chData.Text = "Data (num / ptr / hex string)";
        	this.chData.Width = 290;
        	// 
        	// chAddr
        	// 
        	this.chAddr.Text = "Address";
        	this.chAddr.Width = 180;
        	// 
        	// splitRight
        	// 
        	this.splitRight.Dock = System.Windows.Forms.DockStyle.Fill;
        	this.splitRight.Location = new System.Drawing.Point(0, 0);
        	this.splitRight.Name = "splitRight";
        	this.splitRight.Orientation = System.Windows.Forms.Orientation.Horizontal;
        	// 
        	// splitRight.Panel1
        	// 
        	this.splitRight.Panel1.Controls.Add(this.grpSyntax);
        	this.splitRight.Panel1.Controls.Add(this.grpArch);
        	this.splitRight.Panel1.Controls.Add(this.grpCall);
        	this.splitRight.Panel1.Controls.Add(this.btnAssemble);
        	this.splitRight.Panel1.Controls.Add(this.btnExecute);
        	this.splitRight.Panel1.Controls.Add(this.btnClearLog);
        	// 
        	// splitRight.Panel2
        	// 
        	this.splitRight.Panel2.Controls.Add(this.txtLog);
        	this.splitRight.Size = new System.Drawing.Size(540, 624);
        	this.splitRight.SplitterDistance = 336;
        	this.splitRight.TabIndex = 0;
        	// 
        	// grpSyntax
        	// 
        	this.grpSyntax.Controls.Add(this.rbIntel);
        	this.grpSyntax.Controls.Add(this.rbAtt);
        	this.grpSyntax.Location = new System.Drawing.Point(6, 4);
        	this.grpSyntax.Name = "grpSyntax";
        	this.grpSyntax.Size = new System.Drawing.Size(160, 68);
        	this.grpSyntax.TabIndex = 0;
        	this.grpSyntax.TabStop = false;
        	this.grpSyntax.Text = "Syntax Style";
        	// 
        	// rbIntel
        	// 
        	this.rbIntel.AutoSize = true;
        	this.rbIntel.Checked = true;
        	this.rbIntel.Location = new System.Drawing.Point(14, 20);
        	this.rbIntel.Name = "rbIntel";
        	this.rbIntel.Size = new System.Drawing.Size(85, 17);
        	this.rbIntel.TabIndex = 0;
        	this.rbIntel.TabStop = true;
        	this.rbIntel.Text = "Intel (NASM)";
        	this.rbIntel.UseVisualStyleBackColor = true;
        	this.rbIntel.CheckedChanged += new System.EventHandler(this.SyntaxChanged);
        	// 
        	// rbAtt
        	// 
        	this.rbAtt.AutoSize = true;
        	this.rbAtt.Location = new System.Drawing.Point(14, 42);
        	this.rbAtt.Name = "rbAtt";
        	this.rbAtt.Size = new System.Drawing.Size(77, 17);
        	this.rbAtt.TabIndex = 1;
        	this.rbAtt.Text = "AT&T (GAS)";
        	this.rbAtt.UseVisualStyleBackColor = true;
        	this.rbAtt.CheckedChanged += new System.EventHandler(this.SyntaxChanged);
        	// 
        	// grpArch
        	// 
        	this.grpArch.Controls.Add(this.rbX64);
        	this.grpArch.Controls.Add(this.rbX86);
        	this.grpArch.Location = new System.Drawing.Point(6, 78);
        	this.grpArch.Name = "grpArch";
        	this.grpArch.Size = new System.Drawing.Size(160, 68);
        	this.grpArch.TabIndex = 1;
        	this.grpArch.TabStop = false;
        	this.grpArch.Text = "Target Arch";
        	// 
        	// rbX64
        	// 
        	this.rbX64.AutoSize = true;
        	this.rbX64.Checked = true;
        	this.rbX64.Location = new System.Drawing.Point(14, 20);
        	this.rbX64.Name = "rbX64";
        	this.rbX64.Size = new System.Drawing.Size(42, 17);
        	this.rbX64.TabIndex = 0;
        	this.rbX64.TabStop = true;
        	this.rbX64.Text = "x64";
        	this.rbX64.UseVisualStyleBackColor = true;
        	this.rbX64.CheckedChanged += new System.EventHandler(this.ArchChanged);
        	// 
        	// rbX86
        	// 
        	this.rbX86.AutoSize = true;
        	this.rbX86.Location = new System.Drawing.Point(14, 42);
        	this.rbX86.Name = "rbX86";
        	this.rbX86.Size = new System.Drawing.Size(42, 17);
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
        	this.grpCall.Controls.Add(this.lblModule);
        	this.grpCall.Controls.Add(this.cboModule);
        	this.grpCall.Controls.Add(this.lblApis);
        	this.grpCall.Controls.Add(this.cboApis);
        	this.grpCall.Controls.Add(this.lblArgs);
        	this.grpCall.Controls.Add(this.txtArgs);
        	this.grpCall.Location = new System.Drawing.Point(182, 4);
        	this.grpCall.Name = "grpCall";
        	this.grpCall.Size = new System.Drawing.Size(336, 286);
        	this.grpCall.TabIndex = 2;
        	this.grpCall.TabStop = false;
        	this.grpCall.Text = "Call Settings";
        	// 
        	// chkModeCall
        	// 
        	this.chkModeCall.AutoSize = true;
        	this.chkModeCall.Location = new System.Drawing.Point(20, 25);
        	this.chkModeCall.Name = "chkModeCall";
        	this.chkModeCall.Size = new System.Drawing.Size(323, 17);
        	this.chkModeCall.TabIndex = 0;
        	this.chkModeCall.Text = "Call target function after execution (marshal args per target ABI)";
        	this.chkModeCall.UseVisualStyleBackColor = true;
        	// 
        	// lblCallTarget
        	// 
        	this.lblCallTarget.AutoSize = true;
        	this.lblCallTarget.Location = new System.Drawing.Point(20, 52);
        	this.lblCallTarget.Name = "lblCallTarget";
        	this.lblCallTarget.Size = new System.Drawing.Size(199, 13);
        	this.lblCallTarget.TabIndex = 1;
        	this.lblCallTarget.Text = "Call target (symbol name or hex address):";
        	// 
        	// txtCallTarget
        	// 
        	this.txtCallTarget.Font = new System.Drawing.Font("Consolas", 9F);
        	this.txtCallTarget.Location = new System.Drawing.Point(22, 70);
        	this.txtCallTarget.Name = "txtCallTarget";
        	this.txtCallTarget.Size = new System.Drawing.Size(292, 22);
        	this.txtCallTarget.TabIndex = 2;
        	this.txtCallTarget.TextChanged += new System.EventHandler(this.TxtCallTargetTextChanged);
        	// 
        	// lblModule
        	// 
        	this.lblModule.AutoSize = true;
        	this.lblModule.Location = new System.Drawing.Point(20, 100);
        	this.lblModule.Name = "lblModule";
        	this.lblModule.Size = new System.Drawing.Size(45, 13);
        	this.lblModule.TabIndex = 3;
        	this.lblModule.Text = "Module:";
        	// 
        	// cboModule
        	// 
        	this.cboModule.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        	this.cboModule.FormattingEnabled = true;
        	this.cboModule.Items.AddRange(new object[] {
			"(connect to target first)"});
        	this.cboModule.Location = new System.Drawing.Point(78, 96);
        	this.cboModule.Name = "cboModule";
        	this.cboModule.Size = new System.Drawing.Size(236, 21);
        	this.cboModule.TabIndex = 4;
        	this.cboModule.SelectedIndexChanged += new System.EventHandler(this.CboModuleSelectedIndexChanged);
        	// 
        	// lblApis
        	// 
        	this.lblApis.AutoSize = true;
        	this.lblApis.Location = new System.Drawing.Point(20, 130);
        	this.lblApis.Name = "lblApis";
        	this.lblApis.Size = new System.Drawing.Size(32, 13);
        	this.lblApis.TabIndex = 5;
        	this.lblApis.Text = "APIs:";
        	// 
        	// cboApis
        	// 
        	this.cboApis.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        	this.cboApis.FormattingEnabled = true;
        	this.cboApis.Location = new System.Drawing.Point(78, 126);
        	this.cboApis.Name = "cboApis";
        	this.cboApis.Size = new System.Drawing.Size(236, 21);
        	this.cboApis.TabIndex = 6;
        	this.cboApis.SelectedIndexChanged += new System.EventHandler(this.CboApisSelectedIndexChanged);
        	// 
        	// lblArgs
        	// 
        	this.lblArgs.AutoSize = true;
        	this.lblArgs.Location = new System.Drawing.Point(20, 158);
        	this.lblArgs.Name = "lblArgs";
        	this.lblArgs.Size = new System.Drawing.Size(158, 13);
        	this.lblArgs.TabIndex = 7;
        	this.lblArgs.Text = "Args (decimal / 0xhex / symbol):";
        	// 
        	// txtArgs
        	// 
        	this.txtArgs.Font = new System.Drawing.Font("Consolas", 9F);
        	this.txtArgs.Location = new System.Drawing.Point(22, 176);
        	this.txtArgs.Multiline = true;
        	this.txtArgs.Name = "txtArgs";
        	this.txtArgs.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
        	this.txtArgs.Size = new System.Drawing.Size(292, 96);
        	this.txtArgs.TabIndex = 8;
        	this.txtArgs.Text = "(0,\"MessageBox\",\"Title\",0x0)";
        	this.txtArgs.WordWrap = false;
        	// 
        	// btnAssemble
        	// 
        	this.btnAssemble.Location = new System.Drawing.Point(6, 296);
        	this.btnAssemble.Name = "btnAssemble";
        	this.btnAssemble.Size = new System.Drawing.Size(160, 32);
        	this.btnAssemble.TabIndex = 3;
        	this.btnAssemble.Text = "Validate + Assemble";
        	this.btnAssemble.UseVisualStyleBackColor = true;
        	this.btnAssemble.Click += new System.EventHandler(this.BtnAssembleClick);
        	// 
        	// btnExecute
        	// 
        	this.btnExecute.Location = new System.Drawing.Point(182, 296);
        	this.btnExecute.Name = "btnExecute";
        	this.btnExecute.Size = new System.Drawing.Size(160, 32);
        	this.btnExecute.TabIndex = 4;
        	this.btnExecute.Text = "Execute (send to target)";
        	this.btnExecute.UseVisualStyleBackColor = true;
        	this.btnExecute.Click += new System.EventHandler(this.BtnExecuteClick);
        	// 
        	// btnClearLog
        	// 
        	this.btnClearLog.Location = new System.Drawing.Point(358, 296);
        	this.btnClearLog.Name = "btnClearLog";
        	this.btnClearLog.Size = new System.Drawing.Size(160, 32);
        	this.btnClearLog.TabIndex = 5;
        	this.btnClearLog.Text = "Clear Log";
        	this.btnClearLog.UseVisualStyleBackColor = true;
        	this.btnClearLog.Click += new System.EventHandler(this.MenuLogClearClick);
        	// 
        	// txtLog
        	// 
        	this.txtLog.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(34)))));
        	this.txtLog.ContextMenuStrip = this.logClearMenu;
        	this.txtLog.Dock = System.Windows.Forms.DockStyle.Fill;
        	this.txtLog.Font = new System.Drawing.Font("Consolas", 9F);
        	this.txtLog.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(200)))), ((int)(((byte)(205)))), ((int)(((byte)(210)))));
        	this.txtLog.Location = new System.Drawing.Point(0, 0);
        	this.txtLog.Multiline = true;
        	this.txtLog.Name = "txtLog";
        	this.txtLog.ReadOnly = true;
        	this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
        	this.txtLog.Size = new System.Drawing.Size(540, 284);
        	this.txtLog.TabIndex = 0;
        	this.txtLog.WordWrap = false;
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
        	this.Text = "AsmStudio — Assemble / Validate / Remote Execute";
        	this.menuStrip.ResumeLayout(false);
        	this.menuStrip.PerformLayout();
        	this.tabCloseMenu.ResumeLayout(false);
        	this.logClearMenu.ResumeLayout(false);
        	this.statusStrip.ResumeLayout(false);
        	this.statusStrip.PerformLayout();
        	this.toolbar.ResumeLayout(false);
        	this.toolbar.PerformLayout();
        	this.splitContainerMain.Panel1.ResumeLayout(false);
        	this.splitContainerMain.Panel2.ResumeLayout(false);
        	((System.ComponentModel.ISupportInitialize)(this.splitContainerMain)).EndInit();
        	this.splitContainerMain.ResumeLayout(false);
        	this.splitLeft.Panel1.ResumeLayout(false);
        	this.splitLeft.Panel2.ResumeLayout(false);
        	((System.ComponentModel.ISupportInitialize)(this.splitLeft)).EndInit();
        	this.splitLeft.ResumeLayout(false);
        	this.tabControlResults.ResumeLayout(false);
        	this.tabHex.ResumeLayout(false);
        	this.tabHex.PerformLayout();
        	this.tabErrors.ResumeLayout(false);
        	this.tabRegs.ResumeLayout(false);
        	this.tabRegs.PerformLayout();
        	this.tabMem.ResumeLayout(false);
        	this.tabMem.PerformLayout();
        	this.splitRight.Panel1.ResumeLayout(false);
        	this.splitRight.Panel2.ResumeLayout(false);
        	this.splitRight.Panel2.PerformLayout();
        	((System.ComponentModel.ISupportInitialize)(this.splitRight)).EndInit();
        	this.splitRight.ResumeLayout(false);
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
        private System.Windows.Forms.ToolStripMenuItem menuLanguage;
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
        private System.Windows.Forms.SplitContainer splitContainerMain;
        private System.Windows.Forms.SplitContainer splitLeft;
        private System.Windows.Forms.TabControl tabControlDocs;
        private System.Windows.Forms.SplitContainer splitRight;
        private System.Windows.Forms.TabControl tabControlResults;
        private System.Windows.Forms.TabPage tabHex;
        private System.Windows.Forms.TextBox txtHex;
        private System.Windows.Forms.TabPage tabErrors;
        private System.Windows.Forms.ListBox lstErrors;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.TabPage tabRegs;
        private System.Windows.Forms.TabPage tabMem;
        private System.Windows.Forms.Label lblMemName;
        private System.Windows.Forms.TextBox txtMemName;
        private System.Windows.Forms.RadioButton rbMemText;
        private System.Windows.Forms.RadioButton rbMemQword;
        private System.Windows.Forms.RadioButton rbMemHex;
        private System.Windows.Forms.Label lblMemValue;
        private System.Windows.Forms.TextBox txtMemValue;
        private System.Windows.Forms.Button btnMemAlloc;
        private System.Windows.Forms.Button btnMemFree;
        private System.Windows.Forms.ListView lstMem;
        private System.Windows.Forms.ColumnHeader chName;
        private System.Windows.Forms.ColumnHeader chData;
        private System.Windows.Forms.ColumnHeader chAddr;
        private System.Windows.Forms.TextBox txtRegs;
        private System.Windows.Forms.GroupBox grpSyntax;
        private System.Windows.Forms.RadioButton rbIntel;
        private System.Windows.Forms.RadioButton rbAtt;
        private System.Windows.Forms.GroupBox grpArch;
        private System.Windows.Forms.RadioButton rbX64;
        private System.Windows.Forms.RadioButton rbX86;
        private System.Windows.Forms.GroupBox grpCall;
        private System.Windows.Forms.Label lblModule;
        private System.Windows.Forms.ComboBox cboModule;
        private System.Windows.Forms.Label lblApis;
        private System.Windows.Forms.ComboBox cboApis;
        private System.Windows.Forms.CheckBox chkModeCall;
        private System.Windows.Forms.Label lblCallTarget;
        private System.Windows.Forms.TextBox txtCallTarget;
        private System.Windows.Forms.Label lblArgs;
        private System.Windows.Forms.TextBox txtArgs;
        private System.Windows.Forms.Button btnAssemble;
        private System.Windows.Forms.Button btnExecute;
        private System.Windows.Forms.Button btnClearLog;
        private System.Windows.Forms.ContextMenuStrip logClearMenu;
        private System.Windows.Forms.ToolStripMenuItem logClearMenuItem;
    }
}
