// cli --install / --uninstall: the parts of Multi_calude that live in ~/.claude.
//
// Install points the Claude Code status line at "cli --statusline" (it records each
// account's limits for the menu) and adds the /switch-project and /switch-account
// commands. Uninstall removes exactly what install added; accounts, sessions and
// ~/.claude-accounts.json are left alone.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

static partial class Program
{
    // Marks the command files this program wrote, so uninstall never deletes the user's own.
    const string CommandMarker = "<!-- multi_calude -->";

    static readonly string[,] SwitchCommands =
    {
        { "switch-project", "project", "Сменить проект — закрыть сессию и открыть список проектов" },
        { "switch-account", "account", "Сменить аккаунт — закрыть сессию и открыть выбор аккаунта, чтобы продолжить её под другим" },
    };

    static string SettingsFile
    {
        get { return Path.Combine(MainConfigDir, "settings.json"); }
    }

    static string CommandsDir
    {
        get { return Path.Combine(MainConfigDir, "commands"); }
    }

    static string ExecutablePath()
    {
        return Process.GetCurrentProcess().MainModule.FileName;
    }

    // The program path as a shell command: forward slashes, quoted only when it has spaces.
    static string ShellPath(string path)
    {
        path = path.Replace('\\', '/');
        return path.IndexOf(' ') >= 0 ? "\"" + path + "\"" : path;
    }

    static bool IsOurStatusLine(object statusLine)
    {
        string command = Text(statusLine as Dictionary<string, object>, "command");
        return command != null && command.EndsWith("--statusline") && command.IndexOf("cli", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static int Install()
    {
        string exe = ExecutablePath();
        Directory.CreateDirectory(MainConfigDir);

        Dictionary<string, object> settings;
        if (File.Exists(SettingsFile))
        {
            settings = ReadJson(SettingsFile);
            if (settings == null)
            {
                Console.Error.WriteLine("Не удалось прочитать " + SettingsFile + " — исправь JSON и запусти cli --install ещё раз.");
                return 1;
            }
        }
        else settings = new Dictionary<string, object>();

        object current;
        if (settings.TryGetValue("statusLine", out current) && !IsOurStatusLine(current))
        {
            Console.WriteLine("· Строка состояния уже настроена другой программой — оставляю её.");
            Console.WriteLine("  Без неё меню не увидит лимиты. Чтобы заменить: удали statusLine из " + SettingsFile);
        }
        else
        {
            settings["statusLine"] = new Dictionary<string, object>
            {
                { "type", "command" },
                { "command", ShellPath(exe) + " --statusline" },
            };
            File.WriteAllText(SettingsFile, Json.Write(settings, true) + "\n");
            Console.WriteLine("✔ Строка состояния: лимиты аккаунтов видны в меню и внизу Claude Code");
        }

        Directory.CreateDirectory(CommandsDir);
        for (int i = 0; i < SwitchCommands.GetLength(0); i++)
        {
            string file = Path.Combine(CommandsDir, SwitchCommands[i, 0] + ".md");
            if (File.Exists(file) && File.ReadAllText(file).IndexOf(CommandMarker, StringComparison.Ordinal) < 0)
            {
                Console.WriteLine("· /" + SwitchCommands[i, 0] + " уже есть и не наша — пропускаю.");
                continue;
            }
            File.WriteAllText(file, CommandFile(exe, SwitchCommands[i, 1], SwitchCommands[i, 2]));
            Console.WriteLine("✔ Команда /" + SwitchCommands[i, 0]);
        }

        Console.WriteLine();
        Console.WriteLine("Готово. Запусти cli — откроется меню аккаунтов.");
        return 0;
    }

    // The slash command runs "cli --request <action>" before the prompt reaches Claude.
    // On Windows without Git Bash, Claude Code runs it in PowerShell.
    static string CommandFile(string exe, string action, string description)
    {
        string shell, tool, call;
        if (IsWindows && GitBashPath() == null)
        {
            shell = "powershell";
            call = "& '" + exe.Replace("'", "''") + "' --request " + action;
            tool = "PowerShell(& '" + exe.Replace("'", "''") + "' --request *)";
        }
        else
        {
            shell = "bash";
            call = ShellPath(exe) + " --request " + action;
            tool = "Bash(" + ShellPath(exe) + " --request *)";
        }

        return "---\n"
            + "description: " + description + "\n"
            + "disable-model-invocation: true\n"
            + "allowed-tools: " + tool + "\n"
            + "shell: " + shell + "\n"
            + "---\n"
            + CommandMarker + "\n\n"
            + "!`" + call + "`\n\n"
            + "Если строка выше не «Открываю меню…», передай её пользователю одной фразой и больше ничего не делай.\n";
    }

    static string GitBashPath()
    {
        string configured = Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH");
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
        foreach (string root in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("LOCALAPPDATA") + "\\Programs" })
        {
            if (string.IsNullOrEmpty(root)) continue;
            string bash = Path.Combine(root, "Git", "bin", "bash.exe");
            if (File.Exists(bash)) return bash;
        }
        return null;
    }

    static int Uninstall()
    {
        Dictionary<string, object> settings = ReadJson(SettingsFile);
        object current;
        if (settings != null && settings.TryGetValue("statusLine", out current) && IsOurStatusLine(current))
        {
            settings.Remove("statusLine");
            File.WriteAllText(SettingsFile, Json.Write(settings, true) + "\n");
            Console.WriteLine("✔ Строка состояния убрана");
        }

        for (int i = 0; i < SwitchCommands.GetLength(0); i++)
        {
            string file = Path.Combine(CommandsDir, SwitchCommands[i, 0] + ".md");
            if (!File.Exists(file) || File.ReadAllText(file).IndexOf(CommandMarker, StringComparison.Ordinal) < 0) continue;
            File.Delete(file);
            Console.WriteLine("✔ Команда /" + SwitchCommands[i, 0] + " убрана");
        }

        Console.WriteLine("Аккаунты, сессии и настройки меню (~/.claude-accounts.json) не тронуты.");
        return 0;
    }
}
