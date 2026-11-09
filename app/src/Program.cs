using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace BatteryBar
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--icon=", StringComparison.OrdinalIgnoreCase))
                {
                    try { TrayIconFactory.SaveIco(args[i].Substring(7)); } catch { }
                    return;
                }
                if (args[i] == "--scan")
                {
                    try
                    {
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        foreach (DiscoveredDevice d in DeviceScanner.Scan())
                            sb.AppendLine(d.Id + " | " + d.Source + " | " + d.Address + " | " + d.Name + " | " + d.Detail);
                        File.WriteAllText(Path.Combine(Path.GetTempPath(), "bb_scan.txt"), sb.ToString());
                    }
                    catch (Exception ex)
                    {
                        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "bb_scan.txt"), ex.ToString()); } catch { }
                    }
                    return;
                }
                if (args[i].StartsWith("--preview=", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string dir = args[i].Substring(10);
                        Directory.CreateDirectory(dir);
                        PreviewRenderer.SaveTraySheet(Path.Combine(dir, "tray-icons.png"));
                        PreviewRenderer.SaveHero(Path.Combine(dir, "hero.png"));
                    }
                    catch { }
                    return;
                }
            }

            bool minimized = false;
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--minimized" || args[i] == "/minimized") minimized = true;

            bool created;
            using (Mutex mutex = new Mutex(true, "BatteryBar.SingleInstance." + Environment.UserName, out created))
            {
                if (!created)
                {
                    try
                    {
                        using (EventWaitHandle signal = EventWaitHandle.OpenExisting("BatteryBar.ShowSignal"))
                            signal.Set();
                    }
                    catch { }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { ShowError(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
                {
                    ShowError(e.ExceptionObject as Exception);
                };

                EventWaitHandle showSignal;
                try { showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "BatteryBar.ShowSignal"); }
                catch { showSignal = null; }

                TrayApp app = new TrayApp(minimized);
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--popup") app.ShowPopupSoon();
                    if (args[i] == "--toast") app.DebugToast();
                    if (args[i] == "--menu") app.DebugMenu();
                    if (args[i] == "--add") app.OpenAddDeviceSoon();
                }

                if (showSignal != null)
                {
                    Thread listener = new Thread(delegate ()
                    {
                        while (true)
                        {
                            if (showSignal.WaitOne())
                                app.ShowSettingsAsync();
                        }
                    });
                    listener.IsBackground = true;
                    listener.Start();
                }
                Application.Run(app);
            }
        }

        private static void ShowError(Exception ex)
        {
            try
            {
                MessageBox.Show("BatteryBar 遇到一个问题：\n\n" + (ex == null ? "未知错误" : ex.Message),
                    "BatteryBar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch { }
        }
    }
}
