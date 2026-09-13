using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("CSDemoCRM2")]
[assembly: AssemblyProduct("CSDemoCRM2 - 顧客・ケース管理")]
[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyFileVersion("2.2.0.0")]

namespace CrmDemo
{
    static class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint GetConsoleProcessList(uint[] processList, uint processCount);
        [DllImport("kernel32.dll")]
        static extern bool FreeConsole();
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [STAThread]
        static int Main(string[] args)
        {
            // 引数あり → コマンドラインモード（標準出力に結果を書いて終了）
            if (args.Length > 0) return Cli.Run(args);

            // 引数なし → GUI。ダブルクリック起動で自分用に作られたコンソール窓は閉じる
            DetachOwnConsole();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show("エラーが発生しました。\r\n\r\n" + e.Exception.Message, "CSDemoCRM2",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

            CrmStore store;
            string path = CrmStore.ResolveDefaultPath();
            try { store = new CrmStore(path); }
            catch (Exception ex)
            {
                MessageBox.Show("データファイルを読み込めませんでした。\r\n" + path + "\r\n\r\n" + ex.Message +
                    "\r\n\r\n同じフォルダの " + CrmStore.FileName + ".bak（直前の版）から復元できます。",
                    "CSDemoCRM2", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return Cli.ExitError;
            }
            Theme.LoadSetting(store.FilePath);
            Application.Run(new MainForm(store));
            return 0;
        }

        static void DetachOwnConsole()
        {
            try
            {
                var list = new uint[4];
                if (GetConsoleProcessList(list, (uint)list.Length) <= 1)
                {
                    IntPtr h = GetConsoleWindow();
                    if (h != IntPtr.Zero) ShowWindow(h, 0);
                    FreeConsole();
                }
            }
            catch { }
        }
    }
}
