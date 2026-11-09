using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace BatteryBar
{
    /// <summary>生成界面/图标预览图（供文档与截图使用）。</summary>
    internal static class PreviewRenderer
    {
        private static readonly int[] Levels = new int[] { 100, 85, 68, 45, 25, 12, -1 };

        public static void SaveTraySheet(string path)
        {
            int cell = 92;
            int pad = 28;
            int width = pad * 2 + cell * Levels.Length;
            int height = 470;
            using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(16, 19, 25));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (Font title = Theme.F(10f, FontStyle.Bold))
                using (Font sub = Theme.F(8.5f))
                using (Font small = Theme.F(8f))
                {
                    Theme.TextLeft(g, "任务栏图标样式", title, Theme.Ink, new RectangleF(pad, 14, width - pad * 2, 24));
                    Theme.TextLeft(g, "放大显示（实际尺寸 16–24 px）", sub, Theme.InkMuted, new RectangleF(pad, 38, width - pad * 2, 20));

                    DrawRow(g, 0, "电池图标", 72, small, pad, cell, false);
                    DrawRow(g, 1, "百分比数字", 208, small, pad, cell, false);
                    DrawRow(g, 0, "电池图标（充电中）", 344, small, pad, cell, true);
                }
                string dir0 = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir0) && !Directory.Exists(dir0)) Directory.CreateDirectory(dir0);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        private static void DrawRow(Graphics g, int style, string label, int top, Font small, int pad, int cell, bool charging)
        {
            Theme.TextLeft(g, label, small, Theme.InkDim, new RectangleF(pad, top - 22, 200, 20));
            for (int i = 0; i < Levels.Length; i++)
            {
                float x = pad + i * cell;
                RectangleF plate = new RectangleF(x, top, 68, 68);
                Theme.FillRound(g, plate, 14f, Color.FromArgb(26, 30, 39));
                int level = Levels[i];
                using (Bitmap icon = TrayIconFactory.RenderBitmap(level, style, true, 64, charging))
                {
                    g.DrawImage(icon, new RectangleF(plate.X + 2, plate.Y + 2, 64, 64));
                }
                string cap = level < 0 ? "未知" : level + "%";
                Theme.TextCenter(g, cap, small, Theme.InkMuted, new RectangleF(x, top + 72, 68, 20));
                using (Bitmap actual = TrayIconFactory.RenderBitmap(level, style, true, 16, charging))
                {
                    g.DrawImageUnscaled(actual, (int)(x + 26), top + 98);
                }
            }
        }

        /// <summary>把两个图标样式拼成一张横幅，用于文档头图。</summary>
        public static void SaveHero(string path)
        {
            int width = 900;
            int height = 320;
            using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, width, height),
                    Color.FromArgb(18, 24, 38), Color.FromArgb(10, 13, 19), LinearGradientMode.ForwardDiagonal))
                    g.FillRectangle(bg, 0, 0, width, height);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                using (Font big = Theme.F(26f, FontStyle.Bold))
                using (Font sub = Theme.F(11f))
                {
                    Theme.TextLeft(g, "BatteryBar", big, Theme.Ink, new RectangleF(56, 96, 420, 52));
                    Theme.TextLeft(g, "任务栏蓝牙 / 无线设备电量", sub, Theme.InkDim, new RectangleF(58, 152, 420, 28));
                    Theme.TextLeft(g, "低电量提醒 · 开机自启 · 多设备管理", sub, Theme.InkMuted, new RectangleF(58, 182, 420, 26));
                }

                // 任务栏示意条
                RectangleF bar = new RectangleF(520, 118, 320, 56);
                Theme.FillRound(g, bar, 16f, Color.FromArgb(30, 34, 42));
                using (Bitmap icon = TrayIconFactory.RenderBitmap(68, 0, true, 40, false))
                    g.DrawImage(icon, new RectangleF(bar.X + 24, bar.Y + 8, 40, 40));
                using (Bitmap icon2 = TrayIconFactory.RenderBitmap(12, 1, true, 40, true))
                    g.DrawImage(icon2, new RectangleF(bar.X + 80, bar.Y + 8, 40, 40));
                using (Font small = Theme.F(9f))
                {
                    Theme.Label(g, "68%", small, Theme.Ink, new RectangleF(bar.X + 130, bar.Y, 80, 56), System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.NoPadding);
                }
                string dir1 = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir1) && !Directory.Exists(dir1)) Directory.CreateDirectory(dir1);
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}
