using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PowerShift
{
    internal sealed class ProcessPickerForm : Form
    {
        public RunningProcess Result;
        public ProcessPickerForm()
        {
            Text = "실행 중인 프로그램 선택";
            Font = new Font("Malgun Gothic", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(820, 440); MinimumSize = new Size(620, 340);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "같은 이름의 EXE도 전체 경로로 구분해서 선택할 수 있습니다.",
                AutoSize = true, Margin = new Padding(3, 3, 3, 12) });
            ListView list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                MultiSelect = false, HideSelection = false, ShowItemToolTips = true };
            list.Columns.Add("프로그램", 200); list.Columns.Add("전체 경로", 550);
            list.Resize += delegate { list.Columns[1].Width = Math.Max(300, list.ClientSize.Width - 210); };
            List<RunningProcess> processes = ProcessCatalog.Capture(null);
            processes.Sort(delegate(RunningProcess a, RunningProcess b)
            {
                int result = StringComparer.OrdinalIgnoreCase.Compare(a.ProcessName, b.ProcessName);
                return result != 0 ? result : StringComparer.OrdinalIgnoreCase.Compare(a.ExecutablePath, b.ExecutablePath);
            });
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RunningProcess process in processes)
            {
                string key = process.ProcessName + "|" + process.ExecutablePath;
                if (!seen.Add(key)) continue;
                string path = process.ExecutablePath ?? "(경로 조회 불가 · 이름으로 추가)";
                list.Items.Add(new ListViewItem(new[] { process.ProcessName + ".exe", path }) { Tag = process, ToolTipText = path });
            }
            layout.Controls.Add(list);
            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            Button cancel = new Button { Text = "취소", AutoSize = true, DialogResult = DialogResult.Cancel };
            Button select = new Button { Text = "선택", AutoSize = true };
            EventHandler choose = delegate
            {
                if (list.SelectedItems.Count == 0) return;
                Result = (RunningProcess)list.SelectedItems[0].Tag; DialogResult = DialogResult.OK;
            };
            select.Click += choose; list.DoubleClick += choose;
            footer.Controls.Add(cancel); footer.Controls.Add(select); layout.Controls.Add(footer);
            CancelButton = cancel; AcceptButton = select;
        }
    }
}
