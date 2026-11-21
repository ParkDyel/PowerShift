using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace PowerShift
{
    internal static class WindowsShutdown
    {
        internal const int MaxDelayMinutes = 315360000 / 60;
        private const int ShutdownInProgress = 1115;
        private const int NoShutdownInProgress = 1116;
        private const int ShutdownAlreadyScheduled = 1190;
        private const string ReservationKey = @"Software\PowerShift\ShutdownReservation";
        private const string ReservationValue = "Reservation";

        internal static string ScheduleArguments(int minutes)
        {
            return "/s /t " + (minutes * 60).ToString(CultureInfo.InvariantCulture);
        }

        internal static void Schedule(int minutes)
        {
            if (minutes < 1 || minutes > MaxDelayMinutes)
                throw new ArgumentOutOfRangeException("minutes");

            int exitCode = Run(ScheduleArguments(minutes));
            if (exitCode == ShutdownInProgress || exitCode == ShutdownAlreadyScheduled)
                throw new InvalidOperationException("이미 Windows 종료 또는 다시 시작이 대기 중입니다.");
            if (exitCode != 0) throw new Win32Exception(exitCode);
        }

        internal static bool Cancel()
        {
            int exitCode = Run("/a");
            if (exitCode == NoShutdownInProgress) return false;
            if (exitCode != 0) throw new Win32Exception(exitCode);
            return true;
        }

        internal static void RememberReservation(DateTimeOffset due, int minutes)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ReservationKey,
                RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.Volatile))
                key.SetValue(ReservationValue, new[] { due.ToString("o", CultureInfo.InvariantCulture),
                    minutes.ToString(CultureInfo.InvariantCulture) }, RegistryValueKind.MultiString);
        }

        internal static string DescribeReservation()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ReservationKey))
            {
                var record = key == null ? null : key.GetValue(ReservationValue) as string[];
                DateTimeOffset due;
                int minutes;
                if (record == null || record.Length != 2 ||
                    !DateTimeOffset.TryParseExact(record[0], "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out due) ||
                    !Int32.TryParse(record[1], NumberStyles.None, CultureInfo.InvariantCulture, out minutes) ||
                    minutes < 1 || minutes > MaxDelayMinutes || due <= DateTimeOffset.UtcNow)
                    return "표시할 PowerShift 종료 예약이 없습니다.";

                return "명령  shutdown.exe " + ScheduleArguments(minutes) + "\n예상 종료 시각  " + due.LocalDateTime.ToString("g");
            }
        }

        internal static void ForgetReservation()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ReservationKey, true))
                if (key != null) key.DeleteValue(ReservationValue, false);
        }

        private static int Run(string arguments)
        {
            string systemDirectory = Environment.SystemDirectory;
            var startInfo = new ProcessStartInfo(Path.Combine(systemDirectory, "shutdown.exe"), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = systemDirectory
            };
            using (Process process = Process.Start(startInfo))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }
    }
}
