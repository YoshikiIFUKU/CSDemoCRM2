using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CrmDemo
{
    /// <summary>CSV / タブ区切りテキストの読み書き（RFC 4180 準拠、改行を含む項目にも対応）</summary>
    public static class Csv
    {
        /// <summary>1行目のタブとカンマの数で区切り文字を推定（Excelからのコピーはタブ区切り）</summary>
        public static char DetectDelimiter(string text)
        {
            int tabs = 0, commas = 0;
            bool inQ = false;
            foreach (char ch in text)
            {
                if (ch == '"') inQ = !inQ;
                else if (!inQ)
                {
                    if (ch == '\n') break;
                    if (ch == '\t') tabs++;
                    else if (ch == ',') commas++;
                }
            }
            return (tabs > 0 && tabs >= commas) ? '\t' : ',';
        }

        public static List<List<string>> Parse(string text, char delim)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var sb = new StringBuilder();
            bool inQ = false, fieldStart = true, any = false;
            int i = (text.Length > 0 && text[0] == '\uFEFF') ? 1 : 0;
            for (; i < text.Length; i++)
            {
                char ch = text[i];
                if (inQ)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQ = false;
                    }
                    else if (ch == '\r')
                    {
                        if (!(i + 1 < text.Length && text[i + 1] == '\n')) sb.Append('\n');
                    }
                    else sb.Append(ch);
                    continue;
                }
                if (ch == '"' && fieldStart) { inQ = true; fieldStart = false; any = true; }
                else if (ch == delim) { row.Add(sb.ToString()); sb.Clear(); fieldStart = true; any = true; }
                else if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(sb.ToString()); sb.Clear();
                    rows.Add(row); row = new List<string>();
                    fieldStart = true; any = false;
                }
                else { sb.Append(ch); fieldStart = false; any = true; }
            }
            if (any || sb.Length > 0) { row.Add(sb.ToString()); rows.Add(row); }
            return rows;
        }

        static readonly char[] Special = { ',', '"', '\n', '\r', '\t' };

        public static string Field(string s)
        {
            s = (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
            if (s.IndexOfAny(Special) >= 0 || s.StartsWith(" ") || s.EndsWith(" "))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        public static string Line(params string[] fields)
        {
            return string.Join(",", fields.Select(Field)) + "\r\n";
        }

        /// <summary>文字コードを自動判別して読む（BOM付きUTF-8/UTF-16、BOMなしUTF-8、Shift_JIS）</summary>
        public static string ReadTextFile(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
                return new UTF8Encoding(false).GetString(b, 3, b.Length - 3);
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            try { return new UTF8Encoding(false, true).GetString(b); }
            catch (DecoderFallbackException) { return Encoding.GetEncoding(932).GetString(b); }
        }

        public static void WriteExcelCsv(string path, string content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(true)); // ExcelでそのままひらけるBOM付きUTF-8
        }
    }

    public class ImportRow
    {
        public int LineNo;
        public string Name = "", Company = "", Phone = "", Email = "", Address = "", Memo = "";
        public string DateText = "", Inquiry = "", Response = "", NextAction = "", NextDateText = "", Staff = "", StatusText = "";

        public bool HasInteraction
        {
            get
            {
                return DateText.Trim().Length > 0 || Inquiry.Trim().Length > 0 ||
                       Response.Trim().Length > 0 || NextAction.Trim().Length > 0;
            }
        }
    }

    public class ParsedImport
    {
        public List<ImportRow> Rows = new List<ImportRow>();
        public bool HasHeader;
        public char Delimiter = ',';
        public List<string> Columns = new List<string>();
    }

    public class ImportResult
    {
        public int NewCustomers, UpdatedCustomers, NewInteractions, Duplicates, Errors;
        public List<string> RowStatus = new List<string>();

        public string Summary
        {
            get
            {
                return string.Format("新規顧客 {0} 件 / 顧客情報の補完 {1} 件 / 対応履歴の追加 {2} 件 / 重複スキップ {3} 件 / エラー {4} 件",
                    NewCustomers, UpdatedCustomers, NewInteractions, Duplicates, Errors);
            }
        }

        public bool HasChanges { get { return NewCustomers + UpdatedCustomers + NewInteractions > 0; } }
    }

    /// <summary>CSV / タブ区切りテキストから顧客と対応履歴を取り込む</summary>
    public static class Importer
    {
        public static readonly string[] DefaultOrder =
            { "name", "company", "phone", "email", "date", "inquiry", "response", "next", "nextdate", "staff", "status" };
        static readonly string[] DateFirstOrder =
            { "date", "inquiry", "response", "next", "nextdate", "staff", "status" };

        static readonly Dictionary<string, string> Alias = BuildAlias(
            "name", "顧客名,お客様名,お客さま名,顧客,氏名,名前,お名前,customer,customername,name",
            "company", "会社名,会社,企業名,法人名,company,companyname,organization",
            "phone", "電話番号,電話,tel,phone,携帯,携帯番号,連絡先",
            "email", "メール,メールアドレス,email,e-mail,mail",
            "address", "住所,所在地,address",
            "memo", "備考,メモ,顧客メモ,memo,note,notes",
            "date", "対応日,日付,対応日時,受付日,date",
            "inquiry", "問い合わせ内容,問合せ内容,問い合せ内容,問い合わせ,問合せ,お問い合わせ内容,お問い合わせ,inquiry,question",
            "response", "対応内容,対応,回答内容,回答,response,answer",
            "next", "次回確認内容,次回確認,次回対応,次回確認事項,次回対応内容,nextaction,next",
            "nextdate", "次回確認日,次回予定日,次回対応日,期限,nextdate,duedate",
            "staff", "担当者,担当,staff,owner",
            "status", "状態,ステータス,完了,status",
            "ignore", "顧客id,id,対応件数,最終対応日,要フォロー");

        public static readonly Dictionary<string, string> KeyLabel = new Dictionary<string, string> {
            {"name","顧客名"},{"company","会社名"},{"phone","電話番号"},{"email","メール"},{"address","住所"},{"memo","備考"},
            {"date","対応日"},{"inquiry","問い合わせ内容"},{"response","対応内容"},{"next","次回確認内容"},
            {"nextdate","次回確認日"},{"staff","担当者"},{"status","状態"},{"ignore","(無視)"}
        };

        static Dictionary<string, string> BuildAlias(params string[] kv)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i < kv.Length; i += 2)
                foreach (string a in kv[i + 1].Split(',')) d[TextUtil.Norm(a)] = kv[i];
            return d;
        }

        public static ParsedImport Parse(string text)
        {
            var p = new ParsedImport();
            if (string.IsNullOrWhiteSpace(text)) return p;
            p.Delimiter = Csv.DetectDelimiter(text);
            var table = Csv.Parse(text, p.Delimiter);
            if (table.Count == 0) return p;

            string[] map = null;
            int start = 0;
            var header = table[0].Select(h => { string k; return Alias.TryGetValue(TextUtil.Norm(h), out k) ? k : null; }).ToArray();
            int mapped = header.Count(k => k != null);
            if (mapped >= 2 || (mapped == 1 && header.Contains("name")))
            {
                map = header; start = 1; p.HasHeader = true;
            }
            else
            {
                // 見出しなし: 先頭列が日付なら「対応日,問い合わせ内容,…」（このツールの標準出力と同じ形）とみなす
                var firstData = table.FirstOrDefault(r => r.Any(c => c.Trim().Length > 0));
                DateTime dummy;
                map = (firstData != null && TextUtil.TryParseDate(firstData[0], out dummy)) ? DateFirstOrder : DefaultOrder;
            }
            p.Columns = map.Select(k => k == null ? "(無視)" : KeyLabel[k]).ToList();

            for (int r = start; r < table.Count; r++)
            {
                var cells = table[r];
                if (!cells.Any(c => c.Trim().Length > 0)) continue;
                var row = new ImportRow { LineNo = r + 1 };
                for (int c = 0; c < cells.Count && c < map.Length; c++)
                    Set(row, map[c], cells[c]);
                p.Rows.Add(row);
            }
            return p;
        }

        static void Set(ImportRow r, string key, string v)
        {
            if (key == null) return;
            v = v ?? "";
            switch (key)
            {
                case "name": r.Name = v.Trim(); break;
                case "company": r.Company = v.Trim(); break;
                case "phone": r.Phone = v.Trim(); break;
                case "email": r.Email = v.Trim(); break;
                case "address": r.Address = v.Trim(); break;
                case "memo": r.Memo = v; break;
                case "date": r.DateText = v.Trim(); break;
                case "inquiry": r.Inquiry = v.Trim(); break;
                case "response": r.Response = v.Trim(); break;
                case "next": r.NextAction = v.Trim(); break;
                case "nextdate": r.NextDateText = v.Trim(); break;
                case "staff": r.Staff = v.Trim(); break;
                case "status": r.StatusText = v.Trim(); break;
            }
        }

        static bool IsDone(string s)
        {
            string n = TextUtil.Norm(s);
            return n == "完了" || n == "済" || n == "済み" || n == "対応済" || n == "対応済み" || n == "done" ||
                   n == "true" || n == "1" || n == "○" || n == "yes" || n == "closed";
        }

        /// <summary>空欄の項目だけ新しい値で埋める（既存の値は上書きしない）</summary>
        static bool Fill(ref string field, string value)
        {
            if (string.IsNullOrWhiteSpace(field) && !string.IsNullOrWhiteSpace(value)) { field = value.Trim(); return true; }
            return false;
        }

        /// <summary>
        /// 取り込みを data に適用する。プレビューでは data.Clone() に適用して結果だけ見る。
        /// 同じ「顧客名＋会社名」は同一顧客、同じ顧客・対応日・問い合わせ内容・対応内容の履歴は重複としてスキップ。
        /// </summary>
        public static ImportResult Apply(CrmData data, List<ImportRow> rows, int? defaultCustomerId)
        {
            var res = new ImportResult();
            var created = new HashSet<int>();
            var updated = new HashSet<int>();

            foreach (var row in rows)
            {
                // 1) 履歴部分の検証（エラー行で顧客だけ作られないよう先に行う）
                DateTime date = DateTime.Today;
                DateTime? next = null;
                if (row.HasInteraction)
                {
                    if (!TextUtil.TryParseDate(row.DateText, out date))
                    {
                        res.Errors++;
                        res.RowStatus.Add(row.DateText.Length == 0 ? "エラー: 対応日がありません" : "エラー: 対応日を解釈できません「" + row.DateText + "」");
                        continue;
                    }
                    if (row.NextDateText.Length > 0)
                    {
                        DateTime nd;
                        if (!TextUtil.TryParseDate(row.NextDateText, out nd))
                        {
                            res.Errors++;
                            res.RowStatus.Add("エラー: 次回確認日を解釈できません「" + row.NextDateText + "」");
                            continue;
                        }
                        next = nd;
                    }
                }

                // 2) 顧客の特定・作成
                Customer c = null;
                string custStatus;
                if (row.Name.Length == 0)
                {
                    if (defaultCustomerId.HasValue) c = data.GetCustomer(defaultCustomerId.Value);
                    if (c == null || !row.HasInteraction)
                    {
                        res.Errors++;
                        res.RowStatus.Add("エラー: 顧客名がありません");
                        continue;
                    }
                    custStatus = "選択中の顧客";
                }
                else
                {
                    c = data.FindExact(row.Name, row.Company);
                    if (c == null && row.Company.Length == 0)
                    {
                        var cands = data.FindByName(row.Name, false, null);
                        if (cands.Count == 1) c = cands[0];
                    }
                    if (c == null)
                    {
                        c = data.AddCustomer(new Customer
                        {
                            Name = row.Name, Company = row.Company, Phone = row.Phone,
                            Email = row.Email, Address = row.Address, Memo = row.Memo.Trim()
                        });
                        created.Add(c.Id);
                        res.NewCustomers++;
                        custStatus = "新規顧客";
                    }
                    else
                    {
                        bool upd = Fill(ref c.Phone, row.Phone) | Fill(ref c.Email, row.Email) |
                                   Fill(ref c.Address, row.Address) | Fill(ref c.Memo, row.Memo) | Fill(ref c.Company, row.Company);
                        if (upd) c.UpdatedAt = DateTime.Now;
                        if (upd && !created.Contains(c.Id) && updated.Add(c.Id)) res.UpdatedCustomers++;
                        custStatus = created.Contains(c.Id) ? "新規顧客" : (upd ? "既存顧客(情報補完)" : "既存顧客");
                    }
                }

                // 3) 対応履歴の追加
                if (!row.HasInteraction)
                {
                    res.RowStatus.Add(custStatus + "（履歴なし）");
                    continue;
                }
                string ni = TextUtil.Norm(row.Inquiry), nr = TextUtil.Norm(row.Response);
                int cid = c.Id;
                bool dup = data.Interactions.Any(i => i.CustomerId == cid && i.Date == date &&
                                                      TextUtil.Norm(i.Inquiry) == ni && TextUtil.Norm(i.Response) == nr);
                if (dup)
                {
                    res.Duplicates++;
                    res.RowStatus.Add(custStatus + " / 重複のためスキップ");
                    continue;
                }
                data.AddInteraction(new Interaction
                {
                    CustomerId = c.Id, Date = date, Inquiry = row.Inquiry, Response = row.Response,
                    NextAction = row.NextAction, NextDate = next, Staff = row.Staff, Done = IsDone(row.StatusText)
                });
                res.NewInteractions++;
                res.RowStatus.Add(custStatus + " / 履歴を追加");
            }
            return res;
        }
    }

    /// <summary>デモ用サンプルデータ（日付は今日を基準に生成するので、いつ使ってもフォローアップ一覧が埋まる）</summary>
    public static class Samples
    {
        public static string Csv()
        {
            var sb = new StringBuilder();
            sb.Append(CrmDemo.Csv.Line("顧客名", "会社名", "電話番号", "メール", "対応日", "問い合わせ内容", "対応内容", "次回確認内容", "次回確認日", "担当者", "状態"));
            Action<string, string, string, string, int, string, string, string, int?, string, bool> R =
                (name, co, tel, mail, ago, inq, resp, next, nextIn, staff, done) =>
                sb.Append(CrmDemo.Csv.Line(name, co, tel, mail, TextUtil.Date(DateTime.Today.AddDays(-ago)), inq, resp, next,
                    nextIn.HasValue ? TextUtil.Date(DateTime.Today.AddDays(nextIn.Value)) : "", staff, done ? "完了" : "未完了"));

            string y = "山田 太郎", yc = "株式会社サンプル商事", yt = "03-1234-5678", ym = "yamada@sample-shoji.example";
            R(y, yc, yt, ym, 60, "新システムの導入を検討中。製品資料を送ってほしい。", "製品資料と価格表をメールで送付。", "資料の確認状況をヒアリング", -46, "田村", true);
            R(y, yc, yt, ym, 45, "資料を確認した。10名規模での見積もりがほしい。", "10ユーザー・年間契約で見積書を作成し送付。", "見積もりの社内検討結果を確認", -25, "田村", true);
            R(y, yc, yt, ym, 20, "見積もり金額について、上長から値引きの可否を聞かれている。", "年間一括払いなら5%割引が可能と回答。導入支援プランも併せて提案。", "稟議の進捗を確認", -3, "田村", false);
            R(y, yc, yt, ym, 5, "稟議が通りそう。導入スケジュールを知りたい。", "最短2週間の導入スケジュール案を送付。\nキックオフ日程の候補を3つ提示。", "キックオフ日程の確定", 2, "田村", false);

            string s = "佐藤 花子";
            R(s, "有限会社テスト工業", "06-2345-6789", "sato@test-kogyo.example", 30, "ログインできない。", "パスワード再設定の手順を案内し、ログインできることを確認。", "", null, "鈴木", true);
            R(s, "有限会社テスト工業", "06-2345-6789", "sato@test-kogyo.example", 12, "CSV出力をExcelで開くと文字化けする。", "Excelで開く際の手順（UTF-8指定）を案内。BOM付き出力の設定も紹介。", "設定変更後に解消したか確認", -1, "鈴木", false);
            R(s, "みらいデザイン株式会社", "045-111-2222", "hanako.sato@mirai-design.example", 15, "デザイン部門5名での利用を検討。無料トライアルは可能か。", "30日間の無料トライアルアカウントを発行。", "トライアル中の利用状況を確認", 10, "田村", false);

            string k = "鈴木 一郎", kt = "090-1111-2222", km = "ichiro.suzuki@mail.example";
            R(k, "", kt, km, 40, "個人事業主向けのプランはあるか。", "個人向けライトプランを案内。", "申し込み意向の確認", -33, "佐々木", true);
            R(k, "", kt, km, 25, "ライトプランに申し込みたい。", "申込フォームを案内し、申し込み完了を確認。", "初期設定のフォロー電話", -18, "佐々木", true);
            R(k, "", kt, km, 18, "初期設定の方法がわからない。", "電話で画面共有しながら初期設定を完了。", "", null, "佐々木", true);

            string t = "高橋 美咲", tc = "株式会社ネクストソリューション", tt = "052-333-4444", tm = "m.takahashi@next-sol.example";
            R(t, tc, tt, tm, 50, "既存システムからのデータ移行について相談したい。", "移行ツールの仕様書を送付。項目対応表の作成を依頼。", "項目対応表の受領", -40, "鈴木", true);
            R(t, tc, tt, tm, 35, "項目対応表を送付した。", "移行テストを実施し、日付形式の差異を2件検出。先方へ修正を依頼。", "修正版データの受領", -12, "鈴木", true);
            R(t, tc, tt, tm, 10, "修正版データを送付した。", "本番移行を完了。件数が一致することを確認済み。", "移行後1週間の利用状況ヒアリング", 0, "鈴木", false);
            R(t, tc, tt, tm, 2, "一部のユーザーに権限が付与されていない。", "権限設定を修正し、該当ユーザーでの動作を確認。", "他ユーザーへの影響がないか確認", 5, "鈴木", false);

            R("田中 健", "田中建設株式会社", "011-555-6666", "ken.tanaka@tanaka-kensetsu.example", 90, "展示会で名刺交換。", "お礼メールと会社案内を送付。", "3か月後に導入意向を再確認", 1, "佐々木", false);

            string i = "伊藤 由美", ic = "株式会社グリーンフーズ", it = "092-777-8888", im = "yumi.ito@green-foods.example";
            R(i, ic, it, im, 22, "請求書の送付先を経理部に変更したい。", "送付先を経理部宛に変更し、請求書を再発行。", "", null, "田村", true);
            R(i, ic, it, im, 8, "契約更新にあたり、値上げの予定はあるか。", "来年度も現行価格で据え置きと回答。", "更新契約書の返送確認", 14, "田村", false);

            R("渡辺 誠", "ブルーオーシャン合同会社", "03-9876-5432", "makoto.w@blue-ocean.example", 3, "利用頻度が低いので解約を検討している。", "利用状況を確認し、下位プランへの変更を提案。", "プラン変更か解約かの回答を確認", 4, "鈴木", false);
            return sb.ToString();
        }
    }
}
