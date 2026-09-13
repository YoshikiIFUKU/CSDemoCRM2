using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CrmDemo
{
    /// <summary>PCごとの設定（テーマ、一覧に表示する列）。データファイルと同じフォルダの crm_settings.ini に保存する。</summary>
    public static class Settings
    {
        public const string FileName = "crm_settings.ini";
        static readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string path;

        public static void Load(string dataFilePath)
        {
            try
            {
                path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataFilePath)), FileName);
                values.Clear();
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch { }
        }

        public static string Get(string key, string fallback = "")
        {
            string v;
            return values.TryGetValue(key, out v) ? v : fallback;
        }

        public static void Set(string key, string value)
        {
            values[key] = value ?? "";
            Save();
        }

        static void Save()
        {
            if (path == null) return;
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in values) sb.AppendLine(kv.Key + "=" + kv.Value);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>ケース一覧に表示する列（変数名のリスト）。未設定なら「一覧・標準出力に含める」フィールド。</summary>
        public static List<FieldDef> ListColumns(CrmData data)
        {
            string saved = Get("columns");
            if (saved.Length > 0)
            {
                var cols = saved.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
                                .Select(data.FieldByApi).Where(f => f != null).ToList();
                if (cols.Count > 0) return cols;
            }
            return data.ListFields;
        }

        public static void SetListColumns(IEnumerable<FieldDef> fields)
        {
            Set("columns", string.Join(",", fields.Select(f => f.ApiName).ToArray()));
        }
    }
}
