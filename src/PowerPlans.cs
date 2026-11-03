using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PowerShift
{
    internal sealed class Plan
    {
        public Guid Id;
        public string Name;
    }

    internal static class PowerPlans
    {
        [DllImport("powrprof.dll")]
        private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup,
            uint access, uint index, byte[] buffer, ref uint size);
        [DllImport("powrprof.dll")]
        private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme,
            IntPtr subgroup, IntPtr setting, byte[] buffer, ref uint size);
        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        private static void Check(uint code)
        {
            if (code != 0) throw new Win32Exception((int)code);
        }

        public static Guid Active()
        {
            IntPtr pointer;
            Check(PowerGetActiveScheme(IntPtr.Zero, out pointer));
            try { return (Guid)Marshal.PtrToStructure(pointer, typeof(Guid)); }
            finally { LocalFree(pointer); }
        }

        public static List<Plan> List()
        {
            List<Plan> result = new List<Plan>();
            for (uint index = 0; ; index++)
            {
                byte[] buffer = new byte[16];
                uint size = 16;
                uint code = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    16, index, buffer, ref size);
                if (code == 259) break; // ERROR_NO_MORE_ITEMS
                Check(code);
                Guid id = new Guid(buffer);
                uint nameSize = 0;
                code = PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero,
                    IntPtr.Zero, null, ref nameSize);
                if (code != 0 && code != 234) Check(code);
                byte[] name = new byte[nameSize];
                Check(PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero,
                    IntPtr.Zero, name, ref nameSize));
                result.Add(new Plan { Id = id,
                    Name = Encoding.Unicode.GetString(name).TrimEnd('\0') });
            }
            return result;
        }

        public static void Set(Guid id)
        {
            Check(PowerSetActiveScheme(IntPtr.Zero, ref id));
            if (Active() != id)
                throw new InvalidOperationException("Windows에서 전원 관리 옵션 변경을 적용하지 못했습니다.");
        }
    }

}
