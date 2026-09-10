using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CrmDemo
{
    /// <summary>画面部品の共通処理（高DPIでも崩れないよう、ピクセル値は S() で拡大する。色は Theme が決める）</summary>
    public static class Ui
    {
        static float scale = -1;
        public static float Scale
        {
            get
            {
                if (scale < 0) using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
                return scale;
            }
        }
        public static int S(int px) { return (int)Math.Round(px * Scale); }

        public static readonly Font BaseFont = new Font("Yu Gothic UI", 9.5f);
        public static readonly Font BoldFont = new Font("Yu Gothic UI", 9.5f, FontStyle.Bold);
        public static readonly Font TitleFont = new Font("Yu Gothic UI", 13f, FontStyle.Bold);

        public static DataGridView Grid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.FixedSingle, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = S(30), StandardTab = true, ShowCellToolTips = true
            };
            g.ColumnHeadersDefaultCellStyle.Font = BoldFont;
            g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            g.DefaultCellStyle.Padding = new Padding(S(3), 0, S(3), 0);
            g.RowTemplate.Height = S(28);
            typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(g, true, null);
            Theme.StyleGrid(g);
            return g;
        }

        public static DataGridViewTextBoxColumn Col(DataGridView g, string header, float weight, int minWidth, Type valueType = null)
        {
            var c = new DataGridViewTextBoxColumn
            {
                HeaderText = header, FillWeight = weight, MinimumWidth = S(minWidth),
                SortMode = DataGridViewColumnSortMode.Automatic
            };
            if (valueType != null) c.ValueType = valueType;
            g.Columns.Add(c);
            return c;
        }

        public static Button Btn(string text, EventHandler onClick, bool primary = false)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, MinimumSize = new Size(S(76), S(30)),
                Padding = new Padding(S(6), 0, S(6), 0), Margin = new Padding(0, S(3), S(6), S(3)),
                Tag = primary ? "primary" : null
            };
            Theme.StyleButton(b);
            b.EnabledChanged += delegate { Theme.StyleButton(b); };
            if (onClick != null) b.Click += onClick;
            return b;
        }

        public static FlowLayoutPanel Flow(DockStyle dock = DockStyle.Top)
        {
            return new FlowLayoutPanel
            {
                Dock = dock, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
                Padding = new Padding(0, S(2), 0, S(4))
            };
        }

        public static Label FlowLabel(string text, string role = null)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(0, S(9), S(6), 0), Tag = role };
        }

        public static void SetupDialog(Form f, string title, int w, int h)
        {
            f.Text = title;
            f.Font = BaseFont;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ShowInTaskbar = false;
            f.MinimizeBox = false;
            f.ClientSize = new Size(S(w), S(h));
            f.MinimumSize = new Size(S(w * 3 / 4), S(h * 3 / 4));
            f.Padding = new Padding(S(12));
            Theme.Attach(f);
        }

        public static TableLayoutPanel FormTable()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return t;
        }

        public static void AddRow(TableLayoutPanel t, string label, Control c, float percent = 0)
        {
            int row = t.RowCount++;
            t.RowStyles.Add(percent > 0 ? new RowStyle(SizeType.Percent, percent) : new RowStyle(SizeType.AutoSize));
            var lb = new Label
            {
                Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, S(7), S(12), 0)
            };
            c.Margin = new Padding(0, S(3), 0, S(3));
            if (percent > 0) c.Dock = DockStyle.Fill;
            else if (c is TextBox) c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            t.Controls.Add(lb, 0, row);
            t.Controls.Add(c, 1, row);
        }

        public static TextBox AddText(TableLayoutPanel t, string label, string value, float percent = 0)
        {
            var tb = new TextBox { Text = value ?? "" };
            if (percent > 0)
            {
                tb.Multiline = true; tb.AcceptsReturn = true; tb.ScrollBars = ScrollBars.Vertical;
                tb.Text = (value ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            }
            AddRow(t, label, tb, percent);
            return tb;
        }

        /// <summary>右下の OK / キャンセル ボタン行</summary>
        public static FlowLayoutPanel OkCancel(Form f, string okText, EventHandler onOk)
        {
            var p = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, S(8), 0, 0)
            };
            var cancel = Btn("キャンセル", null);
            cancel.DialogResult = DialogResult.Cancel;
            var ok = Btn(okText, onOk, true);
            p.Controls.Add(cancel);
            p.Controls.Add(ok);
            f.CancelButton = cancel;
            return p;
        }

        public static void Warn(IWin32Window owner, string msg)
        {
            MessageBox.Show(owner, msg, "CRM Demo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static void Info(IWin32Window owner, string msg)
        {
            MessageBox.Show(owner, msg, "CRM Demo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static bool Confirm(IWin32Window owner, string msg, MessageBoxIcon icon = MessageBoxIcon.Question)
        {
            return MessageBox.Show(owner, msg, "CRM Demo", MessageBoxButtons.YesNo, icon, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
    }

    /// <summary>顧客の新規登録・編集</summary>
    public class CustomerForm : Form
    {
        readonly CrmData data;
        readonly int editingId;
        readonly TextBox tName, tCompany, tPhone, tEmail, tAddress, tMemo;
        public Customer Result { get; private set; }

        public CustomerForm(Customer c, CrmData data)
        {
            this.data = data;
            editingId = c == null ? 0 : c.Id;
            Ui.SetupDialog(this, c == null ? "新規顧客の登録" : "顧客情報の編集", 560, 470);
            MaximizeBox = false;
            c = c ?? new Customer();

            var t = Ui.FormTable();
            tName = Ui.AddText(t, "顧客名 *", c.Name);
            tCompany = Ui.AddText(t, "会社名", c.Company);
            tPhone = Ui.AddText(t, "電話番号", c.Phone);
            tEmail = Ui.AddText(t, "メール", c.Email);
            tAddress = Ui.AddText(t, "住所", c.Address);
            tMemo = Ui.AddText(t, "備考", c.Memo, 100);

            Controls.Add(t);
            Controls.Add(Ui.OkCancel(this, "保存", OnOk));
            Shown += delegate { tName.Focus(); };
        }

        void OnOk(object sender, EventArgs e)
        {
            string name = tName.Text.Trim();
            if (name.Length == 0) { Ui.Warn(this, "顧客名を入力してください。"); tName.Focus(); return; }
            string email = tEmail.Text.Trim();
            if (email.Length > 0 && !email.Contains("@")) { Ui.Warn(this, "メールアドレスの形式が正しくありません。"); tEmail.Focus(); return; }
            var ex = data.FindExact(name, tCompany.Text);
            if (ex != null && ex.Id != editingId &&
                !Ui.Confirm(this, string.Format("同じ顧客名・会社名の顧客（ID:{0}）がすでに登録されています。\r\nこのまま保存しますか？", ex.Id)))
                return;
            Result = new Customer
            {
                Name = name, Company = tCompany.Text.Trim(), Phone = tPhone.Text.Trim(), Email = email,
                Address = tAddress.Text.Trim(), Memo = tMemo.Text.Replace("\r\n", "\n").Trim()
            };
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>対応履歴の追加・編集</summary>
    public class InteractionForm : Form
    {
        readonly DateTimePicker dtDate, dtNext;
        readonly TextBox tStaff, tInquiry, tResponse, tNext;
        readonly CheckBox chkDone;
        public Interaction Result { get; private set; }

        public InteractionForm(Interaction it, Customer c, string defaultStaff)
        {
            Ui.SetupDialog(this, it == null ? "対応履歴の追加" : "対応履歴の編集", 640, 620);
            bool isNew = it == null;
            it = it ?? new Interaction { Staff = defaultStaff ?? "" };

            var head = new Label
            {
                Text = "顧客: " + c.DisplayName, Font = Ui.BoldFont, Dock = DockStyle.Top, AutoSize = false,
                Height = Ui.S(28), Tag = "accent"
            };

            var t = Ui.FormTable();
            dtDate = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd (ddd)", Width = Ui.S(170), Value = it.Date };
            var dateRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            dateRow.Controls.Add(dtDate);
            dateRow.Controls.Add(Ui.FlowLabel("　担当者"));
            tStaff = new TextBox { Text = it.Staff, Width = Ui.S(150), Margin = new Padding(0, Ui.S(3), 0, 0) };
            dateRow.Controls.Add(tStaff);
            Ui.AddRow(t, "対応日 *", dateRow);

            tInquiry = Ui.AddText(t, "問い合わせ内容", it.Inquiry, 34);
            tResponse = Ui.AddText(t, "対応内容", it.Response, 33);
            tNext = Ui.AddText(t, "次回確認内容", it.NextAction, 33);

            dtNext = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd (ddd)", Width = Ui.S(170),
                ShowCheckBox = true, Value = it.NextDate ?? DateTime.Today.AddDays(7)
            };
            dtNext.Checked = it.NextDate.HasValue;
            var nextRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            nextRow.Controls.Add(dtNext);
            nextRow.Controls.Add(QuickBtn("+3日", 3));
            nextRow.Controls.Add(QuickBtn("+1週", 7));
            nextRow.Controls.Add(QuickBtn("+1か月", 30));
            chkDone = new CheckBox { Text = "完了（フォロー不要）", AutoSize = true, Checked = it.Done, Margin = new Padding(Ui.S(12), Ui.S(7), 0, 0) };
            nextRow.Controls.Add(chkDone);
            Ui.AddRow(t, "次回確認日", nextRow);

            Controls.Add(t);
            Controls.Add(head);
            Controls.Add(Ui.OkCancel(this, "保存", OnOk));
            Shown += delegate { (isNew ? (Control)tInquiry : tResponse).Focus(); };
        }

        Button QuickBtn(string text, int days)
        {
            var b = Ui.Btn(text, delegate { dtNext.Value = dtDate.Value.Date.AddDays(days); dtNext.Checked = true; });
            b.MinimumSize = new Size(Ui.S(56), Ui.S(28));
            b.Margin = new Padding(Ui.S(6), Ui.S(1), 0, 0);
            return b;
        }

        void OnOk(object sender, EventArgs e)
        {
            if (tInquiry.Text.Trim().Length == 0 && tResponse.Text.Trim().Length == 0)
            {
                Ui.Warn(this, "問い合わせ内容・対応内容のどちらかを入力してください。");
                tInquiry.Focus();
                return;
            }
            Func<string, string> clean = s => s.Replace("\r\n", "\n").Trim();
            Result = new Interaction
            {
                Date = dtDate.Value.Date, Staff = tStaff.Text.Trim(), Inquiry = clean(tInquiry.Text),
                Response = clean(tResponse.Text), NextAction = clean(tNext.Text),
                NextDate = dtNext.Checked ? dtNext.Value.Date : (DateTime?)null, Done = chkDone.Checked
            };
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>CSV・タブ区切りテキストの取り込み（ファイル / 貼り付け / 直接入力）とプレビュー</summary>
    public class ImportForm : Form
    {
        readonly CrmStore store;
        readonly Customer current;
        readonly TextBox txt;
        readonly DataGridView grid;
        readonly Label lblInfo;
        readonly CheckBox chkDefault;
        readonly Button btnImport;
        readonly Timer timer = new Timer { Interval = 400 };
        ParsedImport parsed;
        ImportResult preview;

        public ImportForm(CrmStore store, Customer current)
        {
            this.store = store;
            this.current = current;
            Ui.SetupDialog(this, "データのインポート（CSV / テキスト）", 1060, 720);

            var help = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = Ui.S(88), Tag = "sub",
                Text = "CSV（カンマ区切り）や、Excelからコピーしたタブ区切りのテキストを下の欄に貼り付けるか直接入力してください。ファイルのドラッグ＆ドロップも使えます。\r\n" +
                       "1行目が見出しなら列名で自動判別します（顧客名, 会社名, 電話番号, メール, 住所, 備考, 対応日, 問い合わせ内容, 対応内容, 次回確認内容, 次回確認日, 担当者, 状態）。\r\n" +
                       "見出しがない場合は「顧客名,会社名,電話番号,メール,対応日,問い合わせ内容,対応内容,次回確認内容」の順とみなします。同じ顧客名＋会社名は同じ顧客として履歴を追加し、同じ内容の履歴は重複として取り込みません。"
            };

            var tools = Ui.Flow();
            tools.Controls.Add(Ui.Btn("ファイルを開く...", OpenFile));
            tools.Controls.Add(Ui.Btn("サンプルCSVを入力", delegate { txt.Text = Samples.Csv(); }));
            tools.Controls.Add(Ui.Btn("入力例（見出しなし）", delegate
            {
                txt.Text = "中村 翔,株式会社サンプル物産,03-5555-0101,nakamura@sample-bussan.example," + TextUtil.Date(DateTime.Today) +
                           ",サービス内容を知りたい,概要を電話で説明し資料を送付,資料到着の確認\r\n";
            }));
            tools.Controls.Add(Ui.Btn("クリア", delegate { txt.Clear(); }));
            chkDefault = new CheckBox
            {
                AutoSize = true, Margin = new Padding(Ui.S(12), Ui.S(9), 0, 0), Enabled = current != null, Checked = current != null,
                Text = current != null ? "顧客名が空の行は選択中の顧客「" + current.DisplayName + "」に取り込む" : "顧客名が空の行は選択中の顧客に取り込む（顧客未選択）"
            };
            chkDefault.CheckedChanged += delegate { UpdatePreview(); };
            tools.Controls.Add(chkDefault);

            txt = new TextBox
            {
                Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                Dock = DockStyle.Fill, Font = new Font("MS Gothic", 10f), AllowDrop = true, MaxLength = 0
            };
            txt.TextChanged += delegate { timer.Stop(); timer.Start(); };
            txt.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            txt.DragDrop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) LoadFile(files[0]);
            };
            timer.Tick += delegate { timer.Stop(); UpdatePreview(); };

            grid = Ui.Grid();
            foreach (var h in new[] { "行", "顧客名", "会社名", "対応日", "問い合わせ内容", "対応内容", "次回確認内容", "判定" })
                Ui.Col(grid, h, h == "行" ? 30 : (h.EndsWith("内容") ? 140 : (h == "判定" ? 150 : 80)), 40);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            var lblText = new Label { Text = "取り込むテキスト", Dock = DockStyle.Top, Font = Ui.BoldFont, Height = Ui.S(22) };
            var lblPrev = new Label { Text = "プレビュー（まだ登録されていません）", Dock = DockStyle.Top, Font = Ui.BoldFont, Height = Ui.S(22) };
            split.Panel1.Controls.Add(txt); split.Panel1.Controls.Add(lblText);
            split.Panel2.Controls.Add(grid); split.Panel2.Controls.Add(lblPrev);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, Ui.S(8), 0, 0)
            };
            var close = Ui.Btn("閉じる", null); close.DialogResult = DialogResult.Cancel; CancelButton = close;
            btnImport = Ui.Btn("取り込み実行", DoImport, true); btnImport.Enabled = false;
            lblInfo = new Label { AutoSize = true, Margin = new Padding(0, Ui.S(9), Ui.S(12), 0) };
            bottom.Controls.Add(close); bottom.Controls.Add(btnImport); bottom.Controls.Add(lblInfo);

            Controls.Add(split);
            Controls.Add(tools);
            Controls.Add(help);
            Controls.Add(bottom);
            Load += delegate { try { split.SplitterDistance = split.Height * 45 / 100; } catch { } UpdatePreview(); };
        }

        void OpenFile(object sender, EventArgs e)
        {
            using (var d = new OpenFileDialog { Filter = "CSV / テキスト (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|すべてのファイル (*.*)|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) LoadFile(d.FileName);
        }

        void LoadFile(string path)
        {
            try { txt.Text = Csv.ReadTextFile(path); }
            catch (Exception ex) { Ui.Warn(this, "ファイルを読み込めませんでした。\r\n" + ex.Message); }
        }

        int? DefaultId { get { return chkDefault.Checked && current != null ? current.Id : (int?)null; } }

        void UpdatePreview()
        {
            parsed = Importer.Parse(txt.Text);
            preview = Importer.Apply(store.Data.Clone(), parsed.Rows, DefaultId);
            grid.Rows.Clear();
            for (int i = 0; i < parsed.Rows.Count; i++)
            {
                var r = parsed.Rows[i];
                string st = preview.RowStatus[i];
                int idx = grid.Rows.Add(r.LineNo.ToString(), r.Name, r.Company, r.DateText, TextUtil.OneLine(r.Inquiry),
                    TextUtil.OneLine(r.Response), TextUtil.OneLine(r.NextAction), st);
                var cell = grid.Rows[idx].Cells[7];
                if (st.StartsWith("エラー")) { cell.Style.ForeColor = Theme.Danger; cell.Style.Font = Ui.BoldFont; }
                else if (st.Contains("重複")) grid.Rows[idx].DefaultCellStyle.ForeColor = Theme.Muted;
                else if (st.StartsWith("新規")) cell.Style.ForeColor = Theme.Success;
            }
            if (parsed.Rows.Count == 0)
            {
                lblInfo.Text = "テキストを入力するとプレビューが表示されます";
                btnImport.Enabled = false;
                return;
            }
            lblInfo.Text = string.Format("{0}・見出し{1}　→　{2}",
                parsed.Delimiter == '\t' ? "タブ区切り" : "カンマ区切り", parsed.HasHeader ? "あり" : "なし（既定の列順）", preview.Summary);
            btnImport.Enabled = preview.HasChanges;
        }

        void DoImport(object sender, EventArgs e)
        {
            timer.Stop();
            UpdatePreview();
            if (!preview.HasChanges) return;
            if (preview.Errors > 0 &&
                !Ui.Confirm(this, preview.Errors + " 行にエラーがあります。エラーの行を除いて取り込みますか？", MessageBoxIcon.Warning))
                return;
            try
            {
                if (store.ChangedOnDisk) store.Load(); // コマンドラインからの登録を上書きしないよう最新を読み直す
                var work = store.Data.Clone();
                var res = Importer.Apply(work, parsed.Rows, DefaultId);
                store.ReplaceData(work);
                Ui.Info(this, "取り込みました。\r\n\r\n" + res.Summary.Replace(" / ", "\r\n"));
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                Ui.Warn(this, "保存に失敗しました。\r\n" + ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>テキスト表示（連携出力プレビュー・ヘルプ）</summary>
    public class TextViewerForm : Form
    {
        public TextViewerForm(string title, string intro, string body)
        {
            Ui.SetupDialog(this, title, 820, 560);
            var tb = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Dock = DockStyle.Fill,
                Font = new Font("MS Gothic", 10.5f), Text = body
            };
            var top = new Label { Text = intro ?? "", Dock = DockStyle.Top, AutoSize = false, Height = string.IsNullOrEmpty(intro) ? 0 : Ui.S(60) };
            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, Ui.S(8), 0, 0)
            };
            var close = Ui.Btn("閉じる", null); close.DialogResult = DialogResult.Cancel; CancelButton = close;
            var copy = Ui.Btn("クリップボードにコピー", delegate
            {
                if (body.Length > 0) Clipboard.SetText(body);
                Ui.Info(this, "コピーしました。");
            }, true);
            bottom.Controls.Add(close); bottom.Controls.Add(copy);
            Controls.Add(tb); Controls.Add(top); Controls.Add(bottom);
            Shown += delegate { tb.SelectionStart = 0; tb.SelectionLength = 0; close.Focus(); };
        }
    }
}
