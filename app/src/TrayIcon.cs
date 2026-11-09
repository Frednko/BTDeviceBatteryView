using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace BatteryBar
{
    /// <summary>托盘图标渲染与多尺寸 ICO 生成。</summary>
    internal static class TrayIconFactory
    {
        private static readonly int[] Sizes = new int[] { 16, 20, 24, 32, 40, 48, 64 };
        private static readonly List<Icon> KeepAlive = new List<Icon>();

        /// <summary>任务栏电量图标。style: 0 = 电池, 1 = 数字。</summary>
        public static Icon Render(int level, int style, bool online, bool charging)
        {
            List<Bitmap> frames = new List<Bitmap>();
            foreach (int size in Sizes) frames.Add(RenderBitmap(level, style, online, size, charging));
            Icon icon = BuildIco(frames);
            foreach (Bitmap b in frames) b.Dispose();
            return icon;
        }

        /// <summary>单个尺寸的真彩位图（供预览与图标打包复用）。</summary>
        public static Bitmap RenderBitmap(int level, int style, bool online, int size, bool charging)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = size <= 20 ? SmoothingMode.None : SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                bool known = level >= 0 && level <= 100;
                Color color = known ? Theme.ForLevel(level) : Theme.InkMuted;
                if (!online) color = Color.FromArgb(140, color.R, color.G, color.B);
                if (style == 1)
                {
                    DrawDigits(g, size, level, color, known);
                    if (charging) DrawBolt(g, new RectangleF(size * 0.62f, 0, size * 0.38f, size * 0.42f), color, size);
                }
                else
                {
                    DrawBatteryGlyph(g, size, charging ? -1 : (known ? level : -1), color);
                    if (charging) DrawBolt(g, new RectangleF(size * 0.30f, size * 0.22f, size * 0.40f, size * 0.56f), Color.White, size);
                }
            }
            return bmp;
        }

        /// <summary>闪电标志（充电中）。</summary>
        private static void DrawBolt(Graphics g, RectangleF area, Color color, int size)
        {
            float x = area.X, y = area.Y, w = area.Width, h = area.Height;
            PointF[] points = new PointF[]
            {
                new PointF(x + w * 0.60f, y),
                new PointF(x + w * 0.05f, y + h * 0.58f),
                new PointF(x + w * 0.42f, y + h * 0.58f),
                new PointF(x + w * 0.34f, y + h),
                new PointF(x + w * 0.95f, y + h * 0.40f),
                new PointF(x + w * 0.54f, y + h * 0.40f)
            };
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddPolygon(points);
                path.CloseFigure();
                if (size >= 20)
                {
                    using (Pen outline = new Pen(Color.FromArgb(20, 24, 32), Math.Max(1f, size * 0.05f)))
                        g.DrawPath(outline, path);
                }
                using (SolidBrush brush = new SolidBrush(color))
                    g.FillPath(brush, path);
            }
        }

        private static void DrawBatteryGlyph(Graphics g, int size, int level, Color color)
        {
            float s = size;
            float bodyH = s * 0.46f;
            float nubW = s * 0.06f;
            float bodyW = s * 0.80f;
            if (size <= 20)
            {
                bodyH = Math.Max(7f, (float)Math.Round(s * 0.50f));
                bodyW = s - 4f;
            }
            float x = (s - bodyW - nubW - 1) / 2f;
            float y = (s - bodyH) / 2f;
            float radius = size <= 20 ? 2f : s * 0.16f;
            RectangleF body = new RectangleF(x, y, bodyW, bodyH);

            using (GraphicsPath path = Theme.Rounded(body, radius))
            {
                using (SolidBrush fill = new SolidBrush(Theme.Alpha(color, 46)))
                    g.FillPath(fill, path);
                using (Pen pen = new Pen(Theme.Alpha(color, 200), Math.Max(1f, size / 16f)))
                    g.DrawPath(pen, path);
            }
            using (GraphicsPath nub = Theme.Rounded(new RectangleF(body.Right + 1f, y + bodyH * 0.3f, nubW, bodyH * 0.4f), 1f))
            using (SolidBrush nb = new SolidBrush(Theme.Alpha(color, 190)))
                g.FillPath(nb, nub);

            if (level >= 0)
            {
                float pad = Math.Max(1.4f, s * 0.09f);
                RectangleF inner = new RectangleF(body.X + pad, body.Y + pad, body.Width - pad * 2f, body.Height - pad * 2f);
                float w = inner.Width * Math.Max(0f, Math.Min(1f, level / 100f));
                if (w > 0.6f)
                {
                    using (GraphicsPath ip = Theme.Rounded(new RectangleF(inner.X, inner.Y, w, inner.Height), Math.Max(1f, radius * 0.5f)))
                    using (SolidBrush ib = new SolidBrush(color))
                        g.FillPath(ib, ip);
                }
            }
        }

        private static void DrawDigits(Graphics g, int size, int level, Color color, bool known)
        {
            string text = known ? level.ToString() : "–";
            Font font = null;
            FontFamily family = ResolveDigitFamily();
            try
            {
                Size ts = Size.Empty;
                for (float pt = size * 0.82f; pt >= 5f; pt -= 0.5f)
                {
                    if (font != null) font.Dispose();
                    font = new Font(family, pt, FontStyle.Bold, GraphicsUnit.Pixel);
                    ts = TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                    if (ts.Width <= size * 0.94f && ts.Height <= size * 0.96f) break;
                }
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
                Rectangle rect = new Rectangle(0, 0, size, size);
                if (size >= 24)
                {
                    Color outline = known ? Color.FromArgb(20, 24, 32) : Color.FromArgb(20, 24, 32);
                    TextRenderer.DrawText(g, text, font, new Rectangle(1, 0, size, size), outline, flags);
                    TextRenderer.DrawText(g, text, font, new Rectangle(-1, 0, size, size), outline, flags);
                    TextRenderer.DrawText(g, text, font, new Rectangle(0, 1, size, size), outline, flags);
                    TextRenderer.DrawText(g, text, font, new Rectangle(0, -1, size, size), outline, flags);
                }
                TextRenderer.DrawText(g, text, font, rect, color, flags);
            }
            finally
            {
                if (font != null) font.Dispose();
                family.Dispose();
            }
        }

        private static FontFamily ResolveDigitFamily()
        {
            string[] names = new string[] { "Segoe UI Variable Display", "Segoe UI", "Microsoft YaHei UI", "Tahoma" };
            for (int i = 0; i < names.Length; i++)
            {
                try { return new FontFamily(names[i]); }
                catch { }
            }
            return new FontFamily("Arial");
        }

        /// <summary>应用品牌图标（窗口 / 菜单 / exe）。</summary>
        public static Icon Brand()
        {
            List<Bitmap> frames = new List<Bitmap>();
            int[] sizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            foreach (int size in sizes)
            {
                Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float s = size;
                    float pad = size <= 20 ? 0f : s * 0.045f;
                    RectangleF plate = new RectangleF(pad, pad, s - pad * 2f, s - pad * 2f);
                    using (LinearGradientBrush brush = new LinearGradientBrush(plate, Color.FromArgb(96, 160, 255), Color.FromArgb(42, 92, 210), LinearGradientMode.ForwardDiagonal))
                    using (GraphicsPath path = Theme.Rounded(plate, s * 0.26f))
                        g.FillPath(brush, path);

                    float bodyW = s * 0.52f;
                    float bodyH = s * 0.30f;
                    float bx = (s - bodyW) / 2f - s * 0.03f;
                    float by = (s - bodyH) / 2f;
                    RectangleF body = new RectangleF(bx, by, bodyW, bodyH);
                    using (GraphicsPath path = Theme.Rounded(body, Math.Max(1f, s * 0.07f)))
                    {
                        using (Pen pen = new Pen(Color.White, Math.Max(1.2f, s * 0.055f)))
                            g.DrawPath(pen, path);
                    }
                    using (GraphicsPath nub = Theme.Rounded(new RectangleF(body.Right + s * 0.015f, by + bodyH * 0.32f, s * 0.04f, bodyH * 0.36f), 1f))
                    using (SolidBrush nb = new SolidBrush(Color.White))
                        g.FillPath(nb, nub);
                    float fillW = body.Width * 0.62f;
                    RectangleF fill = new RectangleF(body.X + s * 0.035f, body.Y + s * 0.045f, fillW, body.Height - s * 0.09f);
                    if (fill.Width > 0.5f)
                    {
                        using (GraphicsPath path = Theme.Rounded(fill, Math.Max(1f, s * 0.035f)))
                        using (SolidBrush b = new SolidBrush(Color.White))
                            g.FillPath(b, path);
                    }
                }
                frames.Add(bmp);
            }
            Icon icon = BuildIco(frames);
            foreach (Bitmap b in frames) b.Dispose();
            return icon;
        }

        /// <summary>32 位 DIB 形式的图标帧（兼容性最好）。</summary>
        private static byte[] ToDib(Bitmap bmp)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    BinaryWriter bw = new BinaryWriter(ms);
                    bw.Write(40);
                    bw.Write(w);
                    bw.Write(h * 2);
                    bw.Write((ushort)1);
                    bw.Write((ushort)32);
                    bw.Write(0);
                    bw.Write(w * h * 4);
                    bw.Write(0);
                    bw.Write(0);
                    bw.Write(0);
                    bw.Write(0);
                    byte[] row = new byte[w * 4];
                    for (int y = h - 1; y >= 0; y--)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row, 0, w * 4);
                        bw.Write(row);
                    }
                    int maskRow = ((w + 31) / 32) * 4;
                    byte[] zeros = new byte[maskRow];
                    for (int y = 0; y < h; y++) bw.Write(zeros);
                    bw.Flush();
                    return ms.ToArray();
                }
            }
            finally { bmp.UnlockBits(data); }
        }

        /// <summary>把多张位图打包成多尺寸 ICO。</summary>
        public static Icon BuildIco(List<Bitmap> frames)
        {
            List<byte[]> pngs = new List<byte[]>();
            for (int i = 0; i < frames.Count; i++) pngs.Add(ToDib(frames[i]));

            MemoryStream outStream = new MemoryStream();
            BinaryWriter w = new BinaryWriter(outStream);
            w.Write((ushort)0);
            w.Write((ushort)1);
            w.Write((ushort)frames.Count);
            int offset = 6 + 16 * frames.Count;
            for (int i = 0; i < frames.Count; i++)
            {
                int size = frames[i].Width;
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write((uint)pngs[i].Length);
                w.Write((uint)offset);
                offset += pngs[i].Length;
            }
            for (int i = 0; i < pngs.Count; i++) w.Write(pngs[i]);
            w.Flush();
            outStream.Position = 0;
            Icon icon = new Icon(outStream, frames[0].Width, frames[0].Height);
            KeepAlive.Add(icon);
            return icon;
        }

        public static void SaveIco(string path)
        {
            Icon brand = Brand();
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                brand.Save(fs);
        }
    }
}
