// PickHintForm.cs - 拖拽选取时的跟随提示窗（不抢焦点、不进 Alt+Tab、不挡取点）
using System;
using System.Drawing;
using System.Windows.Forms;

namespace AsmStudio.Controls
{
    internal sealed class PickHintForm : Form
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TRANSPARENT = 0x00000020;   // 鼠标穿透：别把自己算进 WindowFromPoint

        private readonly Label _label;

        public PickHintForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(32, 32, 32);

            _label = new Label();
            _label.AutoSize = true;
            _label.BackColor = Color.Transparent;
            _label.ForeColor = Color.Gainsboro;
            _label.Font = new Font("Consolas", 9f);
            _label.Padding = new Padding(8, 6, 8, 6);
            Controls.Add(_label);
        }

        /// <summary>显示时不激活自身，否则拖拽期间主窗口会被抢焦点</summary>
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
                return cp;
            }
        }

        /// <summary>更新文本并跟到光标右下侧；贴近屏幕边缘时自动翻到另一侧</summary>
        public void ShowHint(string text, Point cursorScreen)
        {
            if (!string.Equals(_label.Text, text, StringComparison.Ordinal))
            {
                _label.Text = text;
                ClientSize = _label.PreferredSize;
            }
            if (!Visible) Show();

            Rectangle wa = Screen.FromPoint(cursorScreen).WorkingArea;
            int x = cursorScreen.X + 18;
            int y = cursorScreen.Y + 22;
            if (x + Width > wa.Right) x = cursorScreen.X - Width - 12;
            if (y + Height > wa.Bottom) y = cursorScreen.Y - Height - 12;
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;
            Location = new Point(x, y);
        }

        public void HideHint()
        {
            if (Visible) Hide();
        }
    }
}
