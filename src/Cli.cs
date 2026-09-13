using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CrmDemo
{
    /// <summary>
    /// コマンドライン（引数あり起動）
    /// ・検索: 顧客名・会社名・電話番号で検索し、ケースをCSVで標準出力する
    /// ・登録（--add）: 顧客を特定して（無ければ作って）ケースを起票する。値は --set 変数名=値 で指定
    /// ・実行ごとに crm_log.txt へ1行ログを残す
    /// </summary>
    public static class Cli
    {
        public const string NotFoundMessage = "該当する情報がありません";
        public const int ExitFound = 0, ExitNotFound = 1, ExitAmbiguous = 2, ExitError = 9;
        public const string DefaultEnvPrefix = "AMIVOICE_CS_";

        public static readonly string[] CustomerColumns = { "顧客ID", "顧客名", "会社名", "電話番号", "メール", "ケース番号" };

        class CliError : Exception { public CliError(string m) : base(m) { } }

        /// <summary>1回の実行の情報（ログ用）</summary>
        class RunInfo
        {
            public string Mode = "search", Detail = "", LogPath, DataPath;
            public bool NoLog;
            public Encoding Enc;
        }

        // ------------------------------------------------------------------ 出力の組み立て

        /// <summary>
        /// 指定顧客のケースCSV（並び順の日付の昇順）。
        /// full=false は「一覧・標準出力に含める」フィールドだけ、full=true は顧客情報＋全フィールド。
        /// GUIの「連携CSV出力プレビュー」「連携検索テスト」でも同じものを使う。
        /// </summary>
        public static string BuildHistoryCsv(CrmData data, IEnumerable<Customer> customers, bool full, bool header, out int count)
        {
            var custs = customers.ToDictionary(c => c.Id);
            var cases = data.Cases.Where(c => custs.ContainsKey(c.CustomerId))
                            .OrderBy(c => data.SortDate(c)).ThenBy(c => c.CustomerId).ThenBy(c => c.Id).ToList();
            count = cases.Count;
            var fields = full ? data.SortedFields : data.ListFields;

            var sb = new StringBuilder();
            if (header)
            {
                var cols = new List<string>();
                if (full) cols.AddRange(CustomerColumns);
                cols.AddRange(fields.Select(f => f.Label));
                sb.Append(Csv.Line(cols.ToArray()));
            }
            foreach (var cs in cases)
            {
                var c = custs[cs.CustomerId];
                var row = new List<string>();
                if (full) row.AddRange(new[] { c.Id.ToString(), c.Name, c.Company, c.Phone, c.Email, cs.Number });
                row.AddRange(fields.Select(f => cs.Get(f.ApiName)));
                sb.Append(Csv.Line(row.ToArray()));
            }
            return sb.ToString();
        }

        public static string BuildCustomerCsv(CrmData data)
        {
            var sb = new StringBuilder();
            sb.Append(Csv.Line("顧客ID", "顧客名", "会社名", "電話番号", "メール", "住所", "備考", "ケース件数", "最終対応日"));
            foreach (var c in data.Customers.OrderBy(x => x.Id))
            {
                var cases = data.Cases.Where(x => x.CustomerId == c.Id).ToList();
                sb.Append(Csv.Line(c.Id.ToString(), c.Name, c.Company, c.Phone, c.Email, c.Address, c.Memo,
                    cases.Count.ToString(),
                    cases.Count > 0 ? TextUtil.Date(cases.Max(x => data.SortDate(x))) : ""));
            }
            return sb.ToString();
        }

        public static string BuildFieldsCsv(CrmData data)
        {
            var sb = new StringBuilder();
            sb.Append(Csv.Line("表示名", "変数名", "型", "選択肢", "必須", "一覧・標準出力", "キー項目"));
            foreach (var f in data.SortedFields)
                sb.Append(Csv.Line(f.Label, f.ApiName, FieldTypes.Label(f.Type), f.Options.Replace("\n", " / "),
                    f.Required ? "○" : "", f.InList ? "○" : "", f.IsKey ? "○" : ""));
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
"CSDemoCRM2 - 顧客・ケース管理\r\n" +
"\r\n" +
"■ 使い方\r\n" +
"  " + exe + "                         GUIを起動\r\n" +
"  " + exe + " <顧客名> [オプション]       その顧客のケースをCSVで標準出力\r\n" +
"  " + exe + " [検索条件...] [オプション]\r\n" +
"  " + exe + " --add [検索条件...] --set <変数名>=<値> ...   ケースを起票\r\n" +
"\r\n" +
"■ 検索条件（検索・登録で共通。顧客を特定する条件）\r\n" +
"  -n, --name <顧客名>       顧客名（完全一致。--partial で部分一致）\r\n" +
"  -c, --company <会社名>    会社名（部分一致。株式会社・(株) などの法人格は無視）\r\n" +
"  -t, --phone <電話番号>    電話番号（数字だけで比較。10桁以上は全桁一致、4〜9桁は末尾一致）\r\n" +
"  -a, --any <値>            顧客名・会社名・電話番号のいずれかに一致\r\n" +
"      --id <顧客ID>         顧客ID\r\n" +
"  ・値はカンマ（, 、）区切りで複数指定でき、同じ項目内は OR 検索になります。\r\n" +
"  ・異なる項目どうしは AND で絞り込みます（--name 佐藤花子 --company テスト工業）。\r\n" +
"  ・空白の有無・全角半角・大文字小文字、末尾の「様」「さん」は区別しません。\r\n" +
"  ・オプションなしの引数は顧客名として扱います。\r\n" +
"  ・条件のオプションを指定して値が空のときはエラーにせず、該当なし（終了コード 1）にします。\r\n" +
"\r\n" +
"■ 検索の出力（既定）\r\n" +
"  フィールド設定で「一覧・標準出力に含める」にした項目を、並び順どおりCSVで出力します。\r\n" +
"  ・並びは日付項目の古い順。1行目は見出し（表示名）。\r\n" +
"  ・--full を付けると 顧客ID,顧客名,会社名,電話番号,メール,ケース番号 と全フィールドを出力します。\r\n" +
"  ・該当なしの場合は「" + NotFoundMessage + "」だけを出力します（理由によらず同じ1行）。\r\n" +
"      --fields             フィールド定義の一覧をCSVで出力（指定できる変数名の確認用）\r\n" +
"\r\n" +
"■ ケースの起票（--add）\r\n" +
"      --add                 登録モード\r\n" +
"      --set <変数名>=<値>   ケースの値（複数指定可。--set は省略して 変数名=値 でも可）\r\n" +
"      --case CS-00001       ケース番号を指定して更新\r\n" +
"      --new                 キー項目が一致しても必ず新しいケースを作る\r\n" +
"      --no-create           顧客が見つからないとき、新規作成せず終了コード 1 にする\r\n" +
"      --email <メール>      顧客を新規作成するときのメールアドレス\r\n" +
"  ・顧客は検索条件で特定します。見つからない場合は --name / --company / --phone の値で\r\n" +
"    新しい顧客を作り、そこにケースを起票します（--name は必須。--no-create で抑止）。\r\n" +
"  ・顧客が複数該当したときは起票せず、候補を出力して終了コード 2（--pick で選択画面）。\r\n" +
"  ・キー項目（既定は 通話ID / call_id）に同じ値のケースがあれば、そのケースを更新します。\r\n" +
"    通話中に複数回コマンドを送っても1件のケースにまとまります（--new で常に新規）。\r\n" +
"  ・値の中の \\n は改行、%VAR% は環境変数の値に展開します。\r\n" +
"\r\n" +
"■ 環境変数からの取り込み\r\n" +
"      --env-prefix [接頭辞]     接頭辞に一致する環境変数を、同じ名前のフィールドへ入れる\r\n" +
"                                （省略時は " + DefaultEnvPrefix + "。" + DefaultEnvPrefix + "SUMMARY → summary）\r\n" +
"      --set-env <変数名>=<環境変数名>  フィールドと環境変数を明示的に対応させる\r\n" +
"      --show-env [接頭辞]       渡された環境変数と対応するフィールドを一覧表示\r\n" +
"\r\n" +
"■ ログ\r\n" +
"  実行するたびに、データファイルと同じフォルダの " + RunLog.FileName + " に1行追記します。\r\n" +
"      --log <ファイル>      ログの保存先（環境変数 CRM_LOG でも可）\r\n" +
"      --no-log              ログを出力しない（CRM_LOG=off でも可）\r\n" +
"\r\n" +
"■ その他のオプション\r\n" +
"  -p, --partial            顧客名を部分一致で検索する\r\n" +
"  -f, --full               顧客情報の列と全フィールドを出力\r\n" +
"      --no-header          見出し行を出力しない\r\n" +
"  -e, --encoding <名前>    標準出力・標準エラー出力の文字コード: utf8（既定）/ utf8bom / sjis\r\n" +
"      --pick               顧客が複数該当したとき、選択画面を出して1件に絞る\r\n" +
"      --pick-timeout <秒>  選択画面が操作されないときに自動でキャンセルする\r\n" +
"      --all                全顧客のケースを出力（--full 形式）\r\n" +
"      --list               顧客一覧をCSVで出力\r\n" +
"      --sample             デモ用のサンプルデータを投入（顧客8件・ケース18件）\r\n" +
"      --import <ファイル>  CSV / タブ区切りファイルを取り込む\r\n" +
"      --data <ファイル>    データファイルを指定（既定: exeと同じフォルダの " + CrmStore.FileName + "）\r\n" +
"  -h, --help               このヘルプを表示\r\n" +
"\r\n" +
"■ 終了コード\r\n" +
"  0 = 該当あり・登録完了 / 1 = 該当なし / 2 = 顧客が複数該当・選択がキャンセル / 9 = エラー\r\n" +
"\r\n" +
"■ 例\r\n" +
"  " + exe + " \"山田 太郎\"\r\n" +
"  " + exe + " --any \"山田太郎,サンプル商事,03-1234-5678\" --full\r\n" +
"  " + exe + " --add --name 山田太郎 --phone 03-1234-5678 --set call_id=C12345 --set summary=\"導入時期の相談\"\r\n" +
"  " + exe + " --add --name \"%CUSTOMER%\" --phone \"%ANI%\" --env-prefix " + DefaultEnvPrefix + "\r\n";
        }

        // ------------------------------------------------------------------ 入出力

        static string NextArg(string[] args, ref int i, string opt)
        {
            if (i + 1 >= args.Length) throw new CliError(opt + " の後に値を指定してください");
            return args[++i];
        }

        /// <summary>次の引数が値かどうか（--show-env のように省略できるオプション用）</summary>
        static string OptionalArg(string[] args, ref int i)
        {
            if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) return args[++i];
            return null;
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

        /// <summary>標準エラー出力へ書く（標準出力と同じ文字コード。BOMは付けない）</summary>
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

        // ------------------------------------------------------------------ 環境変数

        static readonly Regex EnvRefRe = new Regex("%([A-Za-z_][A-Za-z0-9_]*)%");
        static readonly Regex EnvNameRe = new Regex("^[A-Za-z_][A-Za-z0-9_]*$");

        /// <summary>値の中の %VAR% を環境変数の値に置き換える（exe を直接起動されると cmd が展開しないため）</summary>
        static List<string> ExpandEnvRefs(Dictionary<string, string> values)
        {
            var warnings = new List<string>();
            foreach (var key in values.Keys.ToList())
            {
                string text = values[key] ?? "";
                var refs = EnvRefRe.Matches(text);
                if (refs.Count > 0)
                {
                    foreach (Match m in refs)
                        if (Environment.GetEnvironmentVariable(m.Groups[1].Value) == null)
                            warnings.Add(string.Format("環境変数 %{0}% が定義されていないため、{1} には展開されない文字列が入りました。",
                                m.Groups[1].Value, key));
                    values[key] = EnvRefRe.Replace(text, m =>
                        Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
                }
                else if (EnvNameRe.IsMatch(text) && Environment.GetEnvironmentVariable(text) != null)
                {
                    warnings.Add(string.Format("{0} の値「{1}」は環境変数名と一致します。値ではなく名前が渡っています。%{1}% と書くか --env-prefix を使ってください。",
                        key, text));
                }
            }
            return warnings;
        }

        /// <summary>接頭辞に一致する環境変数を {変数名: 値} として取り込む（AMIVOICE_CS_SUMMARY → summary）</summary>
        static Dictionary<string, string> ValuesFromEnv(List<string> prefixes, CrmData data, out List<string> unmatched)
        {
            var collected = new Dictionary<string, string>();
            var un = new List<string>();
            foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            {
                string key = (string)e.Key;
                foreach (var prefix in prefixes)
                {
                    if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    string suffix = key.Substring(prefix.Length).Trim('_');
                    if (suffix.Length == 0) break;
                    var f = data.FieldByApi(suffix);
                    if (f != null) collected[f.ApiName] = (string)(e.Value ?? "");
                    else un.Add(key);
                    break;
                }
            }
            unmatched = un.Distinct().OrderBy(x => x).ToList();
            return collected;
        }

        static string ShowEnvText(string prefix, CrmData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("環境変数（" + (string.IsNullOrEmpty(prefix) ? "すべて" : "接頭辞 " + prefix + " に一致するもの") + "）");
            sb.AppendLine(new string('-', 72));
            var names = new List<string>();
            foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            {
                string key = (string)e.Key;
                if (string.IsNullOrEmpty(prefix) || key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) names.Add(key);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            if (names.Count == 0)
            {
                sb.AppendLine("該当する環境変数がありません。");
                sb.AppendLine("音声認識システムから起動されたときだけ渡される変数は、手動で実行しても表示されません。");
                sb.AppendLine("音声認識システム側のコマンドに次を登録して実行してください:");
                sb.AppendLine("  " + ExeName + " --show-env " + (string.IsNullOrEmpty(prefix) ? DefaultEnvPrefix : prefix));
            }
            foreach (var key in names)
            {
                string value = Environment.GetEnvironmentVariable(key) ?? "";
                string mark = "";
                if (!string.IsNullOrEmpty(prefix) && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string suffix = key.Substring(prefix.Length).Trim('_');
                    var f = data.FieldByApi(suffix);
                    mark = f != null ? "  -> フィールド " + f.Label + "（" + f.ApiName + "）" : "  -> 対応するフィールドなし";
                }
                sb.AppendLine(string.Format("{0,-34} = {1}{2}", key, value.Length > 40 ? value.Substring(0, 40) + "…" : value, mark));
            }
            sb.AppendLine(new string('-', 72));
            sb.AppendLine("接頭辞つきで取り込む場合:  " + ExeName + " --add --name ... --env-prefix " +
                          (string.IsNullOrEmpty(prefix) ? DefaultEnvPrefix : prefix));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ 実行

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

            bool full = false, header = true, list = false, all = false, rest = false, add = false, pick = false;
            bool showFields = false, forceNew = false, noCreate = false, showEnv = false, sample = false;
            int pickTimeout = 0;
            bool condGiven = false;
            string importFile = null, caseNumber = null, email = null, showEnvPrefix = null;
            Encoding enc = info.Enc;
            var names = new List<string>();
            var query = new SearchQuery();
            var sets = new List<KeyValuePair<string, string>>();     // 変数名=値
            var setEnv = new List<KeyValuePair<string, string>>();   // 変数名=環境変数名
            var envPrefixes = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (rest) { names.Add(a); condGiven = true; continue; }
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
                    case "--fields": showFields = true; break;
                    case "--sample": sample = true; break;
                    case "-n": case "--name": query.Names.AddRange(SearchQuery.Split(NextArg(args, ref i, a))); condGiven = true; break;
                    case "-c": case "--company": query.Companies.AddRange(SearchQuery.Split(NextArg(args, ref i, a))); condGiven = true; break;
                    case "-t": case "--phone": case "--tel": query.Phones.AddRange(SearchQuery.Split(NextArg(args, ref i, a))); condGiven = true; break;
                    case "-a": case "--any": query.Anys.AddRange(SearchQuery.Split(NextArg(args, ref i, a))); condGiven = true; break;
                    case "--id":
                        condGiven = true;
                        foreach (var v in SearchQuery.Split(NextArg(args, ref i, a)))
                        {
                            int id;
                            if (!int.TryParse(v, out id)) throw new CliError("顧客IDは数字で指定してください: " + v);
                            query.Ids.Add(id);
                        }
                        break;
                    case "--add": add = true; break;
                    case "--set":
                        {
                            string kv = NextArg(args, ref i, a);
                            int eq = kv.IndexOf('=');
                            if (eq <= 0) throw new CliError("--set は 変数名=値 の形で指定してください: " + kv);
                            sets.Add(new KeyValuePair<string, string>(kv.Substring(0, eq).Trim(), Unescape(kv.Substring(eq + 1))));
                            break;
                        }
                    case "--set-env":
                        {
                            string kv = NextArg(args, ref i, a);
                            int eq = kv.IndexOf('=');
                            if (eq <= 0) throw new CliError("--set-env は 変数名=環境変数名 の形で指定してください: " + kv);
                            setEnv.Add(new KeyValuePair<string, string>(kv.Substring(0, eq).Trim(), kv.Substring(eq + 1).Trim()));
                            break;
                        }
                    case "--env-prefix": envPrefixes.Add(OptionalArg(args, ref i) ?? DefaultEnvPrefix); break;
                    case "--show-env": showEnv = true; showEnvPrefix = OptionalArg(args, ref i) ?? DefaultEnvPrefix; break;
                    case "--case": caseNumber = NextArg(args, ref i, a).Trim(); break;
                    case "--new": forceNew = true; break;
                    case "--no-create": noCreate = true; break;
                    case "--create": break;   // 既定で作成するので何もしない（旧版との互換）
                    case "--email": email = NextArg(args, ref i, a).Trim(); break;
                    case "--pick": pick = true; break;
                    case "--pick-timeout":
                        {
                            string v = NextArg(args, ref i, a);
                            if (!int.TryParse(v, out pickTimeout) || pickTimeout < 0)
                                throw new CliError("--pick-timeout には0以上の秒数を指定してください: " + v);
                            pick = true;
                            break;
                        }
                    // 旧版の項目指定（既定のフィールドに対応づける）
                    case "--inquiry": sets.Add(new KeyValuePair<string, string>("inquiry", Unescape(NextArg(args, ref i, a)))); break;
                    case "--response": sets.Add(new KeyValuePair<string, string>("response", Unescape(NextArg(args, ref i, a)))); break;
                    case "--next": sets.Add(new KeyValuePair<string, string>("next_action", Unescape(NextArg(args, ref i, a)))); break;
                    case "--date": sets.Add(new KeyValuePair<string, string>("date", NextArg(args, ref i, a))); break;
                    case "--next-date": sets.Add(new KeyValuePair<string, string>("next_date", NextArg(args, ref i, a))); break;
                    case "--staff": sets.Add(new KeyValuePair<string, string>("staff", NextArg(args, ref i, a))); break;
                    case "--done": sets.Add(new KeyValuePair<string, string>("status", "完了")); break;
                    case "-e": case "--encoding": NextArg(args, ref i, a); break;   // 先読み済み
                    case "--data": NextArg(args, ref i, a); break;                   // 先読み済み
                    case "--log": NextArg(args, ref i, a); break;                    // 先読み済み
                    case "--no-log": break;
                    case "--import": importFile = NextArg(args, ref i, a); break;
                    case "--": rest = true; break;
                    default:
                        if (a.StartsWith("--")) throw new CliError("不明なオプション: " + a + "（--help で使い方を表示）");
                        // 変数名=値 の形は --set と同じ扱い（CSDemoCRM と同じ書き方）
                        int p = a.IndexOf('=');
                        if (p > 0 && !a.StartsWith("-"))
                        {
                            sets.Add(new KeyValuePair<string, string>(a.Substring(0, p).Trim(), Unescape(a.Substring(p + 1))));
                            break;
                        }
                        names.Add(a); condGiven = true; break;
                }
            }
            query.Names.AddRange(SearchQuery.Split(string.Join(" ", names)));

            var store = new CrmStore(info.DataPath ?? CrmStore.ResolveDefaultPath());
            info.DataPath = store.FilePath;
            var data = store.Data;

            if (showEnv)
            {
                info.Mode = "show-env";
                Write(ShowEnvText(showEnvPrefix, data), enc);
                return ExitFound;
            }

            if (sample)
            {
                info.Mode = "sample";
                var work = data.Clone();
                var res = Importer.Apply(work, Importer.Parse(Samples.Csv(), work).Rows, null);
                if (res.HasChanges) store.ReplaceData(work);
                Write(res.Summary + "\r\n", enc);
                info.Detail = res.Summary;
                return ExitFound;
            }

            if (showFields)
            {
                info.Mode = "fields";
                Write(BuildFieldsCsv(data), enc);
                return ExitFound;
            }

            if (importFile != null)
            {
                info.Mode = "import";
                if (!File.Exists(importFile)) throw new CliError("ファイルが見つかりません: " + importFile);
                var parsed = Importer.Parse(Csv.ReadTextFile(importFile), data);
                var work = data.Clone();
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
                info.Mode = "list";
                string cl = BuildCustomerCsv(data);
                Write(header ? cl : StripHeader(cl), enc);
                info.Detail = "顧客 " + data.Customers.Count + " 件";
                return data.Customers.Count > 0 ? ExitFound : ExitNotFound;
            }

            if (add)
            {
                info.Mode = "add";
                return RunAdd(store, query, sets, setEnv, envPrefixes, caseNumber, email, forceNew, noCreate,
                              enc, info, pick, pickTimeout, condGiven);
            }
            if (sets.Count > 0 || setEnv.Count > 0 || envPrefixes.Count > 0 || caseNumber != null || forceNew)
                throw new CliError("--set / --env-prefix / --case などは --add と一緒に指定してください");

            List<Customer> targets;
            if (all) { targets = data.Customers.ToList(); full = true; info.Mode = "all"; }
            else
            {
                if (query.IsEmpty)
                {
                    if (!condGiven)
                        throw new CliError("検索条件（顧客名 / --name / --company / --phone / --any / --id）を指定してください（--help で使い方を表示）");
                    // 連携元が値を取り出せなかった場合（--any "," など）。呼び出し方の誤りではないので該当なしとして扱う
                    Write(NotFoundMessage + "\r\n", enc);
                    info.Detail = "検索条件の値が空のため該当なし";
                    return ExitNotFound;
                }
                targets = data.Search(query);
            }

            string pickNote = "";
            if (pick && targets.Count > 1)
            {
                Customer chosen;
                Theme.LoadSetting(store.FilePath);
                if (PickerForm.TryPick(targets, data,
                        string.Format("検索条件に {0} 件の顧客が該当しました。ケースを出力する顧客を選んでください。", targets.Count),
                        pickTimeout, out chosen))
                {
                    if (chosen == null)
                    {
                        Write(NotFoundMessage + "\r\n", enc);   // 連携先が扱いやすいよう、結果なしの出力は常に同じ1行
                        info.Detail = string.Format("選択がキャンセルされました（候補 {0} 件）", targets.Count);
                        return ExitAmbiguous;
                    }
                    pickNote = string.Format("（{0} 件から選択）", targets.Count);
                    targets = new List<Customer> { chosen };
                }
            }

            int count;
            string csv = BuildHistoryCsv(data, targets, full, header, out count);
            info.Detail = string.Format("該当顧客 {0} 件・ケース {1} 件{2}", targets.Count, count, count == 0 ? "（該当なし）" : "") +
                          (targets.Count > 0 && targets.Count <= 5 ? "：" + string.Join("、", targets.Select(c => c.DisplayName)) : "") + pickNote;
            if (count == 0)
            {
                Write(NotFoundMessage + "\r\n", enc);
                return ExitNotFound;
            }
            Write(csv, enc);
            return ExitFound;
        }

        /// <summary>顧客を特定（無ければ作成）して、ケースを起票または更新する</summary>
        static int RunAdd(CrmStore store, SearchQuery q, List<KeyValuePair<string, string>> sets,
                          List<KeyValuePair<string, string>> setEnv, List<string> envPrefixes,
                          string caseNumber, string email, bool forceNew, bool noCreate,
                          Encoding enc, RunInfo info, bool pick, int pickTimeout, bool condGiven)
        {
            var data = store.Data;
            var warnings = new List<string>();
            var values = new Dictionary<string, string>();
            var unknown = new List<string>();

            // 1) 値を集める（--env-prefix → --set-env → --set の順に上書き）
            if (envPrefixes.Count > 0)
            {
                List<string> unmatched;
                foreach (var kv in ValuesFromEnv(envPrefixes, data, out unmatched)) values[kv.Key] = kv.Value;
                if (unmatched.Count > 0)
                    warnings.Add("対応するフィールドが無い環境変数: " + string.Join(", ", unmatched.Take(10).ToArray()));
            }
            foreach (var kv in setEnv)
            {
                var f = data.FieldByLabelOrApi(kv.Key);
                if (f == null) { unknown.Add(kv.Key); continue; }
                string v = Environment.GetEnvironmentVariable(kv.Value);
                if (v == null) warnings.Add("環境変数 " + kv.Value + " が定義されていません（" + f.Label + " は設定しません）");
                else values[f.ApiName] = v;
            }
            foreach (var kv in sets)
            {
                var f = data.FieldByLabelOrApi(kv.Key);
                if (f == null) { unknown.Add(kv.Key); continue; }
                values[f.ApiName] = kv.Value;
            }
            warnings.AddRange(ExpandEnvRefs(values));
            if (unknown.Count > 0)
                warnings.Add("定義されていない変数名は無視しました: " + string.Join(", ", unknown.Distinct().ToArray()) +
                             "（--fields で一覧を確認できます）");
            if (values.Count == 0)
                throw new CliError("登録する値を --set 変数名=値 で指定してください（--fields で変数名の一覧を表示）");

            // 値をフィールドの型にそろえる
            foreach (var key in values.Keys.ToList())
            {
                var f = data.FieldByApi(key);
                if (f != null) values[key] = FieldTypes.Normalize(f.Type, values[key]);
            }

            // 2) 更新先のケース（--case 指定 → キー項目の一致）
            Case target = null;
            if (!string.IsNullOrEmpty(caseNumber))
            {
                target = data.GetCaseByNumber(caseNumber);
                if (target == null) throw new CliError("ケース番号が見つかりません: " + caseNumber);
            }
            else if (!forceNew)
            {
                var kf = data.KeyField;
                if (kf != null && values.ContainsKey(kf.ApiName) && values[kf.ApiName].Length > 0)
                    target = data.FindCaseByKey(values[kf.ApiName]);
            }

            // 3) 顧客の特定（更新対象のケースがあるときはその顧客）
            Customer customer = null;
            bool createdCustomer = false;
            if (target != null)
            {
                customer = data.GetCustomer(target.CustomerId);
            }
            else
            {
                if (q.IsEmpty)
                {
                    if (!condGiven)
                        throw new CliError("顧客を --name / --company / --phone / --any / --id で指定してください");
                    Write(NotFoundMessage + "\r\n", enc);
                    info.Detail = "登録せず：検索条件の値が空";
                    return ExitNotFound;
                }
                var hits = data.Search(q);
                if (hits.Count > 1 && pick)
                {
                    Customer chosen;
                    Theme.LoadSetting(store.FilePath);
                    if (PickerForm.TryPick(hits, data,
                            string.Format("{0} 件の顧客が該当しました。ケースを起票する顧客を選んでください。", hits.Count),
                            pickTimeout, out chosen))
                    {
                        if (chosen == null)
                        {
                            Write(NotFoundMessage + "\r\n", enc);
                            info.Detail = string.Format("登録せず：選択がキャンセル（候補 {0} 件）", hits.Count);
                            return ExitAmbiguous;
                        }
                        hits = new List<Customer> { chosen };
                    }
                }
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
                if (hits.Count == 1)
                {
                    customer = hits[0];
                    // 空欄の項目だけ補完（登録済みの値は上書きしない）
                    if (customer.Phone.Length == 0 && q.Phones.Count == 1) customer.Phone = q.Phones[0];
                    if (customer.Company.Length == 0 && q.Companies.Count == 1) customer.Company = q.Companies[0];
                    if (customer.Email.Length == 0 && !string.IsNullOrEmpty(email)) customer.Email = email;
                }
                else
                {
                    // 該当する顧客がいないので、指定された顧客名・会社名・電話番号で新規作成する
                    if (noCreate)
                    {
                        Write(NotFoundMessage + "\r\n", enc);
                        info.Detail = "登録せず：該当する顧客なし（--no-create 指定）";
                        return ExitNotFound;
                    }
                    if (q.Names.Count != 1)
                        throw new CliError("該当する顧客がいません。新規作成するには --name で顧客名を1つ指定してください（--no-create で作成しない）");
                    customer = data.AddCustomer(new Customer
                    {
                        Name = q.Names[0],
                        Company = q.Companies.Count == 1 ? q.Companies[0] : "",
                        Phone = q.Phones.Count == 1 ? q.Phones[0] : "",
                        Email = email ?? ""
                    });
                    createdCustomer = true;
                }
            }
            if (customer == null) throw new CliError("顧客を特定できませんでした");

            // 4) ケースの作成・更新
            string action;
            if (target == null)
            {
                target = new Case { CustomerId = customer.Id, Source = "cli" };
                data.AddCase(target);
                action = "起票";
                // 必須の日付項目が指定されていなければ今日を入れる（音声連携では日付が渡らないことがある）
                foreach (var f in data.SortedFields)
                {
                    if (!f.Required) continue;
                    if (values.ContainsKey(f.ApiName) && values[f.ApiName].Length > 0) continue;
                    if (f.Type == FieldTypes.Date) values[f.ApiName] = DateTime.Today.ToString("yyyy/MM/dd");
                    else if (f.Type == FieldTypes.DateTimeType) values[f.ApiName] = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                    else warnings.Add("必須項目 " + f.Label + "（" + f.ApiName + "）が指定されていません");
                }
            }
            else
            {
                target.Source = "cli";
                action = "更新";
            }
            foreach (var kv in values) target.Set(kv.Key, kv.Value);
            target.UpdatedAt = DateTime.Now;
            customer.UpdatedAt = DateTime.Now;
            store.Save();

            string msg = string.Format("{0}しました（{1}顧客ID:{2} {3}、ケース番号:{4}）",
                action, createdCustomer ? "新規顧客 " : "", customer.Id, customer.DisplayName, target.Number);
            Write(msg + "\r\n", enc);
            foreach (var w in warnings) WriteErr("警告: " + w + "\r\n", enc);
            info.Detail = msg + (warnings.Count > 0 ? "／警告 " + warnings.Count + " 件" : "") +
                          "／設定: " + string.Join(",", values.Keys.ToArray());
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
