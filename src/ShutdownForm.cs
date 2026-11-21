using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace PowerShift
{
    internal sealed class ShutdownForm : Form
    {
        internal int DelayMinutes { get; private set; }

        internal ShutdownForm()
        {
            Text = "Windows 종료 예약";
            Font = new Font("Malgun Gothic", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var layout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
                Padding = new Padding(16), ColumnCount = 1 };
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "종료까지 남은 시간 (분)", AutoSize = true });
            var delay = new TextBox { Text = "30", Width = 120 };
            layout.Controls.Add(delay);
            layout.Controls.Add(new Label
            {
                Text = "예약 시간이 되면 Windows가 앱을 강제로 닫을 수 있습니다.\n열려 있는 작업을 미리 저장하세요.",
                AutoSize = true,
                Margin = new Padding(3, 9, 3, 9)
            });
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, AutoSize = true };
            var schedule = new Button { Text = "종료 예약", AutoSize = true };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(schedule);
            layout.Controls.Add(buttons);
            AcceptButton = schedule;
            CancelButton = cancel;

            schedule.Click += delegate
            {
                int minutes;
                if (!Int32.TryParse(delay.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out minutes)
                    || minutes < 1 || minutes > WindowsShutdown.MaxDelayMinutes)
                {
                    MessageBox.Show(this, "1~" + WindowsShutdown.MaxDelayMinutes.ToString("N0") + "분 사이의 정수를 입력하세요.",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    delay.Focus();
                    delay.SelectAll();
                    return;
                }
                DelayMinutes = minutes;
                DialogResult = DialogResult.OK;
            };
        }
    }
}
