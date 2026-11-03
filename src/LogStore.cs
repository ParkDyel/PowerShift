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
        public readonly string DirectoryPath;
        public long Revision { get; private set; }
        public string LastError { get; private set; }
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        public LogStore(string directoryPath) { DirectoryPath = directoryPath; }
        public static string DefaultPath { get { return Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath), "logs"); } }
        public bool Append(LogEntry entry)
        {
            try
            {
                DateTimeOffset timestamp;
                if (!DateTimeOffset.TryParse(entry.TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
                    entry.TimestampUtc = (timestamp = DateTimeOffset.UtcNow).ToString("o", CultureInfo.InvariantCulture);
                Directory.CreateDirectory(DirectoryPath);
                string file = Path.Combine(DirectoryPath, "PowerShift-" + timestamp.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");
                File.AppendAllText(file, serializer.Serialize(entry) + Environment.NewLine, new UTF8Encoding(false));
                Revision++; LastError = null; return true;
            }
            catch (Exception error) { LastError = error.Message; return false; }
        }
        private List<string> Files()
        {
            List<string> result = new List<string>();
            if (!Directory.Exists(DirectoryPath)) return result;
            foreach (string file in Directory.GetFiles(DirectoryPath, "PowerShift-*.jsonl"))
            {
                string name = Path.GetFileName(file);
                DateTime date;
                if (name.Length == 27 && DateTime.TryParseExact(name.Substring(11, 10), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) result.Add(file);
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
            if (limit <= 0) return result;
            foreach (string file in Files())
            {
                Queue<LogEntry> tail = new Queue<LogEntry>();
                foreach (string line in File.ReadLines(file))
                {
                    LogEntry entry = Parse(line);
                    if (entry == null) continue;
                    tail.Enqueue(entry);
                    if (tail.Count > limit - result.Count) tail.Dequeue();
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
                    List<string> keep = new List<string>();
                    bool changed = false;
                    foreach (string line in File.ReadLines(file))
                    {
                        LogEntry entry = Parse(line);
                        DateTimeOffset timestamp;
                        if (entry != null && DateTimeOffset.TryParse(entry.TimestampUtc, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out timestamp) && timestamp < cutoff) changed = true;
                        else keep.Add(line); // Preserve unreadable records rather than deleting unknown data.
                    }
                    if (!changed) continue;
                    if (keep.Count == 0) File.Delete(file);
                    else
                    {
                        string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            File.WriteAllLines(temp, keep.ToArray(), new UTF8Encoding(false));
                            File.Replace(temp, file, null);
                        }
                        finally { if (File.Exists(temp)) File.Delete(temp); }
                    }
                    Revision++;
                }
                LastError = null; return true;
            }
            catch (Exception error) { LastError = error.Message; return false; }
        }
    }
}
