using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BatteryBar
{
    internal class CaptionButton : Surface
    {
        public string Glyph = Glyphs.Close;
        public bool Danger;
        private bool _hover;
        private Font _font;

        public CaptionButton()
        {
            Cursor = Cursors.Hand;
            Size = new Size(40, 30);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_font == null) _font = Theme.Icon(10f);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0, 0, Width, Height);
            if (_hover)
            {
                Color bg = Danger ? Color.FromArgb(196, 62, 62) : Color.FromArgb(38, 44, 56);
                Theme.FillRound(g, new RectangleF(2, 2, Width - 4, Height - 4), 7f * Sc, bg);
            }
            Theme.TextCenter(g, Glyph, _font, _hover ? Color.White : Theme.InkDim, r);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing && _font != null) _font.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>右侧设备详情与全局设置。</summary>
    internal class DetailView : Surface
    {
        private readonly TrayApp _app;
        private readonly UiButton _btnRefresh;
        private readonly UiButton _btnRemove;
        private readonly UiButton _btnRename;
        private readonly ToggleSwitch _toggleNotify;
        private readonly ToggleSwitch _toggleAutoStart;
        private readonly ToggleSwitch _toggleTray;
        private readonly Slider _sliderThreshold;
        private readonly Segmented _segCooldown;
        private readonly Segmented _segPoll;
        private readonly Segmented _segIcon;
        private readonly ToolTip _tips;
        private readonly System.Windows.Forms.Timer _tickTimer;
        private DeviceConfig _device;

        private Font _fName, _fSub, _fSection, _fHint, _fLabel, _fPill, _fBig, _fBigUnit, _fIcon, _fValue;

        public DetailView(TrayApp app)
        {
            _app = app;
            SurfaceColor = Theme.Bg;

            _btnRefresh = new UiButton();
            _btnRefresh.Glyph = Glyphs.Refresh;
            _btnRefresh.Text = "刷新";
            _btnRefresh.Click += delegate { if (_device != null) _app.RefreshDevice(_device); };

            _btnRemove = new UiButton();
            _btnRemove.Text = "移除";
            _btnRemove.Click += delegate { if (_device != null) _app.RemoveDevice(_device); };

            _btnRename = new UiButton();
            _btnRename.Glyph = Glyphs.Edit;
            _btnRename.Click += delegate
            {
                if (_device == null) return;
                using (InputBoxForm box = new InputBoxForm("重命名设备", "设备显示名称", _device.Name))
                {
                    Form owner = FindForm();
                    if (owner != null) box.Icon = owner.Icon;
                    if (box.ShowDialog(owner) == DialogResult.OK && box.Value.Length > 0)
                    {
                        _device.Name = box.Value;
                        _app.SaveConfig();
                        _app.RaiseChanged();
                    }
                }
            };

            _toggleNotify = new ToggleSwitch();
            _toggleNotify.CheckedChanged += delegate
            {
                _app.Config.Notifications = _toggleNotify.Checked;
                _app.SaveConfig();
            };

            _toggleAutoStart = new ToggleSwitch();
            _toggleAutoStart.CheckedChanged += delegate
            {
                _app.Config.AutoStart = _toggleAutoStart.Checked;
                _app.SetAutoStart(_toggleAutoStart.Checked);
                _app.SaveConfig();
            };

            _toggleTray = new ToggleSwitch();
            _toggleTray.CheckedChanged += delegate
            {
                if (_device == null) return;
                if (_toggleTray.Checked) _app.SetTrayDevice(_device.Id);
                else if (_app.Config.TrayDeviceId == _device.Id) _app.SetTrayDevice("");
            };

            _sliderThreshold = new Slider();
            _sliderThreshold.Min = 5;
            _sliderThreshold.Max = 50;
            _sliderThreshold.ValueChanged += delegate
            {
                _app.Config.LowThreshold = _sliderThreshold.Value;
                _app.SaveConfig();
                Invalidate();
            };
            _segCooldown = new Segmented();
            _segCooldown.Items = new string[] { "30 分钟", "2 小时", "6 小时" };
            _segCooldown.SelectedChanged += delegate
            {
                int[] values = new int[] { 30, 120, 360 };
                _app.Config.NotifyCooldownMinutes = values[Math.Max(0, Math.Min(2, _segCooldown.SelectedIndex))];
                _app.SaveConfig();
            };

            // 每分钟重绘一次，让「x 分钟前」按 1 分钟粒度更新
            _tickTimer = new System.Windows.Forms.Timer();
            _tickTimer.Interval = 60000;
            _tickTimer.Tick += delegate { Invalidate(); };
            _tickTimer.Start();

            _segPoll = new Segmented();
            _segPoll.Items = new string[] { "30 秒", "1 分钟", "5 分钟", "10 分钟" };
            _segPoll.SelectedChanged += delegate
            {
                int[] values = new int[] { 30, 60, 300, 600 };
                _app.Config.PollSeconds = values[Math.Max(0, Math.Min(3, _segPoll.SelectedIndex))];
                _app.SaveConfig();
                _app.Kick();
                Invalidate();          // 立刻重绘上方「自动读取 · 每 X」提示
                _app.RaiseChanged();
            };

            _segIcon = new Segmented();
            _segIcon.Items = new string[] { "电池图标", "百分比数字" };
            _segIcon.SelectedChanged += delegate
            {
                _app.Config.IconStyle = _segIcon.SelectedIndex;
                _app.SaveConfig();
                _app.UpdateTrayIcon(true);
            };

            Controls.Add(_btnRefresh);
            Controls.Add(_btnRemove);
            Controls.Add(_btnRename);
            _tips = new ToolTip();
            _tips.SetToolTip(_btnRename, "重命名设备");
            _tips.SetToolTip(_btnRefresh, "立即重新读取电量");
            _tips.SetToolTip(_btnRemove, "从列表中移除该设备");
            Controls.Add(_toggleNotify);
            Controls.Add(_toggleAutoStart);
            Controls.Add(_toggleTray);
            Controls.Add(_sliderThreshold);
            Controls.Add(_segCooldown);
            Controls.Add(_segPoll);
            Controls.Add(_segIcon);
        }

        public void Bind(DeviceConfig d)
        {
            _device = d;
            SyncFromConfig();
            Invalidate();
        }

        public void SyncFromConfig()
        {
            _toggleNotify.SetCheckedSilent(_app.Config.Notifications);
            _toggleAutoStart.SetCheckedSilent(_app.Config.AutoStart);
            _sliderThreshold.SetValueSilent(_app.Config.LowThreshold);
            _segCooldown.SetSelectedSilent(Index(_app.Config.NotifyCooldownMinutes, new int[] { 30, 120, 360 }));
            _segPoll.SetSelectedSilent(Index(_app.Config.PollSeconds, new int[] { 30, 60, 300, 600 }));
            _segIcon.SetSelectedSilent(Math.Max(0, Math.Min(1, _app.Config.IconStyle)));
            _toggleTray.SetCheckedSilent(_device != null && _device.Id == _app.Config.TrayDeviceId);
            LayoutChildren();
            Invalidate();
        }

        private static int Index(int value, int[] options)
        {
            int best = 0;
            int delta = int.MaxValue;
            for (int i = 0; i < options.Length; i++)
            {
                int d = Math.Abs(options[i] - value);
                if (d < delta) { delta = d; best = i; }
            }
            return best;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            if (_btnRefresh == null) return;
            bool has = _device != null;
            foreach (Control c in Controls) c.Visible = has;
            if (!has) return;

            int w = Width;
            _btnRemove.Bounds = new Rectangle(Px(w - 26 - 78), Px(20), Px(78), Px(32));
            _btnRefresh.Bounds = new Rectangle(Px(w - 26 - 78 - 8 - 78), Px(20), Px(78), Px(32));
            _btnRename.Bounds = new Rectangle(Px(w - 26 - 78 - 8 - 78 - 8 - 32), Px(20), Px(32), Px(32));

            _toggleTray.Bounds = new Rectangle(Px(252), Px(142), Px(46), Px(26));
            _toggleNotify.Bounds = new Rectangle(Px(w - 26 - 46), Px(206 + 14), Px(46), Px(26));
            _sliderThreshold.Bounds = new Rectangle(Px(w - 22 - 210 - 64), Px(206 + 61), Px(210), Px(24));
            _segCooldown.Bounds = new Rectangle(Px(w - 22 - 210), Px(206 + 100), Px(210), Px(28));

            _toggleAutoStart.Bounds = new Rectangle(Px(w - 26 - 46), Px(372 + 44), Px(46), Px(26));
            _segPoll.Bounds = new Rectangle(Px(w - 22 - 210), Px(372 + 96), Px(210), Px(28));
            _segIcon.Bounds = new Rectangle(Px(w - 22 - 210), Px(372 + 148), Px(210), Px(28));

        }

        private void EnsureFonts()
        {
            if (_fName != null) return;
            _fName = Theme.F(13.5f, FontStyle.Bold);
            _fSub = Theme.F(9f);
            _fSection = Theme.F(10.5f, FontStyle.Bold);
            _fHint = Theme.F(8.5f);
            _fLabel = Theme.F(9.5f);
            _fPill = Theme.F(8f);
            _fBig = Theme.F(23f, FontStyle.Bold);
            _fBigUnit = Theme.F(9f, FontStyle.Bold);
            _fIcon = Theme.Icon(10f);
            _fValue = Theme.F(9f, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureFonts();
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (_device == null)
            {
                using (Font icon = Theme.Icon(34f))
                using (Font t1 = Theme.F(12f, FontStyle.Bold))
                using (Font t2 = Theme.F(9f))
                {
                    float cy = Height / 2f - 60 * Sc;
                    Theme.TextCenter(g, Glyphs.Battery, icon, Color.FromArgb(58, 68, 86), new RectangleF(0, cy, Width, 56 * Sc));
                    Theme.TextCenter(g, "选择或添加一台设备", t1, Theme.InkDim, new RectangleF(0, cy + 66 * Sc, Width, 26 * Sc));
                    Theme.TextCenter(g, "在这里查看电量、设置低电量提醒", t2, Theme.InkMuted, new RectangleF(0, cy + 96 * Sc, Width, 22 * Sc));
                }
                return;
            }

            PaintHero(g);
            PaintNotificationCard(g);
            PaintGeneralCard(g);
        }

        private void PaintHero(Graphics g)
        {
            int w = Width;
            float cardH = 196 * Sc;
            RectangleF card = new RectangleF(0, 0, w, cardH);
            Theme.FillRound(g, card, 16f * Sc, Theme.Card);
            Theme.StrokeRound(g, new RectangleF(0.5f, 0.5f, w - 1, cardH - 1), 16f * Sc, Theme.StrokeSoft, 1f);

            int level = _device.Level;
            Color color = Theme.ForLevel(level);
            float ringSize = 112 * Sc;
            RectangleF ring = new RectangleF(24 * Sc, 38 * Sc, ringSize, ringSize);
            Theme.DrawRing(g, RectangleF.Inflate(ring, -5 * Sc, -5 * Sc), level < 0 ? 0f : level / 100f, color, 9f * Sc, true);
            if (level < 0)
            {
                using (Font dash = Theme.F(18f, FontStyle.Bold))
                    Theme.TextCenter(g, "–", dash, Theme.InkMuted, ring);
            }
            else
            {
                Size unitSize = TextRenderer.MeasureText(g, "%", _fBigUnit, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                Size numSize = TextRenderer.MeasureText(g, level.ToString(), _fBig, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                float total = numSize.Width + unitSize.Width + 2 * Sc;
                float cx = ring.X + ring.Width / 2f;
                float baseY = ring.Y + ring.Height / 2f - numSize.Height / 2f;
                Theme.Label(g, level.ToString(), _fBig, color,
                    new RectangleF(cx - total / 2f, baseY, numSize.Width, numSize.Height), TextFormatFlags.NoPadding);
                Theme.Label(g, "%", _fBigUnit, Theme.Alpha(color, 200),
                    new RectangleF(cx - total / 2f + numSize.Width + 2 * Sc, baseY + numSize.Height - unitSize.Height - 2 * Sc, unitSize.Width, unitSize.Height),
                    TextFormatFlags.NoPadding);
            }

            float textLeft = 160 * Sc;
            float textWidth = w - textLeft - 232 * Sc;
            Theme.TextLeft(g, _device.Name, _fName, Theme.Ink, new RectangleF(textLeft, 18 * Sc, textWidth, 30 * Sc));

            string state = _device.Busy ? "正在读取…" : (string.IsNullOrEmpty(_device.ErrorText) ? _device.StatusText : _device.ErrorText);
            Color stateColor = string.IsNullOrEmpty(_device.ErrorText) ? Theme.InkDim : Color.FromArgb(228, 140, 122);
            if (_device.Level < 0 && _device.UnknownSource) stateColor = Theme.InkMuted;
            Theme.TextLeft(g, state, _fSub, stateColor, new RectangleF(textLeft, 52 * Sc, textWidth + 30 * Sc, 20 * Sc));

            float pillX = textLeft;
            pillX = DrawPill(g, pillX, 76 * Sc, Sources.Display(_device.Source), Theme.Ink, Color.FromArgb(40, 46, 60),
                _device.Source == Sources.Unknown || string.IsNullOrEmpty(_device.Source) ? Glyphs.Warning : Glyphs.Lightning);
            pillX += 8 * Sc;
            string updated = _device.LastUpdateTicks <= 0 ? "从未更新" : DeviceScanner.Human(_device.LastUpdate);
            DrawPill(g, pillX, 76 * Sc, updated, Theme.InkMuted, Color.FromArgb(32, 37, 47), null);

            // 充电中：闪电标志
            bool lowNoise = _device.Source == Sources.Hidpp && Hidpp.LogitechSoftwareRunning();
            int refreshSeconds = _app.Config.PollSeconds;
            string refreshHint;
            if (lowNoise)
                refreshHint = "自动读取 · 活跃时每 15 秒 / 空闲每 " + Duration(refreshSeconds) + "（低干扰模式）";
            else
                refreshHint = "自动读取 · 每 " + Duration(refreshSeconds) + " 刷新一次";

            if (_device.Level >= 0 && _device.IsCharging)
            {
                float boltSize = 15 * Sc;
                RectangleF bolt = new RectangleF(textLeft, 104 * Sc, boltSize, boltSize + 2 * Sc);
                Theme.DrawBolt(g, bolt, Theme.Amber);
                Theme.TextLeft(g, "正在充电", _fHint, Theme.Amber, new RectangleF(textLeft + boltSize + 5 * Sc, 103 * Sc, 90 * Sc, 22 * Sc));
                Theme.TextLeft(g, refreshHint, _fHint, Theme.InkMuted,
                    new RectangleF(textLeft + boltSize + 5 * Sc + 66 * Sc, 106 * Sc, 240 * Sc, 20 * Sc));
            }
            else if (_device.Level >= 0)
            {
                Theme.TextLeft(g, refreshHint, _fHint, Theme.InkMuted,
                    new RectangleF(textLeft, 106 * Sc, 400 * Sc, 22 * Sc));
            }
            else
            {
                Theme.TextLeft(g, "无法识别 · " + DeviceRules.Hint(_device.Name), _fHint, Theme.InkMuted,
                    new RectangleF(textLeft, 106 * Sc, 400 * Sc, 22 * Sc));
            }

            Theme.TextLeft(g, "在任务栏显示", _fHint, Theme.InkMuted, new RectangleF(textLeft, 144 * Sc, 80 * Sc, 22 * Sc));
        }

        private static string Duration(int seconds)
        {
            if (seconds < 60) return seconds + " 秒";
            if (seconds < 3600) return (seconds / 60) + " 分钟";
            return (seconds / 3600) + " 小时";
        }

        private float DrawPill(Graphics g, float x, float y, string text, Color fg, Color bg, string glyph)
        {
            float h = 22 * Sc;
            Size ts = TextRenderer.MeasureText(g, text, _fPill, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            Size gs = Size.Empty;
            if (!string.IsNullOrEmpty(glyph))
                gs = TextRenderer.MeasureText(g, glyph, _fIcon, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            float contentW = ts.Width + (gs.Width > 0 ? gs.Width + 5 * Sc : 0);
            float w = contentW + 22 * Sc;
            RectangleF pill = new RectangleF(x, y, w, h);
            Theme.FillRound(g, pill, h / 2f, bg);
            float cx = x + 11 * Sc;
            if (gs.Width > 0)
            {
                Theme.Label(g, glyph, _fIcon, fg, new RectangleF(cx, y, gs.Width + 2 * Sc, h), TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                cx += gs.Width + 5 * Sc;
            }
            Theme.Label(g, text, _fPill, fg, new RectangleF(cx, y, ts.Width + 8 * Sc, h), TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return x + w;
        }

        private void PaintNotificationCard(Graphics g)
        {
            int w = Width;
            float top = 206 * Sc;
            float cardH = 150 * Sc;
            RectangleF card = new RectangleF(0, top, w, cardH);
            Theme.FillRound(g, card, 16f * Sc, Theme.Card);
            Theme.StrokeRound(g, new RectangleF(0.5f, top + 0.5f, w - 1, cardH - 1), 16f * Sc, Theme.StrokeSoft, 1f);

            DrawSectionTitle(g, Glyphs.Bell, "低电量提醒", 22 * Sc, top + 16 * Sc, w);
            Theme.TextLeft(g, "电量低于阈值时在任务栏弹出提醒", _fHint, Theme.InkMuted, new RectangleF(48 * Sc, top + 40 * Sc, w - 320 * Sc, 18 * Sc));

            Theme.TextLeft(g, "低电量阈值", _fLabel, Theme.Ink, new RectangleF(24 * Sc, top + 64 * Sc, 120 * Sc, 22 * Sc));
            Theme.Label(g, _app.Config.LowThreshold + "%", _fValue, Theme.Amber,
                new RectangleF(w - 22 * Sc - 58 * Sc, top + 61 * Sc, 50 * Sc, 24 * Sc),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            Theme.TextLeft(g, "提醒间隔", _fLabel, Theme.Ink, new RectangleF(24 * Sc, top + 104 * Sc, 120 * Sc, 22 * Sc));
        }

        private void PaintGeneralCard(Graphics g)
        {
            int w = Width;
            float top = 372 * Sc;
            float cardH = 190 * Sc;
            RectangleF card = new RectangleF(0, top, w, cardH);
            Theme.FillRound(g, card, 16f * Sc, Theme.Card);
            Theme.StrokeRound(g, new RectangleF(0.5f, top + 0.5f, w - 1, cardH - 1), 16f * Sc, Theme.StrokeSoft, 1f);

            DrawSectionTitle(g, Glyphs.Settings, "通用设置", 22 * Sc, top + 16 * Sc, w);

            Theme.TextLeft(g, "开机自动启动", _fLabel, Theme.Ink, new RectangleF(48 * Sc, top + 47 * Sc, 180 * Sc, 22 * Sc));
            Theme.TextLeft(g, "刷新间隔", _fLabel, Theme.Ink, new RectangleF(48 * Sc, top + 99 * Sc, 180 * Sc, 22 * Sc));
            Theme.TextLeft(g, "任务栏图标", _fLabel, Theme.Ink, new RectangleF(48 * Sc, top + 151 * Sc, 180 * Sc, 22 * Sc));
        }

        private void DrawSectionTitle(Graphics g, string glyph, string title, float x, float y, int w)
        {
            Theme.Label(g, glyph, _fIcon, Theme.Accent, new RectangleF(x, y, 22 * Sc, 24 * Sc), TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            Theme.TextLeft(g, title, _fSection, Theme.Ink, new RectangleF(x + 26 * Sc, y, w - 200 * Sc, 24 * Sc));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Font[] fonts = new Font[] { _fName, _fSub, _fSection, _fHint, _fLabel, _fPill, _fBig, _fBigUnit, _fIcon, _fValue };
                for (int i = 0; i < fonts.Length; i++) if (fonts[i] != null) fonts[i].Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal class MainForm : Form
    {
        private readonly TrayApp _app;
        private readonly DeviceList _list;
        private readonly DetailView _detail;
        private readonly UiButton _btnAdd;
        private readonly CaptionButton _btnClose;
        private readonly CaptionButton _btnMin;
        private Font _titleFont, _subFont, _badgeFont, _footerFont, _h1Font;

        public MainForm(TrayApp app)
        {
            _app = app;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            KeyPreview = true;
            Text = "BatteryBar 电池助手";
            AutoScaleMode = AutoScaleMode.None;
            try { Icon = TrayIconFactory.Brand(); } catch { }

            _list = new DeviceList();
            _list.SurfaceColor = Theme.BgSide;
            _list.SelectionChanged += delegate (object s, EventArgs e) { OnSelect((DeviceConfig)s); };
            _list.TrayToggleRequested += delegate (object s, EventArgs e) { _app.SetTrayDevice(((DeviceConfig)s).Id); };

            _detail = new DetailView(_app);

            _btnAdd = new UiButton();
            _btnAdd.Glyph = Glyphs.Add;
            _btnAdd.Text = "添加设备";
            _btnAdd.Primary = true;
            _btnAdd.Click += delegate { OpenAddDevice(); };

            _btnClose = new CaptionButton();
            _btnClose.Glyph = Glyphs.Close;
            _btnClose.Danger = true;
            _btnClose.Click += delegate { Hide(); };

            _btnMin = new CaptionButton();
            _btnMin.Glyph = Glyphs.Minimize;
            _btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };

            Controls.Add(_list);
            Controls.Add(_detail);
            Controls.Add(_btnAdd);
            Controls.Add(_btnClose);
            Controls.Add(_btnMin);

            MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.Y < TitleHeight) Native.DragWindow(Handle);
            };

            _app.Changed += delegate { RefreshAll(); };
        }

        private float TitleHeight { get { return 50 * (DeviceDpi / 96f); } }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyScale();
            RefreshAll();
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
            if (sc <= 0) sc = 1f;
            ClientSize = new Size((int)Math.Round(980 * sc), (int)Math.Round(680 * sc));
            SetRoundedRegion();
            LayoutUi();
        }

        private void LayoutUi()
        {
            float sc = DeviceDpi / 96f;
            int leftWidth = (int)Math.Round(312 * sc);
            int titleH = (int)Math.Round(50 * sc);
            int listTop = (int)Math.Round(112 * sc);
            int listBottom = ClientSize.Height - (int)Math.Round(76 * sc);
            _list.Bounds = new Rectangle((int)Math.Round(14 * sc), listTop, leftWidth - (int)Math.Round(28 * sc), Math.Max(80, listBottom - listTop));
            _detail.Bounds = new Rectangle(leftWidth + (int)Math.Round(24 * sc), titleH + (int)Math.Round(18 * sc),
                ClientSize.Width - leftWidth - (int)Math.Round(48 * sc), ClientSize.Height - titleH - (int)Math.Round(42 * sc));
            _btnAdd.Bounds = new Rectangle(leftWidth - (int)Math.Round(24 * sc) - (int)Math.Round(112 * sc), (int)Math.Round(64 * sc), (int)Math.Round(112 * sc), (int)Math.Round(34 * sc));

            int capW = (int)Math.Round(42 * sc);
            int capH = (int)Math.Round(32 * sc);
            _btnClose.Bounds = new Rectangle(ClientSize.Width - (int)Math.Round(12 * sc) - capW, (int)Math.Round(9 * sc), capW, capH);
            _btnMin.Bounds = new Rectangle(_btnClose.Left - capW - (int)Math.Round(2 * sc), (int)Math.Round(9 * sc), capW, capH);

            Invalidate();
        }

        private void SetRoundedRegion()
        {
            if (Environment.OSVersion.Version.Build >= 22000) return;
            using (GraphicsPath path = Theme.Rounded(new RectangleF(0, 0, Width, Height), 14f * (DeviceDpi / 96f)))
                Region = new Region(path);
        }

        public void RefreshAll()
        {
            if (IsDisposed) return;
            List<DeviceConfig> devices = _app.Config.Devices;
            DeviceConfig selected = _app.Config.Find(_app.Config.SelectedDeviceId);
            if (selected == null && devices.Count > 0)
            {
                selected = devices[0];
                _app.Config.SelectedDeviceId = selected.Id;
            }
            _list.Devices = devices;
            _list.SelectedId = selected == null ? null : selected.Id;
            _list.TrayId = _app.Config.TrayDeviceId;
            _detail.Bind(selected);
            _list.Invalidate();
            Invalidate();
        }

        private void OnSelect(DeviceConfig device)
        {
            _app.Config.SelectedDeviceId = device.Id;
            _app.SaveConfig();
            _detail.Bind(device);
            _list.Invalidate();
            Invalidate();
        }

        public void OpenAddDevice()
        {
            using (AddDeviceForm dlg = new AddDeviceForm(_app.Config))
            {
                dlg.Icon = Icon;
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
                {
                    _app.AddConfig(dlg.Result);
                    DeviceConfig d = _app.Config.Find(dlg.Result.Id);
                    if (d != null) _detail.Bind(d);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            float sc = DeviceDpi / 96f;
            if (_titleFont == null)
            {
                _titleFont = Theme.F(10.5f, FontStyle.Bold);
                _subFont = Theme.F(8.5f);
                _badgeFont = Theme.Icon(8.5f);
                _footerFont = Theme.F(8.5f);
                _h1Font = Theme.F(12.5f, FontStyle.Bold);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int leftWidth = (int)Math.Round(312 * sc);
            using (SolidBrush side = new SolidBrush(Theme.BgSide))
                g.FillRectangle(side, 0, 0, leftWidth, ClientSize.Height);
            using (Pen pen = new Pen(Color.FromArgb(27, 32, 42)))
                g.DrawLine(pen, leftWidth, (int)(50 * sc), leftWidth, ClientSize.Height);

            float iconSize = 22 * sc;
            RectangleF iconRect = new RectangleF(24 * sc, (50 * sc - iconSize) / 2f, iconSize, iconSize);
            using (LinearGradientBrush brush = new LinearGradientBrush(iconRect, Color.FromArgb(98, 162, 255), Color.FromArgb(44, 96, 214), LinearGradientMode.ForwardDiagonal))
            using (GraphicsPath path = Theme.Rounded(iconRect, iconSize * 0.26f))
                g.FillPath(brush, path);
            Theme.Label(g, Glyphs.Battery, _badgeFont, Color.White, iconRect,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            Theme.TextLeft(g, "BatteryBar", _titleFont, Theme.Ink, new RectangleF(iconSize + 34 * sc, 8 * sc, 140 * sc, 34 * sc));
            Theme.TextLeft(g, "电池助手", _subFont, Theme.InkMuted, new RectangleF(leftWidth - 110 * sc, 9 * sc, 100 * sc, 32 * sc));

            Theme.TextLeft(g, "我的设备", _h1Font, Theme.Ink, new RectangleF(24 * sc, 62 * sc, 160 * sc, 34 * sc));

            int count = _app.Config.Devices.Count;
            int autoCount = 0;
            for (int i = 0; i < _app.Config.Devices.Count; i++)
                if (_app.Config.Devices[i].IsAuto) autoCount++;
            string footer = count == 0 ? "暂无设备" : "共 " + count + " 台设备";
            if (autoCount > 0) footer += " · " + autoCount + " 台自动读取";
            string trayName = "未选择";
            DeviceConfig tray = _app.Config.TrayDevice;
            if (tray != null) trayName = tray.Name + (tray.Level >= 0 ? " " + tray.Level + "%" : "");
            Theme.TextLeft(g, "任务栏显示：" + trayName, _footerFont, Theme.InkDim,
                new RectangleF(24 * sc, ClientSize.Height - (int)(36 * sc), leftWidth - 48 * sc, 20 * sc));
            Theme.TextLeft(g, footer, _footerFont, Theme.InkMuted,
                new RectangleF(24 * sc, ClientSize.Height - (int)(58 * sc), leftWidth - 48 * sc, 20 * sc));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_list.Bounds.Contains(PointToClient(MousePosition)))
                _list.ScrollBy(e.Delta);
            base.OnMouseWheel(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Font[] fonts = new Font[] { _titleFont, _subFont, _badgeFont, _footerFont, _h1Font };
                for (int i = 0; i < fonts.Length; i++) if (fonts[i] != null) fonts[i].Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
