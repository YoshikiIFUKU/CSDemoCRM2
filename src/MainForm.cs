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
        DataGridView gridCust, gridHist, gridFollow;
        CheckBox chkShowDone;
        Panel pageCust, pageFollow;
        Button tabBtnCust, tabBtnFollow;
        int currentTab;
        SplitContainer splitMain, splitRight;
        Button btnEditCust, btnDelCust, btnAddHist, btnEditHist, btnDelHist, btnToggleDone, btnCliPreview;
        ToolStripStatusLabel stInfo, stNotice, stFile, stTheme;
        ToolStripMenuItem miLight, miDark, miSystem;
        readonly Timer syncTimer = new Timer { Interval = 1500 };
        bool loading, suppressSearch;

        public MainForm(CrmStore store)
        {
            this.store = store;
            Text = "CRM Demo - 顧客対応履歴管理";
            Font = Ui.BaseFont;
            Size = new Size(Ui.S(1320), Ui.S(820));
            MinimumSize = new Size(Ui.S(960), Ui.S(600));
            StartPosition = FormStartPosition.CenterScreen;
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
                    splitMain.SplitterDistance = Ui.S(580);
                    splitRight.SplitterDistance = Math.Max(Ui.S(150), splitRight.Height - Ui.S(230));
                }
                catch { }
                SelectTab(0);
                RefreshAll();
                UpdateThemeChecks();
                syncTimer.Start();
            };
            Shown += delegate
            {
                if (D.Customers.Count == 0 &&
                    Ui.Confirm(this, "データがまだありません。\r\nデモ用のサンプルデータ（顧客8件・対応履歴18件）を投入しますか？\r\n\r\n※ あとから［データ］メニューでも投入できます。"))
                    LoadSample(false);
            };
        }

        // ================================================================ 画面構築

        void BuildUi()
        {
            var menu = BuildMenu();

            var status = new StatusStrip();
            stInfo = new ToolStripStatusLabel();
            stNotice = new ToolStripStatusLabel { Margin = new Padding(Ui.S(16), 0, 0, 0) };
            stFile = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight };
            stTheme = new ToolStripStatusLabel { Margin = new Padding(Ui.S(12), 0, Ui.S(4), 0), ToolTipText = "クリックでライト / ダークを切り替え（Ctrl+Shift+L）" };
            stTheme.Click += delegate { ToggleTheme(); };
            // 入りきらない項目は右から隠れるため、テーマ切り替えを左端に置く
            status.Items.AddRange(new ToolStripItem[] { stTheme, stInfo, stNotice, stFile });

            // タブ（標準の TabControl はダークモードで配色を変えられないため、ボタンで作る）
            var tabBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                Padding = new Padding(Ui.S(8), Ui.S(6), Ui.S(8), Ui.S(6)), Tag = "tabbar"
            };
            tabBtnCust = TabButton("顧客・対応履歴", 0);
            tabBtnFollow = TabButton("フォローアップ", 1);
            tabBar.Controls.Add(tabBtnCust);
            tabBar.Controls.Add(tabBtnFollow);

            pageCust = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(8)) };
            pageFollow = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(8)), Visible = false };
            BuildCustomerTab();
            BuildFollowTab();

            Controls.Add(pageCust);
            Controls.Add(pageFollow);
            Controls.Add(tabBar);
            Controls.Add(status);
            Controls.Add(menu);
            MainMenuStrip = menu;
        }

        Button TabButton(string text, int index)
        {
            var b = Ui.Btn(text, delegate { SelectTab(index); });
            b.Font = Ui.BoldFont;
            b.MinimumSize = new Size(Ui.S(150), Ui.S(32));
            b.Margin = new Padding(0, 0, Ui.S(6), 0);
            b.Tag = "tab";
            return b;
        }

        void SelectTab(int index)
        {
            currentTab = index;
            pageCust.Visible = index == 0;
            pageFollow.Visible = index == 1;
            tabBtnCust.Tag = index == 0 ? "tab-active" : "tab";
            tabBtnFollow.Tag = index == 1 ? "tab-active" : "tab";
            Theme.StyleButton(tabBtnCust);
            Theme.StyleButton(tabBtnFollow);
            if (index == 1) RefreshFollow();
        }

        MenuStrip BuildMenu()
        {
            var m = new MenuStrip { Font = Ui.BaseFont };
            var file = new ToolStripMenuItem("ファイル(&F)");
            file.DropDownItems.Add(Item("CSV / テキストのインポート...", Keys.Control | Keys.I, ImportData));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("顧客一覧をCSVエクスポート...", Keys.None, ExportCustomers));
            file.DropDownItems.Add(Item("全対応履歴をCSVエクスポート...", Keys.Control | Keys.E, ExportAllHistory));
            file.DropDownItems.Add(Item("選択中の顧客の履歴をCSVエクスポート...", Keys.None, ExportCustomerHistory));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("バックアップを作成", Keys.None, Backup));
            file.DropDownItems.Add(Item("データフォルダを開く", Keys.None, OpenDataFolder));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("終了", Keys.Alt | Keys.F4, delegate { Close(); }));

            var edit = new ToolStripMenuItem("編集(&E)");
            edit.DropDownItems.Add(Item("検索", Keys.Control | Keys.F, delegate { SelectTab(0); txtSearch.Focus(); txtSearch.SelectAll(); }));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("新規顧客...", Keys.Control | Keys.N, NewCustomer));
            edit.DropDownItems.Add(Item("顧客を編集...", Keys.None, EditCustomer));
            edit.DropDownItems.Add(Item("顧客を削除", Keys.None, DeleteCustomer));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("対応を追加...", Keys.Control | Keys.T, AddInteraction));
            edit.DropDownItems.Add(Item("連携CSV出力プレビュー...", Keys.Control | Keys.P, CliPreview));
            edit.DropDownItems.Add(Item("連携検索テスト（顧客名・会社名・電話番号）...", Keys.Control | Keys.Shift | Keys.P, SearchTest));

            var view = new ToolStripMenuItem("表示(&V)");
            miLight = Item("ライトモード", Keys.None, delegate { Theme.Set(ThemeMode.Light); });
            miDark = Item("ダークモード", Keys.None, delegate { Theme.Set(ThemeMode.Dark); });
            miSystem = Item("Windows の設定に合わせる", Keys.None, delegate { Theme.Set(ThemeMode.System); });
            view.DropDownItems.Add(miLight);
            view.DropDownItems.Add(miDark);
            view.DropDownItems.Add(miSystem);
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(Item("ライト / ダークを切り替え", Keys.Control | Keys.Shift | Keys.L, delegate { ToggleTheme(); }));

            var data = new ToolStripMenuItem("データ(&D)");
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
                Ui.Info(this, "CRM Demo 1.1\r\n顧客対応履歴管理（デモ用）\r\n\r\n動作環境: Windows 10 / 11（追加インストール不要）\r\nデータ: " + store.FilePath);
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

        void BuildCustomerTab()
        {
            splitMain = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = Ui.S(6) };

            // --- 左: 顧客一覧
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
                Text = "顧客名・会社名・電話番号・メール・対応履歴の内容で絞り込めます", Dock = DockStyle.Top,
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
            Ui.Col(gridCust, "件数", 34, 48, typeof(int));
            Ui.Col(gridCust, "最終対応日", 80, 96);
            Ui.Col(gridCust, "要フォロー", 56, 84, typeof(int));
            gridCust.SelectionChanged += delegate { if (!loading) OnCustomerSelected(); };
            gridCust.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditCustomer(null, null); };
            gridCust.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) DeleteCustomer(null, null); };

            splitMain.Panel1.Controls.Add(gridCust);
            splitMain.Panel1.Controls.Add(custBtns);
            splitMain.Panel1.Controls.Add(hint);
            splitMain.Panel1.Controls.Add(search);

            // --- 右: 顧客情報 + 対応履歴
            var custPanel = new Panel { Dock = DockStyle.Top, Height = Ui.S(112), Padding = new Padding(Ui.S(12), Ui.S(8), Ui.S(12), Ui.S(6)), Tag = "panel" };
            lblCustName = new Label { Dock = DockStyle.Top, Height = Ui.S(30), Font = Ui.TitleFont };
            lblCustInfo = new Label { Dock = DockStyle.Fill, Tag = "sub" };
            custPanel.Controls.Add(lblCustInfo);
            custPanel.Controls.Add(lblCustName);

            var histBtns = Ui.Flow();
            histBtns.Padding = new Padding(0, Ui.S(8), 0, Ui.S(4));
            var histTitle = Ui.FlowLabel("対応履歴");
            histTitle.Font = Ui.BoldFont;
            histBtns.Controls.Add(histTitle);
            histBtns.Controls.Add(btnAddHist = Ui.Btn("＋ 対応を追加", AddInteraction, true));
            histBtns.Controls.Add(btnEditHist = Ui.Btn("編集", EditInteraction));
            histBtns.Controls.Add(btnDelHist = Ui.Btn("削除", DeleteInteraction));
            histBtns.Controls.Add(btnToggleDone = Ui.Btn("完了 / 未完了", ToggleDone));
            histBtns.Controls.Add(btnCliPreview = Ui.Btn("連携CSV出力プレビュー", CliPreview));

            splitRight = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2, SplitterWidth = Ui.S(6) };
            gridHist = Ui.Grid();
            Ui.Col(gridHist, "対応日", 62, 92);
            Ui.Col(gridHist, "問い合わせ内容", 150, 100);
            Ui.Col(gridHist, "対応内容", 150, 100);
            Ui.Col(gridHist, "次回確認内容", 120, 90);
            Ui.Col(gridHist, "次回確認日", 62, 96);
            Ui.Col(gridHist, "担当者", 46, 64);
            Ui.Col(gridHist, "状態", 54, 84);
            gridHist.SelectionChanged += delegate { if (!loading) ShowDetail(); };
            gridHist.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditInteraction(null, null); };
            gridHist.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) DeleteInteraction(null, null); };
            splitRight.Panel1.Controls.Add(gridHist);

            txtDetail = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                Font = new Font("Yu Gothic UI", 10.5f)
            };
            var detailTitle = new Label { Text = "選択中の対応の詳細", Dock = DockStyle.Top, Height = Ui.S(22), Font = Ui.BoldFont };
            splitRight.Panel2.Controls.Add(txtDetail);
            splitRight.Panel2.Controls.Add(detailTitle);

            splitMain.Panel2.Controls.Add(splitRight);
            splitMain.Panel2.Controls.Add(histBtns);
            splitMain.Panel2.Controls.Add(custPanel);
            pageCust.Controls.Add(splitMain);
        }

        void BuildFollowTab()
        {
            var top = Ui.Flow();
            top.Controls.Add(Ui.FlowLabel("未完了の「次回確認」を期限順に表示します。赤＝期限切れ／黄＝3日以内。ダブルクリックで顧客の履歴へ移動します。", "sub"));
            chkShowDone = new CheckBox { Text = "完了済みも表示", AutoSize = true, Margin = new Padding(Ui.S(8), Ui.S(8), Ui.S(12), 0) };
            chkShowDone.CheckedChanged += delegate { RefreshFollow(); };
            top.Controls.Add(chkShowDone);
            top.Controls.Add(Ui.Btn("完了 / 未完了", delegate
            {
                var it = CurrentFollow();
                if (it == null) return;
                int id = it.Id;
                if (Commit(d => { var x = d.GetInteraction(id); if (x != null) { x.Done = !x.Done; x.UpdatedAt = DateTime.Now; } }))
                {
                    RefreshFollow();
                    RefreshCustomers(SelectedCustomerId());
                }
            }));
            top.Controls.Add(Ui.Btn("顧客の履歴を開く", delegate { JumpToInteraction(CurrentFollow()); }));

            gridFollow = Ui.Grid();
            Ui.Col(gridFollow, "次回確認日", 60, 96);
            Ui.Col(gridFollow, "残り日数", 40, 76);
            Ui.Col(gridFollow, "顧客名", 70, 70);
            Ui.Col(gridFollow, "会社名", 110, 80);
            Ui.Col(gridFollow, "電話番号", 70, 70);
            Ui.Col(gridFollow, "次回確認内容", 170, 120);
            Ui.Col(gridFollow, "担当者", 45, 64);
            Ui.Col(gridFollow, "元の対応日", 60, 96);
            Ui.Col(gridFollow, "状態", 40, 64);
            gridFollow.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) JumpToInteraction(CurrentFollow()); };

            pageFollow.Controls.Add(gridFollow);
            pageFollow.Controls.Add(top);
        }

        // ================================================================ テーマ

        void ToggleTheme()
        {
            Theme.Set(Theme.IsDark ? ThemeMode.Light : ThemeMode.Dark);
        }

        void OnThemeChanged(object sender, EventArgs e)
        {
            Theme.Apply(this);
            SelectTab(currentTab);
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

        // ================================================================ 別プロセスからの更新の反映・保存

        /// <summary>データファイルが別プロセスで更新されていたら読み直す。新しい対応履歴があればその顧客と履歴を選択する。</summary>
        void SyncFromDisk(bool showNew)
        {
            if (!store.ChangedOnDisk || Application.OpenForms.Count > 1) return; // ダイアログ表示中は閉じてから反映
            int prevMax = D.Interactions.Count > 0 ? D.Interactions.Max(i => i.Id) : 0;
            int prevCustomers = D.Customers.Count;
            int? cust = SelectedCustomerId();
            var cur = CurrentInteraction();
            int? curId = cur == null ? (int?)null : cur.Id;
            try { store.Load(); }
            catch { return; } // 書き込み中などで読めないときは次回に再試行

            var added = D.Interactions.Where(i => i.Id > prevMax).OrderBy(i => i.Id).ToList();
            if (showNew && added.Count > 0)
            {
                var last = added[added.Count - 1];
                ClearSearchSilently();
                if (currentTab != 0) SelectTab(0);
                RefreshCustomers(last.CustomerId, last.Id);
                RefreshFollow();
                var c = D.GetCustomer(last.CustomerId);
                int newCust = D.Customers.Count - prevCustomers;
                stNotice.Text = string.Format("{0}  外部から対応履歴が登録されました：{1}（{2}件{3}）",
                    DateTime.Now.ToString("HH:mm:ss"), c == null ? "" : c.DisplayName, added.Count,
                    newCust > 0 ? "・新規顧客 " + newCust + " 件" : "");
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            }
            else
            {
                RefreshCustomers(cust, curId);
                RefreshFollow();
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
            RefreshCustomers(SelectedCustomerId(), CurrentInteraction() == null ? (int?)null : CurrentInteraction().Id);
            RefreshFollow();
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

        Interaction CurrentInteraction()
        {
            if (gridHist == null || gridHist.CurrentRow == null) return null;
            var id = gridHist.CurrentRow.Tag as int?;
            return id.HasValue ? D.GetInteraction(id.Value) : null;
        }

        Interaction CurrentFollow()
        {
            if (gridFollow.CurrentRow == null) return null;
            var id = gridFollow.CurrentRow.Tag as int?;
            return id.HasValue ? D.GetInteraction(id.Value) : null;
        }

        static string Plain(string s) { return TextUtil.Norm(s).Replace("-", ""); }

        bool Matches(Customer c, string q, ILookup<int, Interaction> hist)
        {
            if (Plain(c.Name + c.Company + c.Phone + c.Email + c.Address + c.Memo).Contains(q)) return true;
            return hist[c.Id].Any(i => Plain(i.Inquiry + i.Response + i.NextAction + i.Staff).Contains(q));
        }

        void RefreshCustomers(int? selectCustomer = null, int? selectInteraction = null)
        {
            string q = Plain(txtSearch.Text);
            var hist = D.Interactions.ToLookup(i => i.CustomerId);
            var list = D.Customers.Where(c => q.Length == 0 || Matches(c, q, hist)).OrderBy(c => c.Id).ToList();

            loading = true;
            gridCust.Rows.Clear();
            DataGridViewRow target = null;
            foreach (var c in list)
            {
                var h = hist[c.Id].ToList();
                int open = h.Count(i => i.NeedsFollowUp);
                int idx = gridCust.Rows.Add(c.Id, c.Name, c.Company, c.Phone, h.Count,
                    h.Count > 0 ? TextUtil.Date(h.Max(i => i.Date)) : "", open);
                var row = gridCust.Rows[idx];
                row.Tag = c.Id;
                if (open == 0) row.Cells[6].Style.ForeColor = Theme.Muted;
                else if (h.Any(i => i.NeedsFollowUp && i.NextDate.HasValue && i.NextDate.Value < DateTime.Today))
                {
                    row.Cells[6].Style.ForeColor = Theme.Danger;
                    row.Cells[6].Style.Font = Ui.BoldFont;
                }
                if (selectCustomer.HasValue && c.Id == selectCustomer.Value) target = row;
            }
            if (target == null && gridCust.Rows.Count > 0) target = gridCust.Rows[0];
            if (target != null) gridCust.CurrentCell = target.Cells[1];
            loading = false;

            lblCount.Text = string.Format("{0} / {1} 件", list.Count, D.Customers.Count);
            OnCustomerSelected(selectInteraction);
            UpdateStatus();
        }

        void OnCustomerSelected(int? selectInteraction = null)
        {
            var c = CurrentCustomer();
            bool has = c != null;
            btnEditCust.Enabled = btnDelCust.Enabled = btnAddHist.Enabled = btnCliPreview.Enabled = has;
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
            RefreshHistory(selectInteraction);
        }

        static string Dash(string s) { return string.IsNullOrWhiteSpace(s) ? "—" : s; }

        void RefreshHistory(int? selectInteraction)
        {
            var c = CurrentCustomer();
            loading = true;
            gridHist.Rows.Clear();
            DataGridViewRow target = null;
            if (c != null)
            {
                // 画面では新しい順（標準出力は古い順）
                foreach (var i in D.GetHistory(c.Id).AsEnumerable().Reverse())
                {
                    int idx = gridHist.Rows.Add(TextUtil.Date(i.Date), TextUtil.OneLine(i.Inquiry), TextUtil.OneLine(i.Response),
                        TextUtil.OneLine(i.NextAction), TextUtil.Date(i.NextDate), i.Staff, StatusText(i));
                    var row = gridHist.Rows[idx];
                    row.Tag = i.Id;
                    StyleStatus(row, i, 6, 4);
                    if (selectInteraction.HasValue && i.Id == selectInteraction.Value) target = row;
                }
            }
            if (target == null && gridHist.Rows.Count > 0) target = gridHist.Rows[0];
            if (target != null) gridHist.CurrentCell = target.Cells[0];
            loading = false;
            ShowDetail();
        }

        static string StatusText(Interaction i)
        {
            if (i.Done) return "完了";
            if (!i.NeedsFollowUp) return "—";
            if (i.NextDate.HasValue && i.NextDate.Value < DateTime.Today) return "期限切れ";
            return "要フォロー";
        }

        static void StyleStatus(DataGridViewRow row, Interaction i, int statusCol, int dateCol)
        {
            if (i.Done) { row.Cells[statusCol].Style.ForeColor = Theme.Success; return; }
            if (!i.NeedsFollowUp) { row.Cells[statusCol].Style.ForeColor = Theme.Muted; return; }
            if (i.NextDate.HasValue && i.NextDate.Value < DateTime.Today)
            {
                row.Cells[statusCol].Style.ForeColor = row.Cells[dateCol].Style.ForeColor = Theme.Danger;
                row.Cells[statusCol].Style.Font = row.Cells[dateCol].Style.Font = Ui.BoldFont;
            }
            else row.Cells[statusCol].Style.ForeColor = Theme.Warn;
        }

        void ShowDetail()
        {
            var i = CurrentInteraction();
            bool has = i != null;
            btnEditHist.Enabled = btnDelHist.Enabled = btnToggleDone.Enabled = has;
            if (!has)
            {
                txtDetail.Text = CurrentCustomer() == null ? "" : "対応履歴はまだありません。「＋ 対応を追加」から登録してください。";
                return;
            }
            Func<string, string> body = s => string.IsNullOrWhiteSpace(s) ? "（なし）" : s.Replace("\n", "\r\n");
            txtDetail.Text = string.Format(
                "対応日: {0}　　担当者: {1}　　状態: {2}\r\n\r\n■ 問い合わせ内容\r\n{3}\r\n\r\n■ 対応内容\r\n{4}\r\n\r\n■ 次回確認内容{5}\r\n{6}",
                i.Date.ToString("yyyy/MM/dd (ddd)"), Dash(i.Staff), StatusText(i), body(i.Inquiry), body(i.Response),
                i.NextDate.HasValue ? "（次回確認日: " + i.NextDate.Value.ToString("yyyy/MM/dd (ddd)") + "）" : "", body(i.NextAction));
        }

        void RefreshFollow()
        {
            var keep = CurrentFollow();
            loading = true;
            gridFollow.Rows.Clear();
            var items = D.Interactions
                .Where(i => chkShowDone.Checked ? (i.NextDate.HasValue || !string.IsNullOrWhiteSpace(i.NextAction)) : i.NeedsFollowUp)
                .OrderBy(i => i.NextDate.HasValue ? 0 : 1).ThenBy(i => i.NextDate ?? DateTime.MaxValue).ThenBy(i => i.Date);
            DataGridViewRow target = null;
            foreach (var i in items)
            {
                var c = D.GetCustomer(i.CustomerId);
                if (c == null) continue;
                string days = "";
                if (i.NextDate.HasValue)
                {
                    int d = (i.NextDate.Value - DateTime.Today).Days;
                    days = d == 0 ? "今日" : (d < 0 ? (-d) + "日超過" : "あと" + d + "日");
                }
                int idx = gridFollow.Rows.Add(i.NextDate.HasValue ? TextUtil.Date(i.NextDate) : "（未設定）", days, c.Name, c.Company,
                    c.Phone, TextUtil.OneLine(i.NextAction), i.Staff, TextUtil.Date(i.Date), i.Done ? "完了" : "未完了");
                var row = gridFollow.Rows[idx];
                row.Tag = i.Id;
                if (i.Done) row.DefaultCellStyle.ForeColor = Theme.Muted;
                else if (i.NextDate.HasValue && i.NextDate.Value < DateTime.Today)
                {
                    row.DefaultCellStyle.ForeColor = Theme.Danger;
                    row.Cells[0].Style.Font = row.Cells[1].Style.Font = Ui.BoldFont;
                }
                else if (i.NextDate.HasValue && i.NextDate.Value <= DateTime.Today.AddDays(3))
                    row.DefaultCellStyle.BackColor = Theme.WarnBack;
                if (keep != null && keep.Id == i.Id) target = row;
            }
            if (target != null) gridFollow.CurrentCell = target.Cells[0];
            loading = false;
            int open = D.Interactions.Count(i => i.NeedsFollowUp);
            tabBtnFollow.Text = open > 0 ? "フォローアップ（" + open + "）" : "フォローアップ";
            UpdateStatus();
        }

        void UpdateStatus()
        {
            int overdue = D.Interactions.Count(i => i.NeedsFollowUp && i.NextDate.HasValue && i.NextDate.Value < DateTime.Today);
            stInfo.Text = string.Format("顧客 {0} 件　／　対応履歴 {1} 件　／　期限切れのフォロー {2} 件",
                D.Customers.Count, D.Interactions.Count, overdue);
            stInfo.ForeColor = overdue > 0 ? Theme.Danger : Theme.Fore;
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
                SelectTab(0);
                RefreshCustomers(added.Id);
                RefreshFollow();
            }
        }

        void EditCustomer(object sender, EventArgs e)
        {
            Sync();
            var c = CurrentCustomer();
            if (c == null) return;
            int id = c.Id;
            var cur = CurrentInteraction();
            int? curId = cur == null ? (int?)null : cur.Id;
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
                RefreshCustomers(id, curId);
                RefreshFollow();
            }
        }

        void DeleteCustomer(object sender, EventArgs e)
        {
            Sync();
            var c = CurrentCustomer();
            if (c == null) return;
            int id = c.Id;
            int n = D.Interactions.Count(i => i.CustomerId == id);
            if (!Ui.Confirm(this, string.Format("顧客「{0}」と対応履歴 {1} 件を削除します。よろしいですか？\r\n（直前の状態はデータフォルダの {2}.bak に残ります）",
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

        // ================================================================ 対応履歴

        string LastStaff()
        {
            var last = D.Interactions.OrderByDescending(i => i.CreatedAt).FirstOrDefault(i => i.Staff.Length > 0);
            return last == null ? "" : last.Staff;
        }

        void AddInteraction(object sender, EventArgs e)
        {
            Sync();
            var c = CurrentCustomer();
            if (c == null) { Ui.Info(this, "先に顧客を選択してください。"); return; }
            int cid = c.Id;
            using (var f = new InteractionForm(null, c, LastStaff()))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                var r = f.Result;
                Interaction added = null;
                if (!Commit(d => { if (d.GetCustomer(cid) == null) return; r.CustomerId = cid; added = d.AddInteraction(r); })) return;
                if (added == null) { Ui.Warn(this, "この顧客は別の場所で削除されたため、登録できませんでした。"); RefreshAll(); return; }
                RefreshCustomers(cid, added.Id);
                RefreshFollow();
            }
        }

        void EditInteraction(object sender, EventArgs e)
        {
            Sync();
            var c = CurrentCustomer();
            var it = CurrentInteraction();
            if (c == null || it == null) return;
            int cid = c.Id, iid = it.Id;
            using (var f = new InteractionForm(it, c, null))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                var r = f.Result;
                if (!Commit(d =>
                {
                    var x = d.GetInteraction(iid);
                    if (x == null) return;
                    x.Date = r.Date; x.Staff = r.Staff; x.Inquiry = r.Inquiry; x.Response = r.Response;
                    x.NextAction = r.NextAction; x.NextDate = r.NextDate; x.Done = r.Done; x.UpdatedAt = DateTime.Now;
                })) return;
                RefreshCustomers(cid, iid);
                RefreshFollow();
            }
        }

        void DeleteInteraction(object sender, EventArgs e)
        {
            Sync();
            var c = CurrentCustomer();
            var it = CurrentInteraction();
            if (c == null || it == null) return;
            int cid = c.Id, iid = it.Id;
            if (!Ui.Confirm(this, string.Format("{0} の対応履歴を削除します。よろしいですか？\r\n\r\n{1}",
                    TextUtil.Date(it.Date), TextUtil.OneLine(it.Inquiry)), MessageBoxIcon.Warning)) return;
            if (!Commit(d => d.DeleteInteraction(iid))) return;
            RefreshCustomers(cid);
            RefreshFollow();
        }

        void ToggleDone(object sender, EventArgs e)
        {
            Sync();
            var c = CurrentCustomer();
            var it = CurrentInteraction();
            if (c == null || it == null) return;
            int cid = c.Id, iid = it.Id;
            if (!Commit(d => { var x = d.GetInteraction(iid); if (x != null) { x.Done = !x.Done; x.UpdatedAt = DateTime.Now; } })) return;
            RefreshCustomers(cid, iid);
            RefreshFollow();
        }

        void JumpToInteraction(Interaction it)
        {
            if (it == null) return;
            SelectTab(0);
            ClearSearchSilently();
            RefreshCustomers(it.CustomerId, it.Id);
            gridHist.Focus();
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
                           "この顧客名で exe を実行したときに標準出力される内容です（対応日の古い順）。" +
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
            if (confirm && !Ui.Confirm(this, "デモ用のサンプルデータを投入します。\r\n（登録済みのデータは残ります。同じ内容の履歴は重複して登録されません）"))
                return;
            try
            {
                if (store.ChangedOnDisk) store.Load();
                var work = D.Clone();
                var res = Importer.Apply(work, Importer.Parse(Samples.Csv()).Rows, null);
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

        void ExportAllHistory(object sender, EventArgs e)
        {
            Sync();
            int n;
            ExportTo("対応履歴_" + DateTime.Today.ToString("yyyyMMdd") + ".csv", Cli.BuildHistoryCsv(D, D.Customers, true, true, out n));
        }

        void ExportCustomerHistory(object sender, EventArgs e)
        {
            Sync();
            var c = CurrentCustomer();
            if (c == null) { Ui.Info(this, "先に顧客を選択してください。"); return; }
            int n;
            ExportTo("対応履歴_" + c.Name.Replace(" ", "").Replace("　", "") + "_" + DateTime.Today.ToString("yyyyMMdd") + ".csv",
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
            try { store.Load(); RefreshAll(); stNotice.Text = DateTime.Now.ToString("HH:mm:ss") + "  再読み込みしました"; }
            catch (Exception ex) { Ui.Warn(this, "再読み込みに失敗しました。\r\n" + ex.Message); }
        }

        void ClearAll(object sender, EventArgs e)
        {
            if (!Ui.Confirm(this, "すべての顧客と対応履歴を削除します。\r\n削除前に backup フォルダへ自動でバックアップを作成します。\r\n\r\nよろしいですか？", MessageBoxIcon.Warning))
                return;
            try
            {
                string bk = store.CreateBackup();
                store.ReplaceData(new CrmData());
                RefreshAll();
                Ui.Info(this, "全データを削除しました。\r\nバックアップ: " + bk);
            }
            catch (Exception ex) { Ui.Warn(this, "削除に失敗しました。\r\n" + ex.Message); }
        }
    }
}
