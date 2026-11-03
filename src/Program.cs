using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PowerShift
{
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly System.Windows.Forms.Timer timer;
        private readonly EventWaitHandle showSettings;
        private readonly EventWaitHandle exitSignal;
        private readonly SettingsStore store;
        private AppSettings settings;
        private List<Plan> plans;
        private HashSet<Guid> available;
        private Icon currentIcon;
        private Guid lastIconPlan;
        private string status = "";
        private string lastError;
        private int ticks;
        private SettingsForm settingsForm;
        private bool disposed;

        public TrayContext(EventWaitHandle signal, EventWaitHandle shutdown, bool openSettings)
        {
            showSettings = signal;
            exitSignal = shutdown;
            store = new SettingsStore(SettingsStore.DefaultPath);
            Guid active = PowerPlans.Active();
            settings = store.Load(active);
            ReloadPlans();
            store.Save(settings);
            menu = new ContextMenuStrip { Font = new Font("Malgun Gothic", 9F), ShowImageMargin = false, ShowCheckMargin = true };
            menu.Opening += delegate(object sender, CancelEventArgs args) { BuildMenu(); args.Cancel = false; };
            tray = new NotifyIcon { ContextMenuStrip = menu };
            tray.MouseUp += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button == MouseButtons.Left) { BuildMenu(); menu.Show(Cursor.Position); }
            };
            EvaluateSafely();
            tray.Visible = true;
            timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += delegate
            {
                if (exitSignal.WaitOne(0)) { ExitThread(); return; }
                if (showSettings.WaitOne(0)) OpenSettings();
                EvaluateSafely();
            };
            timer.Start();
            if (openSettings) showSettings.Set();
        }

        private void ReloadPlans()
        {
            plans = PowerPlans.List();
            if (plans.Count == 0) throw new InvalidOperationException("등록된 전원 관리 옵션이 없습니다.");
            available = new HashSet<Guid>();
            foreach (Plan plan in plans) available.Add(plan.Id);
        }
        private string PlanName(Guid id)
        {
            foreach (Plan plan in plans) if (plan.Id == id) return plan.Name;
            return id.ToString();
        }
        private static List<string> RunningProcesses()
        {
            List<string> names = new List<string>();
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    try { names.Add(process.ProcessName); }
                    catch (InvalidOperationException) { } // Process exited during the snapshot.
                    catch (Win32Exception) { } // Protected process with no readable name.
                }
            }
            return names;
        }
        private void Evaluate()
        {
            if (++ticks % 30 == 0) ReloadPlans();
            Guid actual = PowerPlans.Active();
            bool hasRules = settings.AutomaticSwitching && settings.Rules.Exists(delegate(ProcessRule r) { return r.Enabled; });
            Decision decision = RuleEngine.Resolve(settings, hasRules ? RunningProcesses() : new List<string>(), available, actual);
            if (actual != decision.PlanId) PowerPlans.Set(decision.PlanId);
            status = decision.IsOverride
                ? PlanName(decision.PlanId) + " · " + decision.ProcessName + ".exe 규칙 적용 중"
                : PlanName(decision.PlanId) + " · 기본값";
            UpdateIcon(decision.PlanId);
            string tooltip = "PowerShift · " + status;
            tray.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.UpdateStatus(status);
            lastError = null;
        }
        private void EvaluateSafely()
        {
            try { Evaluate(); }
            catch (Exception error)
            {
                if (currentIcon == null) UpdateIcon(Guid.Empty);
                tray.Text = "PowerShift · 전환 실패 (설정에서 확인)";
                status = "전환 실패: " + error.Message;
                if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.UpdateStatus(status);
                if (lastError != error.Message && tray.Visible)
                    tray.ShowBalloonTip(5000, "PowerShift", status, ToolTipIcon.Warning);
                lastError = error.Message;
            }
        }
        private void UpdateIcon(Guid id)
        {
            if (currentIcon != null && id == lastIconPlan) return;
            Icon next = Desktop.CreateIcon(id);
            tray.Icon = next;
            if (currentIcon != null) currentIcon.Dispose();
            currentIcon = next; lastIconPlan = id;
        }
        private static string MenuText(string text) { return text.Replace("&", "&&"); }
        private void BuildMenu()
        {
            while (menu.Items.Count > 0)
            {
                ToolStripItem old = menu.Items[0]; menu.Items.RemoveAt(0); old.Dispose();
            }
            menu.Items.Add(new ToolStripMenuItem("PowerShift") { Enabled = false });
            menu.Items.Add(new ToolStripMenuItem(MenuText(status)) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("기본값 변경") { Enabled = false });
            try
            {
                ReloadPlans();
                foreach (Plan plan in plans)
                {
                    Plan selected = plan;
                    ToolStripMenuItem item = new ToolStripMenuItem(MenuText(plan.Name))
                        { Checked = plan.Id.ToString() == settings.DefaultPlanId };
                    item.Click += delegate
                    {
                        try
                        {
                            AppSettings next = settings.Copy(); next.DefaultPlanId = selected.Id.ToString();
                            store.Save(next); settings = next; EvaluateSafely();
                        }
                        catch (Exception error) { Desktop.Error("기본값을 저장할 수 없습니다.", error); }
                    };
                    menu.Items.Add(item);
                }
            }
            catch (Exception error) { menu.Items.Add(new ToolStripMenuItem("전원 옵션 조회 실패") { Enabled = false, ToolTipText = error.Message }); }
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem automatic = new ToolStripMenuItem("프로그램별 자동 전환") { Checked = settings.AutomaticSwitching };
            automatic.Click += delegate
            {
                try
                {
                    AppSettings next = settings.Copy(); next.AutomaticSwitching = !next.AutomaticSwitching;
                    store.Save(next); settings = next; EvaluateSafely();
                }
                catch (Exception error) { Desktop.Error("자동 전환 설정을 저장할 수 없습니다.", error); }
            };
            menu.Items.Add(automatic);
            ToolStripMenuItem configure = new ToolStripMenuItem("설정…");
            configure.Click += delegate { OpenSettings(); }; menu.Items.Add(configure);
            ToolStripMenuItem powerOptions = new ToolStripMenuItem("Windows 전원 옵션 열기");
            powerOptions.Click += delegate { Desktop.OpenPowerOptions(); }; menu.Items.Add(powerOptions);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exit = new ToolStripMenuItem("종료");
            exit.Click += delegate { ExitThread(); }; menu.Items.Add(exit);
        }
        private void CommitSettings(AppSettings next, bool startup)
        {
            bool previousStartup = Startup.Enabled;
            try
            {
                if (startup != previousStartup) Startup.Set(startup);
                store.Save(next);
            }
            catch
            {
                if (startup != previousStartup) Startup.Set(previousStartup);
                throw;
            }
            settings = next.Copy(); EvaluateSafely();
        }
        private void OpenSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed)
            { settingsForm.Show(); settingsForm.WindowState = FormWindowState.Normal; settingsForm.Activate(); return; }
            try
            {
                ReloadPlans();
                settingsForm = new SettingsForm(settings, plans, status, CommitSettings);
                settingsForm.FormClosed += delegate { SettingsForm old = settingsForm; settingsForm = null; old.Dispose(); };
                settingsForm.Show();
            }
            catch (Exception error) { Desktop.Error("설정 화면을 열 수 없습니다.", error); }
        }
        protected override void ExitThreadCore()
        {
            try
            {
                Guid baseline;
                if (Guid.TryParse(settings.DefaultPlanId, out baseline) && available.Contains(baseline) && PowerPlans.Active() != baseline)
                    PowerPlans.Set(baseline);
            }
            catch (Exception error) { Desktop.Error("기본 전원 옵션으로 복원할 수 없습니다.", error); }
            Dispose(); base.ExitThreadCore();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                timer.Stop(); timer.Dispose();
                if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close();
                tray.Visible = false; tray.Dispose();
                Font font = menu.Font; menu.Dispose(); font.Dispose();
                if (currentIcon != null) currentIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                if (Array.IndexOf(args, "--enable-startup") >= 0) { Startup.Set(true); return; }
                if (Array.IndexOf(args, "--disable-startup") >= 0) { Startup.Set(false); return; }
                if (Array.IndexOf(args, "--exit") >= 0)
                {
                    try { using (EventWaitHandle signal = EventWaitHandle.OpenExisting(@"Local\PowerShift.Exit.3B8D7A3E")) signal.Set(); }
                    catch (WaitHandleCannotBeOpenedException) { }
                    return;
                }
                bool created;
                using (Mutex mutex = new Mutex(true, @"Local\PowerShift.3B8D7A3E", out created))
                {
                    if (!created)
                    {
                        try { using (EventWaitHandle signal = EventWaitHandle.OpenExisting(@"Local\PowerShift.ShowSettings.3B8D7A3E")) signal.Set(); }
                        catch (WaitHandleCannotBeOpenedException) { }
                        return;
                    }
                    try
                    {
                        SetProcessDPIAware(); Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        using (EventWaitHandle signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PowerShift.ShowSettings.3B8D7A3E"))
                        using (EventWaitHandle shutdown = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PowerShift.Exit.3B8D7A3E"))
                        using (TrayContext context = new TrayContext(signal, shutdown, Array.IndexOf(args, "--settings") >= 0))
                            Application.Run(context);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception error)
            { Desktop.Error("PowerShift를 실행할 수 없습니다. 설정 파일이 손상된 경우 원본을 보관한 뒤 확인해 주세요.", error); Environment.ExitCode = 1; }
        }
    }
}
