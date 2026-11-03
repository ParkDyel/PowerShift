using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PowerShift
{
    internal static class Startup
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "PowerShift";
        private static string Command
        {
            get
            {
                string script = AppDomain.CurrentDomain.GetData("PowerShift.ScriptPath") as string;
                if (script == null) return "\"" + Application.ExecutablePath + "\"";
                return "\"" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe") +
                    "\" -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy RemoteSigned -File \"" + script + "\"";
            }
        }
        public static bool Enabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Key))
                    return key != null && String.Equals(key.GetValue(Name) as string,
                        Command, StringComparison.OrdinalIgnoreCase);
            }
        }
        public static void Set(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (enabled) key.SetValue(Name, Command, RegistryValueKind.String);
                else if (Enabled) key.DeleteValue(Name, false);
            }
        }
    }

    internal static class Desktop
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);
        public static readonly Guid Saver = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");
        public static readonly Guid Performance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public static Icon CreateIcon(Guid id)
        {
            Color color = id == Saver ? Color.FromArgb(234, 179, 8) :
                id == Performance ? Color.FromArgb(63, 143, 235) : Color.FromArgb(42, 174, 117);
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush brush = new SolidBrush(color)) g.FillEllipse(brush, 1, 1, 30, 30);
                PointF[] bolt = { new PointF(18, 5), new PointF(9, 18), new PointF(15, 18),
                    new PointF(13, 27), new PointF(24, 13), new PointF(18, 13) };
                g.FillPolygon(Brushes.White, bolt);
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        public static void Error(string message, Exception error)
        {
            MessageBox.Show(message + "\n\n" + error.Message, "PowerShift",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        public static void OpenPowerOptions()
        {
            try { Process.Start(new ProcessStartInfo("control.exe", "/name Microsoft.PowerOptions") { UseShellExecute = true }); }
            catch (Exception error) { Error("전원 옵션을 열 수 없습니다.", error); }
        }
    }
}
