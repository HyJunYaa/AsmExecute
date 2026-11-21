// AsmEditor.cs - 汇编语法高亮编辑控件（RichTextBox + 行号槽 + 错误标记）
// Intel/AT&T 双着色；高亮采用延迟防抖。
// 高亮期间通过 WM_SETREDRAW/EM_SETEVENTMASK 抑制重绘与事件，避免闪烁与"全选"可见。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AsmStudio.Engine;

namespace AsmStudio.Controls
{
    internal sealed class AsmEditor : UserControl
    {
        private readonly RichTextBox _rtb;
        private readonly Panel _gutter;
        private readonly Timer _timer;
        private SyntaxKindView _syntax = SyntaxKindView.Intel;
        private readonly HashSet<int> _errorLines = new HashSet<int>();
        private bool _dirty;
        private bool _highlighting;            // 防止 TextChanged 递归触发
        private string _lastHighlighted;       // 内容未变时跳过重着色
        private Font _gutterFont;

        // ---- 快照式撤销/重做 ----
        // RichTextBox 原生撤销栈会被高亮的格式化操作（SelectionColor）污染：
        // 每次着色压入几十条"颜色"撤销记录，Ctrl+Z 先回退颜色而非文本，
        // 且很快耗尽撤销上限挤掉文本编辑历史。
        // 因此撤销/重做改为自管理：只记录 文本+选区 快照，着色不入栈。
        private struct UndoState
        {
            public string Text;
            public int SelStart;
            public int SelLength;
        }
        private const int MaxUndoEntries = 200;
        private readonly List<UndoState> _undoStack = new List<UndoState>();
        private readonly List<UndoState> _redoStack = new List<UndoState>();
        private UndoState _lastSnapshot;       // 最近一次事件后的稳定状态（本组输入前的现场）
        private UndoState _pendingBefore;      // 当前打字组开始前的状态（组结束时入栈）
        private bool _burstPending;            // 打字组进行中（连续输入合并为一条撤销记录）

        // ---- Win32: 抑制重绘/事件、保存滚动位置 ----
        private const int WM_SETREDRAW = 0x000B;
        private const int WM_VSCROLL = 0x0115;
        private const int EM_SETEVENTMASK = 0x0431;
        private const int EM_GETEVENTMASK = 0x0435;
        private const int EM_GETSCROLLPOS = 0x0400 + 221;   // WM_USER+221
        private const int EM_SETSCROLLPOS = 0x0400 + 222;   // WM_USER+222

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);

        public enum SyntaxKindView { Intel, Att }

        public event EventHandler TextChangedDelayed;
        public event EventHandler CaretInfoChanged;

        public AsmEditor()
        {
            _gutter = new Panel();
            _gutter.Dock = DockStyle.Left;
            _gutter.Width = 52;
            _gutter.BackColor = EditorTheme.GutterBackground;
            _gutter.Paint += GutterPaint;

            HighlightedTextBox rtb = new HighlightedTextBox();
            rtb.Owner = this;
            _rtb = rtb;
            _rtb.Dock = DockStyle.Fill;
            _rtb.Font = new Font("Consolas", 10f);
            _rtb.BackColor = EditorTheme.EditorBackground;
            _rtb.ForeColor = EditorTheme.EditorForeground;
            _rtb.AcceptsTab = true;
            _rtb.WordWrap = false;
            _rtb.HideSelection = true;          // 高亮过程不外显
            _rtb.TextChanged += RtbTextChanged;
            _rtb.VScroll += delegate { _gutter.Invalidate(); };
            _rtb.SelectionChanged += delegate
            {
                if (CaretInfoChanged != null) CaretInfoChanged(this, EventArgs.Empty);
            };
            // 安全网：控件重新可见时确保重绘已开启（防止单一标签停留在"重绘关闭"白屏态）
            _rtb.VisibleChanged += delegate
            {
                if (_rtb.Visible && _rtb.IsHandleCreated)
                {
                    SendMessage(_rtb.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    _rtb.Invalidate();
                }
            };

            Controls.Add(_rtb);
            Controls.Add(_gutter);

            _timer = new Timer();
            _timer.Interval = 300;
            _timer.Tick += TimerTick;

            _gutterFont = new Font("Consolas", 8.5f);
            _lastSnapshot.Text = "";
            _lastSnapshot.SelStart = 0;
            _lastSnapshot.SelLength = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _gutterFont.Dispose();
            }
            base.Dispose(disposing);
        }

        public SyntaxKindView Syntax
        {
            get { return _syntax; }
            set
            {
                if (_syntax != value)
                {
                    _syntax = value;
                    _lastHighlighted = null;    // 语法切换强制重着色
                    _dirty = true;
                    _timer.Stop();
                    _timer.Start();
                }
            }
        }

        public override string Text
        {
            get { return _rtb.Text; }
            set
            {
                // 程序化替换（打开文件/语法转换）：旧内容入撤销栈，Ctrl+Z 可还原本次替换
                if (!string.Equals(_rtb.Text, value))
                {
                    CommitPending();
                    if (_rtb.Text.Length > 0) PushUndo(CurrentSnapshot());
                    _redoStack.Clear();
                }
                ReplaceTextAtomically(value, 0, 0);
                _burstPending = false;
                _lastSnapshot = CurrentSnapshot();
                _dirty = true;
                _timer.Stop();
                _timer.Start();
            }
        }

        public Font EditorFont
        {
            get { return _rtb.Font; }
            set { _rtb.Font = value; }
        }

        /// <summary>标记错误行（1 起）</summary>
        public void SetErrorLines(IEnumerable<int> lines)
        {
            _errorLines.Clear();
            foreach (int l in lines)
            {
                if (l > 0) _errorLines.Add(l);
            }
            _gutter.Invalidate();
        }

        public int CaretLine
        {
            get
            {
                return _rtb.GetLineFromCharIndex(_rtb.SelectionStart) + 1;
            }
        }

        public int CaretColumn
        {
            get
            {
                int pos = _rtb.SelectionStart;
                int line = _rtb.GetLineFromCharIndex(pos);
                int lineStart = _rtb.GetFirstCharIndexFromLine(line);
                return pos - lineStart + 1;
            }
        }

        /// <summary>定位光标到指定字符位置（供错误跳转）</summary>
        public void SelectAt(int start, int length)
        {
            _rtb.Select(start, length);
            _rtb.Focus();
        }

        // ---- 撤回 / 重做（自管理快照栈） ----
        public bool CanUndo
        {
            get { return _burstPending || _undoStack.Count > 0; }
        }

        public bool CanRedo
        {
            get { return _redoStack.Count > 0; }
        }

        public void Undo()
        {
            CommitPending();               // 打字组未闭合时先入栈（如：输入后立刻 Ctrl+Z）
            if (_undoStack.Count == 0) return;
            UndoState cur = CurrentSnapshot();
            UndoState st = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _redoStack.Add(cur);
            ApplyState(st);
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;
            UndoState cur = CurrentSnapshot();
            UndoState st = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _undoStack.Add(cur);
            ApplyState(st);
        }

        /// <summary>把当前打字组（若有）落为一条撤销记录</summary>
        private void CommitPending()
        {
            if (!_burstPending) return;
            _burstPending = false;
            PushUndo(_pendingBefore);
        }

        private void PushUndo(UndoState st)
        {
            _undoStack.Add(st);
            if (_undoStack.Count > MaxUndoEntries)
                _undoStack.RemoveAt(0);
        }

        private UndoState CurrentSnapshot()
        {
            UndoState s;
            s.Text = _rtb.Text;
            s.SelStart = _rtb.SelectionStart;
            s.SelLength = _rtb.SelectionLength;
            return s;
        }

        /// <summary>
        /// 原子整段替换（撤销回放 / 程序化赋值共用）：
        /// 抑制重绘与事件 → 换文本 → 恢复滚动/选区 → 同步重着色 → 一次性重绘。
        /// 避免"先渲染无色文本、300ms 防抖后才着色"的两段式闪烁。
        /// </summary>
        private void ReplaceTextAtomically(string value, int selStart, int selLen)
        {
            bool handle = _rtb.IsHandleCreated;
            _highlighting = true;      // 着色/入栈保护（结束后 _lastHighlighted 缓存已更新）
            try
            {
                int oldMask = 0;
                if (handle)
                {
                    SendMessage(_rtb.Handle, WM_SETREDRAW, (IntPtr)0, IntPtr.Zero);
                    oldMask = (int)SendMessage(_rtb.Handle, EM_GETEVENTMASK, IntPtr.Zero, IntPtr.Zero);
                    SendMessage(_rtb.Handle, EM_SETEVENTMASK, IntPtr.Zero, (IntPtr)0);
                }
                try
                {
                    Point scroll = new Point(0, 0);
                    if (handle)
                        SendMessage(_rtb.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref scroll);

                    _rtb.Text = value;

                    if (handle)
                        SendMessage(_rtb.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref scroll);

                    selStart = Math.Min(selStart, value.Length);
                    selLen = Math.Min(selLen, value.Length - selStart);
                    _rtb.Select(selStart, selLen);

                    HighlightCore();   // 同步着色（重绘仍关闭，用户看不到中间态）
                }
                finally
                {
                    if (handle)
                        SendMessage(_rtb.Handle, EM_SETEVENTMASK, IntPtr.Zero, (IntPtr)oldMask);
                }
            }
            finally
            {
                _highlighting = false;
                if (handle)
                {
                    SendMessage(_rtb.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    _rtb.Invalidate();
                }
            }
        }

        /// <summary>回放一个快照（撤销/重做共用）：原子换文本 + 同步重着色，无闪烁</summary>
        private void ApplyState(UndoState st)
        {
            ReplaceTextAtomically(st.Text, st.SelStart, st.SelLength);
            _burstPending = false;
            _lastSnapshot = st;
            _dirty = true;             // 仍走防抖：TimerTick 会跳过已缓存着色并触发校验刷新
            _timer.Stop();
            _timer.Start();
            if (!_rtb.Focused) _rtb.Focus();
        }

        private void RtbTextChanged(object sender, EventArgs e)
        {
            if (_highlighting) return;      // 着色过程内部修改不入撤销栈
            if (!_burstPending)
            {
                _pendingBefore = _lastSnapshot;   // 上一个稳定状态 = 本组输入前的现场
                _burstPending = true;
                _redoStack.Clear();               // 新编辑使 redo 链失效
            }
            _lastSnapshot = CurrentSnapshot();
            _dirty = true;
            _timer.Stop();
            _timer.Start();
            if (CaretInfoChanged != null) CaretInfoChanged(this, EventArgs.Empty);
            _gutter.Invalidate();
        }

        private void TimerTick(object sender, EventArgs e)
        {
            _timer.Stop();
            if (_dirty)
            {
                _dirty = false;
                CommitPending();               // 连续输入结束（300ms 无输入）→ 合并为一条撤销记录
                HighlightNow();
                if (TextChangedDelayed != null) TextChangedDelayed(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 重着色入口（防抖定时器调用）。内容与语法均未变化时跳过。
        /// 期间抑制重绘与事件，保存/恢复光标选区与滚动位置 —— 用户看不到中间的 SelectAll 过程。
        /// 结构保证：重绘关闭后，任何异常路径都会在 finally 中恢复重绘。
        /// </summary>
        private void HighlightNow()
        {
            if (_rtb.Text == _lastHighlighted && _syntax == _lastSyntax) return;

            _highlighting = true;
            try
            {
                SendMessage(_rtb.Handle, WM_SETREDRAW, (IntPtr)0, IntPtr.Zero);
                try
                {
                    HighlightCore();
                }
                finally
                {
                    // 任何路径都必须恢复重绘
                    SendMessage(_rtb.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    _rtb.Invalidate();
                }
            }
            finally
            {
                _highlighting = false;
            }
        }

        /// <summary>着色核心：调用方负责抑制重绘；本方法抑制事件并保存/恢复选区与滚动。</summary>
        private void HighlightCore()
        {
            string text = _rtb.Text;
            // 超长文本跳过着色（防止卡顿），只上底色
            bool colorize = text.Length <= 256 * 1024;

            int oldMask = (int)SendMessage(_rtb.Handle, EM_GETEVENTMASK, IntPtr.Zero, IntPtr.Zero);
            SendMessage(_rtb.Handle, EM_SETEVENTMASK, IntPtr.Zero, (IntPtr)0);
            try
            {
                // 保存光标/选区/滚动位置
                int sel = _rtb.SelectionStart;
                int selLen = _rtb.SelectionLength;
                Point scroll = new Point(0, 0);
                SendMessage(_rtb.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref scroll);

                // 着色
                _rtb.SelectAll();
                _rtb.SelectionColor = EditorTheme.EditorForeground;
                _rtb.SelectionBackColor = EditorTheme.EditorBackground;

                if (colorize)
                {
                    foreach (Token tk in Tokenize(text))
                    {
                        _rtb.Select(tk.Start, tk.Length);
                        _rtb.SelectionColor = tk.Color;
                    }
                }

                // 恢复光标/选区/滚动位置
                // 注意：SelectionStart 的 setter 会保留原选区终点（EM_SETSEL(start, 旧终点)），
                // 着色刚 SelectAll/选过 token，直接赋值会把"光标→旧终点"整段选中，
                // 导致下次输入覆盖所选文本 —— 必须用 Select(sel, 0) 显式塌缩为光标。
                if (selLen > 0)
                    _rtb.Select(sel, selLen);
                else
                    _rtb.Select(sel, 0);
                SendMessage(_rtb.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref scroll);

                _lastHighlighted = text;
                _lastSyntax = _syntax;
            }
            finally
            {
                SendMessage(_rtb.Handle, EM_SETEVENTMASK, IntPtr.Zero, (IntPtr)oldMask);
            }
        }

        private SyntaxKindView _lastSyntax = SyntaxKindView.Intel;

        // ---- 词法着色 ----
        private struct Token
        {
            public int Start;
            public int Length;
            public Color Color;
        }

        private IEnumerable<Token> Tokenize(string text)
        {
            bool isAtt = _syntax == SyntaxKindView.Att;
            List<Token> tokens = new List<Token>();
            int i = 0;
            int n = text.Length;
            while (i < n)
            {
                char c = text[i];
                // 注释
                if (c == ';' || c == '#')
                {
                    int j = i;
                    while (j < n && text[j] != '\n') j++;
                    tokens.Add(Make(i, j - i, EditorTheme.Comment));
                    i = j;
                    continue;
                }
                if (c == '/' && i + 1 < n && text[i + 1] == '/')
                {
                    int j = i;
                    while (j < n && text[j] != '\n') j++;
                    tokens.Add(Make(i, j - i, EditorTheme.Comment));
                    i = j;
                    continue;
                }
                // 字符串
                if (c == '"' || c == '\'')
                {
                    char q = c;
                    int j = i + 1;
                    while (j < n && text[j] != q && text[j] != '\n')
                    {
                        if (text[j] == '\\' && j + 1 < n) j++;
                        j++;
                    }
                    if (j < n && text[j] == q) j++;
                    tokens.Add(Make(i, j - i, EditorTheme.String));
                    i = j;
                    continue;
                }
                // 括号 + AT&T 寄存器/符号前缀 %
                if (c == '[' || c == ']' || c == '(' || c == ')' || (isAtt && c == '%'))
                {
                    tokens.Add(Make(i, 1, EditorTheme.Bracket));
                    i++;
                    continue;
                }
                // 标识符
                if (char.IsLetter(c) || c == '_' || c == '.' || c == '$')
                {
                    int j = i;
                    while (j < n && (char.IsLetterOrDigit(text[j]) || text[j] == '_' ||
                        text[j] == '.' || text[j] == '$' || text[j] == '@')) j++;
                    string word = text.Substring(i, j - i);
                    int k = j;
                    while (k < n && (text[k] == ' ' || text[k] == '\t')) k++;
                    bool isLabelDef = k < n && text[k] == ':';
                    if (RegisterLexicon.IsSegmentKeyword(word))
                        tokens.Add(Make(i, j - i, EditorTheme.Register));   // 段寄存器（ds: 前缀不误判为标签）
                    else if (isLabelDef)
                        tokens.Add(Make(i, j - i, EditorTheme.Label));
                    else if (IsRegister(word))
                        tokens.Add(Make(i, j - i, EditorTheme.Register));
                    else if (isAtt ? IsMnemonicAtt(word) : InstructionLexicon.IsMnemonicKeyword(word))
                        tokens.Add(Make(i, j - i, EditorTheme.Mnemonic));
                    else if (RegisterLexicon.IsSizeSpecifierWord(word))
                        tokens.Add(Make(i, j - i, EditorTheme.SizeSpecifier));  // byte/word/dword/qword/ptr
                    i = j;
                    continue;
                }
                // 数字（含 AT&T $imm）
                if (char.IsDigit(c) || (c == '$' && i + 1 < n))
                {
                    int j = c == '$' ? i + 1 : i;
                    bool isNum = c != '$';
                    if (c == '$')
                    {
                        char nc = text[j];
                        isNum = char.IsDigit(nc) || nc == '-';
                    }
                    if (isNum)
                    {
                        while (j < n && (char.IsLetterOrDigit(text[j]) || text[j] == '-'))
                            j++;
                        tokens.Add(Make(i, j - i, EditorTheme.Number));
                    }
                    else
                    {
                        // $label → 符号着色
                        while (j < n && (char.IsLetterOrDigit(text[j]) || text[j] == '_' ||
                            text[j] == '.' || text[j] == '$')) j++;
                        tokens.Add(Make(i, j - i, EditorTheme.Label));
                    }
                    i = j;
                    continue;
                }
                i++;
            }
            return tokens;
        }

        /// <summary>AT&T 助记符判定：词库命中，或剥尺寸后缀（movq→mov、movzbl→movzb 前缀）后命中</summary>
        private static bool IsMnemonicAtt(string word)
        {
            string w = word.ToLowerInvariant();
            if (InstructionLexicon.IsMnemonicKeyword(w)) return true;
            if (w.Length <= 2) return false;
            char last = w[w.Length - 1];
            if (last != 'b' && last != 'w' && last != 'l' && last != 'q') return false;
            string stem = w.Substring(0, w.Length - 1);
            if (InstructionLexicon.IsMnemonicKeyword(stem)) return true;   // movq→mov / xorl→xor
            if (stem.Length > 3 && (stem.StartsWith("movz") || stem.StartsWith("movs")))
            {
                string stem2 = stem.Substring(0, stem.Length - 1);         // movzbl→movzb
                if (stem2 == "movz" || stem2 == "movs") return true;
                if (InstructionLexicon.IsMnemonicKeyword(stem2)) return true;
            }
            return false;
        }

        private static bool IsRegister(string word)
        {
            string w = word.ToLowerInvariant();
            if (word.Length > 0 && word[0] == '%') w = w.Substring(1);
            return RegisterLexicon.IsRegisterKeyword(w);
        }

        private static Token Make(int start, int len, Color color)
        {
            Token t;
            t.Start = start;
            t.Length = len;
            t.Color = color;
            return t;
        }

            /// <summary>
        /// 拦截 Ctrl+Z / Ctrl+Y 改走自管理撤销栈：
        /// RichEdit 原生撤销栈被着色的格式化操作污染，直接放行会先回退颜色而非文本。
        /// ProcessCmdKey 在消息到达原生控件前被调用，返回 true 即吞掉按键。
        /// </summary>
        private sealed class HighlightedTextBox : RichTextBox
        {
            public AsmEditor Owner;

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                Keys mods = keyData & Keys.Modifiers;
                Keys key = keyData & Keys.KeyCode;
                bool ctrl = (mods & Keys.Control) != 0;
                if (ctrl && key == Keys.Z && (mods & Keys.Shift) == 0)
                {
                    Owner.Undo();
                    return true;
                }
                if (ctrl && (key == Keys.Y || (key == Keys.Z && (mods & Keys.Shift) != 0)))
                {
                    Owner.Redo();
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }
        }

    // ---- 行号槽 ----
        private void GutterPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(_gutter.BackColor);

            // RichTextBox 行高一致 → 用字体高度
            int lineHeight = _rtb.Font.Height;
            int firstLine = _rtb.GetLineFromCharIndex(
                _rtb.GetCharIndexFromPosition(new Point(0, 0)));
            int totalLines = _rtb.GetLineFromCharIndex(_rtb.TextLength) + 1;
            int visibleLines = (_gutter.Height / lineHeight) + 1;

            for (int i = 0; i < visibleLines; i++)
            {
                int line = firstLine + i;
                if (line >= totalLines && totalLines > 0) break;
                int y = i * lineHeight + 2;
                bool isErr = _errorLines.Contains(line + 1);
                if (isErr)
                {
                    using (SolidBrush b = new SolidBrush(EditorTheme.GutterError))
                    {
                        g.FillEllipse(b, 2, y + lineHeight / 2 - 4, 8, 8);
                    }
                }
                using (SolidBrush b = new SolidBrush(
                    isErr ? EditorTheme.GutterError : EditorTheme.GutterText))
                {
                    g.DrawString((line + 1).ToString(), _gutterFont, b, 14f, (float)y);
                }
            }

            using (Pen p = new Pen(EditorTheme.GutterBorder))
                g.DrawLine(p, _gutter.Width - 1, 0, _gutter.Width - 1, _gutter.Height);
        }
    }
}
