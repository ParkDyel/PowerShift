using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace PowerShift
{
    public sealed class ProcessRule
    {
        public bool Enabled { get; set; }
        public string ProcessName { get; set; }
        public string PlanId { get; set; }
        public ProcessRule() { Enabled = true; }
    }

    public sealed class AppSettings
    {
        public int Version { get; set; }
        public string DefaultPlanId { get; set; }
        public bool AutomaticSwitching { get; set; }
        public List<ProcessRule> Rules { get; set; }
        public AppSettings()
        {
            Version = 1;
            AutomaticSwitching = true;
            Rules = new List<ProcessRule>();
        }
        public AppSettings Copy()
        {
            AppSettings copy = new AppSettings { Version = Version,
                DefaultPlanId = DefaultPlanId, AutomaticSwitching = AutomaticSwitching };
            foreach (ProcessRule rule in Rules)
                copy.Rules.Add(new ProcessRule { Enabled = rule.Enabled,
                    ProcessName = rule.ProcessName, PlanId = rule.PlanId });
            return copy;
        }
    }

    public sealed class SettingsStore
    {
        public readonly string FilePath;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        public SettingsStore(string filePath) { FilePath = filePath; }
        public static string DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PowerShift", "settings.json"); }
        }
        public AppSettings Load(Guid initialPlan)
        {
            if (!File.Exists(FilePath))
                return new AppSettings { DefaultPlanId = initialPlan.ToString() };
            AppSettings settings = serializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
            Validate(settings);
            return settings;
        }
        public void Save(AppSettings settings)
        {
            Validate(settings);
            string directory = Path.GetDirectoryName(FilePath);
            Directory.CreateDirectory(directory);
            string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, serializer.Serialize(settings), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                else File.Move(temp, FilePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static void Validate(AppSettings settings)
        {
            Guid id;
            if (settings == null || settings.Version != 1 || settings.Rules == null ||
                !Guid.TryParse(settings.DefaultPlanId, out id))
                throw new InvalidDataException("PowerShift 설정 파일의 형식을 확인해 주세요.");
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProcessRule rule in settings.Rules)
            {
                if (rule == null || String.IsNullOrWhiteSpace(rule.ProcessName) ||
                    !Guid.TryParse(rule.PlanId, out id))
                    throw new InvalidDataException("프로그램별 규칙에 올바른 프로세스 이름과 전원 옵션이 필요합니다.");
                string normalized = RuleEngine.Normalize(rule.ProcessName);
                if (String.IsNullOrWhiteSpace(normalized) || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidDataException("프로세스 이름을 확인해 주세요.");
                if (!names.Add(normalized))
                    throw new InvalidDataException("같은 프로세스에 대한 규칙은 하나만 등록할 수 있습니다: " + normalized);
            }
        }
    }

    public sealed class Decision
    {
        public Guid PlanId;
        public string ProcessName;
        public bool IsOverride { get { return ProcessName != null; } }
    }

    public static class RuleEngine
    {
        public static string Normalize(string process)
        {
            string name = Path.GetFileName((process ?? "").Trim());
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - 4) : name;
        }
        public static Decision Resolve(AppSettings settings, IEnumerable<string> running,
            ISet<Guid> available, Guid fallback)
        {
            if (settings.AutomaticSwitching && settings.Rules.Count > 0)
            {
                HashSet<string> processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in running) processes.Add(Normalize(name));
                foreach (ProcessRule rule in settings.Rules)
                {
                    Guid id;
                    if (rule.Enabled && processes.Contains(Normalize(rule.ProcessName)) &&
                        Guid.TryParse(rule.PlanId, out id) && available.Contains(id))
                        return new Decision { PlanId = id, ProcessName = Normalize(rule.ProcessName) };
                }
            }
            Guid baseline;
            if (!Guid.TryParse(settings.DefaultPlanId, out baseline) || !available.Contains(baseline))
                baseline = fallback;
            return new Decision { PlanId = baseline };
        }
    }
}
