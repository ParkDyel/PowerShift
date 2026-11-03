using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PowerShift
{
    internal sealed class LogsView : UserControl
    {
        private readonly LogStore store;
        private readonly ListView entries = new ListView();
        private readonly TextBox details = new TextBox();
        private readonly Label info = new Label();
        private readonly NumericUpDown retention = new NumericUpDown();
        private long revision = -1;
        private string lastError;
        public int RetentionDays { get { return Decimal.ToInt32(retention.Value); } }
        public LogsView(LogStore logs, int days)
        {
            store = logs; Dock = DockStyle.Fill;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            Controls.Add(layout);
            FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            options.Controls.Add(new Label { Text = "로그 자동 삭제", AutoSize = true, Margin = new Padding(3, 7, 8, 3) });
            retention.Minimum = 0; retention.Maximum = Int32.MaxValue; retention.Value = days; retention.Width = 85;
            options.Controls.Add(retention);
            options.Controls.Add(new Label { Text = "일  (0 = 자동 삭제 안 함 · 기본값 3일)", AutoSize = true, Margin = new Padding(3, 7, 12, 3) });
            Button refresh = new Button { Text = "새로고침", AutoSize = true };
            refresh.Click += delegate { RefreshEntries(true); }; options.Controls.Add(refresh);
            layout.Controls.Add(options);
            info.AutoSize = true; info.ForeColor = SystemColors.GrayText; info.Margin = new Padding(3, 6, 3, 8);
            info.Text = "전환 시 적용된 규칙과 이전 → 변경 모드를 기록합니다. 최근 500개를 표시합니다.";
            layout.Controls.Add(info);
            entries.Dock = DockStyle.Fill; entries.View = View.Details; entries.FullRowSelect = true;
            entries.MultiSelect = false; entries.HideSelection = false; entries.ShowItemToolTips = true;
            entries.Columns.Add("시간", 145); entries.Columns.Add("이전 모드", 90); entries.Columns.Add("변경 모드", 90);
            entries.Columns.Add("적용 규칙", 210); entries.Columns.Add("전환 이유", 230); entries.Columns.Add("전체 경로", 300);
            layout.Controls.Add(entries);
            details.Dock = DockStyle.Fill; details.Multiline = true; details.ReadOnly = true; details.ScrollBars = ScrollBars.Vertical;
            details.BackColor = SystemColors.Control; layout.Controls.Add(details);
            entries.SelectedIndexChanged += delegate
            {
                details.Text = entries.SelectedItems.Count == 0 ? "" : Describe((LogEntry)entries.SelectedItems[0].Tag);
            };
            VisibleChanged += delegate { if (Visible) RefreshEntries(true); };
        }
        private static string Describe(LogEntry entry)
        {
            return entry.LocalTime + " · " + entry.Level + " · " + entry.FromPlan + " → " + entry.ToPlan + Environment.NewLine +
                "규칙: " + entry.PreviousRule + " → " + entry.Rule + " · " + entry.Reason +
                (String.IsNullOrWhiteSpace(entry.ExecutablePath) ? "" : Environment.NewLine + entry.ExecutablePath);
        }
        public void RefreshEntries(bool force = false)
        {
            if (!Visible || (!force && revision == store.Revision && lastError == store.LastError)) return;
            try
            {
                List<LogEntry> recent = store.ReadRecent(500);
                entries.BeginUpdate();
                try
                {
                    entries.Items.Clear(); details.Text = "";
                    foreach (LogEntry entry in recent)
                        entries.Items.Add(new ListViewItem(new[] { entry.LocalTime, entry.FromPlan ?? "", entry.ToPlan ?? "",
                            entry.Rule ?? "", entry.Reason ?? "", entry.ExecutablePath ?? "" }) { Tag = entry, ToolTipText = Describe(entry) });
                }
                finally { entries.EndUpdate(); }
                revision = store.Revision; lastError = store.LastError;
                info.Text = lastError == null ? "최근 " + recent.Count + "개 표시 (최대 500개). 보관 기간은 저장하면 적용됩니다."
                    : "로그 저장/정리 실패: " + lastError;
                info.ForeColor = lastError == null ? SystemColors.GrayText : Color.Firebrick;
            }
            catch (Exception error) { info.Text = "로그를 읽을 수 없습니다: " + error.Message; info.ForeColor = Color.Firebrick; }
        }
    }
}
