using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace CrmDemo
{
    /// <summary>顧客（一意の顧客単位の情報）。項目は固定。</summary>
    public class Customer
    {
        public int Id;
        public string Name = "";
        public string Company = "";
        public string Phone = "";
        public string Email = "";
        public string Address = "";
        public string Memo = "";
        public DateTime CreatedAt = DateTime.Now;
        public DateTime UpdatedAt = DateTime.Now;

        public string DisplayName
        {
            get { return string.IsNullOrEmpty(Company) ? Name : Name + "（" + Company + "）"; }
        }
    }

    public static class FieldTypes
    {
        public const string Text = "text", TextArea = "textarea", Url = "url", Number = "number",
                            Date = "date", DateTimeType = "datetime", Select = "select", Checkbox = "checkbox";

        public static readonly string[] All = { Text, TextArea, Url, Number, Date, DateTimeType, Select, Checkbox };

        public static string Label(string type)
        {
            switch (type)
            {
                case TextArea: return "長文テキスト";
                case Url: return "URL（リンク）";
                case Number: return "数値";
                case Date: return "日付";
                case DateTimeType: return "日時";
                case Select: return "選択リスト";
                case Checkbox: return "チェックボックス";
                default: return "テキスト";
            }
        }

        /// <summary>保存する形にそろえる（日付は yyyy/MM/dd、チェックは 1 か空）</summary>
        public static string Normalize(string type, string value)
        {
            string v = (value ?? "").Replace("\r\n", "\n").Trim();
            if (v.Length == 0) return "";
            DateTime d;
            switch (type)
            {
                case Date:
                    return TextUtil.TryParseDate(v, out d) ? d.ToString("yyyy/MM/dd") : v;
                case DateTimeType:
                    if (DateTime.TryParse(v, new CultureInfo("ja-JP"), DateTimeStyles.AllowWhiteSpaces, out d))
                        return d.ToString("yyyy/MM/dd HH:mm");
                    return TextUtil.TryParseDate(v, out d) ? d.ToString("yyyy/MM/dd HH:mm") : v;
                case Checkbox:
                    string n = TextUtil.Norm(v);
                    return (n == "1" || n == "true" || n == "yes" || n == "はい" || n == "完了" ||
                            n == "済" || n == "済み" || n == "on" || n == "○") ? "1" : "";
                default:
                    return v;
            }
        }
    }

    /// <summary>ケースの入力項目の定義（画面の入力欄と、コマンドで指定できる変数名が同時に決まる）</summary>
    public class FieldDef
    {
        public int Id;
        public string Label = "";      // 表示名（例: 問い合わせ内容）
        public string ApiName = "";    // 変数名（例: inquiry）
        public string Type = FieldTypes.Text;
        public string Options = "";    // 選択リストの候補（改行区切り）
        public bool Required;
        public bool InList;            // 一覧と標準出力に含める
        public bool IsKey;             // キー項目（同じ値のケースがあれば更新）
        public int Sort;

        public string[] OptionList
        {
            get
            {
                return (Options ?? "").Replace("\r\n", "\n").Split('\n')
                       .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            }
        }
    }

    /// <summary>ケースの値（変数名と値の組）</summary>
    public class CaseValue
    {
        [XmlAttribute("name")]
        public string Name = "";
        [XmlText]
        public string Value = "";

        public CaseValue() { }
        public CaseValue(string name, string value) { Name = name; Value = value ?? ""; }
    }

    /// <summary>ケース（1件の問い合わせ・対応。項目はフィールド定義で決まる）</summary>
    public class Case
    {
        public int Id;
        public int CustomerId;
        public string Number = "";       // CS-00001
        public string Source = "manual"; // manual / cli / import / migrated
        public DateTime CreatedAt = DateTime.Now;
        public DateTime UpdatedAt = DateTime.Now;
        public List<CaseValue> Values = new List<CaseValue>();

        public string Get(string apiName)
        {
            var v = Values.FirstOrDefault(x => x.Name == apiName);
            return v == null ? "" : (v.Value ?? "");
        }

        public void Set(string apiName, string value)
        {
            if (value == null) value = "";
            var v = Values.FirstOrDefault(x => x.Name == apiName);
            if (v == null) Values.Add(new CaseValue(apiName, value));
            else v.Value = value;
        }
    }

    /// <summary>旧形式（対応履歴）の読み込み用。読み込み後にケースへ移行する。</summary>
    public class Interaction
    {
        public int Id;
        public int CustomerId;
        public DateTime Date = DateTime.Today;
        public string Inquiry = "";
        public string Response = "";
        public string NextAction = "";
        public DateTime? NextDate;
        public string Staff = "";
        public bool Done;
        public DateTime CreatedAt = DateTime.Now;
        public DateTime UpdatedAt = DateTime.Now;
    }

    [XmlRoot("CrmData")]
    public class CrmData
    {
        public int Version = 2;
        public int NextCustomerId = 1;
        public int NextCaseId = 1;
        public int NextFieldId = 1;
        public List<Customer> Customers = new List<Customer>();
        public List<FieldDef> Fields = new List<FieldDef>();
        public List<Case> Cases = new List<Case>();
        public List<Interaction> Interactions = new List<Interaction>();   // 旧形式の読み込み用

        // 初期のフィールド定義（表示名, 変数名, 型, 選択肢, 必須, 一覧/出力, キー）
        public static readonly object[][] DefaultFields =
        {
            new object[] { "通話ID", "call_id", FieldTypes.Text, "", false, false, true },
            new object[] { "件名", "subject", FieldTypes.Text, "", false, false, false },
            new object[] { "通話日時", "call_date", FieldTypes.DateTimeType, "", false, false, false },
            new object[] { "オペレータ", "operator_name", FieldTypes.Text, "", false, false, false },
            new object[] { "顧客名", "customer_name", FieldTypes.Text, "", false, false, false },
            new object[] { "用件区分", "category", FieldTypes.Select, "問い合わせ\n申込\n変更\n解約\nクレーム\nその他", false, false, false },
            new object[] { "通話詳細URL", "detail_url", FieldTypes.Url, "", false, false, false },
            new object[] { "問い合わせ内容", "summary", FieldTypes.TextArea, "", false, true, false },
            new object[] { "対応内容", "response", FieldTypes.TextArea, "", false, true, false },
            new object[] { "次回確認内容", "next_action", FieldTypes.TextArea, "", false, true, false },
        };

        public void SeedFields()
        {
            if (Fields.Count > 0) return;
            int i = 0;
            foreach (var f in DefaultFields)
            {
                Fields.Add(new FieldDef
                {
                    Id = NextFieldId++,
                    Label = (string)f[0],
                    ApiName = (string)f[1],
                    Type = (string)f[2],
                    Options = (string)f[3],
                    Required = (bool)f[4],
                    InList = (bool)f[5],
                    IsKey = (bool)f[6],
                    Sort = (i++) * 10
                });
            }
        }

        /// <summary>フィールドを末尾に1つ足す（移行時に使う）</summary>
        public FieldDef AddField(string label, string api, string type, string options)
        {
            var f = new FieldDef
            {
                Id = NextFieldId++, Label = label, ApiName = api, Type = type, Options = options,
                Sort = Fields.Count == 0 ? 0 : Fields.Max(x => x.Sort) + 10
            };
            Fields.Add(f);
            return f;
        }

        public List<FieldDef> SortedFields
        {
            get { return Fields.OrderBy(f => f.Sort).ThenBy(f => f.Id).ToList(); }
        }

        /// <summary>一覧・標準出力に含めるフィールド（1つも指定が無ければ先頭4つ）</summary>
        public List<FieldDef> ListFields
        {
            get
            {
                var f = SortedFields.Where(x => x.InList).ToList();
                return f.Count > 0 ? f : SortedFields.Take(4).ToList();
            }
        }

        public FieldDef KeyField { get { return SortedFields.FirstOrDefault(f => f.IsKey); } }

        public FieldDef FieldByApi(string api)
        {
            if (string.IsNullOrEmpty(api)) return null;
            return Fields.FirstOrDefault(f => string.Equals(f.ApiName, api, StringComparison.OrdinalIgnoreCase));
        }

        public FieldDef FieldByLabelOrApi(string name)
        {
            string n = TextUtil.Norm(name);
            if (n.Length == 0) return null;
            return Fields.FirstOrDefault(f => TextUtil.Norm(f.ApiName) == n) ??
                   Fields.FirstOrDefault(f => TextUtil.Norm(f.Label) == n);
        }

        /// <summary>一覧・出力の並び順に使う日付（最初の日付型フィールド。無ければ作成日時）</summary>
        public DateTime SortDate(Case c)
        {
            var f = SortedFields.FirstOrDefault(x => x.Type == FieldTypes.Date || x.Type == FieldTypes.DateTimeType);
            DateTime d;
            if (f != null && TextUtil.TryParseDate(c.Get(f.ApiName), out d)) return d;
            return c.CreatedAt;
        }

        public Customer GetCustomer(int id) { return Customers.FirstOrDefault(c => c.Id == id); }
        public Case GetCase(int id) { return Cases.FirstOrDefault(c => c.Id == id); }

        public Case GetCaseByNumber(string number)
        {
            if (string.IsNullOrWhiteSpace(number)) return null;
            return Cases.FirstOrDefault(c => string.Equals(c.Number, number.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public Customer AddCustomer(Customer c)
        {
            c.Id = NextCustomerId++;
            c.CreatedAt = c.UpdatedAt = DateTime.Now;
            Customers.Add(c);
            return c;
        }

        public Case AddCase(Case c)
        {
            c.Id = NextCaseId++;
            c.Number = "CS-" + c.Id.ToString("00000");
            c.CreatedAt = c.UpdatedAt = DateTime.Now;
            Cases.Add(c);
            return c;
        }

        public void DeleteCustomer(int id)
        {
            Customers.RemoveAll(c => c.Id == id);
            Cases.RemoveAll(c => c.CustomerId == id);
        }

        public void DeleteCase(int id) { Cases.RemoveAll(c => c.Id == id); }

        /// <summary>顧客のケース（並び順の日付の昇順、同じならID順）</summary>
        public List<Case> GetCases(int customerId)
        {
            return Cases.Where(c => c.CustomerId == customerId)
                        .OrderBy(c => SortDate(c)).ThenBy(c => c.Id).ToList();
        }

        /// <summary>キー項目の値が一致するケースを探す（通話中に複数回送られたときの集約用）</summary>
        public Case FindCaseByKey(string value)
        {
            var kf = KeyField;
            if (kf == null || string.IsNullOrWhiteSpace(value)) return null;
            string v = value.Trim();
            return Cases.Where(c => string.Equals(c.Get(kf.ApiName), v, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(c => c.Id).FirstOrDefault();
        }

        /// <summary>顧客名で検索（空白・全角半角・大文字小文字を無視）</summary>
        public List<Customer> FindByName(string name, bool partial, string company)
        {
            string n = TextUtil.Norm(name);
            string co = TextUtil.Norm(company);
            return Customers.Where(c =>
            {
                string cn = TextUtil.Norm(c.Name);
                bool hit = partial ? (n.Length > 0 && cn.Contains(n)) : cn == n;
                if (hit && co.Length > 0) hit = TextUtil.Norm(c.Company).Contains(co);
                return hit;
            }).OrderBy(c => c.Id).ToList();
        }

        /// <summary>
        /// 連携用の検索。同じ項目内の複数値は OR、異なる項目どうしは AND。
        /// Anys は顧客名・会社名・電話番号のいずれかに一致すれば該当。
        /// </summary>
        public List<Customer> Search(SearchQuery q)
        {
            return Customers.Where(c =>
                (q.Ids.Count == 0 || q.Ids.Contains(c.Id)) &&
                (q.Names.Count == 0 || q.Names.Any(v => Matcher.Name(c, v, q.Partial))) &&
                (q.Companies.Count == 0 || q.Companies.Any(v => Matcher.Company(c, v))) &&
                (q.Phones.Count == 0 || q.Phones.Any(v => Matcher.Phone(c, v, false))) &&
                (q.Anys.Count == 0 || q.Anys.Any(v => Matcher.Name(c, v, q.Partial) || Matcher.Company(c, v) || Matcher.Phone(c, v, true)))
            ).OrderBy(c => c.Id).ToList();
        }

        public Customer FindExact(string name, string company)
        {
            string n = TextUtil.Norm(name), co = TextUtil.Norm(company);
            return Customers.FirstOrDefault(c => TextUtil.Norm(c.Name) == n && TextUtil.Norm(c.Company) == co);
        }

        public CrmData Clone()
        {
            var ser = new XmlSerializer(typeof(CrmData));
            using (var ms = new MemoryStream())
            {
                ser.Serialize(ms, this);
                ms.Position = 0;
                return (CrmData)ser.Deserialize(ms);
            }
        }

        /// <summary>旧形式（対応履歴）をケースへ移す。移行したら true。</summary>
        public bool MigrateOldInteractions()
        {
            SeedFields();
            if (Interactions.Count == 0) return false;
            // 旧版にしかない項目（次回確認日・状態）は、値があるときだけフィールドを足して引き継ぐ
            if (Interactions.Any(i => i.NextDate.HasValue) && FieldByApi("next_date") == null)
                AddField("次回確認日", "next_date", FieldTypes.Date, "");
            if (Interactions.Any(i => i.Done) && FieldByApi("status") == null)
                AddField("状態", "status", FieldTypes.Select, "未完了\n完了");

            foreach (var it in Interactions.OrderBy(i => i.Id))
            {
                var c = new Case { CustomerId = it.CustomerId, Source = "migrated" };
                AddCase(c);
                c.CreatedAt = it.CreatedAt;
                c.UpdatedAt = it.UpdatedAt;
                var owner = GetCustomer(it.CustomerId);
                c.Set("call_date", it.Date.ToString("yyyy/MM/dd HH:mm"));
                c.Set("customer_name", owner == null ? "" : owner.Name);
                c.Set("operator_name", it.Staff);
                c.Set("summary", it.Inquiry);
                c.Set("response", it.Response);
                c.Set("next_action", it.NextAction);
                if (it.NextDate.HasValue && FieldByApi("next_date") != null)
                    c.Set("next_date", it.NextDate.Value.ToString("yyyy/MM/dd"));
                if (FieldByApi("status") != null) c.Set("status", it.Done ? "完了" : "未完了");
            }
            Interactions.Clear();
            Version = 2;
            return true;
        }
    }

    /// <summary>データファイルの読み書き</summary>
    public class CrmStore
    {
        public const string FileName = "crm_data.xml";
        public string FilePath { get; private set; }
        public CrmData Data { get; private set; }

        DateTime stamp;
        DateTime Stamp()
        {
            try { return File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        /// <summary>別のプロセス（コマンドラインでの登録など）がデータファイルを更新したか</summary>
        public bool ChangedOnDisk { get { return Stamp() != stamp; } }

        public CrmStore(string path)
        {
            FilePath = Path.GetFullPath(path);
            Load();
        }

        /// <summary>
        /// データファイルの場所: 環境変数 CRM_DATA → exeと同じフォルダ →
        /// (exeフォルダに書き込めない場合) %LOCALAPPDATA%\CSDemoCRM2
        /// </summary>
        public static string ResolveDefaultPath()
        {
            string env = Environment.GetEnvironmentVariable("CRM_DATA");
            if (!string.IsNullOrEmpty(env)) return Path.GetFullPath(env);

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string local = Path.Combine(exeDir, FileName);
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CSDemoCRM2", FileName);
            if (File.Exists(local)) return local;
            if (File.Exists(appData)) return appData;
            return IsWritable(exeDir) ? local : appData;
        }

        static bool IsWritable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".crm_write_test_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public void Load()
        {
            stamp = Stamp();
            if (!File.Exists(FilePath))
            {
                Data = new CrmData();
                Data.SeedFields();
                return;
            }
            var ser = new XmlSerializer(typeof(CrmData));
            using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                Data = (CrmData)ser.Deserialize(fs);
            }
            bool migrated = Data.MigrateOldInteractions();   // 旧形式（対応履歴）はケースへ移す
            if (Data.Customers.Count > 0 && Data.NextCustomerId <= Data.Customers.Max(c => c.Id))
                Data.NextCustomerId = Data.Customers.Max(c => c.Id) + 1;
            if (Data.Cases.Count > 0 && Data.NextCaseId <= Data.Cases.Max(c => c.Id))
                Data.NextCaseId = Data.Cases.Max(c => c.Id) + 1;
            if (Data.Fields.Count > 0 && Data.NextFieldId <= Data.Fields.Max(f => f.Id))
                Data.NextFieldId = Data.Fields.Max(f => f.Id) + 1;
            if (migrated) { try { Save(); } catch { } }
        }

        /// <summary>一時ファイルに書いてから置き換える（書き込み途中で壊れないように）。直前の版は .bak に残す。</summary>
        public void Save() { SaveCore(); stamp = Stamp(); }

        void SaveCore()
        {
            string dir = Path.GetDirectoryName(FilePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string tmp = FilePath + ".tmp";
            var ser = new XmlSerializer(typeof(CrmData));
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                ser.Serialize(w, Data);
            }
            if (File.Exists(FilePath))
            {
                File.Copy(FilePath, FilePath + ".bak", true);
                File.Copy(tmp, FilePath, true);
                File.Delete(tmp);
            }
            else
            {
                File.Move(tmp, FilePath);
            }
        }

        public void ReplaceData(CrmData data)
        {
            Data = data;
            Save();
        }

        public string CreateBackup()
        {
            string dir = Path.Combine(Path.GetDirectoryName(FilePath), "backup");
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, "crm_data_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xml");
            Save();
            File.Copy(FilePath, dest, true);
            return dest;
        }
    }

    /// <summary>文字列ユーティリティ</summary>
    public static class TextUtil
    {
        /// <summary>比較用の正規化: 全角半角統一(NFKC)、空白除去、小文字化</summary>
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Normalize(NormalizationForm.FormKC);
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s) if (!char.IsWhiteSpace(ch)) sb.Append(ch);
            return sb.ToString().ToLowerInvariant();
        }

        public static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r\n", "\n").Replace("\r", "\n").Trim().Replace("\n", " / ");
        }

        public static string Date(DateTime d) { return d.ToString("yyyy/MM/dd"); }
        public static string Date(DateTime? d) { return d.HasValue ? Date(d.Value) : ""; }

        static readonly string[] DateFormats = {
            "yyyy/M/d", "yyyy-M-d", "yyyy.M.d", "yyyyMMdd", "yyyy/M/d H:mm", "yyyy/M/d H:mm:ss",
            "yyyy-M-d H:mm", "yyyy-M-d H:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy年M月d日"
        };

        public static bool TryParseDate(string s, out DateTime d)
        {
            d = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().Normalize(NormalizationForm.FormKC);
            var ja = new CultureInfo("ja-JP");
            if (DateTime.TryParseExact(s, DateFormats, ja, DateTimeStyles.AllowWhiteSpaces, out d)) { d = d.Date; return true; }
            if (DateTime.TryParse(s, ja, DateTimeStyles.AllowWhiteSpaces, out d)) { d = d.Date; return true; }
            double oa; // Excelのシリアル値
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out oa) && oa > 20000 && oa < 80000)
            { d = DateTime.FromOADate(oa).Date; return true; }
            return false;
        }
    }
}
