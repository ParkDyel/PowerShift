using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PowerShift
{
    public static class Tests
    {
        private static readonly Guid Saver = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");
        private static readonly Guid Balanced = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        private static readonly Guid Performance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        private static int passed;
        private static void Assert(bool ok, string name)
        {
            if (!ok) throw new Exception("FAIL: " + name);
            passed++; Console.WriteLine("PASS: " + name);
        }
        private static AppSettings Sample()
        {
            AppSettings settings = new AppSettings { DefaultPlanId = Saver.ToString() };
            settings.Rules.Add(new ProcessRule { ProcessName = "game", PlanId = Performance.ToString() });
            settings.Rules.Add(new ProcessRule { ProcessName = "editor", PlanId = Balanced.ToString() });
            return settings;
        }
        public static int Run(bool integration)
        {
            try
            {
                HashSet<Guid> available = new HashSet<Guid> { Saver, Balanced, Performance };
                AppSettings settings = Sample();
                Assert(RuleEngine.Resolve(settings, new string[0], available, Balanced).PlanId == Saver, "idle uses saved default");
                Decision result = RuleEngine.Resolve(settings, new[] { "GAME.EXE" }, available, Balanced);
                Assert(result.PlanId == Performance && result.IsOverride, "case insensitive process match with extension");
                Assert(RuleEngine.Resolve(settings, new[] { "game-helper" }, available, Balanced).PlanId == Saver, "exact process name, no partial match");
                Assert(RuleEngine.Resolve(settings, new[] { "editor", "game" }, available, Balanced).PlanId == Performance, "top matching rule wins regardless of process order");
                settings.Rules.Reverse();
                Assert(RuleEngine.Resolve(settings, new[] { "editor", "game" }, available, Balanced).PlanId == Balanced, "reordering changes priority");
                settings.Rules[0].Enabled = false;
                Assert(RuleEngine.Resolve(settings, new[] { "editor", "game" }, available, Balanced).PlanId == Performance, "disabled rule is ignored");
                settings.AutomaticSwitching = false;
                Assert(RuleEngine.Resolve(settings, new[] { "game" }, available, Balanced).PlanId == Saver, "pausing automation restores default");
                settings.AutomaticSwitching = true;
                settings.DefaultPlanId = Balanced.ToString();
                Assert(RuleEngine.Resolve(settings, new[] { "game" }, available, Saver).PlanId == Performance &&
                    RuleEngine.Resolve(settings, new string[0], available, Saver).PlanId == Balanced,
                    "manual default change waits for active rule to finish, then restores new default");
                available.Remove(Performance);
                Assert(RuleEngine.Resolve(settings, new[] { "game" }, available, Saver).PlanId == Balanced, "deleted rule plan falls back to default");
                available.Remove(Balanced);
                Assert(RuleEngine.Resolve(settings, new string[0], available, Saver).PlanId == Saver, "deleted default preserves current plan");
                Assert(RuleEngine.Normalize(@"C:\Games\game.exe") == "game" && RuleEngine.Normalize("foo.bar.exe") == "foo.bar", "path and dotted executable normalization");
                PathRules();
                LogTests();
                SecurityResourceTests();
                LogPagingTests();
                LogViewTests();
                string folder = Path.Combine(Path.GetTempPath(), "PowerShiftTests-" + Guid.NewGuid().ToString("N"));
                try
                {
                    SettingsStore store = new SettingsStore(Path.Combine(folder, "settings.json"));
                    Assert(store.Load(Saver).DefaultPlanId == Saver.ToString(), "first launch adopts current plan");
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(store.FilePath, "{\"Version\":1,\"DefaultPlanId\":\"" + Saver + "\",\"AutomaticSwitching\":true,\"Rules\":[]}");
                    Assert(store.Load(Saver).LogRetentionDays == 3, "existing settings migrate to three-day retention");
                    AppSettings noExpiry = Sample(); noExpiry.LogRetentionDays = 0; store.Save(noExpiry);
                    Assert(store.Load(Saver).LogRetentionDays == 0 && noExpiry.Copy().LogRetentionDays == 0, "zero-day retention persists and survives draft copy");
                    noExpiry.LogRetentionDays = -1;
                    bool negativeRejected = false;
                    try { store.Save(noExpiry); } catch (InvalidDataException) { negativeRejected = true; }
                    Assert(negativeRejected && store.Load(Saver).LogRetentionDays == 0, "negative retention is rejected without overwriting settings");
                    settings = Sample(); store.Save(settings);
                    settings.DefaultPlanId = Balanced.ToString(); store.Save(settings);
                    AppSettings loaded = store.Load(Saver);
                    Assert(loaded.DefaultPlanId == Balanced.ToString() && loaded.Rules.Count == 2 && loaded.Rules[0].ProcessName == "game",
                        "settings round trip and atomic replacement preserve rules");
                    AppSettings copy = loaded.Copy(); copy.Rules[0].Enabled = false;
                    Assert(loaded.Rules[0].Enabled, "cancelled draft cannot mutate live rules");
                    loaded.Rules.Add(new ProcessRule { ProcessName = "GAME.EXE", PlanId = Saver.ToString() });
                    bool duplicateRejected = false;
                    try { store.Save(loaded); } catch (InvalidDataException) { duplicateRejected = true; }
                    Assert(duplicateRejected && store.Load(Saver).Rules.Count == 2, "duplicate rejected before overwriting file");
                    AppSettings paths = new AppSettings { DefaultPlanId = Saver.ToString() };
                    paths.Rules.Add(new ProcessRule { ProcessName = "game", ExecutablePath = @"C:\Apps\A\game.exe", PlanId = Performance.ToString() });
                    paths.Rules.Add(new ProcessRule { ProcessName = "game", ExecutablePath = @"C:\Apps\B\game.exe", PlanId = Balanced.ToString() });
                    store.Save(paths);
                    AppSettings restored = store.Load(Saver);
                    Assert(restored.Rules.Count == 2 && restored.Rules[1].ExecutablePath == @"C:\Apps\B\game.exe" &&
                        restored.Copy().Rules[0].ExecutablePath == @"C:\Apps\A\game.exe", "different paths persist and survive draft copy");
                    restored.Rules.Add(new ProcessRule { ProcessName = "GAME.EXE", ExecutablePath = @"c:\apps\a\.\GAME.exe", PlanId = Saver.ToString() });
                    bool duplicatePathRejected = false;
                    try { store.Save(restored); } catch (InvalidDataException) { duplicatePathRejected = true; }
                    Assert(duplicatePathRejected && store.Load(Saver).Rules.Count == 2, "same canonical path is rejected regardless of case");
                    File.WriteAllText(store.FilePath, "{corrupt");
                    bool corruptRejected = false;
                    try { store.Load(Saver); } catch (ArgumentException) { corruptRejected = true; }
                    Assert(corruptRejected && File.ReadAllText(store.FilePath) == "{corrupt", "corrupt settings preserved");
                }
                finally
                {
                    // Delete only files in this test's explicitly created temp directory.
                    if (Directory.Exists(folder))
                    { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
                }
                if (integration) Integration();
                Console.WriteLine("Passed " + passed + " checks.");
                return 0;
            }
            catch (Exception error) { Console.WriteLine(error); return 1; }
        }
        private static void LogPagingTests()
        {
            string folder = Path.Combine(Path.GetTempPath(), "PowerShiftPagingTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                LogStore store = new LogStore(folder);
                Assert(!new LogPageReader(folder).HasMore, "empty log history has no pages");
                Directory.CreateDirectory(folder);
                string first = Path.Combine(folder, "PowerShift-2026-11-02.jsonl");
                string second = Path.Combine(folder, "PowerShift-2026-11-03.jsonl");
                using (StreamWriter writer = new StreamWriter(first, false, new System.Text.UTF8Encoding(true)))
                    for (int i = 0; i < 153; i++) writer.WriteLine("{\"Rule\":\"한글 🚗 " + i + "\"}");
                using (StreamWriter writer = new StreamWriter(second, false, new System.Text.UTF8Encoding(false)))
                {
                    for (int i = 153; i < 317; i++) writer.WriteLine("{\"Rule\":\"한글 🚗 " + i + "\"}");
                    writer.Write("\n{broken}\r\n{\"Rule\":\"한글 🚗 317\"}");
                }
                LogPageReader reader = new LogPageReader(folder);
                List<LogEntry> page = reader.ReadPage(100);
                Assert(page.Count == 100 && page[0].Rule == "한글 🚗 317" && page[99].Rule == "한글 🚗 218",
                    "first page reads latest 100, skipping malformed and blank lines without final newline");
                File.AppendAllText(second, "\n{\"Rule\":\"later append\"}\n");
                List<LogEntry> all = new List<LogEntry>(page);
                while (reader.HasMore) all.AddRange(reader.ReadPage(100));
                Assert(all.Count == 318 && all.Select((entry, index) => entry.Rule == "한글 🚗 " + (317 - index)).All(ok => ok),
                    "pages cross daily files and UTF-8 BOM with no duplicates, gaps, or appended snapshot rows");
                Assert(reader.ReadPage(100).Count == 0 && new LogPageReader(folder).ReadPage(100)[0].Rule == "later append",
                    "exhausted cursor stays empty and refresh includes newly appended records");
                bool badCount = false;
                try { reader.ReadPage(101); } catch (ArgumentOutOfRangeException) { badCount = true; }
                Assert(badCount, "page size cannot exceed bounded 100 rows");
                File.Delete(first);
                using (StreamWriter writer = new StreamWriter(second))
                {
                    writer.WriteLine("{\"Rule\":\"oldest\"}");
                    for (int i = 0; i < 6000; i++) writer.WriteLine(new string('x', 1000));
                    writer.WriteLine("{\"Rule\":\"newest\"}");
                }
                reader = new LogPageReader(folder);
                page = reader.ReadPage(100);
                Assert(page.Count == 1 && page[0].Rule == "newest" && reader.HasMore,
                    "one page stops at 4 MiB even when malformed records are sparse");
                page = reader.ReadPage(100);
                Assert(page.Count == 1 && page[0].Rule == "oldest" && !reader.HasMore,
                    "next page resumes beyond 4 MiB without losing the boundary record");
                string longRule = new string('가', 20000);
                File.WriteAllText(second, "{\"Rule\":\"" + longRule + "\"}\n{\"Rule\":\"tail\"}\n", System.Text.Encoding.UTF8);
                page = new LogPageReader(folder).ReadPage(100);
                Assert(page.Count == 2 && page[1].Rule == longRule, "reverse chunks preserve a multibyte line longer than the read buffer");
                reader = new LogPageReader(folder);
                File.WriteAllText(second, "{}");
                bool changed = false;
                try { reader.ReadPage(100); } catch (IOException) { changed = true; }
                Assert(changed, "truncated snapshot requires refresh rather than silently skipping history");
                File.WriteAllText(second, new string('x', LogStore.MaxLineCharacters + 1));
                bool oversized = false;
                try { new LogPageReader(folder).ReadPage(100); } catch (InvalidDataException) { oversized = true; }
                Assert(oversized, "paged reader rejects oversized lines like the original bounded reader");
                long before = store.PruneRevision;
                File.WriteAllText(second, "{\"TimestampUtc\":\"2020-01-01T00:00:00Z\"}\n");
                Assert(store.Prune(3, DateTimeOffset.UtcNow) && store.PruneRevision > before,
                    "retention invalidates browsing snapshots");
            }
            finally
            {
                if (Directory.Exists(folder))
                { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
            }
        }
        private static IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control parent)
        {
            foreach (System.Windows.Forms.Control child in parent.Controls)
            {
                yield return child;
                foreach (System.Windows.Forms.Control descendant in Descendants(child)) yield return descendant;
            }
        }
        private static void WaitForLogs(System.Windows.Forms.Button refresh)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (!refresh.Enabled && clock.ElapsedMilliseconds < 10000)
            { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(5); }
            if (!refresh.Enabled) throw new Exception("Log UI worker did not finish.");
        }
        private static void LogViewTests()
        {
            string folder = Path.Combine(Path.GetTempPath(), "PowerShiftLogViewTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                LogStore store = new LogStore(folder);
                string path = Path.Combine(folder, "PowerShift-" + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".jsonl");
                using (StreamWriter writer = new StreamWriter(path))
                    for (int i = 0; i < 317; i++) writer.WriteLine("{\"Rule\":\"rule " + i + "\"}");
                using (System.Windows.Forms.Form host = new System.Windows.Forms.Form
                    { ShowInTaskbar = false, StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                        Location = new System.Drawing.Point(-3000, -3000), Size = new System.Drawing.Size(900, 500) })
                using (LogsView view = new LogsView(store, 3))
                {
                    host.Controls.Add(view);
                    System.Windows.Forms.Button refresh = Descendants(view).OfType<System.Windows.Forms.Button>().Single(b => b.Text == "새로고침");
                    System.Windows.Forms.Button more = Descendants(view).OfType<System.Windows.Forms.Button>().Single(b => b != refresh);
                    System.Windows.Forms.ListView list = Descendants(view).OfType<System.Windows.Forms.ListView>().Single();
                    System.Windows.Forms.ProgressBar progress = Descendants(view).OfType<System.Windows.Forms.ProgressBar>().Single();
                    host.Show();
                    Assert(progress.Visible && !refresh.Enabled, "log load shows a marquee indicator and disables duplicate requests");
                    WaitForLogs(refresh);
                    Assert(list.Items.Count == 100 && list.Items[0].SubItems[3].Text == "rule 316" && !progress.Visible,
                        "log UI initially displays only the latest 100 rows and clears indicator");
                    more.PerformClick(); WaitForLogs(refresh);
                    Assert(list.Items.Count == 200 && list.Items[199].SubItems[3].Text == "rule 117",
                        "load-more appends older rows in order");
                    list.Items[150].Selected = true; list.EnsureVisible(150);
                    int top = list.TopItem.Index;
                    store.Append(new LogEntry { Rule = "new live record" }); view.RefreshEntries();
                    Assert(refresh.Enabled && list.Items.Count == 200 && list.TopItem.Index == top && list.SelectedIndices[0] == 150,
                        "live log notification preserves older rows, scroll position, and selection");
                    refresh.PerformClick(); WaitForLogs(refresh);
                    Assert(list.Items.Count == 100 && list.Items[0].SubItems[3].Text == "new live record",
                        "explicit refresh resets snapshot and includes new live records");
                    Assert(store.Prune(1, DateTimeOffset.UtcNow.AddDays(2)), "retention completes while log viewer is open");
                    view.RefreshEntries(); WaitForLogs(refresh);
                    Assert(list.Items.Count == 100 && list.Items[0].SubItems[3].Text == "rule 316",
                        "retention invalidation reloads safely without stale cursor results");
                    File.WriteAllText(path, new string('x', LogStore.MaxLineCharacters + 1));
                    refresh.PerformClick(); WaitForLogs(refresh);
                    Assert(refresh.Enabled && !progress.Visible && !more.Enabled && Descendants(view).OfType<System.Windows.Forms.Label>()
                        .Any(label => label.Text.StartsWith("로그를 읽을 수 없습니다:")), "read errors clear indicator and offer refresh recovery");
                    using (StreamWriter writer = new StreamWriter(path))
                        for (int i = 0; i < 50; i++) writer.WriteLine("{\"Rule\":\"" + new string('x', 100000) + "\"}");
                    long length = new FileInfo(path).Length;
                    refresh.PerformClick(); WaitForLogs(refresh);
                    Assert(list.Items.Count > 0 && list.Items.Count < 50 && !more.Enabled && new FileInfo(path).Length == length &&
                        Descendants(view).OfType<System.Windows.Forms.Label>().Any(label => label.Text.Contains("화면 메모리 한도")),
                        "display memory limit stops further loading without truncating stored history");
                    File.WriteAllText(path, "{\"Rule\":\"recovered\"}\n");
                    refresh.PerformClick(); WaitForLogs(refresh);
                    Assert(list.Items.Count == 1 && list.Items[0].SubItems[3].Text == "recovered" && !more.Enabled,
                        "refresh recovers from read errors and an exhausted history disables load-more");
                    using (StreamWriter writer = new StreamWriter(path))
                        for (int i = 0; i < 200; i++) writer.WriteLine("{\"Rule\":\"close test " + i + "\"}");
                    refresh.PerformClick(); WaitForLogs(refresh);
                    more.PerformClick(); host.Close();
                    // A pending completion must not access disposed controls.
                    Stopwatch clock = Stopwatch.StartNew();
                    while (clock.ElapsedMilliseconds < 100) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                }
            }
            finally
            {
                if (Directory.Exists(folder))
                { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
            }
        }
        private static void LogTests()
        {
            string folder = Path.Combine(Path.GetTempPath(), "PowerShiftLogTests-" + Guid.NewGuid().ToString("N"));
            DateTimeOffset now = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
            try
            {
                LogStore store = new LogStore(folder);
                DateTimeOffset[] times = { now.AddDays(-4), now.AddDays(-3).AddTicks(-1), now.AddDays(-3), now.AddHours(-1), now.AddHours(1) };
                foreach (DateTimeOffset time in times)
                    Assert(store.Append(new LogEntry { TimestampUtc = time.ToString("o"), Level = "전환", FromPlan = "절전", ToPlan = "고성능",
                        PreviousRule = "기본값", Rule = "프로그램 규칙 #1: game.exe", ExecutablePath = @"C:\Games\game.exe", Reason = "실행 중인 프로그램 규칙 적용" }), "append audit record at " + time.ToString("o"));
                Assert(store.ReadRecent(2).Count == 2 && store.ReadRecent(2)[0].TimestampUtc == times[4].ToString("o"), "recent log view is bounded and newest first");
                LogEntry entry = store.ReadRecent(1)[0];
                Assert(entry.FromPlan == "절전" && entry.ToPlan == "고성능" && entry.Rule.Contains("#1") && entry.PreviousRule == "기본값" &&
                    entry.ExecutablePath == @"C:\Games\game.exe" && entry.Reason.Contains("규칙"), "audit fields survive serialization including Unicode");
                long revision = store.Revision;
                Assert(store.Prune(0, now) && store.ReadRecent(100).Count == 5 && store.Revision == revision, "zero retention never deletes old logs");
                string unrelated = Path.Combine(folder, "unrelated.jsonl"); File.WriteAllText(unrelated, "keep");
                string malformedFile = Path.Combine(folder, "PowerShift-2026-10-30.jsonl");
                File.AppendAllText(malformedFile, "not-json" + Environment.NewLine);
                Assert(store.Prune(3, now) && store.ReadRecent(100).Count == 3, "three-day retention removes only expired entries");
                Assert(store.ReadRecent(100).Any(e => e.TimestampUtc == times[2].ToString("o")), "exact retention boundary is retained");
                Assert(File.ReadAllText(malformedFile).Trim() == "not-json" && File.ReadAllText(unrelated) == "keep", "unknown records and unrelated files are preserved");
                Assert(store.Prune(Int32.MaxValue, now) && store.ReadRecent(100).Count == 3, "very large retention cannot overflow date arithmetic");
                LogStore reopened = new LogStore(folder);
                Assert(reopened.ReadRecent(100).Count == 3, "log history survives reopening");
                string blocked = Path.Combine(folder, "blocked"); File.WriteAllText(blocked, "file");
                LogStore unwritable = new LogStore(blocked);
                Assert(!unwritable.Append(new LogEntry { Reason = "test" }) && !String.IsNullOrEmpty(unwritable.LastError), "log write failure is reported without throwing into power switching");
            }
            finally
            {
                if (Directory.Exists(folder))
                { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
            }
        }
        private static void PathRules()
        {
            HashSet<Guid> available = new HashSet<Guid> { Saver, Balanced, Performance };
            AppSettings settings = new AppSettings { DefaultPlanId = Saver.ToString() };
            settings.Rules.Add(new ProcessRule { ProcessName = "game", ExecutablePath = @"C:\Apps\A\game.exe", PlanId = Performance.ToString() });
            settings.Rules.Add(new ProcessRule { ProcessName = "game", ExecutablePath = @"C:\Apps\B\game.exe", PlanId = Balanced.ToString() });
            SettingsStore.Validate(settings);
            Assert(RuleEngine.ResolveProcesses(settings, new[] {
                new RunningProcess { ProcessName = "game", ExecutablePath = @"C:\Apps\C\game.exe" },
                new RunningProcess { ProcessName = "helper", ExecutablePath = @"C:\Apps\A\game.exe" }
                }, available, Saver).PlanId == Saver, "name and full path must match the same process record");
            Assert(RuleEngine.ResolveProcesses(settings, new[] { new RunningProcess { ProcessName = "game", ExecutablePath = @"c:/apps/a/./GAME.EXE" } },
                available, Saver).PlanId == Performance, "full path matches with case and separator normalization");
            Assert(RuleEngine.ResolveProcesses(settings, new[] { new RunningProcess { ProcessName = "game", ExecutablePath = @"C:\Apps\B\game.exe" } },
                available, Saver).PlanId == Balanced, "same EXE name at a different path uses its own rule");
            Assert(RuleEngine.Resolve(settings, new[] { @"C:\Apps\C\game.exe" }, available, Saver).PlanId == Saver,
                "same EXE name at unrelated path cannot activate a path rule");
            Assert(RuleEngine.ResolveProcesses(settings, new[] { new RunningProcess { ProcessName = "game" } }, available, Saver).PlanId == Saver,
                "unreadable process path cannot activate a path rule");
            Assert(RuleEngine.Resolve(settings, new[] { @"C:\Apps\B\game.exe", @"C:\Apps\A\game.exe" }, available, Saver).PlanId == Performance,
                "path rules preserve list priority when both executables run");
            settings.Rules.Add(new ProcessRule { ProcessName = "game", PlanId = Balanced.ToString() });
            SettingsStore.Validate(settings);
            Assert(RuleEngine.Resolve(settings, new[] { @"C:\Apps\C\game.exe" }, available, Saver).PlanId == Balanced,
                "legacy name-only rule remains a wildcard fallback");
            settings.Rules[0].ExecutablePath = @"C:\Apps\A\other.exe";
            bool mismatchRejected = false;
            try { SettingsStore.Validate(settings); } catch (InvalidDataException) { mismatchRejected = true; }
            Assert(mismatchRejected, "mismatched process name and path are rejected");
            bool relativeRejected = false;
            try { RuleEngine.NormalizePath(@"Apps\game.exe"); } catch (InvalidDataException) { relativeRejected = true; }
            Assert(relativeRejected, "relative executable paths are rejected");
        }
        private static void SecurityResourceTests()
        {
            string folder = Path.Combine(Path.GetTempPath(), "PowerShiftSecurityTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string settingsFile = Path.Combine(folder, "settings.json");
                File.WriteAllText(settingsFile, new string(' ', SettingsStore.MaxSettingsBytes + 1));
                bool oversizedSettingsRejected = false;
                try { new SettingsStore(settingsFile).Load(Saver); } catch (InvalidDataException) { oversizedSettingsRejected = true; }
                Assert(oversizedSettingsRejected && new FileInfo(settingsFile).Length == SettingsStore.MaxSettingsBytes + 1,
                    "oversized settings rejected before deserialization without rewriting original");
                string logFile = Path.Combine(folder, "PowerShift-2026-11-03.jsonl");
                string filler = "{\"TimestampUtc\":\"2026-10-30T00:00:00Z\",\"Reason\":\"" + new string('x', 512) + "\"}";
                using (StreamWriter writer = new StreamWriter(logFile, false, new System.Text.UTF8Encoding(false)))
                {
                    for (int i = 0; i < 10000; i++) writer.WriteLine(filler);
                    writer.WriteLine("{\"TimestampUtc\":\"2026-11-03T00:00:00Z\",\"Reason\":\"newest\"}");
                }
                LogStore logs = new LogStore(folder);
                long originalSize = new FileInfo(logFile).Length;
                Assert(originalSize > LogStore.MaxViewBytes && logs.ReadRecent(500)[0].Reason == "newest" && logs.ViewWasLimited,
                    "large log view reads a bounded tail and retains newest entry");
                Assert(new FileInfo(logFile).Length == originalSize, "bounded log display never truncates stored history");
                Assert(logs.Prune(3, new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero)) && logs.ReadRecent(10).Count == 1,
                    "streaming retention prunes large logs without buffering all retained lines");
                string oversizedLine = filler + Environment.NewLine + new string('x', LogStore.MaxLineCharacters + 1);
                File.WriteAllText(logFile, oversizedLine);
                bool oversizedLineRejected = false;
                try { logs.ReadRecent(10); } catch (InvalidDataException) { oversizedLineRejected = true; }
                Assert(oversizedLineRejected, "oversized log line rejected by bounded reader");
                Assert(!logs.Prune(3, new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero)) && File.ReadAllText(logFile) == oversizedLine,
                    "failed pruning preserves entire original log");
                Assert(Directory.GetFiles(folder, "*.tmp").Length == 0, "failed pruning cleans only its temporary output");
                Assert(!logs.Append(new LogEntry { Reason = new string('x', LogStore.MaxLineCharacters + 1) }) && File.ReadAllText(logFile) == oversizedLine,
                    "oversized audit entry cannot append an unbounded record");
            }
            finally
            { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
        }
        public static int RunRedirectChecks(string root)
        {
            try
            {
                string outside = Path.Combine(root, "outside");
                LogStore original = new LogStore(outside);
                original.Append(new LogEntry { TimestampUtc = "2026-10-01T00:00:00Z", Reason = "must remain" });
                string originalLog = Path.Combine(outside, "PowerShift-2026-10-01.jsonl");
                string originalLogText = File.ReadAllText(originalLog);
                LogStore redirected = new LogStore(Path.Combine(root, "log-link"));
                Assert(!redirected.Prune(3, DateTimeOffset.UtcNow) && File.ReadAllText(originalLog) == originalLogText,
                    "junction log root cannot delete outside records");
                Assert(!redirected.Append(new LogEntry { Reason = "must not write" }) && Directory.GetFiles(outside).Length == 1,
                    "junction log root cannot append outside records");
                bool readRejected = false;
                try { redirected.ReadRecent(10); } catch (IOException) { readRejected = true; }
                Assert(readRejected, "junction log root cannot read outside history");
                bool pageRejected = false;
                try { new LogPageReader(redirected.DirectoryPath).ReadPage(100); } catch (IOException) { pageRejected = true; }
                Assert(pageRejected, "paged reader cannot browse outside history through a junction");
                string outsideSettings = Path.Combine(outside, "settings.json");
                File.WriteAllText(outsideSettings, "must remain");
                SettingsStore redirectedSettings = new SettingsStore(Path.Combine(root, "data-link", "settings.json"));
                bool saveRejected = false;
                try { redirectedSettings.Save(Sample()); } catch (IOException) { saveRejected = true; }
                Assert(saveRejected && File.ReadAllText(outsideSettings) == "must remain", "junction settings root cannot overwrite outside file");
                bool loadRejected = false;
                try { redirectedSettings.Load(Saver); } catch (IOException) { loadRejected = true; }
                Assert(loadRejected, "junction settings root cannot load outside configuration");
                LogStore redirectedParent = new LogStore(Path.Combine(root, "data-link", "logs"));
                Assert(!redirectedParent.Append(new LogEntry { Reason = "must not write" }) && !Directory.Exists(Path.Combine(outside, "logs")),
                    "junction app-data parent cannot redirect log writes");
                return 0;
            }
            catch (Exception error) { Console.WriteLine(error); return 1; }
        }
        private static void Integration()
        {
            Guid original = PowerPlans.Active();
            Process child = null;
            try
            {
                List<Plan> plans = PowerPlans.List();
                HashSet<Guid> available = new HashSet<Guid>(plans.Select(p => p.Id));
                Assert(plans.All(p => !String.IsNullOrWhiteSpace(p.Name)) && available.Contains(original), "native localized enumeration includes active plan");
                foreach (Plan plan in plans)
                {
                    PowerPlans.Set(plan.Id);
                    Assert(PowerPlans.Active() == plan.Id, "native switch: " + plan.Name);
                }
                Guid target = plans.First(p => p.Id != original).Id;
                AppSettings settings = new AppSettings { DefaultPlanId = original.ToString() };
                settings.Rules.Add(new ProcessRule { ProcessName = "ping", PlanId = target.ToString() });
                child = Process.Start(new ProcessStartInfo("ping.exe", "-t 127.0.0.1")
                    { UseShellExecute = false, CreateNoWindow = true });
                string imagePath = ProcessCatalog.ReadPath(child.Id);
                Assert(!String.IsNullOrEmpty(imagePath) && String.Equals(Path.GetFileName(imagePath), "ping.exe", StringComparison.OrdinalIgnoreCase),
                    "native query reads actual executable full path");
                settings.Rules[0].ExecutablePath = imagePath;
                Assert(RuleEngine.ResolveProcesses(settings, ProcessCatalog.Capture(settings.Rules), available, original).PlanId == target,
                    "real process snapshot matches exact executable path");
                settings.Rules[0].ExecutablePath = null;
                Decision active = RuleEngine.Resolve(settings, Snapshot(), available, original);
                PowerPlans.Set(active.PlanId);
                Assert(active.IsOverride && PowerPlans.Active() == target, "real process launch selects profile");
                child.Kill(); child.WaitForExit(); child.Dispose(); child = null;
                Decision idle = RuleEngine.Resolve(settings, Snapshot(), available, original);
                PowerPlans.Set(idle.PlanId);
                Assert(!idle.IsOverride && PowerPlans.Active() == original, "real process exit restores default");
            }
            finally
            {
                if (child != null) { if (!child.HasExited) { child.Kill(); child.WaitForExit(); } child.Dispose(); }
                PowerPlans.Set(original);
                Console.WriteLine("Restored original plan: " + PowerPlans.Active());
            }
        }
        private static string[] Snapshot()
        {
            List<string> names = new List<string>();
            foreach (Process process in Process.GetProcesses())
            {
                using (process) { try { names.Add(process.ProcessName); } catch (InvalidOperationException) { } }
            }
            return names.ToArray();
        }
    }
}
