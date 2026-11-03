using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerShift
{
    public sealed class RunningProcess
    {
        public string ProcessName;
        public string ExecutablePath;
    }

    internal static class ProcessCatalog
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder path, ref uint length);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        public static string ReadPath(int processId)
        {
            IntPtr handle = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
            if (handle == IntPtr.Zero) return null;
            try
            {
                StringBuilder path = new StringBuilder(512);
                uint length = (uint)path.Capacity;
                if (QueryFullProcessImageNameW(handle, 0, path, ref length)) return RuleEngine.NormalizePath(path.ToString());
                if (Marshal.GetLastWin32Error() != 122) return null;
                path = new StringBuilder(32768); length = (uint)path.Capacity;
                return QueryFullProcessImageNameW(handle, 0, path, ref length) ? RuleEngine.NormalizePath(path.ToString()) : null;
            }
            finally { CloseHandle(handle); }
        }
        public static List<RunningProcess> Capture(IEnumerable<ProcessRule> rules)
        {
            HashSet<string> pathNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (rules != null)
                foreach (ProcessRule rule in rules)
                    if (rule.Enabled && !String.IsNullOrWhiteSpace(rule.ExecutablePath)) pathNames.Add(RuleEngine.Normalize(rule.ProcessName));
            List<RunningProcess> result = new List<RunningProcess>();
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        string name = process.ProcessName;
                        result.Add(new RunningProcess { ProcessName = name,
                            ExecutablePath = rules == null || pathNames.Contains(name) ? ReadPath(process.Id) : null });
                    }
                    catch (InvalidOperationException) { } // Process exited during the snapshot.
                    catch (Win32Exception) { } // Protected process with no readable name.
                }
            }
            return result;
        }
    }
}
