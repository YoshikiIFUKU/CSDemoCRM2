using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CrmDemo
{
    /// <summary>
    /// コマンドライン（引数あり起動）
    /// ・検索: 顧客名・会社名・電話番号で検索し、対応履歴をCSVで標準出力する
    /// ・登録（--add）: 特定した顧客に問い合わせ内容・対応内容・次回確認内容を登録する（通話後の自動転記）
    /// ・実行ごとに crm_log.txt へ1行ログを残す
    /// </summary>
    public static class Cli
    {
        public const string NotFoundMessage = "該当する情報がありません";
        public const int ExitFound = 0, ExitNotFound = 1, ExitAmbiguous = 2, ExitError = 9;

        public static readonly string[] HistoryHeader = { "対応日", "問い合わせ内容", "対応内容", "次回確認内容" };
        public static readonly string[] FullHeader =
            { "顧客ID", "顧客名", "会社名", "電話番号", "メール", "対応日", "問い合わせ内容", "対応内容", "次回確認内容", "次回確認日", "担当者", "状態" };

        class CliError : Exception { public CliError(string m) : base(m) { } }

        class AddArgs
        {
            public string Inquiry = "", Response = "", Next = "", Staff = "", Email = "", DateText = "", NextDateText = "";
            public bool Done, Create, Given;
        }

        /// <summary>1回の実行の情報（ログ用）</summary>
        class RunInfo
        {
            public string Mode = "search", Detail = "", LogPath, DataPath;
            public bool NoLog;
            public Encoding Enc;
        }

        /// <summary>指定顧客の対応履歴CSV（対応日の昇順）。GUIの「連携CSV出力プレビュー」「連携検索テスト」でも同じものを使う。</summary>
        public static string BuildHistoryCsv(CrmData data, IEnumerable<Customer> customers, bool full, bool header, out int count)
        {
            var custs = customers.ToDictionary(c => c.Id);
            var items = data.Interactions.Where(i => custs.ContainsKey(i.CustomerId))
                            .OrderBy(i => i.Date).ThenBy(i => i.CustomerId).ThenBy(i => i.Id).ToList();
            count = items.Count;
            var sb = new StringBuilder();
            if (header) sb.Append(Csv.Line(full ? FullHeader : HistoryHeader));
            foreach (var i in items)
            {
                var c = custs[i.CustomerId];
                if (full)
                    sb.Append(Csv.Line(c.Id.ToString(), c.Name, c.Company, c.Phone, c.Email, TextUtil.Date(i.Date),
                        i.Inquiry, i.Response, i.NextAction, TextUtil.Date(i.NextDate), i.Staff, i.Done ? "完了" : "未完了"));
                else
                    sb.Append(Csv.Line(TextUtil.Date(i.Date), i.Inquiry, i.Response, i.NextAction));
            }
            return sb.ToString();
        }

        public static string BuildCustomerCsv(CrmData data)
        {
            var sb = new StringBuilder();
            sb.Append(Csv.Line("顧客ID", "顧客名", "会社名", "電話番号", "メール", "住所", "備考", "対応件数", "最終対応日"));
            foreach (var c in data.Customers.OrderBy(x => x.Id))
            {
                var h = data.Interactions.Where(i => i.CustomerId == c.Id).ToList();
                sb.Append(Csv.Line(c.Id.ToString(), c.Name, c.Company, c.Phone, c.Email, c.Address, c.Memo,
                    h.Count.ToString(), h.Count > 0 ? TextUtil.Date(h.Max(i => i.Date)) : ""));
            }
            return sb.ToString();
        }

        public static string ExeName
        {
            get { return Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location); }
        }

        public static string HelpText()
        {
            string exe = ExeName;
            return
"CRM Demo - 顧客対応履歴管理\r\n" +
"\r\n" +
"■ 使い方\r\n" +
"  " + exe + "                         GUIを起動\r\n" +
"  " + exe + " <顧客名> [オプション]       対応履歴をCSVで標準出力\r\n" +
"  " + exe + " [検索条件...] [オプション]\r\n" +
"  " + exe + " --add [検索条件...] --inquiry ... --response ... --next ...   対応履歴を登録\r\n" +
"\r\n" +
"■ 検索条件（検索・登録で共通）\r\n" +
"  -n, --name <顧客名>       顧客名（完全一致。--partial で部分一致）\r\n" +
"  -c, --company <会社名>    会社名（部分一致。株式会社・(株) などの法人格は無視）\r\n" +
"  -t, --phone <電話番号>    電話番号（数字だけで比較。10桁以上は全桁一致、4〜9桁は末尾一致）\r\n" +
"  -a, --any <値>            顧客名・会社名・電話番号のいずれかに一致\r\n" +
"      --id <顧客ID>         顧客ID\r\n" +
"  ・値はカンマ（, 、）区切りで複数指定でき、同じ項目内は OR 検索になります。\r\n" +
"    同じオプションを繰り返しても OR です（--name 山田太郎 --name 佐藤花子）。\r\n" +
"  ・異なる項目どうしは AND で絞り込みます（--name 佐藤花子 --company テスト工業）。\r\n" +
"  ・どの項目か不確かな値は --any に渡すと、いずれかの項目に一致すれば該当とします。\r\n" +
"  ・空白の有無・全角半角・大文字小文字、末尾の「様」「さん」は区別しません。\r\n" +
"  ・オプションなしの引数は顧客名として扱います（\"山田 太郎\" と 山田 太郎 は同じ）。\r\n" +
"\r\n" +
"■ 検索の出力（既定）\r\n" +
"  対応日,問い合わせ内容,対応内容,次回確認内容\r\n" +
"  ・対応日の古い順。1行目は見出し。改行やカンマを含む項目は \"...\" で囲みます。\r\n" +
"  ・該当なしの場合は「" + NotFoundMessage + "」を出力します。\r\n" +
"  ・複数の顧客に該当しうる検索では --full を付けると、行ごとに顧客名・会社名が付きます。\r\n" +
"\r\n" +
"■ 対応履歴の登録（通話後の自動転記など）\r\n" +
"      --add                 登録モード\r\n" +
"      --inquiry <内容>      問い合わせ内容\r\n" +
"      --response <内容>     対応内容\r\n" +
"      --next <内容>         次回確認内容\r\n" +
"      --date <日付>         対応日（既定: 今日）\r\n" +
"      --next-date <日付>    次回確認日\r\n" +
"      --staff <担当者>      担当者\r\n" +
"      --done                完了（フォロー不要）として登録\r\n" +
"      --create              該当する顧客がいなければ新規顧客として登録（--name 必須）\r\n" +
"      --email <メール>      新規顧客のメールアドレス\r\n" +
"  ・検索条件で顧客がちょうど1件に決まったときだけ登録します。\r\n" +
"      該当なし → 登録せず終了コード 1（--create で新規顧客を作って登録）\r\n" +
"      複数該当 → 登録せず候補（顧客ID,顧客名,会社名,電話番号）を出力して終了コード 2\r\n" +
"  ・誤登録を防ぐため、--name と --phone（または --company）の組み合わせを推奨します。\r\n" +
"  ・既存顧客の電話番号・会社名が空欄なら、指定した値で補完します（入力済みの値は上書きしません）。\r\n" +
"  ・同じ顧客・対応日・問い合わせ内容・対応内容の履歴があれば二重登録しません。\r\n" +
"  ・値の中の \\n は改行として扱います。GUIを開いていれば自動で画面に反映されます。\r\n" +
"\r\n" +
"■ ログ\r\n" +
"  実行するたびに、データファイルと同じフォルダの " + RunLog.FileName + " に1行追記します。\r\n" +
"  （日時・処理・終了コード・処理時間・結果・引数。タブ区切り、UTF-8。5MBを超えると .1 に切り替え）\r\n" +
"      --log <ファイル>      ログの保存先（環境変数 CRM_LOG でも可）\r\n" +
"      --no-log              ログを出力しない（CRM_LOG=off でも可）\r\n" +
"\r\n" +
"■ その他のオプション\r\n" +
"  -p, --partial            顧客名を部分一致で検索する\r\n" +
"  -f, --full               顧客ID・顧客名・会社名・電話番号・メール・次回確認日・担当者・状態の列も出力\r\n" +
"      --no-header          見出し行を出力しない\r\n" +
"  -e, --encoding <名前>    標準出力・標準エラー出力の文字コード: utf8（既定）/ utf8bom / sjis\r\n" +
"                           ※ パイプ・リダイレクト時の既定はBOMなしUTF-8（標準エラー出力にBOMは付けません）。\r\n" +
"                             Windows PowerShell 5.1 で変数に受ける場合などは sjis を指定。\r\n" +
"      --all                全顧客の対応履歴を出力（--full 形式）\r\n" +
"      --list               顧客一覧をCSVで出力\r\n" +
"      --import <ファイル>  CSV / タブ区切りファイルを取り込む\r\n" +
"      --data <ファイル>    データファイルを指定（既定: exeと同じフォルダの " + CrmStore.FileName + "）\r\n" +
"  -h, --help               このヘルプを表示\r\n" +
"\r\n" +
"■ 終了コード\r\n" +
"  0 = 該当あり・登録完了 / 1 = 該当なし / 2 = 顧客が複数該当（登録時） / 9 = エラー（標準エラー出力にメッセージ）\r\n" +
"\r\n" +
"■ 例\r\n" +
"  " + exe + " \"山田 太郎\"\r\n" +
"  " + exe + " --phone 0312345678 --full\r\n" +
"  " + exe + " --name 佐藤花子 --company テスト工業\r\n" +
"  " + exe + " --any \"山田太郎,サンプル商事,03-1234-5678\" --full\r\n" +
"  " + exe + " --add --name 山田太郎 --phone 03-1234-5678 --inquiry \"導入時期の相談\" --response \"来月初旬で合意\" --next \"キックオフ資料の送付\" --next-date 2026/09/18\r\n" +
"  python:  subprocess.run([r\"" + exe + "\", \"--any\", \"山田太郎,0312345678\", \"--full\"], capture_output=True, encoding=\"utf-8\").stdout\r\n";
        }

        static string NextArg(string[] args, ref int i, string opt)
        {
            if (i + 1 >= args.Length) throw new CliError(opt + " の後に値を指定してください");
            return args[++i];
        }

        /// <summary>値の中の \n（文字列）を改行にする</summary>
        static string Unescape(string s)
        {
            return (s ?? "").Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\r\n", "\n").Trim();
        }

        static Encoding ParseEncoding(string name)
        {
            switch (name.ToLowerInvariant().Replace("-", "").Replace("_", ""))
            {
                case "utf8": return new UTF8Encoding(false);
                case "utf8bom": return new UTF8Encoding(true);
                case "sjis": case "shiftjis": case "cp932": case "932": case "ms932": return Encoding.GetEncoding(932);
                default: throw new CliError("不明な文字コード: " + name + "（utf8 / utf8bom / sjis）");
            }
        }

        /// <summary>
        /// 標準出力へ書く。リダイレクト・パイプ時はバイト列で直接書き（既定UTF-8）、
        /// コンソール表示時は画面で読める形にする。
        /// </summary>
        static void Write(string text, Encoding enc)
        {
            if (enc == null && !Console.IsOutputRedirected)
            {
                Encoding old = null;
                try { old = Console.OutputEncoding; Console.OutputEncoding = new UTF8Encoding(false); }
                catch { old = null; }
                try { Console.Out.Write(text); Console.Out.Flush(); }
                finally { if (old != null) try { Console.OutputEncoding = old; } catch { } }
                return;
            }
            if (enc == null) enc = new UTF8Encoding(false);
            var s = Console.OpenStandardOutput();
            byte[] pre = enc.GetPreamble();
            if (pre.Length > 0) s.Write(pre, 0, pre.Length);
            byte[] b = enc.GetBytes(text);
            s.Write(b, 0, b.Length);
            s.Flush();
        }

        /// <summary>
        /// 標準エラー出力へ書く。標準出力と同じ文字コード（既定UTF-8、--encoding sjis で Shift_JIS）にそろえる。
        /// コンソール表示時は画面の文字コードのまま出す。BOM は付けない。
        /// </summary>
        static void WriteErr(string text, Encoding enc)
        {
            if (enc == null && !Console.IsErrorRedirected)
            {
                Console.Error.Write(text);
                Console.Error.Flush();
                return;
            }
            if (enc == null || enc is UTF8Encoding) enc = new UTF8Encoding(false);
            var s = Console.OpenStandardError();
            byte[] b = enc.GetBytes(text);
            s.Write(b, 0, b.Length);
            s.Flush();
        }

        public static int Run(string[] args)
        {
            var sw = Stopwatch.StartNew();
            var info = new RunInfo();
            int code;
            try
            {
                code = RunCore(args, info);
            }
            catch (Exception ex)
            {
                info.Detail = "エラー: " + ex.Message;
                try { WriteErr("エラー: " + ex.Message + "\r\n", info.Enc); } catch { }
                code = ExitError;
            }
            if (info.Mode != "help" && !info.NoLog)
            {
                string path = RunLog.ResolvePath(info.LogPath, info.DataPath);
                if (path != null)
                    RunLog.Append(path, RunLog.Format(DateTime.Now, info.Mode, code, sw.ElapsedMilliseconds, info.Detail, args));
            }
            return code;
        }

        static int RunCore(string[] args, RunInfo info)
        {
            // 文字コードとログの設定は、引数の解析エラーのメッセージ・ログにも使うので先に読む
            for (int i = 0; i < args.Length; i++)
            {
                string k = args[i].ToLowerInvariant();
                if (k == "--no-log") info.NoLog = true;
                if (i + 1 >= args.Length) continue;
                if (k == "--log") info.LogPath = args[i + 1];
                else if (k == "--data") info.DataPath = args[i + 1];
                else if (k == "-e" || k == "--encoding") info.Enc = ParseEncoding(args[i + 1]);
            }

            bool full = false, header = true, list = false, all = false, rest = false, add = false;
            string importFile = null;
            Encoding enc = info.Enc;
            var names = new List<string>();
            var query = new SearchQuery();
            var ad = new AddArgs();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (rest) { names.Add(a); continue; }
                switch (a.ToLowerInvariant())
                {
                    case "-h": case "--help": case "/?": case "-?": case "/h":
                        info.Mode = "help";
                        Write(HelpText(), enc); return ExitFound;
                    case "-p": case "--partial": query.Partial = true; break;
                    case "-f": case "--full": full = true; break;
                    case "--no-header": header = false; break;
                    case "--list": list = true; break;
                    case "--all": all = true; break;
                    case "-n": case "--name": query.Names.AddRange(SearchQuery.Split(NextArg(args, ref i, a))); break;
                    case "-c": case "--company": query.Companies.AddRange(SearchQuery.Split(NextArg(args, ref i, a))); break;
                    case "-t": case "--phone": case "--tel": query.Phones.AddRange(SearchQuery.Split(NextArg(args, ref i, a))); break;
                    case "-a": case "--any": query.Anys.AddRange(SearchQuery.Split(NextArg(args, ref i, a))); break;
                    case "--id":
                        foreach (var v in SearchQuery.Split(NextArg(args, ref i, a)))
                        {
                            int id;
                            if (!int.TryParse(v, out id)) throw new CliError("顧客IDは数字で指定してください: " + v);
                            query.Ids.Add(id);
                        }
                        break;
                    case "--add": add = true; break;
                    case "--inquiry": ad.Inquiry = Unescape(NextArg(args, ref i, a)); ad.Given = true; break;
                    case "--response": ad.Response = Unescape(NextArg(args, ref i, a)); ad.Given = true; break;
                    case "--next": ad.Next = Unescape(NextArg(args, ref i, a)); ad.Given = true; break;
                    case "--date": ad.DateText = NextArg(args, ref i, a); ad.Given = true; break;
                    case "--next-date": ad.NextDateText = NextArg(args, ref i, a); ad.Given = true; break;
                    case "--staff": ad.Staff = NextArg(args, ref i, a).Trim(); ad.Given = true; break;
                    case "--email": ad.Email = NextArg(args, ref i, a).Trim(); ad.Given = true; break;
                    case "--done": ad.Done = true; ad.Given = true; break;
                    case "--create": ad.Create = true; ad.Given = true; break;
                    case "-e": case "--encoding": NextArg(args, ref i, a); break;   // 先読み済み
                    case "--data": NextArg(args, ref i, a); break;                   // 先読み済み
                    case "--log": NextArg(args, ref i, a); break;                    // 先読み済み
                    case "--no-log": break;
                    case "--import": importFile = NextArg(args, ref i, a); break;
                    case "--": rest = true; break;
                    default:
                        if (a.StartsWith("--")) throw new CliError("不明なオプション: " + a + "（--help で使い方を表示）");
                        names.Add(a); break;
                }
            }
            // オプションなしの引数は空白でつないで1つの顧客名（"山田 太郎" と 山田 太郎 を同じに扱う）
            query.Names.AddRange(SearchQuery.Split(string.Join(" ", names)));
            if (ad.Given && !add) throw new CliError("--inquiry / --response / --next などは --add と一緒に指定してください");

            if (add) info.Mode = "add";
            else if (importFile != null) info.Mode = "import";
            else if (list) info.Mode = "list";
            else if (all) info.Mode = "all";

            var store = new CrmStore(info.DataPath ?? CrmStore.ResolveDefaultPath());
            info.DataPath = store.FilePath;

            if (add) return RunAdd(store, query, ad, enc, info);

            if (importFile != null)
            {
                if (!File.Exists(importFile)) throw new CliError("ファイルが見つかりません: " + importFile);
                var parsed = Importer.Parse(Csv.ReadTextFile(importFile));
                var work = store.Data.Clone();
                var res = Importer.Apply(work, parsed.Rows, null);
                if (res.HasChanges) store.ReplaceData(work);
                var sb = new StringBuilder();
                sb.AppendLine(res.Summary);
                for (int r = 0; r < parsed.Rows.Count; r++)
                    if (res.RowStatus[r].StartsWith("エラー"))
                        sb.AppendLine(string.Format("  {0}行目: {1}", parsed.Rows[r].LineNo, res.RowStatus[r]));
                Write(sb.ToString(), enc);
                info.Detail = res.Summary;
                return res.Errors > 0 ? ExitError : ExitFound;
            }

            if (list)
            {
                string cl = BuildCustomerCsv(store.Data);
                Write(header ? cl : StripHeader(cl), enc);
                info.Detail = "顧客 " + store.Data.Customers.Count + " 件";
                return store.Data.Customers.Count > 0 ? ExitFound : ExitNotFound;
            }

            List<Customer> targets;
            if (all) { targets = store.Data.Customers.ToList(); full = true; }
            else
            {
                if (query.IsEmpty)
                    throw new CliError("検索条件（顧客名 / --name / --company / --phone / --any / --id）を指定してください（--help で使い方を表示）");
                targets = store.Data.Search(query);
            }

            int count;
            string csv = BuildHistoryCsv(store.Data, targets, full, header, out count);
            info.Detail = string.Format("該当顧客 {0} 件・対応履歴 {1} 件{2}", targets.Count, count, count == 0 ? "（該当なし）" : "") +
                          (targets.Count > 0 && targets.Count <= 5 ? "：" + string.Join("、", targets.Select(c => c.DisplayName)) : "");
            if (count == 0)
            {
                Write(NotFoundMessage + "\r\n", enc);
                return ExitNotFound;
            }
            Write(csv, enc);
            return ExitFound;
        }

        /// <summary>検索条件で顧客を1件に特定し、対応履歴を登録する</summary>
        static int RunAdd(CrmStore store, SearchQuery q, AddArgs a, Encoding enc, RunInfo info)
        {
            if (a.Inquiry.Length == 0 && a.Response.Length == 0 && a.Next.Length == 0)
                throw new CliError("--inquiry / --response / --next のいずれかを指定してください");
            if (q.IsEmpty)
                throw new CliError("登録先の顧客を --name / --company / --phone / --any / --id で指定してください");
            DateTime date = DateTime.Today;
            if (a.DateText.Length > 0 && !TextUtil.TryParseDate(a.DateText, out date))
                throw new CliError("対応日を解釈できません: " + a.DateText);
            DateTime? next = null;
            if (a.NextDateText.Length > 0)
            {
                DateTime nd;
                if (!TextUtil.TryParseDate(a.NextDateText, out nd)) throw new CliError("次回確認日を解釈できません: " + a.NextDateText);
                next = nd;
            }

            var data = store.Data;
            var hits = data.Search(q);
            if (hits.Count > 1)
            {
                var sb = new StringBuilder();
                sb.Append("該当する顧客が複数います。--id か条件の追加で1件に絞ってください\r\n");
                sb.Append(Csv.Line("顧客ID", "顧客名", "会社名", "電話番号"));
                foreach (var h in hits) sb.Append(Csv.Line(h.Id.ToString(), h.Name, h.Company, h.Phone));
                Write(sb.ToString(), enc);
                info.Detail = "登録せず：顧客が複数該当（" + string.Join("、", hits.Select(h => "ID:" + h.Id + " " + h.DisplayName)) + "）";
                return ExitAmbiguous;
            }

            Customer c;
            bool created = false;
            if (hits.Count == 0)
            {
                if (!a.Create)
                {
                    Write("該当する顧客がいません（--create を付けると新規顧客として登録します）\r\n", enc);
                    info.Detail = "登録せず：該当する顧客なし";
                    return ExitNotFound;
                }
                if (q.Names.Count != 1) throw new CliError("新規顧客の登録には --name で顧客名を1つ指定してください");
                c = data.AddCustomer(new Customer
                {
                    Name = q.Names[0], Company = q.Companies.Count == 1 ? q.Companies[0] : "",
                    Phone = q.Phones.Count == 1 ? q.Phones[0] : "", Email = a.Email
                });
                created = true;
            }
            else
            {
                c = hits[0];
                // 空欄の項目だけ補完（登録済みの値は上書きしない）
                if (c.Phone.Length == 0 && q.Phones.Count == 1) c.Phone = q.Phones[0];
                if (c.Company.Length == 0 && q.Companies.Count == 1) c.Company = q.Companies[0];
                if (c.Email.Length == 0 && a.Email.Length > 0) c.Email = a.Email;

                string ni = TextUtil.Norm(a.Inquiry), nr = TextUtil.Norm(a.Response);
                int cid = c.Id;
                if (data.Interactions.Any(i => i.CustomerId == cid && i.Date == date &&
                                               TextUtil.Norm(i.Inquiry) == ni && TextUtil.Norm(i.Response) == nr))
                {
                    Write(string.Format("同じ内容の対応履歴が登録済みのため、登録しませんでした（顧客ID:{0} {1}）\r\n", c.Id, c.DisplayName), enc);
                    info.Detail = string.Format("登録せず：同じ内容が登録済み（顧客ID:{0} {1}）", c.Id, c.DisplayName);
                    return ExitFound;
                }
            }

            var it = data.AddInteraction(new Interaction
            {
                CustomerId = c.Id, Date = date, Inquiry = a.Inquiry, Response = a.Response, NextAction = a.Next,
                NextDate = next, Staff = a.Staff, Done = a.Done
            });
            store.Save();
            string msg = string.Format("登録しました（{0}顧客ID:{1} {2}、対応履歴ID:{3}、対応日:{4}）",
                created ? "新規顧客 " : "", c.Id, c.DisplayName, it.Id, TextUtil.Date(date));
            Write(msg + "\r\n", enc);
            info.Detail = msg;
            return ExitFound;
        }

        static string StripHeader(string csv)
        {
            int p = csv.IndexOf("\r\n", StringComparison.Ordinal);
            return p < 0 ? "" : csv.Substring(p + 2);
        }
    }

    /// <summary>コマンドライン実行のログ（1実行1行、タブ区切り、UTF-8）</summary>
    public static class RunLog
    {
        public const string FileName = "crm_log.txt";
        const long MaxBytes = 5 * 1024 * 1024;
        const string Header = "日時\t処理\t終了コード\t処理時間\t結果\t引数";

        static bool IsOff(string v)
        {
            v = v.Trim().ToLowerInvariant();
            return v == "off" || v == "none" || v == "0" || v == "false" || v == "no";
        }

        /// <summary>保存先: --log → 環境変数 CRM_LOG → データファイルと同じフォルダの crm_log.txt。無効なら null</summary>
        public static string ResolvePath(string logOption, string dataPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(logOption)) return Path.GetFullPath(logOption);
                string env = Environment.GetEnvironmentVariable("CRM_LOG");
                if (!string.IsNullOrEmpty(env)) return IsOff(env) ? null : Path.GetFullPath(env);
                string data = dataPath ?? CrmStore.ResolveDefaultPath();
                return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(data)), FileName);
            }
            catch { return null; }
        }

        public static string Format(DateTime at, string mode, int code, long ms, string detail, string[] args)
        {
            return string.Join("\t", new[]
            {
                at.ToString("yyyy-MM-dd HH:mm:ss.fff"), mode, "exit=" + code, ms + "ms",
                Clean(detail), string.Join(" ", args.Select(QuoteArg))
            });
        }

        static string Clean(string s)
        {
            return (s ?? "").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n").Replace("\t", " ");
        }

        static string QuoteArg(string a)
        {
            a = Clean(a);
            if (a.Length > 80) a = a.Substring(0, 80) + "…";   // 長い要約文はログでは省略
            return a.Length == 0 || a.IndexOfAny(new[] { ' ', '　', ',', '"' }) >= 0 ? "\"" + a.Replace("\"", "\\\"") + "\"" : a;
        }

        /// <summary>
        /// 1行追記する。GUI や別の呼び出しと同時に書くことがあるので少し待って再試行する。
        /// ログが書けなくても検索・登録の結果には影響させない。
        /// </summary>
        public static void Append(string path, string line)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string old = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + ".1" + Path.GetExtension(path));
                        File.Copy(path, old, true);
                        File.Delete(path);
                    }
                    using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        if (fs.Length == 0)
                        {
                            // メモ帳や Excel でも文字化けしないよう、新しいファイルには BOM と見出しを付ける
                            byte[] pre = Encoding.UTF8.GetPreamble();
                            fs.Write(pre, 0, pre.Length);
                            byte[] h = Encoding.UTF8.GetBytes(Header + "\r\n");
                            fs.Write(h, 0, h.Length);
                        }
                        byte[] b = Encoding.UTF8.GetBytes(line + "\r\n");
                        fs.Write(b, 0, b.Length);
                    }
                    return;
                }
                catch (IOException) { System.Threading.Thread.Sleep(30); }
                catch { return; }
            }
        }
    }
}
