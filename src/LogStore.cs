using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace PowerShift
{
    public sealed class LogEntry
    {
        public string TimestampUtc { get; set; }
        public string Level { get; set; }
        public string FromPlan { get; set; }
        public string ToPlan { get; set; }
        public string PreviousRule { get; set; }
        public string Rule { get; set; }
        public string ExecutablePath { get; set; }
        public string Reason { get; set; }
        [ScriptIgnore]
        public string LocalTime
        {
            get
            {
                DateTimeOffset timestamp;
                return DateTimeOffset.TryParse(TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp)
                    ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : TimestampUtc;
            }
        }
    }

    public sealed class LogStore
    {
        public const int MaxLineCharacters = 128 * 1024;
        public const int MaxViewBytes = 4 * 1024 * 1024;
        public readonly string DirectoryPath;
        public long Revision { get; private set; }
        public long PruneRevision { get; private set; }
        public string LastError { get; private set; }
        public bool ViewWasLimited { get; private set; }
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = MaxLineCharacters, RecursionLimit = 16 };
        public LogStore(string directoryPath) { DirectoryPath = Path.GetFullPath(directoryPath); }
        public static string DefaultPath { get { return Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath), "logs"); } }
        private void CheckDirectory()
        {
            SafeStorage.RejectReparsePoint(Path.GetDirectoryName(DirectoryPath));
            SafeStorage.RejectReparsePoint(DirectoryPath);
        }
        public bool Append(LogEntry entry)
        {
            try
            {
                DateTimeOffset timestamp;
                if (!DateTimeOffset.TryParse(entry.TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
                    entry.TimestampUtc = (timestamp = DateTimeOffset.UtcNow).ToString("o", CultureInfo.InvariantCulture);
                string json = serializer.Serialize(entry);
                CheckDirectory();
                SafeStorage.EnsureDirectory(DirectoryPath);
                string file = Path.Combine(DirectoryPath, "PowerShift-" + timestamp.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");
                SafeStorage.RejectReparsePoint(file);
                using (FileStream stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.WriteLine(json);
                Revision++; LastError = null; return true;
            }
            catch (Exception error) { LastError = error.Message; return false; }
        }
        internal List<string> Files()
        {
            List<string> result = new List<string>();
            CheckDirectory();
            if (!Directory.Exists(DirectoryPath)) return result;
            foreach (string file in Directory.GetFiles(DirectoryPath, "PowerShift-*.jsonl"))
            {
                string name = Path.GetFileName(file);
                DateTime date;
                if (name.Length == 27 && DateTime.TryParseExact(name.Substring(11, 10), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                {
                    try { if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) result.Add(file); }
                    catch (FileNotFoundException) { }
                }
            }
            result.Sort(StringComparer.Ordinal); result.Reverse(); return result;
        }
        private LogEntry Parse(string line)
        {
            try { return serializer.Deserialize<LogEntry>(line); }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }
        public List<LogEntry> ReadRecent(int limit)
        {
            List<LogEntry> result = new List<LogEntry>();
            ViewWasLimited = false;
            if (limit <= 0) return result;
            long budget = MaxViewBytes;
            foreach (string file in Files())
            {
                if (budget <= 0) { ViewWasLimited = true; break; }
                Queue<LogEntry> tail = new Queue<LogEntry>();
                using (FileStream stream = SafeStorage.OpenRead(file))
                {
                    long count = Math.Min(stream.Length, budget);
                    long start = stream.Length - count;
                    budget -= count;
                    bool boundary = start == 0;
                    if (start > 0)
                    {
                        ViewWasLimited = true;
                        stream.Position = start - 1;
                        boundary = stream.ReadByte() == '\n';
                        stream.Position = start;
                    }
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        if (!boundary) SafeStorage.ReadLine(reader, MaxLineCharacters);
                        string line;
                        while ((line = SafeStorage.ReadLine(reader, MaxLineCharacters)) != null)
                        {
                            LogEntry entry = Parse(line);
                            if (entry == null) continue;
                            tail.Enqueue(entry);
                            if (tail.Count > limit - result.Count) tail.Dequeue();
                        }
                    }
                }
                List<LogEntry> newest = new List<LogEntry>(tail); newest.Reverse(); result.AddRange(newest);
                if (result.Count >= limit) break;
            }
            return result;
        }
        public bool Prune(int days, DateTimeOffset now)
        {
            if (days == 0) return true;
            if (days < 0) throw new ArgumentOutOfRangeException("days");
            DateTimeOffset cutoff = days > now.UtcDateTime.Ticks / TimeSpan.TicksPerDay
                ? DateTimeOffset.MinValue : now.ToUniversalTime().AddDays(-days);
            try
            {
                foreach (string file in Files())
                {
                    int kept = 0;
                    bool changed = false;
                    string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (FileStream input = SafeStorage.OpenRead(file))
                        using (StreamReader reader = new StreamReader(input, Encoding.UTF8, true))
                        using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        using (StreamWriter writer = new StreamWriter(output, new UTF8Encoding(false)))
                        {
                            string line;
                            while ((line = SafeStorage.ReadLine(reader, MaxLineCharacters)) != null)
                            {
                                LogEntry entry = Parse(line);
                                DateTimeOffset timestamp;
                                if (entry != null && DateTimeOffset.TryParse(entry.TimestampUtc, CultureInfo.InvariantCulture,
                                    DateTimeStyles.RoundtripKind, out timestamp) && timestamp < cutoff) changed = true;
                                else { writer.WriteLine(line); kept++; }
                            }
                        }
                        if (!changed) continue;
                        CheckDirectory(); SafeStorage.RejectReparsePoint(file);
                        if (kept == 0) File.Delete(file);
                        else File.Replace(temp, file, null);
                        Revision++; PruneRevision++;
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
                LastError = null; return true;
            }
            catch (Exception error) { LastError = error.Message; return false; }
        }
    }
}
