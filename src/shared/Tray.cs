// The tray part of cli-tray.exe (built from cli.cs + this file with TRAY defined):
// account limits in the tooltip and the menu, a notice when an exhausted limit
// resets, quick launch under any account and the Windows autostart switch.

#if TRAY
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static partial class Program
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "ClaudeAccountsTray";
    const int TrayRefreshMs = 20000;

    static readonly string AppDir = Path.GetDirectoryName(Application.ExecutablePath);

    static int RunTray()
    {
        bool created;
        using (var mutex = new Mutex(true, "ClaudeAccountsTray", out created))
        {
            if (!created) return 0;

            // LimitsText animates its bars from screenStart; in the tray they are always full.
            screenStart = -1000;
            Application.EnableVisualStyles();

            var icon = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                Text = "Multi_calude",
                Visible = true,
            };
            var menu = new ContextMenuStrip();
            menu.Opening += (sender, e) => FillTrayMenu(menu);
            icon.ContextMenuStrip = menu;
            icon.MouseDoubleClick += (sender, e) => OpenCli(0);

            var exhausted = new bool[AccountCount];
            var timer = new System.Windows.Forms.Timer { Interval = TrayRefreshMs };
            timer.Tick += (sender, e) => UpdateTray(icon, exhausted);
            UpdateTray(icon, exhausted);
            timer.Start();

            Application.Run();
            icon.Visible = false;
        }
        return 0;
    }

    static string Nickname(Account account)
    {
        return account.Label ?? account.Email;
    }

    // Tooltip like "Claude · 1: 42% · 2: до 18:00", and a notice for each limit that has just reset.
    static void UpdateTray(NotifyIcon icon, bool[] wasExhausted)
    {
        prefs = LoadPrefs();
        var parts = new List<string>();
        for (int i = 0; i < AccountCount; i++)
        {
            Account account = LoadAccount(i + 1);
            if (account.Email == null) continue;

            Window window = ExhaustedWindow(account);
            if (wasExhausted[i] && window == null)
            {
                icon.ShowBalloonTip(10000, "Аккаунт " + (i + 1) + " снова доступен",
                    Nickname(account) + " — лимит сброшен, можно продолжать.", ToolTipIcon.Info);
            }
            wasExhausted[i] = window != null;

            string state = window != null ? "до " + ResetTime(window.Resets)
                : account.FiveHour != null ? (int)account.FiveHour.Used + "%"
                : "—";
            parts.Add((i + 1) + ": " + state);
        }

        string text = parts.Count == 0 ? "Claude: нет подключённых аккаунтов" : "Claude · " + string.Join(" · ", parts);
        icon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
    }

    static void FillTrayMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        for (int i = 0; i < AccountCount; i++)
        {
            Account account = LoadAccount(i + 1);
            if (account.Email == null) continue;

            int number = i + 1;
            string limits = StripColor(LimitsText(account));
            var item = new ToolStripMenuItem(number + ". " + Nickname(account) + (limits.Length > 0 ? "      " + limits : ""));
            item.ToolTipText = "Запустить Claude Code под этим аккаунтом";
            item.Click += (sender, e) => OpenCli(number);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        var open = new ToolStripMenuItem("Открыть меню", null, (sender, e) => OpenCli(0));
        open.Font = new Font(open.Font, FontStyle.Bold);
        menu.Items.Add(open);

        var autostart = new ToolStripMenuItem("Запускать вместе с Windows") { Checked = AutostartEnabled() };
        autostart.Click += (sender, e) => SetAutostart(!AutostartEnabled());
        menu.Items.Add(autostart);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Выход", null, (sender, e) => Application.Exit()));
    }

    // Opens cli.exe in a new terminal window: the menu, or straight into an account.
    static void OpenCli(int number)
    {
        var info = new ProcessStartInfo(Path.Combine(AppDir, "cli.exe"), number > 0 ? number.ToString() : "")
        {
            UseShellExecute = true,
            WorkingDirectory = Home,
        };
        try
        {
            Process.Start(info);
        }
        catch (Exception error)
        {
            MessageBox.Show("Не удалось открыть cli.exe: " + error.Message, "Multi_calude");
        }
    }

    static bool AutostartEnabled()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
        {
            return key != null && key.GetValue(RunValue) != null;
        }
    }

    static void SetAutostart(bool enabled)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enabled) key.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
            else key.DeleteValue(RunValue, false);
        }
    }
}
#endif
