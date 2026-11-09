using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BatteryBar
{
    internal class TrayApp : ApplicationContext
    {
        public AppConfig Config { get; private set; }

        public event EventHandler Changed;

        private readonly bool _startHidden;
        private readonly NotifyIcon _notify;
        private readonly ContextMenuStrip _menu;
        private MainForm _form;
        private PopupForm _popup;
        private ToastForm _toast;
        private readonly SynchronizationContext _ui;
        private readonly Thread _worker;
        private readonly ManualResetEvent _wake = new ManualResetEvent(false);
        private readonly object _sync = new object();
        private readonly List<string> _force = new List<string>();
        private volatile bool _running = true;
        private readonly List<Icon> _iconPool = new List<Icon>();
        private string _lastIconKey = "";
        private readonly Dictionary<string, DateTime> _lastNotified = new Dictionary<string, DateTime>();
        private readonly System.Windows.Forms.Timer _startupTimer;
        private bool _exiting;

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "BatteryBar";

        public TrayApp(bool startHidden)
        {
            _startHidden = startHidden;
            _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            bool firstRun = !System.IO.File.Exists(ConfigStore.FilePath);
            Config = ConfigStore.Load();
            if (firstRun) Config.AutoStart = true;
            SetAutoStart(Config.AutoStart);
            Config.AutoStart = IsAutoStartEnabled();
            if (firstRun) ConfigStore.Save(Config);

            _menu = new ContextMenuStrip();
            _menu.Renderer = new DarkMenuRenderer();
            _menu.BackColor = Color.FromArgb(24, 27, 34);
            _menu.ForeColor = Theme.InkDim;
            _menu.ShowImageMargin = false;
            _menu.Opening += delegate { BuildMenu(); };

            _notify = new NotifyIcon();
            _notify.Visible = true;
            _notify.Text = "BatteryBar";
            _notify.ContextMenuStrip = _menu;
            _notify.MouseClick += NotifyClick;
            _notify.DoubleClick += delegate { ShowSettings(); };
            _notify.BalloonTipClicked += delegate { ShowSettings(); };

            UpdateTrayIcon(true);

            _worker = new Thread(WorkerLoop);
            _worker.IsBackground = true;
            _worker.Start();

            _startupTimer = new System.Windows.Forms.Timer();
            _startupTimer.Interval = 300;
            _startupTimer.Tick += delegate
            {
                _startupTimer.Stop();
                _startupTimer.Dispose();
                if (!_startHidden) ShowSettings();
            };
            _startupTimer.Start();
        }

        private void NotifyClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Clicks == 1) ShowPopup();
        }

        public void ShowPopup()
        {
            if (_popup == null || _popup.IsDisposed) _popup = new PopupForm(this);
            if (_popup.Visible) { _popup.Hide(); return; }
            _popup.Prepare();
            _popup.Show();
            _popup.Activate();
        }

        /// <summary>启动后自动弹出托盘面板（用于截图/演示）。</summary>
        public void ShowPopupSoon()
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 700;
            t.Tick += delegate { t.Stop(); t.Dispose(); ShowPopup(); };
            t.Start();
        }

        /// <summary>调试用：立即显示低电量提醒卡片。</summary>
        public void DebugToast()
        {
            Post(delegate
            {
                try
                {
                    if (_toast == null || _toast.IsDisposed) _toast = new ToastForm();
                    _toast.ShowMessage("WH-CH700N", 12, 20, delegate { ShowSettings(); });
                }
                catch (Exception ex)
                {
                    try { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bb_toast_error.txt"), ex.ToString()); }
                    catch { }
                }
            });
        }

        /// <summary>调试用：直接打开「添加设备」对话框。</summary>
        public void OpenAddDeviceSoon()
        {
            ShowSettings();
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 600;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                if (_form != null && _form.IsHandleCreated) _form.OpenAddDevice();
            };
            t.Start();
        }

        /// <summary>调试用：在鼠标位置打开右键菜单。</summary>
        public void DebugMenu()
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 900;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                BuildMenu();
                _menu.Show(Cursor.Position);
            };
            t.Start();
        }

        public void ShowSettingsAsync()
        {
            Post(delegate { ShowSettings(); });
        }

        public void ShowSettings()
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new MainForm(this);
                _form.Show();
            }
            else
            {
                _form.Show();
                if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
                _form.Activate();
            }
        }

        public void BuildMenu()
        {
            _menu.Items.Clear();
            List<DeviceConfig> devices = Config.Devices;
            if (devices.Count == 0)
            {
                ToolStripMenuItem empty = new ToolStripMenuItem("尚未添加设备");
                empty.Enabled = false;
                _menu.Items.Add(empty);
            }
            for (int i = 0; i < devices.Count; i++)
            {
                DeviceConfig d = devices[i];
                string label = d.Name + (d.Level >= 0 ? "   " + d.Level + "%" : "   –");
                ToolStripMenuItem item = new ToolStripMenuItem(label);
                item.Checked = d.Id == Config.TrayDeviceId;
                item.Tag = d;
                item.Click += delegate (object s, EventArgs e)
                {
                    ToolStripMenuItem mi = (ToolStripMenuItem)s;
                    SetTrayDevice(((DeviceConfig)mi.Tag).Id);
                };
                _menu.Items.Add(item);
            }
            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem add = new ToolStripMenuItem("添加设备…");
            add.Click += delegate
            {
                ShowSettings();
                if (_form != null && _form.IsHandleCreated)
                    _form.BeginInvoke((MethodInvoker)delegate { _form.OpenAddDevice(); });
            };
            _menu.Items.Add(add);

            ToolStripMenuItem settings = new ToolStripMenuItem("设置…");
            settings.Click += delegate { ShowSettings(); };
            _menu.Items.Add(settings);

            ToolStripMenuItem auto = new ToolStripMenuItem("开机自动启动");
            auto.Checked = Config.AutoStart;
            auto.Click += delegate
            {
                Config.AutoStart = !Config.AutoStart;
                SetAutoStart(Config.AutoStart);
                ConfigStore.Save(Config);
                RaiseChanged();
            };
            _menu.Items.Add(auto);

            _menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exit = new ToolStripMenuItem("退出 BatteryBar");
            exit.Click += delegate { ExitApp(); };
            _menu.Items.Add(exit);
        }

        public void UpdateTrayIcon() { UpdateTrayIcon(false); }

        public void UpdateTrayIcon(bool force)
        {
            DeviceConfig d = Config.TrayDevice;
            int level = d == null ? -1 : d.Level;
            bool online = d == null ? true : (level >= 0 && (d.IsAuto ? (d.LastConnected || string.IsNullOrEmpty(d.ErrorText)) : true));
            bool charging = d != null && d.Charging;
            string key = level + "|" + Config.IconStyle + "|" + online + "|" + charging + "|" + (d == null ? "" : d.Id);
            if (!force && key == _lastIconKey) return;
            _lastIconKey = key;
            int style = Config.IconStyle;
            string name = d == null ? "" : d.Name;
            Post(delegate
            {
                try
                {
                    Icon icon = TrayIconFactory.Render(level, style, online, charging);
                    _notify.Icon = icon;
                    _iconPool.Add(icon);
                    while (_iconPool.Count > 6)
                    {
                        Icon old = _iconPool[0];
                        _iconPool.RemoveAt(0);
                        try { old.Dispose(); } catch { }
                    }
                    string text = "BatteryBar";
                    if (name.Length > 0) text = name + (level >= 0 ? " · " + level + "%" : " · 未读数");
                    _notify.Text = text.Length > 62 ? text.Substring(0, 62) : text;
                }
                catch { }
            });
        }

        public void SetTrayDevice(string id)
        {
            Config.SetTrayDevice(id);
            ConfigStore.Save(Config);
            UpdateTrayIcon(true);
            RaiseChanged();
        }

        public void AddConfig(DeviceConfig cfg)
        {
            if (cfg == null) return;
            if (Config.Find(cfg.Id) != null) return;
            Config.Devices.Add(cfg);
            Config.SelectedDeviceId = cfg.Id;
            if (string.IsNullOrEmpty(Config.TrayDeviceId) || Config.Devices.Count == 1)
                Config.SetTrayDevice(cfg.Id);
            ConfigStore.Save(Config);
            RefreshDevice(cfg);
            UpdateTrayIcon(true);
            RaiseChanged();
        }

        public void RemoveDevice(DeviceConfig d)
        {
            if (d == null) return;
            Config.Devices.Remove(d);
            if (Config.TrayDeviceId == d.Id)
                Config.SetTrayDevice(Config.Devices.Count > 0 ? Config.Devices[0].Id : "");
            if (Config.SelectedDeviceId == d.Id)
                Config.SelectedDeviceId = Config.Devices.Count > 0 ? Config.Devices[0].Id : "";
            ConfigStore.Save(Config);
            UpdateTrayIcon(true);
            RaiseChanged();
        }

        public void RefreshDevice(DeviceConfig d)
        {
            if (d == null) return;
            lock (_sync) { if (!_force.Contains(d.Id)) _force.Add(d.Id); }
            d.Busy = true;
            _wake.Set();
            RaiseChanged();
        }

        public void Kick() { _wake.Set(); }

        public void NotifyDeviceUpdated(DeviceConfig d)
        {
            UpdateTrayIcon(true);
            RaiseChanged();
        }

        public void SaveConfig() { ConfigStore.Save(Config); }

        public void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler == null) return;
            Post(delegate { handler(this, EventArgs.Empty); });
        }

        private void Post(Action action)
        {
            try
            {
                if (_ui != null) _ui.Post(delegate { try { action(); } catch { } }, null);
                else action();
            }
            catch { }
        }

        private void WorkerLoop()
        {
            while (_running)
            {
                List<DeviceConfig> snapshot;
                List<string> forced;
                lock (_sync)
                {
                    try { snapshot = new List<DeviceConfig>(Config.Devices); }
                    catch { snapshot = new List<DeviceConfig>(); }
                    forced = new List<string>(_force);
                    _force.Clear();
                }

                int idleMs = Native.IdleMilliseconds();
                bool userActive = idleMs < 6000;   // 6 秒内有键鼠输入 = 用户刚动过设备

                bool any = false;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    if (!_running) break;
                    DeviceConfig d = snapshot[i];
                    if (!d.Enabled) continue;
                    bool forcedThis = forced.Contains(d.Id);

                    double sinceAttempt = d.LastAttemptTicks <= 0
                        ? double.MaxValue
                        : (DateTime.UtcNow.Ticks - d.LastAttemptTicks) / 10000000.0;

                    // 罗技官方软件在跑时，两边共用同一个 HID++ 通道，频繁请求会互相打断应答
                    // （曾导致 G HUB 把 0x0F 读成 15%）。折中方案：
                    //   · 你在用电脑（15 秒内有键鼠输入）→ 每 15 秒轻量探测一次，醒来后立刻刷新
                    //   · 你人不在 → 退回 10 分钟一次，完全不打扰
                    bool logitechBusy = d.Source == Sources.Hidpp && Hidpp.LogitechSoftwareRunning();

                    if (!forcedThis)
                    {
                        double? gap = null;
                        if (logitechBusy)
                        {
                            // 活跃时固定 15 秒探测；空闲时用「刷新间隔」设置（方便对比不同档位）
                            gap = userActive ? 15.0 : Math.Max(30.0, Config.PollSeconds);
                        }
                        else if (d.SleepPending)
                        {
                            // 无官方软件时：休眠后温和重试 5s → 15s → 30s → 60s
                            if (!userActive && d.SleepRetries < 40) continue;
                            if (d.SleepRetries <= 1) gap = 5.0;
                            else if (d.SleepRetries <= 3) gap = 15.0;
                            else if (d.SleepRetries <= 6) gap = 30.0;
                            else gap = 60.0;
                        }
                        else
                        {
                            gap = Config.PollSeconds;
                        }
                        if (gap.HasValue && sinceAttempt < gap.Value) continue;
                    }

                    any = true;
                    ReadDeviceSafe(d);
                }

                if (!_running) break;
                _wake.WaitOne(any ? 400 : 800);
                _wake.Reset();
            }
        }

        /// <summary>
        /// 带看门狗的读取：设备休眠 + 官方软件同时占用通道时，个别 HID 请求可能长时间不返回。
        /// 放到后台线程读取，超时就放弃这次并清除「正在读取…」，保证界面永远不会卡死。
        /// </summary>
        private void ReadDeviceSafe(DeviceConfig d)
        {
            System.Threading.Thread worker = new System.Threading.Thread(delegate() { ReadDevice(d); });
            worker.IsBackground = true;
            worker.Name = "BatteryBar-Read";
            worker.Start();
            if (!worker.Join(6000))
            {
                d.Busy = false;
                d.SleepPending = true;
                if (d.SleepRetries < 1000) d.SleepRetries++;
                d.LastAttemptTicks = DateTime.UtcNow.Ticks;
                d.StatusText = "读取超时 · 稍后重试";
                d.ErrorText = null;
                RaiseChanged();
            }
        }

        private void ReadDevice(DeviceConfig d)
        {
            int oldLevel = d.Level;
            d.LastAttemptTicks = DateTime.UtcNow.Ticks;
            Reading r;
            try
            {
                r = BatteryResolver.Read(d);
            }
            catch (Exception ex)
            {
                r = Reading.Fail("读取异常：" + ex.Message, false);
            }
            d.Busy = false;

            if (r.Ok)
            {
                d.LastLevel = r.Level;
                d.LastConnected = r.Connected;
                d.LastUpdate = DateTime.Now;
                d.StatusText = r.Message;
                d.Charging = r.Charging;
                d.UnknownSource = false;
                d.SleepPending = false;
                d.SleepRetries = 0;
                d.ErrorText = null;
            }
            else if (d.UnknownSource)
            {
                // 没有任何通用方式能读到电量：明确显示「无法识别」
                d.LastConnected = false;
                d.LastLevel = -1;
                d.Charging = false;
                d.StatusText = "无法识别";
                d.ErrorText = null;
                d.SleepPending = false;
                d.SleepRetries = 0;
                d.LastUpdate = DateTime.Now;
            }
            else if (r.Sleeping && d.LastLevel >= 0)
            {
                // 单次没读到不代表设备睡了（可能这一帧被官方软件抢走），
                // 连续 3 次没应答才真正显示「休眠」
                d.FailStrikes++;
                if (d.FailStrikes < 3)
                {
                    d.StatusText = "读取中 · 设备暂未响应（第 " + d.FailStrikes + " 次重试）";
                    d.ErrorText = null;
                    d.SleepPending = true;
                    if (d.SleepRetries < 1000) d.SleepRetries++;
                }
                else
                {
                    // 设备休眠：保留上一次读数，并进入快速重试（用户一动设备就刷新）
                    d.LastConnected = false;
                    d.StatusText = r.Message + "（上次 " + d.LastLevel + "%）";
                    d.ErrorText = null;
                    d.SleepPending = true;
                    if (d.SleepRetries < 1000) d.SleepRetries++;
                }
            }
            else
            {
                d.LastConnected = r.Connected;
                d.ErrorText = r.Message;
                if (d.LastLevel < 0) d.LastUpdate = DateTime.Now;
                d.SleepPending = true;
                if (d.SleepRetries < 1000) d.SleepRetries++;
            }

            CheckNotification(d, oldLevel);
            UpdateTrayIcon(false);
            RaiseChanged();
        }
        private void CheckNotification(DeviceConfig d, int oldLevel)
        {
            if (!Config.Notifications || d.Level < 0) return;
            int threshold = Config.LowThreshold;
            if (d.Level > threshold) return;
            if (oldLevel >= 0 && oldLevel <= threshold) return;
            DateTime last;
            if (_lastNotified.TryGetValue(d.Id, out last) && (DateTime.Now - last).TotalMinutes < Config.NotifyCooldownMinutes) return;
            _lastNotified[d.Id] = DateTime.Now;
            string title = "电量偏低：" + d.Name;
            string text = "当前电量 " + d.Level + "%，低于 " + threshold + "%，请及时充电。";
            int level = d.Level;
            string name = d.Name;
            Post(delegate
            {
                try { _notify.ShowBalloonTip(8000, title, text, ToolTipIcon.Warning); }
                catch { }
                try
                {
                    if (_toast == null || _toast.IsDisposed) _toast = new ToastForm();
                    _toast.ShowMessage(name, level, threshold, delegate { ShowSettings(); });
                }
                catch { }
            });
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && key.GetValue(RunValue) != null;
            }
            catch { return false; }
        }

        public void SetAutoStart(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue(RunValue, "\"" + Application.ExecutablePath + "\" --minimized");
                    else key.DeleteValue(RunValue, false);
                }
            }
            catch { }
        }

        public void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;
            _running = false;
            _wake.Set();
            ConfigStore.Save(Config);
            try { _startupTimer.Stop(); _startupTimer.Dispose(); } catch { }
            try { _notify.Visible = false; _notify.Dispose(); } catch { }
            try { if (_form != null) { _form.Dispose(); _form = null; } } catch { }
            try { if (_popup != null) { _popup.Dispose(); _popup = null; } } catch { }
            try { if (_toast != null) { _toast.Dispose(); _toast = null; } } catch { }
            ExitThread();
        }
    }
}
