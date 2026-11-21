// SchemePickForm.cs - 方案选择对话框
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using AsmStudio.Schemes;

namespace AsmStudio
{
    internal sealed class SchemePickForm : Form
    {
        private readonly List<SchemeFile> _schemes;
        private readonly ListBox _list;
        private readonly TextBox _desc;
        private readonly Button _btnOk;
        private readonly Button _btnCancel;

        public SchemeFile Selected { get; private set; }

        public SchemePickForm(List<SchemeFile> schemes)
        {
            _schemes = schemes;
            Text = "选择方案";
            Width = 560;
            Height = 440;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            Label lbl = new Label();
            lbl.Text = "方案文件（按顺序执行步骤）:";
            lbl.Location = new System.Drawing.Point(12, 12);
            lbl.AutoSize = true;

            _list = new ListBox();
            _list.Location = new System.Drawing.Point(12, 34);
            _list.Size = new System.Drawing.Size(520, 240);
            _list.Font = new Font("Consolas", 9f);
            foreach (SchemeFile s in schemes)
                _list.Items.Add(s.Name + "  [" + s.Syntax.ToUpperInvariant() + "/" +
                    s.Arch + "] " + s.Steps.Count + " 步" +
                    (string.IsNullOrEmpty(s.Description) ? "" : "  — " + s.Description));
            _list.SelectedIndexChanged += ListSelected;
            _list.DoubleClick += delegate { Accept(); };

            Label lblDesc = new Label();
            lblDesc.Text = "步骤预览:";
            lblDesc.Location = new System.Drawing.Point(12, 282);
            lblDesc.AutoSize = true;

            _desc = new TextBox();
            _desc.Location = new System.Drawing.Point(12, 300);
            _desc.Size = new System.Drawing.Size(520, 60);
            _desc.Multiline = true;
            _desc.ReadOnly = true;
            _desc.ScrollBars = ScrollBars.Vertical;
            _desc.Font = new Font("Consolas", 9f);

            _btnOk = new Button();
            _btnOk.Text = "运行(&R)";
            _btnOk.Location = new System.Drawing.Point(360, 370);
            _btnOk.Size = new System.Drawing.Size(84, 28);
            _btnOk.Click += delegate { Accept(); };

            _btnCancel = new Button();
            _btnCancel.Text = "取消";
            _btnCancel.Location = new System.Drawing.Point(450, 370);
            _btnCancel.Size = new System.Drawing.Size(84, 28);
            _btnCancel.DialogResult = DialogResult.Cancel;

            Controls.Add(lbl);
            Controls.Add(_list);
            Controls.Add(lblDesc);
            Controls.Add(_desc);
            Controls.Add(_btnOk);
            Controls.Add(_btnCancel);
            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        private void ListSelected(object sender, EventArgs e)
        {
            int idx = _list.SelectedIndex;
            if (idx < 0 || idx >= _schemes.Count) { _desc.Text = ""; return; }
            SchemeFile s = _schemes[idx];
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("语法=" + s.Syntax + "  架构=" + s.Arch);
            for (int i = 0; i < s.Steps.Count; i++)
            {
                SchemeStep st = s.Steps[i];
                sb.Append(i + 1).Append(". ");
                sb.Append(string.IsNullOrEmpty(st.Title) ? "(无标题)" : st.Title);
                sb.Append("  [").Append(st.Mode).Append("]");
                if (!string.IsNullOrEmpty(st.Call)) sb.Append(" call=").Append(st.Call);
                if (st.Args != null && st.Args.Length > 0)
                    sb.Append(" args=[").Append(string.Join(",", st.Args)).Append("]");
                sb.AppendLine();
                string code = (st.Code ?? "").Replace("\n", " ; ");
                if (code.Length > 0) sb.AppendLine("     " + code);
            }
            _desc.Text = sb.ToString();
        }

        private void Accept()
        {
            int idx = _list.SelectedIndex;
            if (idx < 0 || idx >= _schemes.Count)
            {
                MessageBox.Show(this, "请选择一个方案。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Selected = _schemes[idx];
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
