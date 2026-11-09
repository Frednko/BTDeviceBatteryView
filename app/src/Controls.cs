using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BatteryBar
{
    internal static class Glyphs
    {
        public const string Bluetooth = "\uE702";
        public const string Battery = "\uE83F";
        public const string Settings = "\uE713";
        public const string Bell = "\uEA8F";
        public const string Refresh = "\uE72C";
        public const string Add = "\uE710";
        public const string Delete = "\uE74D";
        public const string Check = "\uE73E";
        public const string Desktop = "\uE7F4";
        public const string Power = "\uE7E8";
        public const string Close = "\uE8BB";
        public const string Minimize = "\uE921";
        public const string Pin = "\uE718";
        public const string Chevron = "\uE76C";
        public const string Edit = "\uE70F";
        public const string More = "\uE712";
        public const string Lightning = "\uE945";
        public const string Mouse = "\uE962";
        public const string Keyboard = "\uE765";
        public const string Headphone = "\uE7F6";
        public const string Warning = "\uE7BA";
    }

    /// <summary>带平坦底色的自绘控件基类。</summary>
    internal class Surface : Control
    {
        public Color SurfaceColor = Theme.Bg;

        public Surface()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            TabStop = false;
            SetStyle(ControlStyles.Selectable, false);
        }

        public float Sc { get { return DeviceDpi <= 0 ? 1f : DeviceDpi / 96f; } }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(SurfaceColor))
                e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            Invalidate();
        }

        protected int Px(float dip) { return (int)Math.Round(dip * Sc); }
    }

    internal class UiButton : Surface
    {
        public string Glyph;
        public bool Primary;
        private bool _hover;
        private bool _down;
        private Font _font;
        private Font _glyphFont;

        public UiButton()
        {
            Cursor = Cursors.Hand;
            Size = new Size(96, 32);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateFonts();
        }

        private void UpdateFonts()
        {
            if (_font != null) _font.Dispose();
            if (_glyphFont != null) _glyphFont.Dispose();
            _font = Theme.F(9f);
            _glyphFont = Theme.Icon(10f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_font == null) UpdateFonts();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0, 0, Width, Height);

            Color bg;
            Color fg;
            if (Primary)
            {
                bg = _down ? Theme.AccentDark : (_hover ? Color.FromArgb(110, 168, 255) : Theme.Accent);
                fg = Color.FromArgb(10, 14, 22);
            }
            else
            {
                bg = _down ? Theme.CardActive : (_hover ? Theme.CardHover : Theme.Card);
                fg = _hover ? Theme.Ink : Theme.InkDim;
            }
            Theme.FillRound(g, r, 8f * Sc, bg);
            if (!Primary)
                Theme.StrokeRound(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8f * Sc, Theme.Stroke, 1f);

            if (string.IsNullOrEmpty(Glyph))
            {
                Theme.TextCenter(g, Text, _font, fg, r);
            }
            else
            {
                Size glyphSize = TextRenderer.MeasureText(g, Glyph, _glyphFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                Size textSize = TextRenderer.MeasureText(g, Text, _font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                float total = glyphSize.Width + (Text.Length > 0 ? 6 * Sc + textSize.Width : 0);
                float x = (Width - total) / 2f;
                Theme.TextCenter(g, Glyph, _glyphFont, fg, new RectangleF(x, 0, glyphSize.Width, Height));
                if (Text.Length > 0)
                    Theme.TextCenter(g, Text, _font, fg, new RectangleF(x + glyphSize.Width + 6 * Sc, 0, textSize.Width, Height));
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_down && e.Button == MouseButtons.Left) { _down = false; Invalidate(); }
            base.OnMouseUp(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_font != null) _font.Dispose();
                if (_glyphFont != null) _glyphFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal class ToggleSwitch : Surface
    {
        private bool _checked;
        private float _pos;
        private Timer _anim;
        private bool _hover;

        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            Size = new Size(46, 26);
            Cursor = Cursors.Hand;
            _anim = new Timer();
            _anim.Interval = 15;
            _anim.Tick += AnimTick;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                _anim.Start();
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public void SetCheckedSilent(bool value)
        {
            _checked = value;
            _pos = value ? 1f : 0f;
            Invalidate();
        }

        private void AnimTick(object sender, EventArgs e)
        {
            float target = _checked ? 1f : 0f;
            float delta = target - _pos;
            if (Math.Abs(delta) < 0.06f)
            {
                _pos = target;
                _anim.Stop();
            }
            else _pos += delta * 0.35f;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float h = Height;
            RectangleF track = new RectangleF(0, 0, Width, h);
            Color on = Theme.Accent;
            Color off = _hover ? Color.FromArgb(58, 66, 82) : Theme.Track;
            Color bg = Blend(off, on, _pos);
            Theme.FillRound(g, track, h / 2f, bg);

            float knob = h - 6f * Sc;
            float x = 3f * Sc + _pos * (Width - knob - 6f * Sc);
            RectangleF k = new RectangleF(x, 3f * Sc, knob, knob);
            using (SolidBrush b = new SolidBrush(_pos > 0.5f ? Color.White : Color.FromArgb(176, 186, 202)))
                g.FillEllipse(b, k);
        }

        private static Color Blend(Color a, Color b, float t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Checked = !Checked;
            base.OnMouseDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _anim != null) { _anim.Stop(); _anim.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal class Segmented : Surface
    {
        private string[] _items = new string[0];
        private int _selected;
        private int _hover = -1;
        private Font _font;

        public event EventHandler SelectedChanged;

        public Segmented()
        {
            Height = 30;
            Cursor = Cursors.Hand;
        }

        public string[] Items
        {
            get { return _items; }
            set { _items = value ?? new string[0]; Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value < 0) value = 0;
                if (value >= _items.Length) value = Math.Max(0, _items.Length - 1);
                if (_selected == value) return;
                _selected = value;
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        public void SetSelectedSilent(int value)
        {
            if (value < 0) value = 0;
            _selected = value;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_font == null) _font = Theme.F(9f);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(g, r, 9f * Sc, Color.FromArgb(22, 26, 34));
            Theme.StrokeRound(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 9f * Sc, Theme.StrokeSoft, 1f);

            if (_items.Length == 0) return;
            float seg = Width / (float)_items.Length;
            for (int i = 0; i < _items.Length; i++)
            {
                RectangleF cell = new RectangleF(i * seg + 2 * Sc, 2 * Sc, seg - 4 * Sc, Height - 4 * Sc);
                if (i == _selected)
                {
                    Theme.FillRound(g, cell, 7f * Sc, Color.FromArgb(48, 62, 88));
                    Theme.StrokeRound(g, cell, 7f * Sc, Color.FromArgb(78, 104, 152), 1f);
                }
                else if (i == _hover)
                {
                    Theme.FillRound(g, cell, 7f * Sc, Color.FromArgb(34, 40, 52));
                }
                Theme.TextCenter(g, _items[i], _font, i == _selected ? Theme.Ink : Theme.InkMuted, cell);
            }
        }

        private int HitIndex(Point p)
        {
            if (Width <= 0 || _items.Length == 0) return -1;
            int i = (int)(p.X / (Width / (float)_items.Length));
            if (i < 0 || i >= _items.Length) return -1;
            return i;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = HitIndex(e.Location);
            if (i != _hover) { _hover = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = HitIndex(e.Location);
            if (i >= 0) SelectedIndex = i;
            base.OnMouseDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _font != null) _font.Dispose();
            base.Dispose(disposing);
        }
    }

    internal class Slider : Surface
    {
        public int Min = 0;
        public int Max = 100;
        public Color Accent = Theme.Accent;
        private int _value = 50;
        private bool _drag;
        private bool _hover;
        private Font _font;

        public event EventHandler ValueChanged;

        public bool IsDragging { get { return _drag; } }

        public Slider()
        {
            Height = 24;
            Cursor = Cursors.Hand;
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(Min, Math.Min(Max, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public void SetValueSilent(int value)
        {
            _value = Math.Max(Min, Math.Min(Max, value));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_font == null) _font = Theme.F(8.5f, FontStyle.Bold);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cy = Height / 2f;
            float trackH = 5f * Sc;
            float knob = 16f * Sc;
            float left = knob / 2f;
            float right = Width - knob / 2f;
            float frac = (Max == Min) ? 0 : (_value - Min) / (float)(Max - Min);
            float x = left + frac * (right - left);

            RectangleF track = new RectangleF(left, cy - trackH / 2f, right - left, trackH);
            Theme.FillRound(g, track, trackH / 2f, Theme.Track);
            RectangleF fill = new RectangleF(left, cy - trackH / 2f, x - left, trackH);
            if (fill.Width > 0.5f) Theme.FillRound(g, fill, trackH / 2f, Accent);

            RectangleF k = new RectangleF(x - knob / 2f, cy - knob / 2f, knob, knob);
            if (_hover || _drag)
            {
                using (SolidBrush halo = new SolidBrush(Theme.Alpha(Accent, 50)))
                    g.FillEllipse(halo, RectangleF.Inflate(k, 4f * Sc, 4f * Sc));
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(248, 250, 253)))
            using (Pen p = new Pen(Accent, 2f * Sc))
            {
                g.FillEllipse(b, k);
                g.DrawEllipse(p, k);
            }
        }

        private void SetFromX(int x)
        {
            float knob = 16f * Sc;
            float left = knob / 2f;
            float right = Width - knob / 2f;
            float frac = (x - left) / Math.Max(1f, right - left);
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;
            Value = Min + (int)Math.Round(frac * (Max - Min));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _drag = true;
                Capture = true;
                SetFromX(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag) SetFromX(e.X);
            if (!_hover) { _hover = true; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_drag) { _drag = false; Capture = false; Invalidate(); }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _font != null) _font.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>设备列表：整块自绘，包含托盘切换按钮。</summary>
    internal class DeviceList : Surface
    {
        public List<DeviceConfig> Devices = new List<DeviceConfig>();
        public string SelectedId;
        public string TrayId;

        public event EventHandler SelectionChanged;
        public event EventHandler TrayToggleRequested;

        private int _hoverIndex = -1;
        private float _scroll;
        private float _scrollTarget;
        private Timer _scrollTimer;
        private Font _nameFont;
        private Font _subFont;
        private Font _pctFont;
        private Font _chipFont;

        private const int CardH = 70;
        private const int Gap = 10;
        private const int PadTop = 4;

        public DeviceList()
        {
            ResizeRedraw = true;
            _scrollTimer = new Timer();
            _scrollTimer.Interval = 15;
            _scrollTimer.Tick += delegate
            {
                float d = _scrollTarget - _scroll;
                if (Math.Abs(d) < 0.6f) { _scroll = _scrollTarget; _scrollTimer.Stop(); }
                else _scroll += d * 0.4f;
                Invalidate();
            };
        }

        private void EnsureFonts()
        {
            if (_nameFont != null) return;
            _nameFont = Theme.F(10.5f, FontStyle.Bold);
            _subFont = Theme.F(8.5f);
            _pctFont = Theme.F(9f, FontStyle.Bold);
            _chipFont = Theme.F(8f);
        }

        private float ContentHeight { get { return PadTop + Devices.Count * (CardH + Gap); } }
        private bool NeedScroll { get { return ContentHeight > Height; } }

        private void ClampScroll()
        {
            float max = Math.Max(0, ContentHeight - Height + 6);
            if (_scrollTarget > max) _scrollTarget = max;
            if (_scrollTarget < 0) _scrollTarget = 0;
            _scrollTimer.Start();
        }

        public void ScrollBy(int delta)
        {
            _scrollTarget -= delta / 3f;
            ClampScroll();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureFonts();
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (Devices.Count == 0)
            {
                using (Font icon = Theme.Icon(30f))
                using (Font f1 = Theme.F(10.5f, FontStyle.Bold))
                using (Font f2 = Theme.F(9f))
                {
                    float cy = Height / 2f - 50 * Sc;
                    Theme.TextCenter(g, Glyphs.Bluetooth, icon, Color.FromArgb(64, 74, 92), new RectangleF(0, cy - 26 * Sc, Width, 52 * Sc));
                    Theme.TextCenter(g, "尚未添加设备", f1, Theme.InkDim, new RectangleF(0, cy + 36 * Sc, Width, 24 * Sc));
                    Theme.TextCenter(g, "点击右上角「添加设备」开始", f2, Theme.InkMuted, new RectangleF(0, cy + 60 * Sc, Width, 22 * Sc));
                }
                return;
            }

            float scrollbarW = NeedScroll ? 5 * Sc : 0;
            for (int i = 0; i < Devices.Count; i++)
            {
                DeviceConfig d = Devices[i];
                float y = PadTop + i * (CardH + Gap) - _scroll;
                if (y + CardH < -4 || y > Height + 4) continue;

                RectangleF card = new RectangleF(0, y, Width - scrollbarW - 2, CardH);
                bool selected = d.Id == SelectedId;
                Color bg = selected ? Theme.CardActive : (i == _hoverIndex ? Theme.CardHover : Theme.Card);
                Theme.FillRound(g, card, 12f * Sc, bg);
                if (selected)
                    Theme.StrokeRound(g, new RectangleF(card.X + 0.5f, card.Y + 0.5f, card.Width - 1, card.Height - 1), 12f * Sc, Color.FromArgb(92, 128, 190), 1.2f);

                // 电量环
                float ring = 40 * Sc;
                RectangleF ringRect = new RectangleF(16 * Sc, y + (CardH - ring) / 2f, ring, ring);
                int level = d.Level;
                Color color = Theme.ForLevel(level);
                Theme.DrawRing(g, RectangleF.Inflate(ringRect, -3 * Sc, -3 * Sc), level < 0 ? 0f : level / 100f, color, 4f * Sc, false);
                string ringText = level < 0 ? "–" : level.ToString();
                using (Font rf = Theme.F(9.5f, FontStyle.Bold))
                    Theme.TextCenter(g, ringText, rf, level < 0 ? Theme.InkMuted : color, ringRect);

                // 文字
                float textLeft = 70 * Sc;
                float textRight = card.Width - 58 * Sc;
                Theme.TextLeft(g, d.Name, _nameFont, Theme.Ink, new RectangleF(textLeft, y + 12 * Sc, textRight - textLeft, 20 * Sc));
                Color subColor = string.IsNullOrEmpty(d.ErrorText) ? Theme.InkMuted : Color.FromArgb(226, 138, 120);
                Theme.TextLeft(g, d.Subtitle, _subFont, subColor, new RectangleF(textLeft, y + 37 * Sc, textRight - textLeft - 46 * Sc, 18 * Sc));

                // 百分比（充电时带闪电）
                if (level >= 0)
                {
                    float pctLeft = textRight - 52 * Sc;
                    if (d.IsCharging)
                    {
                        Theme.DrawBolt(g, new RectangleF(pctLeft - 2 * Sc, y + 38 * Sc, 10 * Sc, 14 * Sc), Theme.Amber);
                        pctLeft += 11 * Sc;
                    }
                    RectangleF pct = new RectangleF(pctLeft, y + 36 * Sc, textRight - pctLeft, 18 * Sc);
                    Theme.Label(g, level + "%", _pctFont, color, pct, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }

                // 托盘按钮
                RectangleF chip = new RectangleF(card.Right - 52 * Sc, y + (CardH - 26 * Sc) / 2f, 42 * Sc, 26 * Sc);
                bool isTray = d.ShowInTray;
                Theme.FillRound(g, chip, 8f * Sc, isTray ? Color.FromArgb(52, 74, 112) : Color.FromArgb(32, 37, 47));
                if (isTray) Theme.StrokeRound(g, chip, 8f * Sc, Color.FromArgb(96, 140, 210), 1f);
                using (Font cf = Theme.Icon(9.5f))
                    Theme.TextCenter(g, isTray ? Glyphs.Check : Glyphs.Desktop, cf, isTray ? Color.FromArgb(146, 186, 255) : Theme.InkMuted, chip);
            }

            if (NeedScroll)
            {
                float trackTop = 2 * Sc;
                float trackH = Height - 4 * Sc;
                float max = Math.Max(1, ContentHeight - Height + 6);
                float thumbH = Math.Max(28 * Sc, trackH * (Height / ContentHeight));
                float thumbY = trackTop + (trackH - thumbH) * (_scroll / max);
                RectangleF thumb = new RectangleF(Width - 4 * Sc, thumbY, 3f * Sc, thumbH);
                Theme.FillRound(g, thumb, 1.5f * Sc, Color.FromArgb(60, 70, 88));
            }
        }

        private int IndexAt(Point p)
        {
            float y = p.Y + _scroll - PadTop;
            if (y < 0) return -1;
            int i = (int)(y / (CardH + Gap));
            if (i < 0 || i >= Devices.Count) return -1;
            if (y - i * (CardH + Gap) > CardH) return -1;
            return i;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Location);
            if (i != _hoverIndex) { _hoverIndex = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverIndex = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                int i = IndexAt(e.Location);
                if (i >= 0 && i < Devices.Count)
                {
                    float y = PadTop + i * (CardH + Gap) - _scroll;
                    float right = Width - (NeedScroll ? 5 * Sc : 0) - 2;
                    RectangleF chip = new RectangleF(right - 52 * Sc, y + (CardH - 26 * Sc) / 2f, 42 * Sc, 26 * Sc);
                    if (chip.Contains(e.Location))
                    {
                        if (TrayToggleRequested != null) TrayToggleRequested(Devices[i], EventArgs.Empty);
                        return;
                    }
                    if (Devices[i].Id != SelectedId)
                    {
                        SelectedId = Devices[i].Id;
                        Invalidate();
                        if (SelectionChanged != null) SelectionChanged(Devices[i], EventArgs.Empty);
                    }
                }
            }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData) { return true; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_scrollTimer != null) { _scrollTimer.Stop(); _scrollTimer.Dispose(); }
                if (_nameFont != null) _nameFont.Dispose();
                if (_subFont != null) _subFont.Dispose();
                if (_pctFont != null) _pctFont.Dispose();
                if (_chipFont != null) _chipFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
