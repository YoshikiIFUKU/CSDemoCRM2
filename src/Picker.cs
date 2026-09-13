using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CrmDemo
{
    /// <summary>
    /// 検索・登録で顧客が複数該当したときに、1件を選んでもらう小さな画面（--pick）。
    /// コマンドラインから呼ぶので、画面のない環境では出せない。その場合は呼び出し側が従来の動作に戻す。
    /// </summary>
    public class PickerForm : Form
    {
        readonly DataGridView grid;
        readonly Label lblCount;
        readonly Timer timer;
        int remain;

        public Customer Selected { get; private set; }

        public PickerForm(List<Customer> candidates, CrmData data, string message, int timeoutSeconds)
        {
            Ui.SetupDialog(this, "顧客の選択 - CSDemoCRM2", 760, 440);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            TopMost = true;
            MaximizeBox = true;

            var head = new Label { Text = message, Dock = DockStyle.Top, AutoSize = false, Height = Ui.S(24), Font = Ui.BoldFont };
            var hint = new Label
            {
                Text = "行をダブルクリック、または選んで［この顧客にする］を押してください。", Dock = DockStyle.Top,
                AutoSize = false, Height = Ui.S(22), Tag = "sub"
            };

            grid = Ui.Grid();
            Ui.Col(grid, "顧客ID", 34, 60, typeof(int));
            Ui.Col(grid, "顧客名", 90, 80);
            Ui.Col(grid, "会社名", 130, 90);
            Ui.Col(grid, "電話番号", 80, 80);
            Ui.Col(grid, "ケース件数", 40, 76, typeof(int));
            Ui.Col(grid, "最終対応日", 60, 96);
            foreach (var c in candidates)
            {
                var h = data.Cases.Where(x => x.CustomerId == c.Id).ToList();
                int idx = grid.Rows.Add(c.Id, c.Name, c.Company, c.Phone, h.Count,
                    h.Count > 0 ? TextUtil.Date(h.Max(x => data.SortDate(x))) : "");
                grid.Rows[idx].Tag = c;
            }
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Choose(); };
            grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; Choose(); } };

            var buttons = Ui.OkCancel(this, "この顧客にする", delegate { Choose(); });
            lblCount = new Label { AutoSize = true, Margin = new Padding(0, Ui.S(9), Ui.S(12), 0), Tag = "sub" };
            buttons.Controls.Add(lblCount);

            Controls.Add(grid);
            Controls.Add(hint);
            Controls.Add(head);
            Controls.Add(buttons);

            if (timeoutSeconds > 0)
            {
                remain = timeoutSeconds;
                timer = new Timer { Interval = 1000 };
                timer.Tick += delegate
                {
                    remain--;
                    lblCount.Text = string.Format("あと {0} 秒で自動的にキャンセルします", remain);
                    if (remain <= 0) { timer.Stop(); DialogResult = DialogResult.Cancel; Close(); }
                };
                lblCount.Text = string.Format("あと {0} 秒で自動的にキャンセルします", remain);
                Load += delegate { timer.Start(); };
            }

            Shown += delegate { Activate(); grid.Focus(); };
        }

        void Choose()
        {
            if (grid.CurrentRow == null) return;
            Selected = grid.CurrentRow.Tag as Customer;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && timer != null) timer.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>
        /// 選択画面を出す。戻り値 false は「画面を出せなかった」＝呼び出し側は従来の動作に戻す。
        /// true でも chosen が null ならキャンセル。
        /// </summary>
        public static bool TryPick(List<Customer> candidates, CrmData data, string message, int timeoutSeconds, out Customer chosen)
        {
            chosen = null;
            try
            {
                Application.EnableVisualStyles();
                using (var f = new PickerForm(candidates, data, message, timeoutSeconds))
                {
                    var r = f.ShowDialog();
                    chosen = r == DialogResult.OK ? f.Selected : null;
                    return true;
                }
            }
            catch { return false; } // 画面を表示できない環境
        }
    }
}
