using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace CrmDemo
{
    /// <summary>顧客（一意の顧客単位の情報）</summary>
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

    /// <summary>対応履歴（顧客ごとに複数）</summary>
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

        /// <summary>フォローアップが必要か（次回確認内容 or 次回確認日があり、未完了）</summary>
        public bool NeedsFollowUp
        {
            get { return !Done && (NextDate.HasValue || !string.IsNullOrWhiteSpace(NextAction)); }
        }
    }

    [XmlRoot("CrmData")]
    public class CrmData
    {
        public int Version = 1;
        public int NextCustomerId = 1;
        public int NextInteractionId = 1;
        public List<Customer> Customers = new List<Customer>();
        public List<Interaction> Interactions = new List<Interaction>();

        public Customer GetCustomer(int id)
        {
            return Customers.FirstOrDefault(c => c.Id == id);
        }

        public Interaction GetInteraction(int id)
        {
            return Interactions.FirstOrDefault(i => i.Id == id);
        }

        public Customer AddCustomer(Customer c)
        {
            c.Id = NextCustomerId++;
            c.CreatedAt = c.UpdatedAt = DateTime.Now;
            Customers.Add(c);
            return c;
        }

        public Interaction AddInteraction(Interaction it)
        {
            it.Id = NextInteractionId++;
            it.CreatedAt = it.UpdatedAt = DateTime.Now;
            Interactions.Add(it);
            return it;
        }

        public void DeleteCustomer(int id)
        {
            Customers.RemoveAll(c => c.Id == id);
            Interactions.RemoveAll(i => i.CustomerId == id);
        }

        public void DeleteInteraction(int id)
        {
            Interactions.RemoveAll(i => i.Id == id);
        }

        /// <summary>対応日の昇順（同日はID順）で顧客の履歴を返す</summary>
        public List<Interaction> GetHistory(int customerId)
        {
            return Interactions.Where(i => i.CustomerId == customerId)
                               .OrderBy(i => i.Date).ThenBy(i => i.Id).ToList();
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
        /// (exeフォルダに書き込めない場合) %LOCALAPPDATA%\CrmDemo
        /// </summary>
        public static string ResolveDefaultPath()
        {
            string env = Environment.GetEnvironmentVariable("CRM_DATA");
            if (!string.IsNullOrEmpty(env)) return Path.GetFullPath(env);

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string local = Path.Combine(exeDir, FileName);
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrmDemo", FileName);
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
            if (!File.Exists(FilePath)) { Data = new CrmData(); return; }
            var ser = new XmlSerializer(typeof(CrmData));
            using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                Data = (CrmData)ser.Deserialize(fs);
            }
            if (Data.Customers.Count > 0 && Data.NextCustomerId <= Data.Customers.Max(c => c.Id))
                Data.NextCustomerId = Data.Customers.Max(c => c.Id) + 1;
            if (Data.Interactions.Count > 0 && Data.NextInteractionId <= Data.Interactions.Max(i => i.Id))
                Data.NextInteractionId = Data.Interactions.Max(i => i.Id) + 1;
        }

        /// <summary>一時ファイルに書いてから置き換える（書き込み途中で壊れないように）。直前の版は .bak に残す。</summary>
        public void Save()
        {
            SaveCore();
            stamp = Stamp();
        }

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
