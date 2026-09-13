using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CrmDemo
{
    public class MainForm : Form
    {
        readonly CrmStore store;
        CrmData D { get { return store.Data; } }

        TextBox txtSearch, txtDetail;
        Label lblCount, lblCustName, lblCustInfo;
        DataGridView gridCust, gridCase;
        SplitContainer splitMain, splitRight;
        Button btnEditCust, btnDelCust, btnAddCase, btnEditCase, btnDelCase, btnCliPreview;
        ToolStripStatusLabel stInfo, stNotice, stFile, stTheme;
        ToolStripMenuItem miLight, miDark, miSystem;
        readonly Timer syncTimer = new Timer { Interval = 1500 };
        List<FieldDef> columns = new List<FieldDef>();
        bool loading, suppressSearch;

        public MainForm(CrmStore store)
        {
            this.store = store;
            Text = "CSDemoCRM2 - 顧客・ケース管理";
            Font = Ui.BaseFont;
            Size = new Size(Ui.S(1320), Ui.S(820));
            MinimumSize = new Size(Ui.S(960), Ui.S(600));
            StartPosition = FormStartPosition.CenterScreen;
            columns = Settings.ListColumns(D);
            BuildUi();
            Theme.Attach(this);
            Theme.Changed += OnThemeChanged;
            FormClosed += delegate { Theme.Changed -= OnThemeChanged; syncTimer.Stop(); };
            // コマンドライン（--add）など別プロセスからの登録を画面へ自動反映
            syncTimer.Tick += delegate { SyncFromDisk(true); };
            Load += delegate
            {
                try
                {
                    splitMain.SplitterDistance = Ui.S(560);
                    splitRight.SplitterDistance = Math.Max(Ui.S(150), splitRight.Height - Ui.S(230));
                }
                catch { }
                RefreshAll();
                UpdateThemeChecks();
                syncTimer.Start();
            };
            Shown += delegate
            {
                if (D.Customers.Count == 0 &&
                    Ui.Confirm(this, "データがまだありません。\r\nデモ用のサンプルデータ（顧客8件・ケース18件）を投入しますか？\r\n\r\n※ あとから［データ］メニューでも投入できます。"))
                    LoadSample(false);
            };
        }

        // ================================================================ 画面構築

        void BuildUi()
        {
            var menu = BuildMenu();

            var status = new StatusStrip();
            stTheme = new ToolStripStatusLabel { Margin = new Padding(Ui.S(4), 0, Ui.S(12), 0), ToolTipText = "クリックでライト / ダークを切り替え（Ctrl+Shift+L）" };
            stTheme.Click += delegate { ToggleTheme(); };
            stInfo = new ToolStripStatusLabel();
            stNotice = new ToolStripStatusLabel { Margin = new Padding(Ui.S(16), 0, 0, 0) };
            stFile = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight };
            status.Items.AddRange(new ToolStripItem[] { stTheme, stInfo, stNotice, stFile });

            splitMain = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = Ui.S(6) };
            BuildCustomerPane();
            BuildCasePane();

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(8)) };
            body.Controls.Add(splitMain);

            Controls.Add(body);
            Controls.Add(status);
            Controls.Add(menu);
            MainMenuStrip = menu;
        }

        MenuStrip BuildMenu()
        {
            var m = new MenuStrip { Font = Ui.BaseFont };
            var file = new ToolStripMenuItem("ファイル(&F)");
            file.DropDownItems.Add(Item("CSV / テキストのインポート...", Keys.Control | Keys.I, ImportData));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("顧客一覧をCSVエクスポート...", Keys.None, ExportCustomers));
            file.DropDownItems.Add(Item("全ケースをCSVエクスポート...", Keys.Control | Keys.E, ExportAllCases));
            file.DropDownItems.Add(Item("選択中の顧客のケースをCSVエクスポート...", Keys.None, ExportCustomerCases));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("バックアップを作成", Keys.None, Backup));
            file.DropDownItems.Add(Item("データフォルダを開く", Keys.None, OpenDataFolder));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("終了", Keys.Alt | Keys.F4, delegate { Close(); }));

            var edit = new ToolStripMenuItem("編集(&E)");
            edit.DropDownItems.Add(Item("検索", Keys.Control | Keys.F, delegate { txtSearch.Focus(); txtSearch.SelectAll(); }));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("新規顧客...", Keys.Control | Keys.N, NewCustomer));
            edit.DropDownItems.Add(Item("顧客を編集...", Keys.None, EditCustomer));
            edit.DropDownItems.Add(Item("顧客を削除", Keys.None, DeleteCustomer));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("ケースを追加...", Keys.Control | Keys.T, AddCase));
            edit.DropDownItems.Add(Item("ケースを編集...", Keys.None, EditCase));
            edit.DropDownItems.Add(Item("ケースを削除", Keys.None, DeleteCase));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("連携CSV出力プレビュー...", Keys.Control | Keys.P, CliPreview));
            edit.DropDownItems.Add(Item("連携検索テスト（顧客名・会社名・電話番号）...", Keys.Control | Keys.Shift | Keys.P, SearchTest));

            var view = new ToolStripMenuItem("表示(&V)");
            view.DropDownItems.Add(Item("表示する列...", Keys.Control | Keys.L, ChooseColumns));
            view.DropDownItems.Add(new ToolStripSeparator());
            miLight = Item("ライトモード", Keys.None, delegate { Theme.Set(ThemeMode.Light); });
            miDark = Item("ダークモード", Keys.None, delegate { Theme.Set(ThemeMode.Dark); });
            miSystem = Item("Windows の設定に合わせる", Keys.None, delegate { Theme.Set(ThemeMode.System); });
            view.DropDownItems.Add(miLight);
            view.DropDownItems.Add(miDark);
            view.DropDownItems.Add(miSystem);
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(Item("ライト / ダークを切り替え", Keys.Control | Keys.Shift | Keys.L, delegate { ToggleTheme(); }));

            var data = new ToolStripMenuItem("データ(&D)");
            data.DropDownItems.Add(Item("フィールド設定（ケースの項目）...", Keys.F4, OpenFieldSettings));
            data.DropDownItems.Add(new ToolStripSeparator());
            data.DropDownItems.Add(Item("サンプルデータを投入", Keys.None, delegate { LoadSample(true); }));
            data.DropDownItems.Add(Item("最新の情報に更新（再読み込み）", Keys.F5, ReloadData));
            data.DropDownItems.Add(new ToolStripSeparator());
            data.DropDownItems.Add(Item("全データを削除...", Keys.None, ClearAll));

            var help = new ToolStripMenuItem("ヘルプ(&H)");
            help.DropDownItems.Add(Item("コマンドライン連携の使い方", Keys.F1, delegate
            {
                using (var f = new TextViewerForm("コマンドライン連携の使い方", null, Cli.HelpText())) f.ShowDialog(this);
            }));
            help.DropDownItems.Add(Item("実行ログを開く（コマンドライン連携）", Keys.None, delegate
            {
                string path = RunLog.ResolvePath(null, store.FilePath);
                if (path == null) { Ui.Info(this, "ログの出力が無効になっています（環境変数 CRM_LOG=off）。"); return; }
                if (!File.Exists(path)) { Ui.Info(this, "まだログがありません。コマンドラインで実行すると作成されます。\r\n\r\n" + path); return; }
                try { Process.Start("notepad.exe", "\"" + path + "\""); }
                catch (Exception ex) { Ui.Warn(this, ex.Message); }
            }));
            help.DropDownItems.Add(Item("バージョン情報", Keys.None, delegate
            {
                Ui.Info(this, "CSDemoCRM2 2.0\r\n顧客・ケース管理（デモ用）\r\n\r\n動作環境: Windows 10 / 11（追加インストール不要）\r\nデータ: " + store.FilePath);
            }));

            m.Items.AddRange(new ToolStripItem[] { file, edit, view, data, help });
            return m;
        }

        static ToolStripMenuItem Item(string text, Keys keys, EventHandler h)
        {
            var i = new ToolStripMenuItem(text, null, h);
            if (keys != Keys.None) i.ShortcutKeys = keys;
            return i;
        }

        void BuildCustomerPane()
        {
            var search = Ui.Flow();
            search.Controls.Add(Ui.FlowLabel("検索"));
            txtSearch = new TextBox { Width = Ui.S(230), Margin = new Padding(0, Ui.S(5), Ui.S(6), 0) };
            txtSearch.TextChanged += delegate { if (!suppressSearch) RefreshCustomers(); };
            search.Controls.Add(txtSearch);
            search.Controls.Add(Ui.Btn("クリア", delegate { txtSearch.Clear(); }));
            lblCount = Ui.FlowLabel("", "sub");
            search.Controls.Add(lblCount);

            var hint = new Label
            {
                Text = "顧客名・会社名・電話番号・メール・ケースの内容で絞り込めます", Dock = DockStyle.Top,
                Height = Ui.S(20), Tag = "sub"
            };

            var custBtns = Ui.Flow();
            custBtns.Controls.Add(Ui.Btn("＋ 新規顧客", NewCustomer, true));
            custBtns.Controls.Add(btnEditCust = Ui.Btn("編集", EditCustomer));
            custBtns.Controls.Add(btnDelCust = Ui.Btn("削除", DeleteCustomer));
            custBtns.Controls.Add(Ui.Btn("インポート...", ImportData));
            custBtns.Controls.Add(Ui.Btn("連携検索テスト...", SearchTest));

            gridCust = Ui.Grid();
            Ui.Col(gridCust, "ID", 30, 36, typeof(int));
            Ui.Col(gridCust, "顧客名", 90, 70);
            Ui.Col(gridCust, "会社名", 130, 80);
            Ui.Col(gridCust, "電話番号", 90, 70);
            Ui.Col(gridCust, "ケース", 36, 56, typeof(int));
            Ui.Col(gridCust, "最終対応日", 80, 96);
            gridCust.SelectionChanged += delegate { if (!loading) OnCustomerSelected(); };
            gridCust.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditCustomer(null, null); };
            gridCust.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) DeleteCustomer(null, null); };

            splitMain.Panel1.Controls.Add(gridCust);
            splitMain.Panel1.Controls.Add(custBtns);
            splitMain.Panel1.Controls.Add(hint);
            splitMain.Panel1.Controls.Add(search);
        }

        void BuildCasePane()
        {
            var custPanel = new Panel { Dock = DockStyle.Top, Height = Ui.S(112), Padding = new Padding(Ui.S(12), Ui.S(8), Ui.S(12), Ui.S(6)), Tag = "panel" };
            lblCustName = new Label { Dock = DockStyle.Top, Height = Ui.S(30), Font = Ui.TitleFont };
            lblCustInfo = new Label { Dock = DockStyle.Fill, Tag = "sub" };
            custPanel.Controls.Add(lblCustInfo);
            custPanel.Controls.Add(lblCustName);

            var caseBtns = Ui.Flow();
            caseBtns.Padding = new Padding(0, Ui.S(8), 0, Ui.S(4));
            var title = Ui.FlowLabel("ケース");
            title.Font = Ui.BoldFont;
            caseBtns.Controls.Add(title);
            caseBtns.Controls.Add(btnAddCase = Ui.Btn("＋ ケースを追加", AddCase, true));
            caseBtns.Controls.Add(btnEditCase = Ui.Btn("編集", EditCase));
            caseBtns.Controls.Add(btnDelCase = Ui.Btn("削除", DeleteCase));
            caseBtns.Controls.Add(Ui.Btn("表示する列...", ChooseColumns));
            caseBtns.Controls.Add(btnCliPreview = Ui.Btn("連携CSV出力プレビュー", CliPreview));

            splitRight = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2, SplitterWidth = Ui.S(6) };
            gridCase = Ui.Grid();
            gridCase.SelectionChanged += delegate { if (!loading) ShowDetail(); };
            gridCase.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditCase(null, null); };
            gridCase.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) DeleteCase(null, null); };
            splitRight.Panel1.Controls.Add(gridCase);

            txtDetail = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                Font = new Font("Yu Gothic UI", 10.5f)
            };
            var detailTitle = new Label { Text = "選択中のケース（ダブルクリックで全項目を編集）", Dock = DockStyle.Top, Height = Ui.S(22), Font = Ui.BoldFont };
            splitRight.Panel2.Controls.Add(txtDetail);
            splitRight.Panel2.Controls.Add(detailTitle);

            splitMain.Panel2.Controls.Add(splitRight);
            splitMain.Panel2.Controls.Add(caseBtns);
            splitMain.Panel2.Controls.Add(custPanel);
            BuildCaseColumns();
        }

        /// <summary>ケース一覧の列を作り直す（表示する列の変更・フィールド設定の変更時）</summary>
        void BuildCaseColumns()
        {
            gridCase.Columns.Clear();
            Ui.Col(gridCase, "ケース番号", 46, 80);
            foreach (var f in columns)
            {
                float weight = f.Type == FieldTypes.TextArea ? 140 : 60;
                int min = f.Type == FieldTypes.Date ? 92 : 70;
                Ui.Col(gridCase, f.Label, weight, min);
            }
        }

        // ================================================================ テーマ・列

        void ToggleTheme() { Theme.Set(Theme.IsDark ? ThemeMode.Light : ThemeMode.Dark); }

        void OnThemeChanged(object sender, EventArgs e)
        {
            Theme.Apply(this);
            RefreshAll();
            UpdateThemeChecks();
            Invalidate(true);
        }

        void UpdateThemeChecks()
        {
            miLight.Checked = Theme.Mode == ThemeMode.Light;
            miDark.Checked = Theme.Mode == ThemeMode.Dark;
            miSystem.Checked = Theme.Mode == ThemeMode.System;
            stTheme.Text = Theme.IsDark ? "☾ ダークモード" : "☀ ライトモード";
            stTheme.ForeColor = Theme.Accent;
        }

        void ChooseColumns(object sender, EventArgs e)
        {
            using (var f = new ColumnChooserForm(D, columns))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                columns = f.Selected;
                Settings.SetListColumns(columns);
                BuildCaseColumns();
                RefreshCases(CurrentCaseId());
            }
        }

        // ================================================================ 別プロセスからの更新・保存

        /// <summary>データファイルが別プロセスで更新されていたら読み直す。新しいケースがあればそれを選択する。</summary>
        void SyncFromDisk(bool showNew)
        {
            if (!store.ChangedOnDisk || Application.OpenForms.Count > 1) return; // ダイアログ表示中は閉じてから反映
            int prevMax = D.Cases.Count > 0 ? D.Cases.Max(c => c.Id) : 0;
            var prevStamps = D.Cases.ToDictionary(c => c.Id, c => c.UpdatedAt);
            int prevCustomers = D.Customers.Count;
            int? cust = SelectedCustomerId();
            int? caseId = CurrentCaseId();
            try { store.Load(); }
            catch { return; } // 書き込み中などで読めないときは次回に再試行

            var added = D.Cases.Where(c => c.Id > prevMax).OrderBy(c => c.Id).ToList();
            var changed = D.Cases.Where(c => c.Id <= prevMax && prevStamps.ContainsKey(c.Id) && prevStamps[c.Id] != c.UpdatedAt).ToList();
            var target = added.Count > 0 ? added[added.Count - 1] : (changed.Count > 0 ? changed[changed.Count - 1] : null);

            columns = Settings.ListColumns(D);
            BuildCaseColumns();
            if (showNew && target != null)
            {
                ClearSearchSilently();
                RefreshCustomers(target.CustomerId, target.Id);
                var c = D.GetCustomer(target.CustomerId);
                int newCust = D.Customers.Count - prevCustomers;
                stNotice.Text = string.Format("{0}  外部からケースを{1}：{2}（{3}）{4}",
                    DateTime.Now.ToString("HH:mm:ss"), added.Count > 0 ? "起票" : "更新",
                    c == null ? "" : c.DisplayName, target.Number,
                    newCust > 0 ? "・新規顧客 " + newCust + " 件" : "");
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            }
            else
            {
                RefreshCustomers(cust, caseId);
                if (showNew) stNotice.Text = DateTime.Now.ToString("HH:mm:ss") + "  データファイルの更新を反映しました";
            }
        }

        void Sync() { SyncFromDisk(false); }

        /// <summary>最新のデータに変更を適用して保存する（別プロセスでの登録を上書きしないよう、変更前に読み直す）</summary>
        bool Commit(Action<CrmData> change)
        {
            try
            {
                if (store.ChangedOnDisk) store.Load();
                change(store.Data);
                store.Save();
                return true;
            }
            catch (Exception ex)
            {
                Ui.Warn(this, "保存に失敗しました。\r\n" + store.FilePath + "\r\n\r\n" + ex.Message);
                try { store.Load(); } catch { }
                RefreshAll();
                return false;
            }
        }

        // ================================================================ 表示更新

        void RefreshAll()
        {
            RefreshCustomers(SelectedCustomerId(), CurrentCaseId());
        }

        int? SelectedCustomerId()
        {
            if (gridCust == null || gridCust.CurrentRow == null) return null;
            return gridCust.CurrentRow.Tag as int?;
        }

        Customer CurrentCustomer()
        {
            int? id = SelectedCustomerId();
            return id.HasValue ? D.GetCustomer(id.Value) : null;
        }

        int? CurrentCaseId()
        {
            if (gridCase == null || gridCase.CurrentRow == null) return null;
            return gridCase.CurrentRow.Tag as int?;
        }

        Case CurrentCase()
        {
            int? id = CurrentCaseId();
            return id.HasValue ? D.GetCase(id.Value) : null;
        }

        static string Plain(string s) { return TextUtil.Norm(s).Replace("-", ""); }

        bool Matches(Customer c, string q, ILookup<int, Case> cases)
        {
            if (Plain(c.Name + c.Company + c.Phone + c.Email + c.Address + c.Memo).Contains(q)) return true;
            return cases[c.Id].Any(cs => Plain(string.Join(" ", cs.Values.Select(v => v.Value).ToArray())).Contains(q));
        }

        void RefreshCustomers(int? selectCustomer = null, int? selectCase = null)
        {
            string q = Plain(txtSearch.Text);
            var cases = D.Cases.ToLookup(c => c.CustomerId);
            var list = D.Customers.Where(c => q.Length == 0 || Matches(c, q, cases)).OrderBy(c => c.Id).ToList();

            loading = true;
            gridCust.Rows.Clear();
            DataGridViewRow target = null;
            foreach (var c in list)
            {
                var cs = cases[c.Id].ToList();
                int idx = gridCust.Rows.Add(c.Id, c.Name, c.Company, c.Phone, cs.Count,
                    cs.Count > 0 ? TextUtil.Date(cs.Max(x => D.SortDate(x))) : "");
                var row = gridCust.Rows[idx];
                row.Tag = c.Id;
                if (selectCustomer.HasValue && c.Id == selectCustomer.Value) target = row;
            }
            if (target == null && gridCust.Rows.Count > 0) target = gridCust.Rows[0];
            if (target != null) gridCust.CurrentCell = target.Cells[1];
            loading = false;

            lblCount.Text = string.Format("{0} / {1} 件", list.Count, D.Customers.Count);
            OnCustomerSelected(selectCase);
            UpdateStatus();
        }

        void OnCustomerSelected(int? selectCase = null)
        {
            var c = CurrentCustomer();
            bool has = c != null;
            btnEditCust.Enabled = btnDelCust.Enabled = btnAddCase.Enabled = btnCliPreview.Enabled = has;
            if (!has)
            {
                lblCustName.Text = D.Customers.Count == 0 ? "顧客が登録されていません" : "顧客を選択してください";
                lblCustInfo.Text = D.Customers.Count == 0 ? "「＋ 新規顧客」で登録するか、［ファイル］→［インポート］でデータを取り込んでください。" : "";
            }
            else
            {
                lblCustName.Text = c.Name + " 様" + (c.Company.Length > 0 ? "　／　" + c.Company : "");
                lblCustInfo.Text = string.Format("電話: {0}　　メール: {1}\r\n住所: {2}\r\n備考: {3}",
                    Dash(c.Phone), Dash(c.Email), Dash(c.Address), Dash(TextUtil.OneLine(c.Memo)));
            }
            RefreshCases(selectCase);
        }

        static string Dash(string s) { return string.IsNullOrWhiteSpace(s) ? "—" : s; }

        void RefreshCases(int? selectCase)
        {
            var c = CurrentCustomer();
            loading = true;
            gridCase.Rows.Clear();
            DataGridViewRow target = null;
            if (c != null)
            {
                // 画面では新しい順（標準出力は古い順）
                foreach (var cs in D.GetCases(c.Id).AsEnumerable().Reverse())
                {
                    var cells = new List<object> { cs.Number };
                    foreach (var f in columns) cells.Add(DisplayValue(f, cs.Get(f.ApiName)));
                    int idx = gridCase.Rows.Add(cells.ToArray());
                    var row = gridCase.Rows[idx];
                    row.Tag = cs.Id;
                    if (cs.Source == "cli") row.Cells[0].Style.ForeColor = Theme.Accent;
                    if (selectCase.HasValue && cs.Id == selectCase.Value) target = row;
                }
            }
            if (target == null && gridCase.Rows.Count > 0) target = gridCase.Rows[0];
            if (target != null) gridCase.CurrentCell = target.Cells[0];
            loading = false;
            ShowDetail();
        }

        static string DisplayValue(FieldDef f, string value)
        {
            if (f.Type == FieldTypes.Checkbox) return value == "1" ? "はい" : "";
            return TextUtil.OneLine(value);
        }

        void ShowDetail()
        {
            var cs = CurrentCase();
            bool has = cs != null;
            btnEditCase.Enabled = btnDelCase.Enabled = has;
            if (!has)
            {
                txtDetail.Text = CurrentCustomer() == null ? "" : "ケースはまだありません。「＋ ケースを追加」から登録してください。";
                return;
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ケース番号: " + cs.Number + "　　更新: " + cs.UpdatedAt.ToString("yyyy/MM/dd HH:mm"));
            sb.AppendLine();
            foreach (var f in D.SortedFields)
            {
                string v = DisplayValue(f, cs.Get(f.ApiName));
                if (f.Type == FieldTypes.TextArea)
                {
                    sb.AppendLine("■ " + f.Label);
                    sb.AppendLine(string.IsNullOrWhiteSpace(v) ? "（なし）" : cs.Get(f.ApiName).Replace("\n", "\r\n"));
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine(f.Label + ": " + (string.IsNullOrWhiteSpace(v) ? "—" : v));
                }
            }
            txtDetail.Text = sb.ToString();
        }

        void UpdateStatus()
        {
            stInfo.Text = string.Format("顧客 {0} 件　／　ケース {1} 件　／　フィールド {2} 項目",
                D.Customers.Count, D.Cases.Count, D.Fields.Count);
            stInfo.ForeColor = Theme.Fore;
            stNotice.ForeColor = Theme.Accent;
            stFile.ForeColor = Theme.SubText;
            stFile.Text = "データ: " + store.FilePath;
        }

        // ================================================================ 顧客

        void NewCustomer(object sender, EventArgs e)
        {
            Sync();
            using (var f = new CustomerForm(null, D))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                var r = f.Result;
                Customer added = null;
                if (!Commit(d => added = d.AddCustomer(r))) return;
                ClearSearchSilently();
                RefreshCustomers(added.Id);
            }
        }

        void EditCustomer(object sender, EventArgs e)
        {
            Sync();
            var c = CurrentCustomer();
            if (c == null) return;
            int id = c.Id;
            int? caseId = CurrentCaseId();
            using (var f = new CustomerForm(c, D))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                var r = f.Result;
                if (!Commit(d =>
                {
                    var x = d.GetCustomer(id);
                    if (x == null) return;
                    x.Name = r.Name; x.Company = r.Company; x.Phone = r.Phone; x.Email = r.Email;
                    x.Address = r.Address; x.Memo = r.Memo; x.UpdatedAt = DateTime.Now;
                })) return;
                RefreshCustomers(id, caseId);
            }
        }

        void DeleteCustomer(object sender, EventArgs e)
        {
            Sync();
            var c = CurrentCustomer();
            if (c == null) return;
            int id = c.Id;
            int n = D.Cases.Count(x => x.CustomerId == id);
            if (!Ui.Confirm(this, string.Format("顧客「{0}」とケース {1} 件を削除します。よろしいですか？\r\n（直前の状態はデータフォルダの {2}.bak に残ります）",
                    c.DisplayName, n, CrmStore.FileName), MessageBoxIcon.Warning)) return;
            if (!Commit(d => d.DeleteCustomer(id))) return;
            RefreshAll();
        }

        void ClearSearchSilently()
        {
            suppressSearch = true;
            txtSearch.Text = "";
            suppressSearch = false;
        }

        // ================================================================ ケース

        void AddCase(object sender, EventArgs e)
        {
            Sync();
            var c = CurrentCustomer();
            if (c == null) { Ui.Info(this, "先に顧客を選択してください。"); return; }
            int cid = c.Id;
            using (var f = new CaseForm(D, c, null))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                var values = f.Values;
                Case added = null;
                if (!Commit(d =>
                {
                    if (d.GetCustomer(cid) == null) return;
                    var cs = new Case { CustomerId = cid, Source = "manual" };
                    d.AddCase(cs);
                    foreach (var kv in values) cs.Set(kv.Key, kv.Value);
                    added = cs;
                })) return;
                if (added == null) { Ui.Warn(this, "この顧客は別の場所で削除されたため、登録できませんでした。"); RefreshAll(); return; }
                RefreshCustomers(cid, added.Id);
            }
        }

        void EditCase(object sender, EventArgs e)
        {
            Sync();
            var c = CurrentCustomer();
            var cs = CurrentCase();
            if (c == null || cs == null) return;
            int cid = c.Id, caseId = cs.Id;
            using (var f = new CaseForm(D, c, cs))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                var values = f.Values;
                if (!Commit(d =>
                {
                    var x = d.GetCase(caseId);
                    if (x == null) return;
                    foreach (var kv in values) x.Set(kv.Key, kv.Value);
                    x.UpdatedAt = DateTime.Now;
                })) return;
                RefreshCustomers(cid, caseId);
            }
        }

        void DeleteCase(object sender, EventArgs e)
        {
            Sync();
            var c = CurrentCustomer();
            var cs = CurrentCase();
            if (c == null || cs == null) return;
            int cid = c.Id, caseId = cs.Id;
            var first = D.ListFields.FirstOrDefault();
            if (!Ui.Confirm(this, string.Format("ケース {0} を削除します。よろしいですか？\r\n\r\n{1}",
                    cs.Number, first == null ? "" : TextUtil.OneLine(cs.Get(first.ApiName))), MessageBoxIcon.Warning)) return;
            if (!Commit(d => d.DeleteCase(caseId))) return;
            RefreshCustomers(cid);
        }

        void OpenFieldSettings(object sender, EventArgs e)
        {
            Sync();
            using (var f = new FieldSettingsForm(store, Commit))
            {
                f.ShowDialog(this);
                if (!f.Changed) return;
                columns = Settings.ListColumns(D);
                BuildCaseColumns();
                RefreshAll();
            }
        }

        void SearchTest(object sender, EventArgs e)
        {
            Sync();
            using (var f = new SearchTestForm(D, CurrentCustomer())) f.ShowDialog(this);
        }

        void CliPreview(object sender, EventArgs e)
        {
            Sync();
            var c = CurrentCustomer();
            if (c == null) { Ui.Info(this, "先に顧客を選択してください。"); return; }
            var same = D.FindByName(c.Name, false, null);
            int count;
            string csv = Cli.BuildHistoryCsv(D, same, false, true, out count);
            string arg = c.Name.Contains(" ") || c.Name.Contains("　") ? "\"" + c.Name + "\"" : c.Name;
            string intro = "コマンド:  " + Cli.ExeName + " " + arg + "\r\n" +
                           "この顧客名で exe を実行したときに標準出力される内容です（フィールド設定で「一覧・標準出力に含める」にした項目）。" +
                           (same.Count > 1 ? "\r\n※ 同名の顧客が " + same.Count + " 件あるため全員分が出力されます。--company 会社名 で絞り込めます。" : "");
            using (var f = new TextViewerForm("連携CSV出力プレビュー - " + c.Name, intro, count == 0 ? Cli.NotFoundMessage + "\r\n" : csv))
                f.ShowDialog(this);
        }

        // ================================================================ インポート / エクスポート / データ

        void ImportData(object sender, EventArgs e)
        {
            Sync();
            using (var f = new ImportForm(store, CurrentCustomer()))
                if (f.ShowDialog(this) == DialogResult.OK) RefreshAll();
        }

        void LoadSample(bool confirm)
        {
            if (confirm && !Ui.Confirm(this, "デモ用のサンプルデータを投入します。\r\n（登録済みのデータは残ります。同じ内容のケースは重複して登録されません）"))
                return;
            try
            {
                if (store.ChangedOnDisk) store.Load();
                var work = D.Clone();
                var res = Importer.Apply(work, Importer.Parse(Samples.Csv(), work).Rows, null);
                store.ReplaceData(work);
                RefreshAll();
                Ui.Info(this, "サンプルデータを投入しました。\r\n\r\n" + res.Summary.Replace(" / ", "\r\n"));
            }
            catch (Exception ex) { Ui.Warn(this, "投入に失敗しました。\r\n" + ex.Message); }
        }

        void ExportTo(string defaultName, string content)
        {
            using (var d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = defaultName })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Csv.WriteExcelCsv(d.FileName, content);
                    Ui.Info(this, "保存しました。\r\n" + d.FileName + "\r\n\r\n（Excelでそのまま開けるBOM付きUTF-8。インポートにもそのまま使えます）");
                }
                catch (Exception ex) { Ui.Warn(this, "保存に失敗しました。\r\n" + ex.Message); }
            }
        }

        void ExportCustomers(object sender, EventArgs e)
        {
            Sync();
            ExportTo("顧客一覧_" + DateTime.Today.ToString("yyyyMMdd") + ".csv", Cli.BuildCustomerCsv(D));
        }

        void ExportAllCases(object sender, EventArgs e)
        {
            Sync();
            int n;
            ExportTo("ケース一覧_" + DateTime.Today.ToString("yyyyMMdd") + ".csv", Cli.BuildHistoryCsv(D, D.Customers, true, true, out n));
        }

        void ExportCustomerCases(object sender, EventArgs e)
        {
            Sync();
            var c = CurrentCustomer();
            if (c == null) { Ui.Info(this, "先に顧客を選択してください。"); return; }
            int n;
            ExportTo("ケース_" + c.Name.Replace(" ", "").Replace("　", "") + "_" + DateTime.Today.ToString("yyyyMMdd") + ".csv",
                Cli.BuildHistoryCsv(D, new[] { c }, true, true, out n));
        }

        void Backup(object sender, EventArgs e)
        {
            try { Ui.Info(this, "バックアップを作成しました。\r\n" + store.CreateBackup()); }
            catch (Exception ex) { Ui.Warn(this, "バックアップに失敗しました。\r\n" + ex.Message); }
        }

        void OpenDataFolder(object sender, EventArgs e)
        {
            try
            {
                if (File.Exists(store.FilePath)) Process.Start("explorer.exe", "/select,\"" + store.FilePath + "\"");
                else Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(store.FilePath) + "\"");
            }
            catch (Exception ex) { Ui.Warn(this, ex.Message); }
        }

        void ReloadData(object sender, EventArgs e)
        {
            try
            {
                store.Load();
                columns = Settings.ListColumns(D);
                BuildCaseColumns();
                RefreshAll();
                stNotice.Text = DateTime.Now.ToString("HH:mm:ss") + "  再読み込みしました";
            }
            catch (Exception ex) { Ui.Warn(this, "再読み込みに失敗しました。\r\n" + ex.Message); }
        }

        void ClearAll(object sender, EventArgs e)
        {
            if (!Ui.Confirm(this, "すべての顧客とケースを削除します（フィールド定義は残ります）。\r\n削除前に backup フォルダへ自動でバックアップを作成します。\r\n\r\nよろしいですか？", MessageBoxIcon.Warning))
                return;
            try
            {
                string bk = store.CreateBackup();
                if (!Commit(d => { d.Customers.Clear(); d.Cases.Clear(); d.NextCustomerId = 1; d.NextCaseId = 1; })) return;
                RefreshAll();
                Ui.Info(this, "顧客とケースを削除しました。\r\nバックアップ: " + bk);
            }
            catch (Exception ex) { Ui.Warn(this, "削除に失敗しました。\r\n" + ex.Message); }
        }
    }
}
