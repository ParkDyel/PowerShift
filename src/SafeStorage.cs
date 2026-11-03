using System;
using System.IO;
using System.Text;

namespace PowerShift
{
    internal static class SafeStorage
    {
        // Best-effort protection against redirected data paths, not a same-user sandbox.
        public static void RejectReparsePoint(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("연결된 파일/폴더에는 설정이나 로그를 쓰지 않습니다: " + path);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        public static void EnsureDirectory(string path)
        {
            RejectReparsePoint(path);
            Directory.CreateDirectory(path);
            RejectReparsePoint(path);
        }
        public static FileStream OpenRead(string path)
        {
            RejectReparsePoint(path);
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        public static string ReadText(string path, int maxBytes)
        {
            using (FileStream stream = OpenRead(path))
            {
                if (stream.Length > maxBytes) throw new InvalidDataException("설정 파일이 허용 크기(1 MiB)를 초과했습니다.");
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true)) return reader.ReadToEnd();
            }
        }
        public static void WriteText(string path, string text)
        {
            EnsureDirectory(Path.GetDirectoryName(path));
            RejectReparsePoint(path);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(text);
                RejectReparsePoint(path);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static string ReadLine(StreamReader reader, int maxCharacters)
        {
            StringBuilder line = new StringBuilder(256);
            int value;
            while ((value = reader.Read()) != -1)
            {
                if (value == '\n') break;
                if (line.Length >= maxCharacters) throw new InvalidDataException("로그 한 줄의 허용 크기를 초과했습니다. 원본은 유지됩니다.");
                line.Append((char)value);
            }
            if (value == -1 && line.Length == 0) return null;
            if (line.Length > 0 && line[line.Length - 1] == '\r') line.Length--;
            return line.ToString();
        }
    }
}
