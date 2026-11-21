using System;
using System.Drawing;
using System.Windows.Forms;

namespace PowerShift
{
    internal sealed class ShutdownStatusView : UserControl
    {
        private readonly Label reservation = new Label { AutoSize = true, Margin = new Padding(3, 3, 3, 8) };
        private readonly Label result = new Label { AutoSize = true, Visible = false };

        internal ShutdownStatusView(Action<IWin32Window> scheduleShutdown, Func<IWin32Window, string> cancelShutdown)
        {
            Dock = DockStyle.Fill;

            var layout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1 };
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "PowerShift 종료 예약", AutoSize = true });
            layout.Controls.Add(reservation);
            layout.Controls.Add(new Label
            {
                Text = "Windows 명령 프롬프트나 다른 앱에서 등록한 예약은 조회할 수 없습니다.\n대기 중인 Windows 예약은 등록한 곳에 관계없이 취소할 수 있습니다.",
                AutoSize = true,
                Margin = new Padding(3, 3, 3, 8)
            });
            layout.Controls.Add(result);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var add = new Button { Text = "예약 추가…", AutoSize = true };
            var cancel = new Button { Text = "Windows 예약 취소…", AutoSize = true };
            var refresh = new Button { Text = "예약 조회", AutoSize = true };
            buttons.Controls.Add(add);
            buttons.Controls.Add(refresh);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons);

            add.Click += delegate { scheduleShutdown(FindForm()); };
            cancel.Click += delegate
            {
                string message = cancelShutdown(FindForm());
                if (message == null) return;
                RefreshReservation();
                result.Text = message;
                result.Visible = true;
            };
            refresh.Click += delegate { RefreshReservation(); };
        }

        internal void RefreshReservation()
        {
            result.Visible = false;
            try { reservation.Text = WindowsShutdown.DescribeReservation(); }
            catch (Exception) { reservation.Text = "PowerShift 종료 예약 정보를 읽을 수 없습니다."; }
        }
    }
}
