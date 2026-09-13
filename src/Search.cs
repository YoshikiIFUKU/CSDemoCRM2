using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace CrmDemo
{
    /// <summary>連携用の検索条件（同じ項目内の複数値は OR、項目どうしは AND）</summary>
    public class SearchQuery
    {
        public List<string> Names = new List<string>();
        public List<string> Companies = new List<string>();
        public List<string> Phones = new List<string>();
        public List<string> Anys = new List<string>();
        public List<int> Ids = new List<int>();
        public bool Partial;

        public bool IsEmpty { get { return Names.Count + Companies.Count + Phones.Count + Anys.Count + Ids.Count == 0; } }

        static readonly char[] Separators = { ',', '、', '，', ';', '；', '\r', '\n' };

        /// <summary>「山田太郎、佐藤花子」のようなカンマ・読点区切りを複数値に分ける</summary>
        public static List<string> Split(string s)
        {
            return (s ?? "").Split(Separators).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }

        /// <summary>空欄も位置を保ったまま分割する（--any を「顧客名,会社名,電話番号」の順で読むとき用）</summary>
        public static List<string> SplitKeepEmpty(string s)
        {
            return (s ?? "").Split(Separators).Select(x => x.Trim()).ToList();
        }
    }

    /// <summary>
    /// 音声認識テキストを想定した照合ルール。
    /// 空白・全角半角・敬称（様/さん）・法人格（株式会社など）・電話番号のハイフンの揺れを吸収する。
    /// </summary>
    public static class Matcher
    {
        static readonly string[] CorpWords =
            { "株式会社", "有限会社", "合同会社", "合資会社", "合名会社", "一般社団法人", "一般財団法人", "(株)", "(有)", "(同)" };
        static readonly string[] Honorifics = { "さま", "さん", "様", "殿", "御中" };

        static string StripHonorific(string n)
        {
            foreach (var h in Honorifics)
                if (n.Length > h.Length && n.EndsWith(h)) return n.Substring(0, n.Length - h.Length);
            return n;
        }

        public static string CompanyKey(string s)
        {
            string n = TextUtil.Norm(s);
            foreach (var w in CorpWords) n = n.Replace(w, "");
            return n;
        }

        /// <summary>顧客名: 既定は完全一致、partial で部分一致</summary>
        public static bool Name(Customer c, string q, bool partial)
        {
            string n = StripHonorific(TextUtil.Norm(q));
            if (n.Length == 0) return false;
            string cn = TextUtil.Norm(c.Name);
            return partial ? cn.Contains(n) : cn == n;
        }

        /// <summary>会社名: 法人格を除いて部分一致（「サンプル商事」で「株式会社サンプル商事」に一致）</summary>
        public static bool Company(Customer c, string q)
        {
            string k = CompanyKey(StripHonorific(TextUtil.Norm(q)));
            string ck = CompanyKey(c.Company);
            if (k.Length < 2 || ck.Length == 0) return false;
            return ck.Contains(k) || k.Contains(ck);
        }

        public static string Digits(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Normalize(NormalizationForm.FormKC);
            var sb = new StringBuilder();
            foreach (char ch in s) if (ch >= '0' && ch <= '9') sb.Append(ch);
            string d = sb.ToString();
            if (s.TrimStart().StartsWith("+81") && d.StartsWith("81")) d = "0" + d.Substring(2); // +81 3-1234-5678 → 0312345678
            return d;
        }

        /// <summary>数字・ハイフン・括弧・空白・+ だけで構成され、数字が4桁以上</summary>
        static bool LooksLikePhone(string s)
        {
            s = s.Normalize(NormalizationForm.FormKC);
            int digits = 0;
            foreach (char ch in s)
            {
                if (ch >= '0' && ch <= '9') digits++;
                else if ("-+().".IndexOf(ch) < 0 && !char.IsWhiteSpace(ch)) return false;
            }
            return digits >= 4;
        }

        /// <summary>
        /// 電話番号: 数字だけで比較。10桁以上なら全桁一致、4〜9桁なら末尾一致（「下4桁5678」など）。
        /// strict=true（--any）のときは値が電話番号らしい形のときだけ照合する。
        /// </summary>
        public static bool Phone(Customer c, string q, bool strict)
        {
            if (strict && !LooksLikePhone(q)) return false;
            string d = Digits(q), cd = Digits(c.Phone);
            if (d.Length < 4 || cd.Length == 0) return false;
            return d.Length >= 10 ? cd == d : cd.EndsWith(d);
        }
    }

    /// <summary>連携検索テスト: exe に渡す検索条件を画面で試し、標準出力と同じ内容を確認する</summary>
    public class SearchTestForm : Form
    {
        readonly CrmData data;
        readonly TextBox tName, tCompany, tPhone, tAny, tCmd, tOut;
        readonly CheckBox chkPartial, chkFull;
        readonly Label lblHit;
        string lastOutput = "";

        public SearchTestForm(CrmData data, Customer current)
        {
            this.data = data;
            Ui.SetupDialog(this, "連携検索テスト", 960, 700);
            var mono = new Font("MS Gothic", 10f);

            var intro = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = Ui.S(70), Tag = "sub",
                Text = "音声テキストから取り出した値を入れると、exe が標準出力する内容をその場で確認できます。" +
                       "カンマ（, 、）区切りで複数指定すると OR 検索、項目どうし（顧客名と会社名など）は AND で絞り込みます。\r\n" +
                       "「いずれか」は顧客名・会社名・電話番号のどれかに一致すれば該当とします（聞き取った項目が不確かなときに使います）。"
            };

            var t = Ui.FormTable();
            t.Dock = DockStyle.Top;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tName = Ui.AddText(t, "顧客名  --name", current != null ? current.Name : "");
            tCompany = Ui.AddText(t, "会社名  --company", "");
            tPhone = Ui.AddText(t, "電話番号  --phone", "");
            tAny = Ui.AddText(t, "いずれか  --any", "");
            var opts = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            chkPartial = new CheckBox { Text = "顧客名を部分一致（--partial）", AutoSize = true, Margin = new Padding(0, Ui.S(4), Ui.S(16), 0) };
            chkFull = new CheckBox { Text = "顧客情報の列も出力（--full）", AutoSize = true, Checked = true, Margin = new Padding(0, Ui.S(4), 0, 0) };
            opts.Controls.Add(chkPartial);
            opts.Controls.Add(chkFull);
            Ui.AddRow(t, "オプション", opts);
            tCmd = new TextBox { ReadOnly = true, Font = mono };
            Ui.AddRow(t, "コマンド", tCmd);

            lblHit = new Label { Dock = DockStyle.Top, Height = Ui.S(30), Font = Ui.BoldFont, Tag = "accent", TextAlign = ContentAlignment.MiddleLeft };
            tOut = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = mono
            };

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, Ui.S(8), 0, 0)
            };
            var close = Ui.Btn("閉じる", null); close.DialogResult = DialogResult.Cancel; CancelButton = close;
            bottom.Controls.Add(close);
            bottom.Controls.Add(Ui.Btn("出力をコピー", delegate { if (lastOutput.Length > 0) Clipboard.SetText(lastOutput); }, true));
            bottom.Controls.Add(Ui.Btn("コマンドをコピー", delegate { if (tCmd.Text.Length > 0) Clipboard.SetText(tCmd.Text); }));

            Controls.Add(tOut);
            Controls.Add(lblHit);
            Controls.Add(t);
            Controls.Add(intro);
            Controls.Add(bottom);

            foreach (var tb in new[] { tName, tCompany, tPhone, tAny }) tb.TextChanged += delegate { UpdateResult(); };
            chkPartial.CheckedChanged += delegate { UpdateResult(); };
            chkFull.CheckedChanged += delegate { UpdateResult(); };
            Load += delegate { UpdateResult(); };
        }

        static string Quote(string s)
        {
            return s.IndexOfAny(new[] { ' ', '　', '"' }) >= 0 ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;
        }

        void UpdateResult()
        {
            var q = new SearchQuery { Partial = chkPartial.Checked };
            q.Names.AddRange(SearchQuery.Split(tName.Text));
            q.Companies.AddRange(SearchQuery.Split(tCompany.Text));
            q.Phones.AddRange(SearchQuery.Split(tPhone.Text));
            q.Anys.AddRange(SearchQuery.Split(tAny.Text));

            var parts = new List<string> { Cli.ExeName };
            Action<string, List<string>> add = (opt, vals) => { if (vals.Count > 0) { parts.Add(opt); parts.Add(Quote(string.Join(",", vals))); } };
            add("--name", q.Names);
            add("--company", q.Companies);
            add("--phone", q.Phones);
            add("--any", q.Anys);
            if (q.Partial) parts.Add("--partial");
            if (chkFull.Checked) parts.Add("--full");
            tCmd.Text = string.Join(" ", parts);

            if (q.IsEmpty)
            {
                lblHit.Text = "検索条件を入力してください";
                tOut.Text = lastOutput = "";
                return;
            }
            var hits = data.Search(q);
            int count;
            string csv = Cli.BuildHistoryCsv(data, hits, chkFull.Checked, true, out count);
            lblHit.Text = hits.Count == 0 ? "該当する顧客はいません"
                : string.Format("該当顧客 {0} 件（{1}）／ ケース {2} 件", hits.Count, string.Join("、", hits.Select(c => c.DisplayName)), count);
            lastOutput = count == 0 ? Cli.NotFoundMessage + "\r\n" : csv;
            tOut.Text = lastOutput.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }
    }
}
