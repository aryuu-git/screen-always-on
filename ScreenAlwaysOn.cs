using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenAlwaysOn
{
    /// <summary>
    /// 屏幕常亮小工具: 不修改系统电源配置, 通过运行时 API 保持屏幕常亮。
    /// 程序退出后系统自动恢复原电源策略。
    /// </summary>
    internal static class Program
    {
        // ES 标志位
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>开启(flag 为 true)或关闭(flag 为 false)防熄屏状态</summary>
        internal static void SetKeepAwake(bool flag)
        {
            SetThreadExecutionState(flag
                ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
                : ES_CONTINUOUS);
        }

        [STAThread]
        private static void Main()
        {
            // 防止重复启动
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\ScreenAlwaysOn.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("屏幕常亮已在运行中, 可在右下角托盘区找到它。",
                        "屏幕常亮", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 启动即开启防熄屏
                SetKeepAwake(true);

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }

        /// <summary>绘制一个太阳样式的托盘图标</summary>
        internal static Icon CreateAppIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    // 太阳本体
                    using (var sunBrush = new SolidBrush(Color.FromArgb(255, 196, 0)))
                    {
                        g.FillEllipse(sunBrush, 9, 9, 14, 14);
                    }

                    // 光芒
                    using (var rayPen = new Pen(Color.FromArgb(255, 196, 0), 2f))
                    {
                        g.DrawLine(rayPen, 16, 3, 16, 7);
                        g.DrawLine(rayPen, 16, 25, 16, 29);
                        g.DrawLine(rayPen, 3, 16, 7, 16);
                        g.DrawLine(rayPen, 25, 16, 29, 16);
                        g.DrawLine(rayPen, 6.5f, 6.5f, 9.5f, 9.5f);
                        g.DrawLine(rayPen, 22.5f, 6.5f, 25.5f, 9.5f);
                        g.DrawLine(rayPen, 6.5f, 25.5f, 9.5f, 22.5f);
                        g.DrawLine(rayPen, 22.5f, 25.5f, 25.5f, 22.5f);
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (var icon = Icon.FromHandle(hIcon))
                    {
                        return (Icon)icon.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }
    }

    /// <summary>托盘图标 + 右键菜单</summary>
    internal sealed class TrayApp : ApplicationContext
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegName = "ScreenAlwaysOn";

        private readonly NotifyIcon _trayIcon;
        private readonly MenuItem _statusItem;
        private readonly MenuItem _toggleItem;
        private bool _enabled = true;

        public TrayApp()
        {
            _trayIcon = new NotifyIcon
            {
                Text = "屏幕常亮 (已开启)",
                Icon = Program.CreateAppIcon(),
                Visible = true
            };

            _statusItem = new MenuItem("状态: 已开启") { Enabled = false };
            _toggleItem = new MenuItem("禁用防熄屏", OnToggle);
            var autoStartItem = new MenuItem("开机自启", OnAutoStart) { Checked = IsAutoStartEnabled() };
            var exitItem = new MenuItem("退出", OnExit);

            _trayIcon.ContextMenu = new ContextMenu(new[]
            {
                _statusItem, _toggleItem, autoStartItem, exitItem
            });

            _trayIcon.DoubleClick += OnToggle;
            _trayIcon.ShowBalloonTip(1500, "屏幕常亮", "防熄屏已开启, 屏幕将保持常亮, 不修改任何系统电源设置。",
                ToolTipIcon.Info);
        }

        private void OnToggle(object sender, EventArgs e)
        {
            _enabled = !_enabled;
            Program.SetKeepAwake(_enabled);
            _toggleItem.Text = _enabled ? "禁用防熄屏" : "开启防熄屏";
            _statusItem.Text = _enabled ? "状态: 已开启" : "状态: 已关闭";
            _trayIcon.Text = _enabled ? "屏幕常亮 (已开启)" : "屏幕常亮 (已关闭)";
        }

        private static void OnAutoStart(object sender, EventArgs e)
        {
            var item = (MenuItem)sender;
            bool enable = !item.Checked;
            SetAutoStartEnabled(enable);
            item.Checked = enable;
        }

        private static void SetAutoStartEnabled(bool enable)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null)
                {
                    return;
                }

                if (enable)
                {
                    key.SetValue(AppRegName, "\"" + Application.ExecutablePath + "\"");
                }
                else
                {
                    key.DeleteValue(AppRegName, false);
                }
            }
        }

        private static bool IsAutoStartEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
            {
                return key != null && key.GetValue(AppRegName) != null;
            }
        }

        private void OnExit(object sender, EventArgs e)
        {
            // 显式恢复防熄屏状态(进程退出后系统也会自动恢复)
            Program.SetKeepAwake(false);
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Exit();
        }
    }
}
