// EditorTheme.cs - 编辑器颜色主题（语法高亮配色集中管理）
//
// 职责边界：
//   本文件只集中存放"颜色"定义，供 AsmEditor 高亮使用；
//   关键词名单见 Engine\Lexicon\，着色逻辑在 Controls\AsmEditor.cs。
using System.Drawing;

namespace AsmStudio.Controls
{
    internal static class EditorTheme
    {
        // ---- 编辑器基底 ----
        public static readonly Color EditorBackground = Color.FromArgb(30, 30, 34);
        public static readonly Color EditorForeground = Color.FromArgb(220, 220, 224);

        // ---- 语法着色 ----
        public static readonly Color Mnemonic = Color.FromArgb(86, 156, 214);    // 助记符 - 蓝
        public static readonly Color Register = Color.FromArgb(206, 145, 120);   // 寄存器 - 橙红
        public static readonly Color Number = Color.FromArgb(184, 215, 163);     // 数字 - 浅绿
        public static readonly Color Comment = Color.FromArgb(106, 153, 85);     // 注释 - 绿
        public static readonly Color Label = Color.FromArgb(220, 220, 170);      // 标签/符号 - 淡黄
        public static readonly Color String = Color.FromArgb(206, 145, 120);     // 字符串 - 橙红
        public static readonly Color Bracket = Color.FromArgb(180, 180, 200);    // 括号 - 灰紫
        public static readonly Color SizeSpecifier = Color.FromArgb(78, 201, 176); // 尺寸声明/ptr - 青绿

        // ---- 行号槽 ----
        public static readonly Color GutterBackground = Color.FromArgb(245, 246, 248);
        public static readonly Color GutterText = Color.FromArgb(120, 124, 130);
        public static readonly Color GutterError = Color.FromArgb(230, 90, 90);
        public static readonly Color GutterBorder = Color.FromArgb(200, 200, 205);
    }
}
