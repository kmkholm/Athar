using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Athar
{
    public class WatchItem { public string Chain { get; set; } public string Address { get; set; } public string Note { get; set; } = ""; public long LastTime { get; set; } }
    public class Alert { public long Seen { get; set; } public Xfer X { get; set; } public string Watched { get; set; } }

    public class Settings
    {
        public string Lang { get; set; } = "ar";
        public string EtherscanKey { get; set; } = "";
        public int CacheSec { get; set; } = 900;
        public int GapMs { get; set; } = 0;
        public string Examiner { get; set; } = "";
        public string DefaultChain { get; set; } = "eth";
        public int Hops { get; set; } = 3;
        public int Branch { get; set; } = 5;
        public int MaxNodes { get; set; } = 50;
        public double MinShare { get; set; } = 3;
        public int WatchMin { get; set; } = 10;
        public string ActiveCase { get; set; } = "";
        public string ClaudeKeyEnc { get; set; } = "";
        public string RelayKey { get; set; } = "";
        public string ClaudeModel { get; set; } = "claude-opus-5-5";
        public List<string> Recent { get; set; } = new();
        public List<WatchItem> Watch { get; set; } = new();
        public List<Alert> Alerts { get; set; } = new();
    }

    public class UserLabel { public string Address { get; set; } public string Name { get; set; } public string Cat { get; set; } = "user"; public string Note { get; set; } = ""; }

    public class CaseItem { public string Chain { get; set; } public string Kind { get; set; } public string Value { get; set; } public string Role { get; set; } = ""; public string Note { get; set; } = ""; public long Added { get; set; } }

    public class CaseFile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Notes { get; set; } = "";
        public long Created { get; set; }
        public List<CaseItem> Items { get; set; } = new();
        public List<Graph> Traces { get; set; } = new();
        public List<string> Findings { get; set; } = new();
    }

    public static class Store
    {
        public static string Data, Cache;
        public static Settings S = new();
        public static List<UserLabel> UserLabels = new();
        public static readonly JsonSerializerOptions Jo = new() { WriteIndented = true, IncludeFields = true };
        static string CasesDir => Path.Combine(Data, "cases");

        public static void Init(string dir)
        {
            if (string.IsNullOrEmpty(dir))
            {
                string near = Path.Combine(AppContext.BaseDirectory, "AtharData");
                try { Directory.CreateDirectory(near); File.WriteAllText(Path.Combine(near, ".w"), ""); File.Delete(Path.Combine(near, ".w")); dir = near; }
                catch { dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Athar"); }
            }
            Data = dir; Cache = Path.Combine(dir, "cache");
            Directory.CreateDirectory(Cache); Directory.CreateDirectory(CasesDir);
            S = Load<Settings>(Path.Combine(Data, "settings.json")) ?? new Settings();
            UserLabels = Load<List<UserLabel>>(Path.Combine(Data, "labels.json")) ?? new();
            Http.HostHeaders["api.relay.link"] = ("x-api-key", S.RelayKey ?? "");
            try { foreach (var f in Directory.GetFiles(Cache)) if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalDays > 7) File.Delete(f); } catch { }
        }

        static T Load<T>(string path) where T : class
        {
            try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Jo) : null; } catch { return null; }
        }
        static void Write(string path, object o) { string tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(o, Jo)); File.Move(tmp, path, true); }

        public static void Save() => Write(Path.Combine(Data, "settings.json"), S);
        public static void SaveLabels() => Write(Path.Combine(Data, "labels.json"), UserLabels);

        public static List<CaseFile> AllCases() =>
            Directory.GetFiles(CasesDir, "*.json").Select(Load<CaseFile>).Where(c => c != null).OrderByDescending(c => c.Created).ToList();
        public static CaseFile NewCase(string name)
        {
            var c = new CaseFile { Id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 4), Name = name, Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            SaveCase(c); return c;
        }
        public static void SaveCase(CaseFile c) => Write(Path.Combine(CasesDir, c.Id + ".json"), c);
        public static void DeleteCase(CaseFile c) { try { File.Delete(Path.Combine(CasesDir, c.Id + ".json")); } catch { } }
        public static void ClearCache() { try { foreach (var f in Directory.GetFiles(Cache)) File.Delete(f); } catch { } }

        public static void AddRecent(string chain, string value)
        {
            string k = chain + "|" + value;
            S.Recent.Remove(k); S.Recent.Insert(0, k);
            if (S.Recent.Count > 12) S.Recent.RemoveRange(12, S.Recent.Count - 12);
            Save();
        }
    }
}
