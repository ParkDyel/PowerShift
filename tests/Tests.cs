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
                string folder = Path.Combine(Path.GetTempPath(), "PowerShiftTests-" + Guid.NewGuid().ToString("N"));
                try
                {
                    SettingsStore store = new SettingsStore(Path.Combine(folder, "settings.json"));
                    Assert(store.Load(Saver).DefaultPlanId == Saver.ToString(), "first launch adopts current plan");
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
