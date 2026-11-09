using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BatteryBar
{
    /// <summary>鍏ㄥ眬閰嶈壊銆佸瓧浣撲笌缁樺埗杈呭姪銆?/summary>
    internal static class Theme
    {
        // 鑳屾櫙
        public static readonly Color Bg = Color.FromArgb(14, 16, 21);
        public static readonly Color BgSide = Color.FromArgb(18, 21, 27);
        public static readonly Color Card = Color.FromArgb(26, 30, 39);
        public static readonly Color CardHover = Color.FromArgb(33, 38, 50);
        public static readonly Color CardActive = Color.FromArgb(38, 45, 60);
        public static readonly Color Stroke = Color.FromArgb(41, 47, 60);
        public static readonly Color StrokeSoft = Color.FromArgb(33, 38, 48);

        // 鏂囧瓧
        public static readonly Color Ink = Color.FromArgb(236, 241, 249);
        public static readonly Color InkDim = Color.FromArgb(156, 166, 184);
        public static readonly Color InkMuted = Color.FromArgb(112, 122, 140);

        // 寮鸿皟
        public static readonly Color Accent = Color.FromArgb(94, 155, 255);
        public static readonly Color AccentDark = Color.FromArgb(64, 118, 214);
        public static readonly Color Green = Color.FromArgb(56, 202, 129);
        public static readonly Color Amber = Color.FromArgb(255, 178, 66);
        public static readonly Color Red = Color.FromArgb(255, 96, 96);
        public static readonly Color Track = Color.FromArgb(45, 52, 66);

        public const string Family = "Microsoft YaHei UI";
        public const string FamilyFallback = "Microsoft YaHei";
        public const string FamilyLatin = "Segoe UI";

        private static string _uiFamily;
        private static string _iconFamily;
        private static bool _iconFamilyChecked;

        public static string UiFamily
        {
            get
            {
                if (_uiFamily == null) _uiFamily = HasFamily(Family) ? Family : (HasFamily(FamilyFallback) ? FamilyFallback : FamilyLatin);
                return _uiFamily;
            }
        }

        /// <summary>Segoe Fluent Icons / Segoe MDL2 Assets 鍥炬爣瀛椾綋銆?/summary>
        public static string IconFamily
        {
            get
            {
                if (!_iconFamilyChecked)
                {
                    _iconFamilyChecked = true;
                    if (HasFamily("Segoe Fluent Icons")) _iconFamily = "Segoe Fluent Icons";
                    else if (HasFamily("Segoe MDL2 Assets")) _iconFamily = "Segoe MDL2 Assets";
                    else _iconFamily = UiFamily;
                }
                return _iconFamily;
            }
        }

        public static bool HasIcons { get { return IconFamily != UiFamily; } }

        public static bool HasFamily(string name)
        {
            try
            {
                using (FontFamily ff = new FontFamily(name)) { return true; }
            }
            catch { return false; }
        }

        public static Font F(float pt) { return new Font(UiFamily, pt, FontStyle.Regular, GraphicsUnit.Point); }
        public static Font F(float pt, FontStyle style) { return new Font(UiFamily, pt, style, GraphicsUnit.Point); }
        public static Font Icon(float pt) { return new Font(IconFamily, pt, FontStyle.Regular, GraphicsUnit.Point); }

        public static Color ForLevel(int level)
        {
            if (level < 0) return InkMuted;
            if (level <= 10) return Red;
            if (level <= 20) return Color.FromArgb(255, 122, 92);
            if (level <= 40) return Amber;
            return Green;
        }

        public static Color Alpha(Color c, int a) { return Color.FromArgb(a, c.R, c.G, c.B); }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Max(1f, radius * 2f);
            if (radius <= 0.5f)
            {
                p.AddRectangle(r);
                return p;
            }
            d = Math.Min(d, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color color)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (SolidBrush b = new SolidBrush(color))
                g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, RectangleF r, float radius, Color color, float width)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (Pen pen = new Pen(color, width))
            {
                pen.Alignment = PenAlignment.Center;
                g.DrawPath(pen, p);
            }
        }

        public static void Label(Graphics g, string s, Font f, Color c, RectangleF r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s, f, Rectangle.Round(r), c, flags);
        }

        public static void TextLeft(Graphics g, string s, Font f, Color c, RectangleF r)
        {
            TextRenderer.DrawText(g, s, f, Rectangle.Round(r), c,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        public static void TextCenter(Graphics g, string s, Font f, Color c, RectangleF r)
        {
            TextRenderer.DrawText(g, s, f, Rectangle.Round(r), c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        /// <summary>缁樺埗鐢甸噺鐜€?/summary>
        public static void DrawRing(Graphics g, RectangleF r, float fraction, Color color, float thickness, bool glow)
        {
            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen track = new Pen(Track, thickness))
            {
                track.StartCap = LineCap.Round;
                track.EndCap = LineCap.Round;
                g.DrawArc(track, r, -90f, 359.99f);
            }
            if (fraction > 0.001f)
            {
                if (glow)
                {
                    using (Pen halo = new Pen(Alpha(color, 46), thickness + 6f))
                    {
                        halo.StartCap = LineCap.Round;
                        halo.EndCap = LineCap.Round;
                        g.DrawArc(halo, r, -90f, 359.99f * fraction);
                    }
                }
                using (Pen pen = new Pen(color, thickness))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, r, -90f, 359.99f * fraction);
                }
            }
            g.SmoothingMode = old;
        }

        /// <summary>鐢垫睜鑳跺泭鍥炬爣銆?/summary>
        public static void DrawBattery(Graphics g, RectangleF r, float fraction, Color color, bool outlineOnly)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float head = r.Width * 0.10f;
            RectangleF body = new RectangleF(r.X, r.Y, r.Width - head - r.Width * 0.06f, r.Height);
            float radius = Math.Max(1.5f, r.Height * 0.22f);
            using (GraphicsPath p = Rounded(body, radius))
            {
                using (SolidBrush b = new SolidBrush(outlineOnly ? Alpha(color, 40) : Alpha(color, 34)))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(outlineOnly ? color : Alpha(color, 110), Math.Max(1f, r.Height * 0.09f)))
                    g.DrawPath(pen, p);

                if (!outlineOnly && fraction > 0)
                {
                    RectangleF inner = RectangleF.Inflate(body, -r.Height * 0.16f, -r.Height * 0.16f);
                    inner.Width = inner.Width * fraction;
                    if (inner.Width > 1f)
                    {
                        using (GraphicsPath ip = Rounded(inner, Math.Max(1f, radius * 0.6f)))
                        using (SolidBrush ib = new SolidBrush(color))
                            g.FillPath(ib, ip);
                    }
                }
            }
            RectangleF nub = new RectangleF(body.Right + r.Width * 0.02f, r.Y + r.Height * 0.3f, head * 0.7f, r.Height * 0.4f);
            using (GraphicsPath np = Rounded(nub, Math.Max(1f, nub.Height * 0.35f)))
            using (SolidBrush nb = new SolidBrush(Alpha(color, 150)))
                g.FillPath(nb, np);
            g.SmoothingMode = old;
        }

        public static void DrawDot(Graphics g, PointF p, float radius, Color c)
        {
            using (SolidBrush b = new SolidBrush(c))
                g.FillEllipse(b, p.X - radius, p.Y - radius, radius * 2f, radius * 2f);
        }

        /// <summary>闪电标志（充电中）。</summary>
        public static void DrawBolt(Graphics g, RectangleF area, Color color)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = area.X, y = area.Y, w = area.Width, h = area.Height;
            PointF[] points = new PointF[]
            {
                new PointF(x + w * 0.62f, y),
                new PointF(x + w * 0.02f, y + h * 0.56f),
                new PointF(x + w * 0.42f, y + h * 0.56f),
                new PointF(x + w * 0.32f, y + h),
                new PointF(x + w * 0.98f, y + h * 0.40f),
                new PointF(x + w * 0.56f, y + h * 0.40f)
            };
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddPolygon(points);
                path.CloseFigure();
                using (SolidBrush b = new SolidBrush(color))
                    g.FillPath(b, path);
            }
            g.SmoothingMode = old;
        }
    }
}

