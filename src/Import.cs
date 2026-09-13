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
            int i = (text.Length > 0 && text[0] == '﻿') ? 1 : 0;
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
            File.WriteAllText(path, content, new UTF8Encoding(true)); // Excelでそのまま開けるBOM付きUTF-8
        }
    }

    /// <summary>取り込む1行（顧客の項目＋ケースのフィールド値）</summary>
    public class ImportRow
    {
        public int LineNo;
        public int? CustomerId;          // 顧客ID列（あれば顧客の特定に使う）
        public string CaseNumber = "";   // ケース番号列（あれば既存ケースを更新）
        public string Name = "", Company = "", Phone = "", Email = "", Address = "", Memo = "";
        public Dictionary<string, string> Values = new Dictionary<string, string>();   // 変数名 → 値

        public bool HasCase { get { return Values.Any(v => !string.IsNullOrWhiteSpace(v.Value)); } }
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
        public int NewCustomers, UpdatedCustomers, NewCases, UpdatedCases, Duplicates, Errors;
        public List<string> RowStatus = new List<string>();

        public string Summary
        {
            get
            {
                return string.Format("新規顧客 {0} 件 / 顧客情報の補完 {1} 件 / ケースの追加 {2} 件 / ケースの更新 {3} 件 / 重複スキップ {4} 件 / エラー {5} 件",
                    NewCustomers, UpdatedCustomers, NewCases, UpdatedCases, Duplicates, Errors);
            }
        }

        public bool HasChanges { get { return NewCustomers + UpdatedCustomers + NewCases + UpdatedCases > 0; } }
    }

    /// <summary>CSV / タブ区切りテキストから顧客とケースを取り込む</summary>
    public static class Importer
    {
        // 顧客の項目の別名（ケースの項目はフィールド定義の表示名・変数名で判定する）
        static readonly Dictionary<string, string> CustomerAlias = BuildAlias(
            "name", "顧客名,お客様名,お客さま名,顧客,氏名,名前,お名前,customer,customername,name",
            "company", "会社名,会社,企業名,法人名,company,companyname,organization",
            "phone", "電話番号,電話,tel,phone,携帯,携帯番号,連絡先",
            "email", "メール,メールアドレス,email,e-mail,mail",
            "address", "住所,所在地,address",
            "memo", "備考,メモ,顧客メモ,memo,note,notes",
            "id", "顧客id,customerid",
            "case", "ケース番号,ケースid,casenumber,caseno",
            "ignore", "id,ケース件数,対応件数,最終対応日,更新日時,登録元");

        static Dictionary<string, string> BuildAlias(params string[] kv)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i < kv.Length; i += 2)
                foreach (string a in kv[i + 1].Split(',')) d[TextUtil.Norm(a)] = kv[i];
            return d;
        }

        /// <summary>見出しの1セルを「顧客項目」か「ケースのフィールド」に対応づける。対応が無ければ null。</summary>
        static string MapColumn(string header, CrmData data)
        {
            string key;
            if (CustomerAlias.TryGetValue(TextUtil.Norm(header), out key)) return "c:" + key;
            var f = data.FieldByLabelOrApi(header);
            return f != null ? "f:" + f.ApiName : null;
        }

        /// <summary>見出しが無いときの既定の列順（顧客の4項目＋一覧に出しているフィールド）</summary>
        static List<string> DefaultOrder(CrmData data)
        {
            var cols = new List<string> { "c:name", "c:company", "c:phone", "c:email" };
            cols.AddRange(data.ListFields.Select(f => "f:" + f.ApiName));
            return cols;
        }

        public static ParsedImport Parse(string text, CrmData data)
        {
            var p = new ParsedImport();
            if (string.IsNullOrWhiteSpace(text)) return p;
            p.Delimiter = Csv.DetectDelimiter(text);
            var table = Csv.Parse(text, p.Delimiter);
            if (table.Count == 0) return p;

            List<string> map;
            int start = 0;
            var header = table[0].Select(h => MapColumn(h, data)).ToList();
            // 同じ対応先が2回出てきたら（顧客の「顧客名」とケースの「顧客名」など）、2つ目はフィールドとして扱う
            var seen = new HashSet<string>();
            for (int i = 0; i < header.Count; i++)
            {
                if (header[i] == null || seen.Add(header[i])) continue;
                var dupField = data.FieldByLabelOrApi(table[0][i]);
                header[i] = dupField != null && seen.Add("f:" + dupField.ApiName) ? "f:" + dupField.ApiName : null;
            }
            int mapped = header.Count(k => k != null);
            if (mapped >= 2 || (mapped == 1 && header.Contains("c:name")))
            {
                map = header; start = 1; p.HasHeader = true;
            }
            else
            {
                // 見出しなし。先頭列が日付なら「日付から始まるケースの列」（このツールの標準出力と同じ形）
                var firstData = table.FirstOrDefault(r => r.Any(c => c.Trim().Length > 0));
                DateTime dummy;
                if (firstData != null && TextUtil.TryParseDate(firstData[0], out dummy))
                    map = data.ListFields.Select(f => "f:" + f.ApiName).ToList();
                else
                    map = DefaultOrder(data);
            }
            p.Columns = map.Select(k => k == null ? "(無視)" : ColumnLabel(k, data)).ToList();

            for (int r = start; r < table.Count; r++)
            {
                var cells = table[r];
                if (!cells.Any(c => c.Trim().Length > 0)) continue;
                var row = new ImportRow { LineNo = r + 1 };
                for (int c = 0; c < cells.Count && c < map.Count; c++) Set(row, map[c], cells[c], data);
                p.Rows.Add(row);
            }
            return p;
        }

        static string ColumnLabel(string key, CrmData data)
        {
            if (key.StartsWith("c:"))
            {
                switch (key.Substring(2))
                {
                    case "name": return "顧客名";
                    case "company": return "会社名";
                    case "phone": return "電話番号";
                    case "email": return "メール";
                    case "address": return "住所";
                    case "memo": return "備考";
                    case "id": return "顧客ID";
                    case "case": return "ケース番号";
                    default: return "(無視)";
                }
            }
            var f = data.FieldByApi(key.Substring(2));
            return f != null ? f.Label : "(無視)";
        }

        static void Set(ImportRow r, string key, string v, CrmData data)
        {
            if (key == null) return;
            v = v ?? "";
            if (key.StartsWith("f:"))
            {
                var f = data.FieldByApi(key.Substring(2));
                if (f != null) r.Values[f.ApiName] = FieldTypes.Normalize(f.Type, v);
                return;
            }
            switch (key.Substring(2))
            {
                case "name": r.Name = v.Trim(); break;
                case "company": r.Company = v.Trim(); break;
                case "phone": r.Phone = v.Trim(); break;
                case "email": r.Email = v.Trim(); break;
                case "address": r.Address = v.Trim(); break;
                case "memo": r.Memo = v; break;
                case "case": r.CaseNumber = v.Trim(); break;
                case "id":
                    {
                        int id;
                        if (int.TryParse(v.Trim(), out id)) r.CustomerId = id;
                        break;
                    }
            }
        }

        /// <summary>空欄の項目だけ新しい値で埋める（既存の値は上書きしない）</summary>
        static bool Fill(ref string field, string value)
        {
            if (string.IsNullOrWhiteSpace(field) && !string.IsNullOrWhiteSpace(value)) { field = value.Trim(); return true; }
            return false;
        }

        /// <summary>
        /// 取り込みを data に適用する。プレビューでは data.Clone() に適用して結果だけ見る。
        ///
        /// 顧客: 顧客ID列があればそれで特定。無ければ「顧客名＋会社名」で特定し、無ければ新規作成。
        /// ケース: ケース番号列があればそのケースを更新。無ければキー項目（call_id など）が一致するケースを更新。
        ///         どちらも無ければ新規。指定した項目の値がすべて同じケースがあれば重複としてスキップ。
        /// </summary>
        public static ImportResult Apply(CrmData data, List<ImportRow> rows, int? defaultCustomerId)
        {
            var res = new ImportResult();
            var created = new HashSet<int>();
            var updated = new HashSet<int>();

            foreach (var row in rows)
            {
                // 1) 必須フィールドの確認（エラー行で顧客だけ作られないよう先に行う）
                if (row.HasCase)
                {
                    var missing = data.SortedFields
                        .Where(f => f.Required && string.IsNullOrWhiteSpace(row.Values.ContainsKey(f.ApiName) ? row.Values[f.ApiName] : ""))
                        .Select(f => f.Label).ToList();
                    if (missing.Count > 0)
                    {
                        res.Errors++;
                        res.RowStatus.Add("エラー: " + string.Join("・", missing.ToArray()) + " がありません");
                        continue;
                    }
                }

                // 2) 更新するケース（ケース番号の指定があれば最優先）
                Case target = null;
                if (row.CaseNumber.Length > 0)
                {
                    target = data.GetCaseByNumber(row.CaseNumber);
                    if (target == null && !row.HasCase)
                    {
                        res.Errors++;
                        res.RowStatus.Add("エラー: ケース番号が見つかりません（" + row.CaseNumber + "）");
                        continue;
                    }
                }

                // 3) 顧客の特定・作成
                Customer c = null;
                string custStatus = "既存顧客";
                if (target != null) c = data.GetCustomer(target.CustomerId);
                if (c == null && row.CustomerId.HasValue)
                {
                    c = data.GetCustomer(row.CustomerId.Value);
                    if (c == null)
                    {
                        res.Errors++;
                        res.RowStatus.Add("エラー: 顧客IDが見つかりません（" + row.CustomerId.Value + "）");
                        continue;
                    }
                }
                if (c != null)
                {
                    // 顧客ID・ケース番号で特定済み。空欄の項目だけ補完する
                    bool upd0 = Fill(ref c.Phone, row.Phone) | Fill(ref c.Email, row.Email) |
                                Fill(ref c.Address, row.Address) | Fill(ref c.Memo, row.Memo) | Fill(ref c.Company, row.Company);
                    if (upd0) c.UpdatedAt = DateTime.Now;
                    if (upd0 && !created.Contains(c.Id) && updated.Add(c.Id)) res.UpdatedCustomers++;
                    custStatus = upd0 ? "既存顧客(情報補完)" : "既存顧客";
                }
                else if (row.Name.Length == 0)
                {
                    c = defaultCustomerId.HasValue ? data.GetCustomer(defaultCustomerId.Value) : null;
                    if (c == null || !row.HasCase)
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
                        custStatus = upd ? "既存顧客(情報補完)" : "既存顧客";
                    }
                }

                // 4) ケースの追加・更新
                if (!row.HasCase)
                {
                    res.RowStatus.Add(custStatus + "（ケースなし）");
                    continue;
                }

                // キー項目（call_id など）が一致するケースがあれば、それを更新する
                if (target == null)
                {
                    var kf = data.KeyField;
                    if (kf != null && row.Values.ContainsKey(kf.ApiName) && row.Values[kf.ApiName].Length > 0)
                    {
                        var byKey = data.FindCaseByKey(row.Values[kf.ApiName]);
                        if (byKey != null && byKey.CustomerId == c.Id) target = byKey;
                    }
                }

                var given = row.Values.Where(v => !string.IsNullOrWhiteSpace(v.Value)).ToList();
                if (target != null)
                {
                    if (given.All(g => TextUtil.Norm(target.Get(g.Key)) == TextUtil.Norm(g.Value)))
                    {
                        res.Duplicates++;
                        res.RowStatus.Add(custStatus + " / 同じ内容のためスキップ（" + target.Number + "）");
                        continue;
                    }
                    foreach (var v in row.Values) target.Set(v.Key, v.Value);
                    target.UpdatedAt = DateTime.Now;
                    res.UpdatedCases++;
                    res.RowStatus.Add(custStatus + " / ケースを更新（" + target.Number + "）");
                    continue;
                }

                int cid = c.Id;
                bool dup = data.Cases.Any(x => x.CustomerId == cid &&
                                               given.All(g => TextUtil.Norm(x.Get(g.Key)) == TextUtil.Norm(g.Value)));
                if (dup)
                {
                    res.Duplicates++;
                    res.RowStatus.Add(custStatus + " / 重複のためスキップ");
                    continue;
                }
                var newCase = new Case { CustomerId = c.Id, Source = "import" };
                data.AddCase(newCase);
                foreach (var v in row.Values) newCase.Set(v.Key, v.Value);
                res.NewCases++;
                res.RowStatus.Add(custStatus + " / ケースを追加（" + newCase.Number + "）");
            }
            return res;
        }
    }

    /// <summary>デモ用サンプルデータ（日付は今日を基準に生成するので、いつ使っても自然な並びになる）</summary>
    public static class Samples
    {
        public static string Csv()
        {
            var sb = new StringBuilder();
            sb.Append(CrmDemo.Csv.Line("顧客名", "会社名", "電話番号", "メール",
                "call_id", "subject", "call_date", "operator_name", "category",
                "summary", "response", "next_action", "detail_url"));
            int seq = 1001;
            // (顧客名, 会社名, 電話番号, メール, 何日前, 時刻, 件名, オペレータ, 用件区分, 問い合わせ内容, 対応内容, 次回確認内容)
            Action<string, string, string, string, int, string, string, string, string, string, string, string> R =
                (name, co, tel, mail, ago, time, subject, op, category, summary, response, next) =>
                {
                    string id = "C-" + (seq++);
                    sb.Append(CrmDemo.Csv.Line(name, co, tel, mail, id, subject,
                        DateTime.Today.AddDays(-ago).ToString("yyyy/MM/dd") + " " + time, op, category,
                        summary, response, next, "https://example.com/calls/" + id));
                };

            string y = "山田 太郎", yc = "株式会社サンプル商事", yt = "03-1234-5678", ym = "yamada@sample-shoji.example";
            R(y, yc, yt, ym, 60, "10:15", "製品資料の請求", "田村", "問い合わせ",
              "新システムの導入を検討中。製品資料を送ってほしい。", "製品資料と価格表をメールで送付。", "資料の確認状況をヒアリング");
            R(y, yc, yt, ym, 45, "14:30", "見積もり依頼（10名）", "田村", "問い合わせ",
              "資料を確認した。10名規模での見積もりがほしい。", "10ユーザー・年間契約で見積書を作成し送付。", "見積もりの社内検討結果を確認");
            R(y, yc, yt, ym, 20, "11:05", "見積もりの値引き相談", "田村", "問い合わせ",
              "見積もり金額について、上長から値引きの可否を聞かれている。", "年間一括払いなら5%割引が可能と回答。導入支援プランも併せて提案。", "稟議の進捗を確認");
            R(y, yc, yt, ym, 5, "16:40", "導入スケジュールの相談", "田村", "申込",
              "稟議が通りそう。導入スケジュールを知りたい。", "最短2週間の導入スケジュール案を送付。\nキックオフ日程の候補を3つ提示。", "キックオフ日程の確定");

            string s = "佐藤 花子", sc = "有限会社テスト工業", st = "06-2345-6789", sm = "sato@test-kogyo.example";
            R(s, sc, st, sm, 30, "09:20", "ログインできない", "鈴木", "問い合わせ",
              "ログインできない。", "パスワード再設定の手順を案内し、ログインできることを確認。", "");
            R(s, sc, st, sm, 12, "13:45", "CSVの文字化け", "鈴木", "問い合わせ",
              "CSV出力をExcelで開くと文字化けする。", "Excelで開く際の手順（UTF-8指定）を案内。BOM付き出力の設定も紹介。", "設定変更後に解消したか確認");
            R(s, "みらいデザイン株式会社", "045-111-2222", "hanako.sato@mirai-design.example", 15, "15:10",
              "無料トライアルの相談", "田村", "申込",
              "デザイン部門5名での利用を検討。無料トライアルは可能か。", "30日間の無料トライアルアカウントを発行。", "トライアル中の利用状況を確認");

            string k = "鈴木 一郎", kt = "090-1111-2222", km = "ichiro.suzuki@mail.example";
            R(k, "", kt, km, 40, "10:50", "個人事業主向けプラン", "佐々木", "問い合わせ",
              "個人事業主向けのプランはあるか。", "個人向けライトプランを案内。", "申し込み意向の確認");
            R(k, "", kt, km, 25, "11:30", "ライトプランの申し込み", "佐々木", "申込",
              "ライトプランに申し込みたい。", "申込フォームを案内し、申し込み完了を確認。", "初期設定のフォロー電話");
            R(k, "", kt, km, 18, "17:05", "初期設定のサポート", "佐々木", "問い合わせ",
              "初期設定の方法がわからない。", "電話で画面共有しながら初期設定を完了。", "");

            string t2 = "高橋 美咲", tc = "株式会社ネクストソリューション", tt = "052-333-4444", tm = "m.takahashi@next-sol.example";
            R(t2, tc, tt, tm, 50, "13:00", "データ移行の相談", "鈴木", "問い合わせ",
              "既存システムからのデータ移行について相談したい。", "移行ツールの仕様書を送付。項目対応表の作成を依頼。", "項目対応表の受領");
            R(t2, tc, tt, tm, 35, "10:25", "移行テストの結果", "鈴木", "問い合わせ",
              "項目対応表を送付した。", "移行テストを実施し、日付形式の差異を2件検出。先方へ修正を依頼。", "修正版データの受領");
            R(t2, tc, tt, tm, 10, "14:15", "本番移行の完了", "鈴木", "変更",
              "修正版データを送付した。", "本番移行を完了。件数が一致することを確認済み。", "移行後1週間の利用状況ヒアリング");
            R(t2, tc, tt, tm, 2, "09:40", "権限が付与されていない", "鈴木", "クレーム",
              "一部のユーザーに権限が付与されていない。", "権限設定を修正し、該当ユーザーでの動作を確認。", "他ユーザーへの影響がないか確認");

            R("田中 健", "田中建設株式会社", "011-555-6666", "ken.tanaka@tanaka-kensetsu.example", 90, "16:00",
              "展示会でのご挨拶", "佐々木", "その他",
              "展示会で名刺交換。", "お礼メールと会社案内を送付。", "3か月後に導入意向を再確認");

            string i = "伊藤 由美", ic = "株式会社グリーンフーズ", it = "092-777-8888", im = "yumi.ito@green-foods.example";
            R(i, ic, it, im, 22, "11:50", "請求書の送付先変更", "田村", "変更",
              "請求書の送付先を経理部に変更したい。", "送付先を経理部宛に変更し、請求書を再発行。", "");
            R(i, ic, it, im, 8, "15:35", "契約更新時の価格", "田村", "問い合わせ",
              "契約更新にあたり、値上げの予定はあるか。", "来年度も現行価格で据え置きと回答。", "更新契約書の返送確認");

            R("渡辺 誠", "ブルーオーシャン合同会社", "03-9876-5432", "makoto.w@blue-ocean.example", 3, "10:05",
              "解約の相談", "鈴木", "解約",
              "利用頻度が低いので解約を検討している。", "利用状況を確認し、下位プランへの変更を提案。", "プラン変更か解約かの回答を確認");
            return sb.ToString();
        }
    }
}
