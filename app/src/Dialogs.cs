using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BatteryBar
{
    /// <summary>扫描结果列表（自绘）。</summary>
    internal class DiscoveredList : Surface
    {
        public List<DiscoveredDevice> Items = new List<DiscoveredDevice>();
        public event EventHandler ItemActivated;

        private int _hover = -1;
        private float _scroll;
        private Font _nameFont, _detailFont, _chipFont, _iconFont;
        private const int RowH = 58;
        private const int Gap = 6;

        public DiscoveredList()
        {
            ResizeRedraw = true;
        }

        private void EnsureFonts()
        {
            if (_nameFont != null) return;
            _nameFont = Theme.F(10f, FontStyle.Bold);
            _detailFont = Theme.F(8.5f);
            _chipFont = Theme.F(8f);
            _iconFont = Theme.Icon(9f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureFonts();
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (Items.Count == 0)
            {
                Theme.TextCenter(g, "没有发现可添加的设备", _detailFont, Theme.InkMuted, new RectangleF(0, 0, Width, Height));
                return;
            }

            for (int i = 0; i < Items.Count; i++)
            {
                DiscoveredDevice d = Items[i];
                float y = 4 + i * (RowH + Gap) - _scroll;
                if (y > Height || y + RowH < 0) continue;
                RectangleF row = new RectangleF(0, y, Width - 8 * Sc, RowH);
                Color bg = d.Added ? Color.FromArgb(23, 27, 34) : (i == _hover ? Theme.CardHover : Theme.Card);
                Theme.FillRound(g, row, 10f * Sc, bg);
                if (i == _hover && !d.Added)
                    Theme.StrokeRound(g, new RectangleF(0.5f, y + 0.5f, Width - 3, RowH - 1), 10f * Sc, Color.FromArgb(80, 116, 176), 1f);

                Color ink = d.Added ? Theme.InkMuted : Theme.Ink;
                Theme.TextLeft(g, d.Name, _nameFont, ink, new RectangleF(16 * Sc, y + 10 * Sc, Width - 110 * Sc, 20 * Sc));
                Theme.TextLeft(g, d.Detail, _detailFont, d.Added ? Color.FromArgb(96, 104, 120) : Theme.InkMuted,
                    new RectangleF(16 * Sc, y + 32 * Sc, Width - 110 * Sc, 18 * Sc));

                string chip = d.Added ? "已添加" : (d.Source == Sources.Ble ? "自动" : d.Source == Sources.Hidpp ? "HID++" : d.Source == Sources.Gamepad ? "手柄" : "自动");
                Size ts = TextRenderer.MeasureText(g, chip, _chipFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                RectangleF chipRect = new RectangleF(Width - 16 * Sc - ts.Width - 18 * Sc, y + (RowH - 22 * Sc) / 2f, ts.Width + 18 * Sc, 22 * Sc);
                Color chipBg = d.Added ? Color.FromArgb(30, 34, 42) : (d.Source == Sources.Ble ? Color.FromArgb(36, 78, 58) : Color.FromArgb(40, 46, 60));
                Color chipFg = d.Added ? Theme.InkMuted : (d.Source == Sources.Ble ? Theme.Green : Theme.InkDim);
                Theme.FillRound(g, chipRect, chipRect.Height / 2f, chipBg);
                Theme.TextCenter(g, chip, _chipFont, chipFg, chipRect);
            }

            float contentH = 4 + Items.Count * (RowH + Gap);
            if (contentH > Height)
            {
                float trackH = Height - 4 * Sc;
                float thumbH = Math.Max(26 * Sc, trackH * (Height / contentH));
                float max = Math.Max(1f, contentH - Height + 4);
                float thumbY = 2 * Sc + (trackH - thumbH) * Math.Min(1f, _scroll / max);
                Theme.FillRound(g, new RectangleF(Width - 4 * Sc, thumbY, 3 * Sc, thumbH), 1.5f * Sc, Color.FromArgb(62, 72, 90));
            }
        }

        private int IndexAt(Point p)
        {
            float y = p.Y + _scroll - 4;
            if (y < 0) return -1;
            int i = (int)(y / (RowH + Gap));
            if (i < 0 || i >= Items.Count) return -1;
            if (y - i * (RowH + Gap) > RowH) return -1;
            return i;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Location);
            if (i != _hover) { _hover = i; Invalidate(); }
            Cursor = i >= 0 && i < Items.Count && !Items[i].Added ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int i = IndexAt(e.Location);
                if (i >= 0 && i < Items.Count && !Items[i].Added && ItemActivated != null)
                    ItemActivated(Items[i], EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float max = Math.Max(0, Items.Count * (RowH + Gap) - Height + 8);
            _scroll = Math.Max(0, Math.Min(max, _scroll - e.Delta / 3f));
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Font[] fonts = new Font[] { _nameFont, _detailFont, _chipFont, _iconFont };
                for (int i = 0; i < fonts.Length; i++) if (fonts[i] != null) fonts[i].Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal class AddDeviceForm : Form
    {
        private readonly AppConfig _config;
        private const string Placeholder = "设备名称，例如「客厅音箱」";
        private DiscoveredList _list;
        private TextBox _nameBox;
        private UiButton _btnAddManual;
        private UiButton _btnRescan;
        private CaptionButton _btnClose;
        private string _hintText = "";
        private Font _h1, _h2, _labelFont, _hintFont;
        private bool _busy;

        public DeviceConfig Result;

        public AddDeviceForm(AppConfig config)
        {
            _config = config;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(600, 620);

            _list = new DiscoveredList();
            _list.ItemActivated += delegate (object s, EventArgs e) { Choose((DiscoveredDevice)s); };

            _nameBox = new TextBox();
            _nameBox.BorderStyle = BorderStyle.None;
            _nameBox.BackColor = Theme.Card;
            _nameBox.ForeColor = Theme.InkMuted;
            _nameBox.Font = Theme.F(10f);
            _nameBox.Height = 28;
            _nameBox.Text = Placeholder;
            _nameBox.MouseDown += delegate { ClearPlaceholder(); };
            _nameBox.KeyPress += delegate { ClearPlaceholder(); };
            _nameBox.LostFocus += delegate
            {
                if ((_nameBox.Text ?? "").Trim().Length == 0)
                {
                    _nameBox.Text = Placeholder;
                    _nameBox.ForeColor = Theme.InkMuted;
                }
            };

            _btnAddManual = new UiButton();
            _btnAddManual.Text = "手动添加";
            _btnAddManual.Primary = true;
            _btnAddManual.Click += delegate { AddManual(); };

            _btnRescan = new UiButton();
            _btnRescan.Glyph = Glyphs.Refresh;
            _btnRescan.Text = "重新扫描";
            _btnRescan.Click += delegate { Rescan(); };

            _btnClose = new CaptionButton();
            _btnClose.Glyph = Glyphs.Close;
            _btnClose.Danger = true;
            _btnClose.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(_list);
            Controls.Add(_nameBox);
            Controls.Add(_btnAddManual);
            Controls.Add(_btnRescan);
            Controls.Add(_btnClose);

            MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) Native.DragWindow(Handle);
            };

            Load += delegate
            {
                ApplyScale();
                Rescan();
            };
            Shown += delegate
            {
                _nameBox.SelectionStart = 0;
                _nameBox.SelectionLength = 0;
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            ApplyScale();
        }

        private void ApplyScale()
        {
            float sc = DeviceDpi / 96f;
            ClientSize = new Size((int)Math.Round(600 * sc), (int)Math.Round(620 * sc));
            LayoutUi();
        }

        private void LayoutUi()
        {
            float sc = DeviceDpi / 96f;
            _btnClose.Bounds = new Rectangle(ClientSize.Width - (int)(12 * sc) - (int)(42 * sc), (int)(10 * sc), (int)(42 * sc), (int)(32 * sc));
            _nameBox.Bounds = new Rectangle((int)(24 * sc) + (int)(14 * sc), (int)(150 * sc) + (int)(11 * sc), ClientSize.Width - (int)(24 * 2 * sc) - (int)(14 * sc) - (int)(130 * sc), _nameBox.PreferredHeight);
            _btnAddManual.Bounds = new Rectangle(ClientSize.Width - (int)(24 * sc) - (int)(112 * sc), (int)(150 * sc), (int)(112 * sc), (int)(38 * sc));
            _btnRescan.Bounds = new Rectangle(ClientSize.Width - (int)(24 * sc) - (int)(108 * sc), ClientSize.Height - (int)(56 * sc), (int)(108 * sc), (int)(34 * sc));
            _list.Bounds = new Rectangle((int)(24 * sc), (int)(232 * sc), ClientSize.Width - (int)(48 * sc), ClientSize.Height - (int)(232 * sc) - (int)(72 * sc));
            Invalidate();
        }

        public void Rescan()
        {
            if (_busy) return;
            _busy = true;
            Cursor = Cursors.WaitCursor;
            try
            {
                List<DiscoveredDevice> found = DeviceScanner.Scan();
                for (int i = 0; i < found.Count; i++)
                    found[i].Added = _config.Find(found[i].Id) != null;
                _list.Items = found;
                _list.Invalidate();
                _hintText = "已发现 " + found.Count + " 台设备";
            }
            catch { }
            finally
            {
                _busy = false;
                Cursor = Cursors.Default;
            }
        }

        private void Choose(DiscoveredDevice d)
        {
            if (_config.Find(d.Id) != null) return;
            DeviceConfig cfg = new DeviceConfig();
            cfg.Id = d.Id;
            cfg.Name = d.Name;
            cfg.Source = d.Source;
            cfg.Address = d.Address;
            cfg.ManualLevel = 80;
            cfg.LastLevel = -1;
            cfg.Enabled = true;
            cfg.AutoSource = d.Source;
            Result = cfg;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ClearPlaceholder()
        {
            if (_nameBox.Text == Placeholder)
            {
                _nameBox.Text = "";
                _nameBox.ForeColor = Theme.Ink;
            }
        }

        private void AddManual()
        {
            string name = (_nameBox.Text ?? "").Trim();
            if (name.Length == 0 || name == Placeholder)
            {
                _nameBox.Focus();
                return;
            }
            DeviceConfig cfg = new DeviceConfig();
            cfg.Id = "man:" + Guid.NewGuid().ToString("N").Substring(0, 10);
            cfg.Name = name;
            cfg.Source = "";
            cfg.Address = "";
            cfg.ManualLevel = 100;
            cfg.LastLevel = 100;
            cfg.LastUpdate = DateTime.Now;
            cfg.Enabled = true;
            Result = cfg;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_h1 == null)
            {
                _h1 = Theme.F(13f, FontStyle.Bold);
                _h2 = Theme.F(9f);
                _labelFont = Theme.F(9.5f, FontStyle.Bold);
                _hintFont = Theme.F(8.5f);
            }
            float sc = DeviceDpi / 96f;
            Theme.TextLeft(g, "添加设备", _h1, Theme.Ink, new RectangleF(24 * sc, 16 * sc, 300 * sc, 32 * sc));
            Theme.TextLeft(g, "选择已配对的蓝牙设备，或手动添加一台（自定义电量）", _h2, Theme.InkMuted, new RectangleF(24 * sc, 50 * sc, 480 * sc, 22 * sc));

            Theme.TextLeft(g, "手动添加", _labelFont, Theme.InkDim, new RectangleF(24 * sc, 118 * sc, 200 * sc, 22 * sc));

            // 输入框背景
            RectangleF box = new RectangleF(24 * sc, 150 * sc, ClientSize.Width - 48 * sc - 124 * sc, 38 * sc);
            Theme.FillRound(g, box, 9f * sc, Theme.Card);
            Theme.StrokeRound(g, new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1), 9f * sc, Theme.Stroke, 1f);

            Theme.TextLeft(g, "扫描结果", _labelFont, Theme.InkDim, new RectangleF(24 * sc, 206 * sc, 160 * sc, 22 * sc));
            if (!string.IsNullOrEmpty(_hintText))
                Theme.Label(g, _hintText, _hintFont, Theme.InkMuted, new RectangleF(120 * sc, 206 * sc, 300 * sc, 22 * sc), TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            Theme.TextLeft(g, "只显示已与 Windows 配对的设备；请先在系统设置中完成配对。", _hintFont, Theme.InkMuted,
                new RectangleF(24 * sc, ClientSize.Height - 46 * sc, 420 * sc, 22 * sc));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Font[] fonts = new Font[] { _h1, _h2, _labelFont, _hintFont };
                for (int i = 0; i < fonts.Length; i++) if (fonts[i] != null) fonts[i].Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>任务栏图标左键弹出的小面板。</summary>
    internal class PopupForm : Form
    {
        private readonly TrayApp _app;
        private int _hoverRow = -1;
        private Font _h1, _nameFont, _subFont, _pctFont, _linkFont, _iconFont;

        public PopupForm(TrayApp app)
        {
            _app = app;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
        }

        public void Prepare()
        {
            float sc = DeviceDpi / 96f;
            if (sc <= 0) sc = 1f;
            int rows = Math.Max(1, _app.Config.Devices.Count);
            int height = (int)Math.Round((68 + rows * 58 + 46) * sc);
            int width = (int)Math.Round(340 * sc);
            ClientSize = new Size(width, height);

            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Right - width - (int)(12 * sc), wa.Bottom - height - (int)(12 * sc));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Hide();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float sc = DeviceDpi / 96f;
            if (_h1 == null)
            {
                _h1 = Theme.F(10.5f, FontStyle.Bold);
                _nameFont = Theme.F(9.5f);
                _subFont = Theme.F(8f);
                _pctFont = Theme.F(10f, FontStyle.Bold);
                _linkFont = Theme.F(9f);
                _iconFont = Theme.Icon(9.5f);
            }
            using (Pen border = new Pen(Theme.Stroke))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            Theme.TextLeft(g, "BatteryBar", _h1, Theme.Ink, new RectangleF(20 * sc, 14 * sc, 160 * sc, 24 * sc));
            RectangleF settingsBtn = new RectangleF(Width - 20 * sc - 58 * sc, 12 * sc, 58 * sc, 26 * sc);
            Theme.FillRound(g, settingsBtn, 7f * sc, Theme.Card);
            Theme.TextCenter(g, "设置", _linkFont, Theme.InkDim, settingsBtn);

            List<DeviceConfig> devices = _app.Config.Devices;
            if (devices.Count == 0)
            {
                Theme.TextCenter(g, "还没有添加设备", _subFont, Theme.InkMuted, new RectangleF(0, 60 * sc, Width, 40 * sc));
            }

            for (int i = 0; i < devices.Count; i++)
            {
                DeviceConfig d = devices[i];
                float y = (68 + i * 58) * sc;
                RectangleF row = new RectangleF(10 * sc, y, Width - 20 * sc, 52 * sc);
                bool active = d.Id == _app.Config.TrayDeviceId;
                if (i == _hoverRow) Theme.FillRound(g, row, 9f * sc, Theme.Card);
                else if (active) Theme.FillRound(g, row, 9f * sc, Color.FromArgb(30, 36, 48));

                int level = d.Level;
                Color color = Theme.ForLevel(level);
                RectangleF bar = new RectangleF(24 * sc, y + 44 * sc, Width - 48 * sc, 3 * sc);
                Theme.FillRound(g, bar, 1.5f * sc, Theme.Track);
                if (level > 0)
                {
                    RectangleF fill = new RectangleF(bar.X, bar.Y, Math.Max(3 * sc, bar.Width * level / 100f), bar.Height);
                    Theme.FillRound(g, fill, 1.5f * sc, color);
                }
                Theme.TextLeft(g, d.Name, _nameFont, Theme.Ink, new RectangleF(20 * sc, y + 6 * sc, Width - 110 * sc, 22 * sc));
                if (level >= 0 && d.IsCharging)
                    Theme.DrawBolt(g, new RectangleF(Width - 20 * sc - 74 * sc, y + 10 * sc, 12 * sc, 14 * sc), Theme.Amber);
                Theme.Label(g, level < 0 ? "–" : level + "%", _pctFont, color,
                    new RectangleF(Width - 20 * sc - 60 * sc, y + 4 * sc, 60 * sc, 22 * sc), TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                Theme.TextLeft(g, d.Subtitle, _subFont, Theme.InkMuted, new RectangleF(20 * sc, y + 27 * sc, Width - 130 * sc, 16 * sc));
            }

            float footerY = Height - 38 * sc;
            using (Pen sep = new Pen(Theme.StrokeSoft))
                g.DrawLine(sep, 0, footerY - 4 * sc, Width, footerY - 4 * sc);
            Theme.TextLeft(g, "左键打开 · 右键菜单", _subFont, Theme.InkMuted, new RectangleF(20 * sc, footerY, 150 * sc, 22 * sc));
            RectangleF exitBtn = new RectangleF(Width - 20 * sc - 58 * sc, footerY, 58 * sc, 24 * sc);
            Theme.TextCenter(g, "退出", _linkFont, Theme.InkDim, exitBtn);
        }

        private int RowAt(Point p)
        {
            float sc = DeviceDpi / 96f;
            if (p.Y < 68 * sc) return -1;
            int i = (int)((p.Y - 68 * sc) / (58 * sc));
            if (i < 0 || i >= _app.Config.Devices.Count) return -1;
            return i;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int row = RowAt(e.Location);
            if (row != _hoverRow) { _hoverRow = row; Invalidate(); }
            Cursor = row >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverRow = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            float sc = DeviceDpi / 96f;
            if (e.Button == MouseButtons.Left)
            {
                RectangleF settingsBtn = new RectangleF(Width - 20 * sc - 58 * sc, 12 * sc, 58 * sc, 26 * sc);
                RectangleF exitBtn = new RectangleF(Width - 20 * sc - 58 * sc, Height - 38 * sc, 58 * sc, 24 * sc);
                if (settingsBtn.Contains(e.Location))
                {
                    Hide();
                    _app.ShowSettings();
                    return;
                }
                if (exitBtn.Contains(e.Location))
                {
                    _app.ExitApp();
                    return;
                }
                int row = RowAt(e.Location);
                if (row >= 0)
                {
                    _app.SetTrayDevice(_app.Config.Devices[row].Id);
                    Invalidate();
                }
            }
            base.OnMouseDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Font[] fonts = new Font[] { _h1, _nameFont, _subFont, _pctFont, _linkFont, _iconFont };
                for (int i = 0; i < fonts.Length; i++) if (fonts[i] != null) fonts[i].Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>轻量输入框（改名等）。</summary>
    internal class InputBoxForm : Form
    {
        private readonly string _label;
        private TextBox _box;
        private UiButton _ok;
        private UiButton _cancel;
        private CaptionButton _close;
        private Font _title, _labelFont;

        public string Value { get { return (_box.Text ?? "").Trim(); } }

        public InputBoxForm(string title, string label, string initial)
        {
            _label = label;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(420, 200);
            Text = title;

            _box = new TextBox();
            _box.BorderStyle = BorderStyle.None;
            _box.BackColor = Theme.Card;
            _box.ForeColor = Theme.Ink;
            _box.Font = Theme.F(10.5f);
            _box.Text = initial ?? "";

            _ok = new UiButton();
            _ok.Text = "确定";
            _ok.Primary = true;
            _ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };

            _cancel = new UiButton();
            _cancel.Text = "取消";
            _cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            _close = new CaptionButton();
            _close.Glyph = Glyphs.Close;
            _close.Danger = true;
            _close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(_box);
            Controls.Add(_ok);
            Controls.Add(_cancel);
            Controls.Add(_close);

            MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) Native.DragWindow(Handle);
            };
            Load += delegate
            {
                float sc = DeviceDpi / 96f;
                ClientSize = new Size((int)Math.Round(420 * sc), (int)Math.Round(200 * sc));
                _close.Bounds = new Rectangle(ClientSize.Width - (int)(12 * sc) - (int)(42 * sc), (int)(8 * sc), (int)(42 * sc), (int)(32 * sc));
                _box.Bounds = new Rectangle((int)(26 * sc), (int)(96 * sc), ClientSize.Width - (int)(52 * sc), _box.PreferredHeight);
                _ok.Bounds = new Rectangle(ClientSize.Width - (int)(26 * sc) - (int)(92 * sc), (int)(142 * sc), (int)(92 * sc), (int)(34 * sc));
                _cancel.Bounds = new Rectangle(_ok.Left - (int)(8 * sc) - (int)(92 * sc), (int)(142 * sc), (int)(92 * sc), (int)(34 * sc));
                _box.Focus();
                _box.SelectAll();
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_title == null)
            {
                _title = Theme.F(12f, FontStyle.Bold);
                _labelFont = Theme.F(9f);
            }
            float sc = DeviceDpi / 96f;
            Theme.TextLeft(g, Text, _title, Theme.Ink, new RectangleF(26 * sc, 16 * sc, ClientSize.Width - 80 * sc, 30 * sc));
            Theme.TextLeft(g, _label, _labelFont, Theme.InkMuted, new RectangleF(26 * sc, 64 * sc, ClientSize.Width - 52 * sc, 22 * sc));
            RectangleF box = new RectangleF(26 * sc, 94 * sc, ClientSize.Width - 52 * sc, 40 * sc);
            Theme.FillRound(g, box, 9f * sc, Theme.Card);
            Theme.StrokeRound(g, new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1), 9f * sc, Theme.Stroke, 1f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_title != null) _title.Dispose();
                if (_labelFont != null) _labelFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>低电量提醒卡片（自绘，不带焦点）。</summary>
    internal class ToastForm : Form
    {
        private string _device = "";
        private int _level;
        private int _threshold;
        private Action _onClick;
        private Font _title, _name, _hint, _pct, _icon;
        private Timer _life;
        private float _targetOpacity = 1f;
        private int _elapsed;
        private bool _fading;

        public ToastForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            Opacity = 0;
            Cursor = Cursors.Hand;
            _life = new Timer();
            _life.Interval = 40;
            _life.Tick += LifeTick;
            Click += delegate { Hide(); if (_onClick != null) _onClick(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        public void ShowMessage(string device, int level, int threshold, Action onClick)
        {
            _device = device;
            _level = level;
            _threshold = threshold;
            _onClick = onClick;
            _elapsed = 0;
            _fading = false;
            _targetOpacity = 1f;

            float sc = DeviceDpi / 96f;
            if (sc <= 0) sc = 1f;
            ClientSize = new Size((int)Math.Round(380 * sc), (int)Math.Round(104 * sc));
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Right - Width - (int)(14 * sc), wa.Bottom - Height - (int)(14 * sc));
            Opacity = 1;
            Show();
            Invalidate();
            _life.Start();
        }

        private void LifeTick(object sender, EventArgs e)
        {
            _elapsed += _life.Interval;
            if (!_fading && _elapsed > 6500)
            {
                _fading = true;
                _targetOpacity = 0f;
            }
            float d = _targetOpacity - (float)Opacity;
            if (Math.Abs(d) < 0.05f)
            {
                Opacity = _targetOpacity;
                if (_fading)
                {
                    _life.Stop();
                    Hide();
                }
            }
            else
            {
                Opacity = Math.Max(0, Math.Min(1, Opacity + Math.Sign(d) * 0.12));
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float sc = DeviceDpi / 96f;
            if (_title == null)
            {
                _title = Theme.F(9f, FontStyle.Bold);
                _name = Theme.F(10.5f, FontStyle.Bold);
                _hint = Theme.F(8.5f);
                _pct = Theme.F(10.5f, FontStyle.Bold);
                _icon = Theme.Icon(9f);
            }

            RectangleF card = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(g, card, 14f * sc, Color.FromArgb(27, 31, 40));
            Theme.StrokeRound(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 14f * sc, Color.FromArgb(58, 76, 104), 1f);

            Color color = Theme.ForLevel(_level);
            RectangleF ring = new RectangleF(16 * sc, (Height - 56 * sc) / 2f, 56 * sc, 56 * sc);
            Theme.DrawRing(g, RectangleF.Inflate(ring, -3 * sc, -3 * sc), _level / 100f, color, 5f * sc, true);
            Theme.TextCenter(g, _level + "%", _pct, color, ring);

            float left = 90 * sc;
            Theme.TextLeft(g, "电量偏低", _title, Theme.Amber, new RectangleF(left, 18 * sc, 200 * sc, 18 * sc));
            Theme.TextLeft(g, _device, _name, Theme.Ink, new RectangleF(left, 36 * sc, Width - left - 40 * sc, 22 * sc));
            Theme.TextLeft(g, "已低于 " + _threshold + "%，请及时充电", _hint, Theme.InkMuted, new RectangleF(left, 62 * sc, Width - left - 40 * sc, 18 * sc));

            Theme.Label(g, Glyphs.Close, _icon, Theme.InkMuted, new RectangleF(Width - 34 * sc, 12 * sc, 20 * sc, 20 * sc), TextFormatFlags.NoPadding);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.X > Width - 40 * (DeviceDpi / 96f) && e.Y < 40 * (DeviceDpi / 96f))
            {
                _life.Stop();
                Hide();
                return;
            }
            base.OnMouseUp(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_life != null) { _life.Stop(); _life.Dispose(); }
                Font[] fonts = new Font[] { _title, _name, _hint, _pct, _icon };
                for (int i = 0; i < fonts.Length; i++) if (fonts[i] != null) fonts[i].Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>深色右键菜单渲染。</summary>
    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Theme.Ink : Theme.InkDim;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(Theme.Stroke))
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
    }

    internal class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(24, 27, 34); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(24, 27, 34); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(24, 27, 34); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(24, 27, 34); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(42, 48, 62); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(42, 48, 62); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(42, 48, 62); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(42, 48, 62); } }
        public override Color MenuBorder { get { return Theme.Stroke; } }
        public override Color SeparatorDark { get { return Theme.Stroke; } }
        public override Color SeparatorLight { get { return Theme.Stroke; } }
    }
}
