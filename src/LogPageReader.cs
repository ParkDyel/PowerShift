using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace PowerShift
{
    // One reader per browsing session, used by one worker at a time. No handles are
    // retained between pages. Captured lengths exclude later appends from the cursor.
    internal sealed class LogPageReader
    {
        private sealed class Source
        {
            public string Path;
            public long End;
        }
        private readonly string directory;
        private readonly List<Source> sources = new List<Source>();
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer
            { MaxJsonLength = LogStore.MaxLineCharacters, RecursionLimit = 16 };
        private int fileIndex;
        public bool HasMore { get { return fileIndex < sources.Count; } }
        public LogPageReader(string directoryPath)
        {
            directory = directoryPath;
            foreach (string path in new LogStore(directory).Files())
            {
                SafeStorage.RejectReparsePoint(path);
                sources.Add(new Source { Path = path, End = new FileInfo(path).Length });
            }
        }
        public List<LogEntry> ReadPage(int count)
        {
            if (count < 1 || count > 100) throw new ArgumentOutOfRangeException("count");
            List<LogEntry> result = new List<LogEntry>();
            int budget = LogStore.MaxViewBytes, files = 0;
            while (HasMore && result.Count < count && budget > 0 && files++ < 128)
            {
                SafeStorage.RejectReparsePoint(Path.GetDirectoryName(directory));
                SafeStorage.RejectReparsePoint(directory);
                Source source = sources[fileIndex];
                SafeStorage.RejectReparsePoint(source.Path);
                // Allow the tray thread to append and retention to replace files.
                // The UI discards a completed page if retention changed its snapshot.
                using (FileStream stream = new FileStream(source.Path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length < source.End) throw new IOException("로그 파일이 변경되었습니다. 새로고침하세요.");
                    ReverseLines lines = new ReverseLines(stream, source.End);
                    while (lines.Position > 0 && result.Count < count)
                    {
                        long before = lines.Position;
                        string line = lines.ReadLine(ref budget);
                        if (line == null) { source.End = before; return result; }
                        source.End = lines.Position;
                        if (line.Length == 0) continue;
                        LogEntry entry;
                        try { entry = serializer.Deserialize<LogEntry>(line); }
                        catch (ArgumentException) { continue; }
                        catch (InvalidOperationException) { continue; }
                        if (entry != null) result.Add(entry);
                    }
                }
                if (source.End == 0) fileIndex++;
            }
            return result;
        }
        private sealed class ReverseLines
        {
            private readonly FileStream stream;
            private readonly byte[] buffer = new byte[16 * 1024];
            private long bufferStart = -1;
            private int bufferLength;
            public long Position;
            public ReverseLines(FileStream input, long end) { stream = input; Position = end; }
            private int Previous(ref int budget)
            {
                if (Position == 0) return -1;
                long offset = Position - 1;
                if (offset < bufferStart || offset >= bufferStart + bufferLength)
                {
                    if (budget == 0) return -2;
                    int length = (int)Math.Min(Math.Min(Position, buffer.Length), budget);
                    bufferStart = Position - length; stream.Position = bufferStart;
                    bufferLength = 0;
                    while (bufferLength < length)
                    {
                        int read = stream.Read(buffer, bufferLength, length - bufferLength);
                        if (read == 0) throw new IOException("로그 파일이 변경되었습니다. 새로고침하세요.");
                        bufferLength += read;
                    }
                    budget -= bufferLength;
                }
                Position--;
                return buffer[(int)(Position - bufferStart)];
            }
            public string ReadLine(ref int budget)
            {
                List<byte> bytes = new List<byte>(256);
                int value = Previous(ref budget);
                if (value == -2) return null;
                if (value == '\n') value = Previous(ref budget);
                while (value >= 0)
                {
                    if (value == '\n') { Position++; break; }
                    if (bytes.Count >= LogStore.MaxLineCharacters * 4)
                        throw new InvalidDataException("로그 한 줄이 허용 크기를 초과했습니다.");
                    bytes.Add((byte)value);
                    value = Previous(ref budget);
                }
                if (value == -2) return null;
                bytes.Reverse();
                string line = Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\r');
                if (Position == 0) line = line.TrimStart('\uFEFF');
                if (line.Length > LogStore.MaxLineCharacters)
                    throw new InvalidDataException("로그 한 줄이 허용 크기를 초과했습니다.");
                return line;
            }
        }
    }
}
