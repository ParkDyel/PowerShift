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
        public string ExecutablePath { get; set; }
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
                    ProcessName = rule.ProcessName, ExecutablePath = rule.ExecutablePath, PlanId = rule.PlanId });
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
                string executablePath = RuleEngine.NormalizePath(rule.ExecutablePath);
                if (executablePath != null && (!String.Equals(RuleEngine.Normalize(executablePath), normalized, StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("전체 경로의 EXE 파일 이름이 프로세스 이름과 일치해야 합니다.");
                if (!names.Add(RuleEngine.RuleKey(rule)))
                    throw new InvalidDataException("같은 프로그램과 경로에 대한 규칙은 하나만 등록할 수 있습니다: " + normalized);
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
        public static string NormalizePath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            string value = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')).Replace('/', '\\');
            if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) value = @"\\" + value.Substring(8);
            else if (value.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)) value = value.Substring(4);
            if (!(value.StartsWith(@"\\", StringComparison.Ordinal) ||
                (value.Length >= 3 && Char.IsLetter(value[0]) && value[1] == ':' && value[2] == '\\')))
                throw new InvalidDataException("실행 파일의 전체 경로를 입력해 주세요.");
            return Path.GetFullPath(value);
        }
        public static string RuleKey(ProcessRule rule)
        {
            return Normalize(rule.ProcessName) + "|" + (NormalizePath(rule.ExecutablePath) ?? "*");
        }
        public static string Normalize(string process)
        {
            string name = Path.GetFileName((process ?? "").Trim());
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - 4) : name;
        }
        public static Decision Resolve(AppSettings settings, IEnumerable<string> running,
            ISet<Guid> available, Guid fallback)
        {
            List<RunningProcess> processes = new List<RunningProcess>();
            foreach (string name in running) processes.Add(new RunningProcess { ProcessName = Normalize(name),
                ExecutablePath = Path.IsPathRooted(name) ? NormalizePath(name) : null });
            return ResolveProcesses(settings, processes, available, fallback);
        }
        public static Decision ResolveProcesses(AppSettings settings, IEnumerable<RunningProcess> running,
            ISet<Guid> available, Guid fallback)
        {
            if (settings.AutomaticSwitching && settings.Rules.Count > 0)
            {
                HashSet<string> processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (RunningProcess process in running)
                {
                    processes.Add(Normalize(process.ProcessName));
                    if (!String.IsNullOrWhiteSpace(process.ExecutablePath)) paths.Add(NormalizePath(process.ExecutablePath));
                }
                foreach (ProcessRule rule in settings.Rules)
                {
                    Guid id;
                    if (rule.Enabled && processes.Contains(Normalize(rule.ProcessName)) &&
                        (String.IsNullOrWhiteSpace(rule.ExecutablePath) || paths.Contains(NormalizePath(rule.ExecutablePath))) &&
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
