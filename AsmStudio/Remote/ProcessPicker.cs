// ProcessPicker.cs - 拖拽取进程：光标处窗口 → 顶层窗口 → pid / 类名 / 标题
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AsmStudio.Remote
{
    /// <summary>光标下命中的窗口信息</summary>
    internal sealed class WindowTarget
    {
        public IntPtr Handle;              // WindowFromPoint 直接返回的（可能是子窗口）
        public IntPtr Root;                // GetAncestor(GA_ROOT) 之后的顶层窗口
        public uint Pid;
        public string ClassName = "";
        public string Title = "";

        public bool IsValid
        {
            get { return Root != IntPtr.Zero && Pid != 0; }
        }

        /// <summary>提示窗里的一行摘要</summary>
        public string Summary()
        {
            string title = Title;
            if (title.Length > 48) title = title.Substring(0, 45) + "...";
            return string.Format("pid {0}  {1}\r\n{2}", Pid, ClassName,
                title.Length > 0 ? title : "(no title)");
        }
    }

    internal static class ProcessPicker
    {
        private const string CursorResource = "AsmStudio.Resources.zz.cur";
        private const string ImageResource = "AsmStudio.Resources.pick.png";

        private static Cursor _pickerCursor;
        private static bool _cursorTried;       // 已缓存结果，不必再解析
        private static bool _cursorResolving;   // 重入闸：CursorReport / PickerCursor 都会进来
        private static string _cursorReport = "(cursor not resolved yet)";

        /// <summary>
        /// 光标是怎么来的，用于日志。跑一次就知道走的是哪条路：
        /// "memory" = 内存合成（正常）；"tempfile" = 退回临时文件加载；"system" = 回退系统十字。
        /// </summary>
        public static string CursorReport
        {
            get { EnsureCursor(); return _cursorReport; }
        }

        /// <summary>拖拽时使用的准星光标（嵌入 zz.cur；任何环节失败都回退系统十字准星）</summary>
        public static Cursor PickerCursor
        {
            get
            {
                EnsureCursor();
                return _pickerCursor != null ? _pickerCursor : Cursors.Cross;
            }
        }

        private static void EnsureCursor()
        {
            if (_cursorTried || _cursorResolving) return;
            _cursorResolving = true;
            try { ResolveCursor(); }
            finally
            {
                _cursorResolving = false;
                /* 只有真拿到自定义光标才缓存：整条链都失败回退系统十字时留个口子，
                 * 下次再试（GDI/资源在启动早期偶发不可用，不该被一次性判死刑）。 */
                _cursorTried = _pickerCursor != null;
            }
        }

        private static void ResolveCursor()
        {
            byte[] raw = null;
            try
            {
                using (Stream s = typeof(ProcessPicker).Assembly.GetManifestResourceStream(CursorResource))
                {
                    if (s != null)
                    {
                        raw = new byte[s.Length];
                        int read = 0;
                        while (read < raw.Length)
                        {
                            int n = s.Read(raw, read, raw.Length - read);
                            if (n <= 0) break;
                            read += n;
                        }
                    }
                }
            }
            catch
            {
                raw = null;
            }

            string why = "";
            if (raw != null && TryCreateFromMemory(raw, out _pickerCursor, out why))
            {
                _cursorReport = "memory (embedded zz.cur " + why + ")";
                return;
            }

            string memWhy = raw == null ? "embedded resource missing" : why;
            if (raw != null && TryCreateFromTempFile(raw, out _pickerCursor, out why))
            {
                _cursorReport = "tempfile (memory path failed: " + memWhy + "; loaded " + why + ")";
                return;
            }

            _pickerCursor = null;
            _cursorReport = "system cross (memory: " + memWhy + "; tempfile: " + why + ")";
        }

        /// <summary>
        /// 直接在内存里把 .cur 合成光标：CreateDIBSection 装 32bpp 颜色平面（保留 alpha）、
        /// CreateBitmap 装 1bpp AND 掩码、CreateIconIndirect 带 hotspot 生成。
        /// 全程不落盘，所以提权/非提权、杀软、%TEMP% 被重定向都不影响结果。
        /// </summary>
        private static bool TryCreateFromMemory(byte[] raw, out Cursor cursor, out string detail)
        {
            cursor = null;
            detail = "";
            IntPtr hbmColor = IntPtr.Zero, hbmMask = IntPtr.Zero;
            try
            {
                if (raw.Length < 22)
                {
                    detail = "resource too small (" + raw.Length + " bytes)";
                    return false;
                }
                if (BitConverter.ToUInt16(raw, 0) != 0 || BitConverter.ToUInt16(raw, 2) != 2)
                {
                    detail = "not a .cur (ICONDIR type != 2)";
                    return false;
                }
                int count = BitConverter.ToUInt16(raw, 4);
                if (count < 1)
                {
                    detail = "empty ICONDIR";
                    return false;
                }

                const int entry = 6;                                  // 取第一张图
                int declW = raw[entry] == 0 ? 256 : raw[entry];
                int declH = raw[entry + 1] == 0 ? 256 : raw[entry + 1];
                int hotX = BitConverter.ToUInt16(raw, entry + 4);
                int hotY = BitConverter.ToUInt16(raw, entry + 6);
                int imgBytes = BitConverter.ToInt32(raw, entry + 8);
                int imgOff = BitConverter.ToInt32(raw, entry + 12);
                if (imgOff <= 0 || imgBytes <= 0 || imgOff + imgBytes > raw.Length)
                {
                    detail = "bad image offset/length";
                    return false;
                }

                int biSize = BitConverter.ToInt32(raw, imgOff);
                if (biSize < 40)
                {
                    detail = "unsupported DIB header (" + biSize + ")";
                    return false;
                }
                int bmpW = BitConverter.ToInt32(raw, imgOff + 4);
                int bmpH = BitConverter.ToInt32(raw, imgOff + 8);
                int bpp = BitConverter.ToUInt16(raw, imgOff + 14);
                if (bpp != 32)
                {
                    detail = bpp + "bpp not supported (only 32bpp)";
                    return false;
                }

                int w = bmpW > 0 ? bmpW : declW;
                if (bmpH <= 0)
                {
                    detail = "top-down cursor DIB not supported";
                    return false;
                }
                int h = bmpH / 2;                                     // 正高度 = XOR/AND 上下堆叠
                if (w <= 0 || h <= 0 || h * 2 != bmpH)
                {
                    detail = "bad cursor DIB size";
                    return false;
                }

                int xorStride = ((w * 32 + 31) / 32) * 4;
                int andStride = ((w + 31) / 32) * 4;
                int xorSize = xorStride * h;
                int andSize = andStride * h;
                if (imgOff + 40 + xorSize + andSize > raw.Length)
                {
                    detail = "truncated DIB";
                    return false;
                }

                NativeInterop.BitmapInfo bmi = new NativeInterop.BitmapInfo();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(NativeInterop.BitmapInfoHeader));
                bmi.bmiHeader.biWidth = w;
                bmi.bmiHeader.biHeight = h;      // 正数=自底向上，与 .cur 的行序一致，可整块拷贝
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0; // BI_RGB
                IntPtr bits;
                hbmColor = NativeInterop.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out bits, IntPtr.Zero, 0);
                if (hbmColor == IntPtr.Zero)
                {
                    detail = "CreateDIBSection failed (" + Marshal.GetLastWin32Error() + ")";
                    return false;
                }
                Marshal.Copy(raw, imgOff + 40, bits, xorSize);

                /* CreateBitmap 的 1bpp 数据行按 WORD 对齐，.cur 的 AND 平面按 DWORD 对齐，w=32 时
                 * 恰好相同、其它宽度需要重打包，这里统一按 WORD 行拷，避免个别尺寸下掩码错行。 */
                int maskRow = ((w + 15) / 16) * 2;
                byte[] maskBits = new byte[maskRow * h];
                for (int y = 0; y < h; y++)
                {
                    int src = imgOff + 40 + xorSize + y * andStride;
                    Buffer.BlockCopy(raw, src, maskBits, y * maskRow, Math.Min(andStride, maskRow));
                }
                hbmMask = NativeInterop.CreateBitmap(w, h, 1, 1, maskBits);
                if (hbmMask == IntPtr.Zero)
                {
                    detail = "CreateBitmap failed (" + Marshal.GetLastWin32Error() + ")";
                    return false;
                }

                NativeInterop.IconInfo ii = new NativeInterop.IconInfo();
                ii.fIcon = 0;                    // 0 = 光标（保留 hotspot）
                ii.xHotspot = hotX;
                ii.yHotspot = hotY;
                ii.hbmColor = hbmColor;
                ii.hbmMask = hbmMask;
                if (hotX >= w || hotY >= h)      // 越界 hotspot 会让系统点击点漂移，夹回合法范围
                {
                    ii.xHotspot = Math.Min(Math.Max(hotX, 0), w - 1);
                    ii.yHotspot = Math.Min(Math.Max(hotY, 0), h - 1);
                }

                IntPtr handle = NativeInterop.CreateIconIndirect(ref ii);
                if (handle == IntPtr.Zero)
                {
                    detail = "CreateIconIndirect failed (" + Marshal.GetLastWin32Error() + ")";
                    return false;
                }

                cursor = new Cursor(handle);
                detail = string.Format("{0}x{1} hotspot({2},{3})", w, h, ii.xHotspot, ii.yHotspot);
                return true;
            }
            catch (Exception ex)
            {
                detail = ex.GetType().Name + ": " + ex.Message;
                cursor = null;
                return false;
            }
            finally
            {
                /* CreateIconIndirect 会复制位图，句柄由调用方释放 */
                if (hbmColor != IntPtr.Zero) NativeInterop.DeleteObject(hbmColor);
                if (hbmMask != IntPtr.Zero) NativeInterop.DeleteObject(hbmMask);
            }
        }

        /// <summary>兜底：落临时文件后交给 LoadCursorFromFile（依赖磁盘与临时目录可用性）</summary>
        private static bool TryCreateFromTempFile(byte[] raw, out Cursor cursor, out string detail)
        {
            cursor = null;
            detail = "";
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "asmstudio_picker.cur");
                File.WriteAllBytes(tmp, raw);
                IntPtr h = NativeInterop.LoadCursorFromFileW(tmp);
                if (h == IntPtr.Zero)
                {
                    detail = "LoadCursorFromFile failed (" + Marshal.GetLastWin32Error() + ")";
                    return false;
                }
                cursor = new Cursor(h);
                detail = tmp;
                return true;
            }
            catch (Exception ex)
            {
                detail = ex.GetType().Name + ": " + ex.Message;
                cursor = null;
                return false;
            }
        }

        /// <summary>工具栏图标（嵌入 pick.png，缩放到 16×16 以适配 ToolStrip）</summary>
        public static Image LoadPickImage()
        {
            try
            {
                using (Stream s = typeof(ProcessPicker).Assembly
                    .GetManifestResourceStream(ImageResource))
                {
                    if (s == null) return null;
                    using (Image src = Image.FromStream(s))
                        return new Bitmap(src, new Size(16, 16));
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>查询屏幕坐标处的窗口。没有有效窗口时返回 IsValid=false 的实例（不返回 null）</summary>
        public static WindowTarget Query(Point screenPt)
        {
            WindowTarget t = new WindowTarget();
            NativeInterop.POINT pt = new NativeInterop.POINT(screenPt.X, screenPt.Y);
            IntPtr h = NativeInterop.WindowFromPoint(pt);
            t.Handle = h;
            if (h == IntPtr.Zero) return t;

            // 子窗口（按钮、编辑框…）要一路提到顶层，用户想选的是它所属的进程窗口
            t.Root = NativeInterop.GetAncestor(h, NativeInterop.GA_ROOT);
            if (t.Root == IntPtr.Zero) t.Root = h;

            uint pid;
            NativeInterop.GetWindowThreadProcessId(t.Root, out pid);
            t.Pid = pid;

            StringBuilder sb = new StringBuilder(260);
            if (NativeInterop.GetClassNameW(t.Root, sb, sb.Capacity) > 0)
                t.ClassName = sb.ToString();
            sb.Length = 0;
            if (NativeInterop.GetWindowTextW(t.Root, sb, sb.Capacity) > 0)
                t.Title = sb.ToString();
            return t;
        }
    }
}
