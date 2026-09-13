using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CrmDemo
{
    /// <summary>ケースの全フィールドを表示・編集する画面（一覧のダブルクリックで開く）</summary>
    public class CaseForm : Form
    {
        readonly CrmData data;
        readonly List<FieldDef> fields;
        readonly Dictionary<string, Control> editors = new Dictionary<string, Control>();

        /// <summary>入力された値（変数名 → 値）</summary>
        public Dictionary<string, string> Values { get; private set; }

        public CaseForm(CrmData data, Customer customer, Case target)
        {
            this.data = data;
            fields = data.SortedFields;
            bool isNew = target == null;
            Ui.SetupDialog(this, isNew ? "ケースの新規作成" : "ケースの編集 - " + target.Number, 720, 720);
            MaximizeBox = true;

            var head = new Label
            {
                Text = "顧客: " + customer.DisplayName + (isNew ? "" : "　／　ケース番号: " + target.Number),
                Font = Ui.BoldFont, Dock = DockStyle.Top, AutoSize = false, Height = Ui.S(26), Tag = "accent"
            };
            var sub = new Label
            {
                Text = isNew ? "フィールド設定で定義された項目です。［データ］→［フィールド設定］で増やせます。"
                             : "更新: " + target.UpdatedAt.ToString("yyyy/MM/dd HH:mm") + "　登録元: " + SourceLabel(target.Source),
                Dock = DockStyle.Top, AutoSize = false, Height = Ui.S(22), Tag = "sub"
            };

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var t = Ui.FormTable();
            t.Dock = DockStyle.Top;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.Width = Ui.S(660);

            foreach (var f in fields)
            {
                string value = isNew ? DefaultValue(f) : target.Get(f.ApiName);
                Control editor = CreateEditor(f, value);
                editors[f.ApiName] = editor;
                string label = f.Label + (f.Required ? " *" : "");
                if (f.Type == FieldTypes.TextArea)
                {
                    editor.Height = Ui.S(76);
                    Ui.AddRow(t, label, editor);
                    editor.Dock = DockStyle.Fill;
                }
                else if (f.Type == FieldTypes.Url)
                {
                    var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
                    editor.Width = Ui.S(430);
                    row.Controls.Add(editor);
                    var open = Ui.Btn("開く", delegate
                    {
                        string url = ((TextBox)editor).Text.Trim();
                        if (url.StartsWith("http://") || url.StartsWith("https://"))
                            try { Process.Start(url); } catch (Exception ex) { Ui.Warn(this, ex.Message); }
                        else Ui.Info(this, "http:// または https:// で始まるURLのときに開けます。");
                    });
                    open.Margin = new Padding(Ui.S(6), Ui.S(2), 0, 0);
                    row.Controls.Add(open);
                    Ui.AddRow(t, label, row);
                }
                else
                {
                    Ui.AddRow(t, label, editor);
                }
            }

            scroll.Controls.Add(t);
            Controls.Add(scroll);
            Controls.Add(sub);
            Controls.Add(head);
            Controls.Add(Ui.OkCancel(this, "保存", OnOk));
            Shown += delegate { if (editors.Count > 0) editors[fields[0].ApiName].Focus(); };
        }

        static string SourceLabel(string source)
        {
            switch (source)
            {
                case "cli": return "コマンドライン";
                case "import": return "インポート";
                case "migrated": return "旧データからの移行";
                default: return "画面";
            }
        }

        /// <summary>新規作成時の初期値（必須の日付項目だけ今日を入れる）</summary>
        static string DefaultValue(FieldDef f)
        {
            if (!f.Required) return "";
            return f.Type == FieldTypes.Date ? DateTime.Today.ToString("yyyy/MM/dd")
                 : f.Type == FieldTypes.DateTimeType ? DateTime.Now.ToString("yyyy/MM/dd HH:mm") : "";
        }

        Control CreateEditor(FieldDef f, string value)
        {
            switch (f.Type)
            {
                case FieldTypes.TextArea:
                    return new TextBox
                    {
                        Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true,
                        Text = (value ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n")
                    };
                case FieldTypes.Select:
                    {
                        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.S(240) };
                        cb.Items.Add("");
                        foreach (var o in f.OptionList) cb.Items.Add(o);
                        cb.SelectedItem = cb.Items.Contains(value ?? "") ? (object)(value ?? "") : "";
                        return cb;
                    }
                case FieldTypes.Checkbox:
                    return new CheckBox { Text = "はい", AutoSize = true, Checked = value == "1" };
                case FieldTypes.Date:
                case FieldTypes.DateTimeType:
                    {
                        bool isDate = f.Type == FieldTypes.Date;
                        var dt = new DateTimePicker
                        {
                            Format = DateTimePickerFormat.Custom,
                            CustomFormat = isDate ? "yyyy/MM/dd (ddd)" : "yyyy/MM/dd HH:mm",
                            Width = Ui.S(190), ShowCheckBox = true
                        };
                        DateTime d;
                        if (TextUtil.TryParseDate(value, out d) || DateTime.TryParse(value, out d))
                        { dt.Value = d == DateTime.MinValue ? DateTime.Today : d; dt.Checked = true; }
                        else { dt.Value = DateTime.Today; dt.Checked = false; }
                        return dt;
                    }
                case FieldTypes.Number:
                    return new TextBox { Text = value ?? "", Width = Ui.S(160) };
                default:
                    return new TextBox { Text = value ?? "" };
            }
        }

        string ReadEditor(FieldDef f)
        {
            var c = editors[f.ApiName];
            switch (f.Type)
            {
                case FieldTypes.Select: return (string)(((ComboBox)c).SelectedItem ?? "");
                case FieldTypes.Checkbox: return ((CheckBox)c).Checked ? "1" : "";
                case FieldTypes.Date:
                case FieldTypes.DateTimeType:
                    {
                        var dt = (DateTimePicker)c;
                        if (!dt.Checked) return "";
                        return f.Type == FieldTypes.Date ? dt.Value.ToString("yyyy/MM/dd") : dt.Value.ToString("yyyy/MM/dd HH:mm");
                    }
                case FieldTypes.Url:
                    {
                        var flow = c as FlowLayoutPanel;
                        if (flow != null) return ((TextBox)flow.Controls[0]).Text.Trim();
                        return ((TextBox)c).Text.Trim();
                    }
                default: return ((TextBox)c).Text.Replace("\r\n", "\n").Trim();
            }
        }

        void OnOk(object sender, EventArgs e)
        {
            var result = new Dictionary<string, string>();
            foreach (var f in fields)
            {
                string v = ReadEditor(f);
                if (f.Required && v.Length == 0)
                {
                    Ui.Warn(this, f.Label + " を入力してください。");
                    editors[f.ApiName].Focus();
                    return;
                }
                if (f.Type == FieldTypes.Number && v.Length > 0)
                {
                    double dummy;
                    if (!double.TryParse(v, out dummy))
                    {
                        Ui.Warn(this, f.Label + " には数値を入力してください。");
                        editors[f.ApiName].Focus();
                        return;
                    }
                }
                result[f.ApiName] = FieldTypes.Normalize(f.Type, v);
            }
            if (result.All(kv => kv.Value.Length == 0))
            {
                Ui.Warn(this, "いずれかの項目を入力してください。");
                return;
            }
            Values = result;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>ケース一覧に表示する列を選ぶ画面</summary>
    public class ColumnChooserForm : Form
    {
        readonly CheckedListBox box;
        readonly List<FieldDef> fields;

        public List<FieldDef> Selected { get; private set; }

        public ColumnChooserForm(CrmData data, List<FieldDef> current)
        {
            fields = data.SortedFields;
            Ui.SetupDialog(this, "表示する列の選択", 420, 480);
            var head = new Label
            {
                Text = "ケース一覧に表示する項目を選んでください。並び順はフィールド設定の順です。",
                Dock = DockStyle.Top, AutoSize = false, Height = Ui.S(36), Tag = "sub"
            };
            box = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
            var keys = new HashSet<string>(current.Select(f => f.ApiName));
            foreach (var f in fields) box.Items.Add(f.Label + "（" + f.ApiName + "）", keys.Contains(f.ApiName));

            var buttons = Ui.OkCancel(this, "決定", OnOk);
            buttons.Controls.Add(Ui.Btn("標準出力の設定に合わせる", delegate
            {
                for (int i = 0; i < fields.Count; i++) box.SetItemChecked(i, fields[i].InList);
            }));

            Controls.Add(box);
            Controls.Add(head);
            Controls.Add(buttons);
        }

        void OnOk(object sender, EventArgs e)
        {
            var sel = new List<FieldDef>();
            for (int i = 0; i < fields.Count; i++) if (box.GetItemChecked(i)) sel.Add(fields[i]);
            if (sel.Count == 0) { Ui.Warn(this, "1つ以上の項目を選んでください。"); return; }
            Selected = sel;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>フィールド定義の一覧・並べ替え画面</summary>
    public class FieldSettingsForm : Form
    {
        readonly CrmStore store;
        readonly Func<Action<CrmData>, bool> commit;   // MainForm の保存処理を借りる
        readonly DataGridView grid;

        public bool Changed { get; private set; }

        public FieldSettingsForm(CrmStore store, Func<Action<CrmData>, bool> commit)
        {
            this.store = store;
            this.commit = commit;
            Ui.SetupDialog(this, "フィールド設定（ケースの項目）", 860, 560);
            MaximizeBox = true;

            var head = new Label
            {
                Text = "ケースの入力項目です。ここで追加すると、画面の入力欄と、コマンドで指定できる変数名が同時に増えます。",
                Dock = DockStyle.Top, AutoSize = false, Height = Ui.S(24), Tag = "sub"
            };

            var tools = Ui.Flow();
            tools.Controls.Add(Ui.Btn("＋ 追加", delegate { Edit(null); }, true));
            tools.Controls.Add(Ui.Btn("編集", delegate { Edit(Current()); }));
            tools.Controls.Add(Ui.Btn("削除", Delete));
            tools.Controls.Add(Ui.Btn("↑ 上へ", delegate { MoveField(-1); }));
            tools.Controls.Add(Ui.Btn("↓ 下へ", delegate { MoveField(1); }));

            grid = Ui.Grid();
            Ui.Col(grid, "表示名", 90, 90);
            Ui.Col(grid, "変数名", 90, 90);
            Ui.Col(grid, "型", 60, 80);
            Ui.Col(grid, "必須", 30, 50);
            Ui.Col(grid, "一覧・標準出力", 50, 90);
            Ui.Col(grid, "キー項目", 40, 70);
            Ui.Col(grid, "選択肢", 110, 90);
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Edit(Current()); };

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, Ui.S(8), 0, 0)
            };
            var close = Ui.Btn("閉じる", null);
            close.DialogResult = DialogResult.OK;
            CancelButton = close;
            bottom.Controls.Add(close);

            Controls.Add(grid);
            Controls.Add(tools);
            Controls.Add(head);
            Controls.Add(bottom);
            Load += delegate { Refresh_(); };
        }

        FieldDef Current()
        {
            if (grid.CurrentRow == null) return null;
            var id = grid.CurrentRow.Tag as int?;
            return id.HasValue ? store.Data.Fields.FirstOrDefault(f => f.Id == id.Value) : null;
        }

        void Refresh_(int? select = null)
        {
            grid.Rows.Clear();
            foreach (var f in store.Data.SortedFields)
            {
                int idx = grid.Rows.Add(f.Label, f.ApiName, FieldTypes.Label(f.Type), f.Required ? "○" : "",
                    f.InList ? "○" : "", f.IsKey ? "○" : "", TextUtil.OneLine(f.Options));
                grid.Rows[idx].Tag = f.Id;
                if (select.HasValue && f.Id == select.Value) grid.CurrentCell = grid.Rows[idx].Cells[0];
            }
        }

        void Edit(FieldDef f)
        {
            using (var dlg = new FieldEditForm(store.Data, f))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var r = dlg.Result;
                int id = f == null ? 0 : f.Id;
                int newId = 0;
                if (!commit(d =>
                {
                    if (r.IsKey) foreach (var x in d.Fields) x.IsKey = false;
                    var target = id == 0 ? null : d.Fields.FirstOrDefault(x => x.Id == id);
                    if (target == null)
                    {
                        r.Id = d.NextFieldId++;
                        r.Sort = d.Fields.Count == 0 ? 0 : d.Fields.Max(x => x.Sort) + 10;
                        d.Fields.Add(r);
                        newId = r.Id;
                    }
                    else
                    {
                        target.Label = r.Label; target.ApiName = r.ApiName; target.Type = r.Type;
                        target.Options = r.Options; target.Required = r.Required; target.InList = r.InList;
                        target.IsKey = r.IsKey;
                        newId = target.Id;
                    }
                })) return;
                Changed = true;
                Refresh_(newId);
            }
        }

        void Delete(object sender, EventArgs e)
        {
            var f = Current();
            if (f == null) return;
            int used = store.Data.Cases.Count(c => c.Get(f.ApiName).Length > 0);
            if (!Ui.Confirm(this, string.Format("フィールド「{0}」を削除します。{1}\r\nよろしいですか？",
                    f.Label, used > 0 ? "\r\n値が入っているケースが " + used + " 件あります（その値も消えます）。" : ""),
                    MessageBoxIcon.Warning)) return;
            int id = f.Id;
            string api = f.ApiName;
            if (!commit(d =>
            {
                d.Fields.RemoveAll(x => x.Id == id);
                foreach (var c in d.Cases) c.Values.RemoveAll(v => v.Name == api);
            })) return;
            Changed = true;
            Refresh_();
        }

        void MoveField(int direction)
        {
            var f = Current();
            if (f == null) return;
            int id = f.Id;
            if (!commit(d =>
            {
                var list = d.SortedFields;
                int i = list.FindIndex(x => x.Id == id);
                int j = i + direction;
                if (i < 0 || j < 0 || j >= list.Count) return;
                var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
                for (int k = 0; k < list.Count; k++) list[k].Sort = k * 10;
            })) return;
            Changed = true;
            Refresh_(id);
        }
    }

    /// <summary>フィールド1件の追加・編集</summary>
    public class FieldEditForm : Form
    {
        readonly CrmData data;
        readonly int editingId;
        readonly TextBox tLabel, tApi, tOptions;
        readonly ComboBox cbType;
        readonly CheckBox chkRequired, chkInList, chkKey;

        public FieldDef Result { get; private set; }

        public FieldEditForm(CrmData data, FieldDef f)
        {
            this.data = data;
            editingId = f == null ? 0 : f.Id;
            Ui.SetupDialog(this, f == null ? "フィールドの追加" : "フィールドの編集", 560, 470);
            f = f ?? new FieldDef();

            var t = Ui.FormTable();
            tLabel = Ui.AddText(t, "表示名 *", f.Label);
            tApi = Ui.AddText(t, "変数名 *", f.ApiName);
            var apiHint = new Label
            {
                Text = "コマンドで --set 変数名=値 と指定するときの名前（半角英数字と _ ）", AutoSize = true, Tag = "sub"
            };
            Ui.AddRow(t, "", apiHint);
            cbType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.S(200) };
            foreach (var ty in FieldTypes.All) cbType.Items.Add(FieldTypes.Label(ty));
            cbType.SelectedIndex = Math.Max(0, Array.IndexOf(FieldTypes.All, f.Type));
            cbType.SelectedIndexChanged += delegate { tOptions.Enabled = SelectedType == FieldTypes.Select; };
            Ui.AddRow(t, "型", cbType);
            tOptions = Ui.AddText(t, "選択肢", f.Options, 100);
            tOptions.Enabled = f.Type == FieldTypes.Select;
            var optHint = new Label { Text = "選択リストのときだけ使います（1行に1つ）", AutoSize = true, Tag = "sub" };
            Ui.AddRow(t, "", optHint);

            var flags = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            chkRequired = new CheckBox { Text = "必須", AutoSize = true, Checked = f.Required, Margin = new Padding(0, Ui.S(4), Ui.S(16), 0) };
            chkInList = new CheckBox { Text = "一覧・標準出力に含める", AutoSize = true, Checked = f.InList, Margin = new Padding(0, Ui.S(4), Ui.S(16), 0) };
            chkKey = new CheckBox { Text = "キー項目（同じ値なら同じケースを更新）", AutoSize = true, Checked = f.IsKey, Margin = new Padding(0, Ui.S(4), 0, 0) };
            flags.Controls.Add(chkRequired);
            flags.Controls.Add(chkInList);
            flags.Controls.Add(chkKey);
            Ui.AddRow(t, "設定", flags);

            Controls.Add(t);
            Controls.Add(Ui.OkCancel(this, "保存", OnOk));
            Shown += delegate { tLabel.Focus(); };
        }

        string SelectedType { get { return FieldTypes.All[Math.Max(0, cbType.SelectedIndex)]; } }

        void OnOk(object sender, EventArgs e)
        {
            string label = tLabel.Text.Trim(), api = tApi.Text.Trim();
            if (label.Length == 0) { Ui.Warn(this, "表示名を入力してください。"); tLabel.Focus(); return; }
            if (api.Length == 0) { Ui.Warn(this, "変数名を入力してください。"); tApi.Focus(); return; }
            if (!api.All(ch => char.IsLetterOrDigit(ch) && ch < 128 || ch == '_'))
            {
                Ui.Warn(this, "変数名は半角英数字とアンダースコアだけで入力してください。");
                tApi.Focus();
                return;
            }
            var dup = data.Fields.FirstOrDefault(f => f.Id != editingId &&
                (string.Equals(f.ApiName, api, StringComparison.OrdinalIgnoreCase) || f.Label == label));
            if (dup != null)
            {
                Ui.Warn(this, "同じ表示名か変数名のフィールドがすでにあります（" + dup.Label + " / " + dup.ApiName + "）。");
                return;
            }
            Result = new FieldDef
            {
                Id = editingId,
                Label = label,
                ApiName = api,
                Type = SelectedType,
                Options = SelectedType == FieldTypes.Select ? tOptions.Text.Replace("\r\n", "\n").Trim() : "",
                Required = chkRequired.Checked,
                InList = chkInList.Checked,
                IsKey = chkKey.Checked
            };
            DialogResult = DialogResult.OK;
        }
    }
}
