using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PowerShift
{
    internal sealed class PlanChoice
    {
        public Guid Id;
        public string Name;
        public override string ToString() { return Name; }
        public static void Populate(ComboBox box, List<Plan> plans, string selected)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (Plan plan in plans)
            {
                PlanChoice choice = new PlanChoice { Id = plan.Id, Name = plan.Name };
                box.Items.Add(choice);
                if (String.Equals(plan.Id.ToString(), selected, StringComparison.OrdinalIgnoreCase))
                    box.SelectedItem = choice;
            }
        }
    }

    internal sealed class RuleForm : Form
    {
        private readonly TextBox process = new TextBox();
        private readonly TextBox executablePath = new TextBox();
        private readonly ComboBox plan = new ComboBox();
        private readonly CheckBox enabled = new CheckBox();
        public ProcessRule Result;
        public RuleForm(List<Plan> plans, ProcessRule existing)
        {
            Text = existing == null ? "프로그램 규칙 추가" : "프로그램 규칙 편집";
            Font = new Font("Malgun Gothic", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 440);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill,
                Padding = new Padding(20), ColumnCount = 2, RowCount = 9 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "프로세스 이름", AutoSize = true }, 0, 0);
            process.Dock = DockStyle.Fill;
            process.Text = existing == null ? "" : RuleEngine.Normalize(existing.ProcessName) + ".exe";
            executablePath.Text = existing == null ? "" : existing.ExecutablePath;
            layout.Controls.Add(process, 0, 1);
            Button browse = new Button { Text = "찾아보기…", AutoSize = true };
            browse.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog { Filter = "실행 파일 (*.exe)|*.exe", CheckFileExists = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    { process.Text = Path.GetFileName(dialog.FileName); executablePath.Text = dialog.FileName; }
            };
            FlowLayoutPanel pickers = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            pickers.Controls.Add(browse);
            Button running = new Button { Text = "실행 중에서 선택…", AutoSize = true };
            running.Click += delegate
            {
                using (ProcessPickerForm picker = new ProcessPickerForm())
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    { process.Text = picker.Result.ProcessName + ".exe"; executablePath.Text = picker.Result.ExecutablePath ?? ""; }
            };
            pickers.Controls.Add(running); layout.Controls.Add(pickers, 1, 1);
            Label pathLabel = new Label { Text = "전체 경로 (선택)", AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
            layout.Controls.Add(pathLabel, 0, 2);
            executablePath.Dock = DockStyle.Fill;
            layout.Controls.Add(executablePath, 0, 3); layout.SetColumnSpan(executablePath, 2);
            Label hint = new Label { Text = "경로를 지정하면 해당 EXE만 적용합니다. 비우면 같은 이름의 모든 EXE에 적용합니다.",
                AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(570, 0) };
            layout.Controls.Add(hint, 0, 4); layout.SetColumnSpan(hint, 2);
            layout.Controls.Add(new Label { Text = "실행 중 사용할 전원 옵션", AutoSize = true, Margin = new Padding(3, 14, 3, 5) }, 0, 5);
            PlanChoice.Populate(plan, plans, existing == null ? plans[0].Id.ToString() : existing.PlanId);
            plan.Dock = DockStyle.Fill;
            layout.Controls.Add(plan, 0, 6); layout.SetColumnSpan(plan, 2);
            enabled.Text = "이 규칙 사용"; enabled.AutoSize = true;
            enabled.Checked = existing == null || existing.Enabled;
            layout.Controls.Add(enabled, 0, 7);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft };
            Button cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, AutoSize = true };
            Button save = new Button { Text = "확인", AutoSize = true };
            buttons.Controls.Add(cancel); buttons.Controls.Add(save);
            layout.Controls.Add(buttons, 0, 8); layout.SetColumnSpan(buttons, 2);
            CancelButton = cancel; AcceptButton = save;
            save.Click += delegate
            {
                try
                {
                    string name = RuleEngine.Normalize(process.Text);
                    if (String.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || plan.SelectedItem == null)
                        throw new InvalidDataException("프로세스 이름과 전원 옵션을 선택해 주세요.");
                    ProcessRule result = new ProcessRule { ProcessName = name, ExecutablePath = RuleEngine.NormalizePath(executablePath.Text),
                        PlanId = ((PlanChoice)plan.SelectedItem).Id.ToString(), Enabled = enabled.Checked };
                    AppSettings validation = new AppSettings { DefaultPlanId = result.PlanId };
                    validation.Rules.Add(result); SettingsStore.Validate(validation);
                    Result = result;
                    DialogResult = DialogResult.OK;
                }
                catch (Exception error) { Desktop.Error("규칙을 확인해 주세요.", error); }
            };
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings draft;
        private readonly List<Plan> plans;
        private readonly ComboBox baseline = new ComboBox();
        private readonly CheckBox automatic = new CheckBox();
        private readonly CheckBox startup = new CheckBox();
        private readonly ListView rules = new ListView();
        private readonly Label status = new Label();
        private readonly Action<AppSettings, bool> saveSettings;
        private readonly LogsView logsView;
        public SettingsForm(AppSettings settings, List<Plan> choices, string currentStatus, Action<AppSettings, bool> save, LogStore logs)
        {
            draft = settings.Copy(); plans = choices; saveSettings = save;
            Text = "PowerShift 설정";
            Font = new Font("Malgun Gothic", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(940, 520); MinimumSize = new Size(760, 460);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Desktop.CreateIcon(Guid.Parse(settings.DefaultPlanId));
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20),
                ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);
            root.Controls.Add(new Label { Text = "PowerShift", Font = new Font(Font.FontFamily, 20, FontStyle.Bold),
                AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
            status.Text = currentStatus; status.AutoSize = true;
            status.ForeColor = SystemColors.GrayText; status.Margin = new Padding(0, 0, 0, 16);
            root.Controls.Add(status);
            TabControl tabs = new TabControl { Dock = DockStyle.Fill };
            TabPage global = new TabPage("기본 설정") { Padding = new Padding(16) };
            TabPage programs = new TabPage("프로그램별 설정") { Padding = new Padding(16) };
            TabPage logTab = new TabPage("로그") { Padding = new Padding(16) };
            logsView = new LogsView(logs, draft.LogRetentionDays); logTab.Controls.Add(logsView);
            tabs.TabPages.Add(global); tabs.TabPages.Add(programs); tabs.TabPages.Add(logTab); root.Controls.Add(tabs);
            TableLayoutPanel general = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            general.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); global.Controls.Add(general);
            general.Controls.Add(new Label { Text = "기본 전원 옵션", AutoSize = true });
            PlanChoice.Populate(baseline, plans, draft.DefaultPlanId); baseline.Dock = DockStyle.Top;
            general.Controls.Add(baseline);
            general.Controls.Add(new Label { Text = "프로그램 규칙이 적용되지 않을 때 사용합니다.\n트레이에서 모드를 선택하면 이 기본값이 변경됩니다.",
                AutoSize = true, Margin = new Padding(3, 10, 3, 20) });
            startup.Text = "Windows 로그인 시 자동 실행"; startup.AutoSize = true;
            startup.Checked = Startup.Enabled; general.Controls.Add(startup);
            TableLayoutPanel programLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            programLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            programLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            programLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            programLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            programs.Controls.Add(programLayout);
            automatic.Text = "프로그램별 자동 전환 사용"; automatic.AutoSize = true;
            automatic.Checked = draft.AutomaticSwitching; programLayout.Controls.Add(automatic);
            programLayout.Controls.Add(new Label { Text = "위쪽 규칙이 우선합니다. 마지막 대상이 종료되면 기본값으로 돌아갑니다.",
                AutoSize = true, Margin = new Padding(3, 6, 3, 10) });
            rules.View = View.Details; rules.FullRowSelect = true; rules.HideSelection = false;
            rules.MultiSelect = false; rules.Dock = DockStyle.Fill;
            rules.ShowItemToolTips = true;
            rules.Columns.Add("사용", 48); rules.Columns.Add("프로그램", 180); rules.Columns.Add("전원 옵션", 150);
            rules.Columns.Add("전체 경로", 420);
            rules.Resize += delegate { rules.Columns[3].Width = Math.Max(260, rules.ClientSize.Width - 390); };
            programLayout.Controls.Add(rules);
            FlowLayoutPanel ruleButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            AddButton(ruleButtons, "추가", delegate { EditRule(false); });
            AddButton(ruleButtons, "편집", delegate { EditRule(true); });
            AddButton(ruleButtons, "삭제", delegate { int i = SelectedIndex(); if (i >= 0) { draft.Rules.RemoveAt(i); RefreshRules(Math.Min(i, draft.Rules.Count - 1)); } });
            AddButton(ruleButtons, "위로", delegate { MoveRule(-1); });
            AddButton(ruleButtons, "아래로", delegate { MoveRule(1); });
            programLayout.Controls.Add(ruleButtons);
            rules.DoubleClick += delegate { EditRule(true); };
            RefreshRules(-1);
            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0) };
            Button cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, AutoSize = true };
            Button apply = new Button { Text = "저장", AutoSize = true };
            footer.Controls.Add(cancel); footer.Controls.Add(apply); root.Controls.Add(footer);
            CancelButton = cancel; AcceptButton = apply;
            apply.Click += delegate
            {
                try
                {
                    if (baseline.SelectedItem == null) throw new InvalidDataException("기본 전원 옵션을 선택해 주세요.");
                    draft.DefaultPlanId = ((PlanChoice)baseline.SelectedItem).Id.ToString();
                    draft.AutomaticSwitching = automatic.Checked;
                    draft.LogRetentionDays = logsView.RetentionDays;
                    SettingsStore.Validate(draft);
                    saveSettings(draft, startup.Checked);
                    DialogResult = DialogResult.OK; Close();
                }
                catch (Exception error) { Desktop.Error("설정을 저장할 수 없습니다.", error); }
            };
            FormClosed += delegate { Icon.Dispose(); };
        }
        public void UpdateStatus(string text) { status.Text = text; }
        public void RefreshLogs() { logsView.RefreshEntries(); }
        private static void AddButton(Control parent, string text, EventHandler click)
        {
            Button button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(64, 0) };
            button.Click += click; parent.Controls.Add(button);
        }
        private int SelectedIndex() { return rules.SelectedIndices.Count == 0 ? -1 : rules.SelectedIndices[0]; }
        private void RefreshRules(int selected)
        {
            rules.Items.Clear();
            foreach (ProcessRule rule in draft.Rules)
            {
                string name = "(삭제된 전원 옵션)";
                foreach (Plan plan in plans) if (plan.Id.ToString() == rule.PlanId) name = plan.Name;
                string path = String.IsNullOrWhiteSpace(rule.ExecutablePath) ? "(이름 기준 · 모든 경로)" : rule.ExecutablePath;
                rules.Items.Add(new ListViewItem(new string[] { rule.Enabled ? "켜짐" : "꺼짐", RuleEngine.Normalize(rule.ProcessName) + ".exe", name, path })
                    { ToolTipText = path });
            }
            if (selected >= 0 && selected < rules.Items.Count) { rules.Items[selected].Selected = true; rules.EnsureVisible(selected); }
        }
        private void EditRule(bool edit)
        {
            int index = edit ? SelectedIndex() : -1;
            if (edit && index < 0) return;
            using (RuleForm dialog = new RuleForm(plans, index < 0 ? null : draft.Rules[index]))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                for (int i = 0; i < draft.Rules.Count; i++)
                    if (i != index && String.Equals(RuleEngine.RuleKey(draft.Rules[i]),
                        RuleEngine.RuleKey(dialog.Result), StringComparison.OrdinalIgnoreCase))
                    { MessageBox.Show(this, "같은 프로그램과 경로가 이미 등록되어 있습니다. 기존 규칙을 편집해 주세요.", "PowerShift"); return; }
                if (index < 0) { draft.Rules.Add(dialog.Result); index = draft.Rules.Count - 1; }
                else draft.Rules[index] = dialog.Result;
                RefreshRules(index);
            }
        }
        private void MoveRule(int offset)
        {
            int index = SelectedIndex(), next = index + offset;
            if (index < 0 || next < 0 || next >= draft.Rules.Count) return;
            ProcessRule rule = draft.Rules[index]; draft.Rules.RemoveAt(index); draft.Rules.Insert(next, rule);
            RefreshRules(next);
        }
    }
}
