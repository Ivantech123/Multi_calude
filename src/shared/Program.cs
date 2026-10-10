// Multi_calude: a launcher for Claude Code with several accounts.
//
//   cli           menu: pick an account, then a session (new or one to resume);
//                 Tab on the session screen switches to another project.
//                 When Claude Code exits, the menu comes back, so the same
//                 session can be resumed under another account.
//   cli 3         account 3 without the menu
//   cli -c        pick an account, then continue the last conversation in this folder
//   cli 3 -c      continue it under account 3
//   cli --statusline
//                 Claude Code status line command: prints the status line and
//                 records the account's rate limits for the menu.
//   cli --request project|account
//                 Run from inside a session (the /switch-project and /switch-account
//                 commands): closes the session and opens the menu.
//   cli-tray.exe  Windows only, the same source built with TRAY (Tray.cs): limits in
//                 the notification area and a notice when an exhausted limit resets.
//
// Labels, favorite projects, renamed sessions and the theme live in ~/.claude-accounts.json.
//
//   cli --install / --uninstall
//                 add or remove the status line and the /switch-* commands in ~/.claude.
//   cli --about   version.
//
// Account 1 uses the default ~/.claude, accounts 2..5 use ~/.claude-acc2..5.
// Shared with account 1: sessions and memory, skills, agents, commands and
// plugins (junctions on Windows, symlinks elsewhere), settings.json (--settings) and user MCP servers
// (--mcp-config).
//
// Build: scripts/build.ps1 or scripts/build.sh (see README).

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

static partial class Program
{
    const int AccountCount = 5;
    const int MaxSessions = 30;
    const int TitleScanLines = 200;
    const int CwdScanLines = 50;
    const int MaxLineLength = 500000;
    const int BarCells = 8;
    const string LimitsFile = "account-limits.json";
    const string SharedMcpFile = "shared-mcp.json";

    // Claude Code dark theme.
    // Accent, Focus and Shine change with the theme (T in the menu).
    static string Accent = "\u001b[38;2;215;119;87m";
    static string Focus = "\u001b[38;2;177;185;249m";
    const string Success = "\u001b[38;2;78;186;101m";
    const string Warning = "\u001b[38;2;255;193;7m";
    const string Error = "\u001b[38;2;255;107;128m";
    const string Dim = "\u001b[38;2;153;153;153m";
    const string Bold = "\u001b[1m";
    const string Reset = "\u001b[0m";

    static string Shine = "\u001b[38;2;255;176;148m";

    static readonly string[] ThemeNames = { "orange", "night", "mono" };
    static readonly string[] ThemeTitles = { "оранжевая", "ночная", "монохром" };

    static void ApplyTheme(string name)
    {
        switch (name)
        {
            case "night":
                Accent = "\u001b[38;2;122;162;255m";
                Focus = "\u001b[38;2;203;166;247m";
                Shine = "\u001b[38;2;196;220;255m";
                break;
            case "mono":
                Accent = "\u001b[38;2;210;210;210m";
                Focus = "\u001b[38;2;255;255;255m";
                Shine = "\u001b[38;2;255;255;255m";
                break;
            default:
                Accent = "\u001b[38;2;215;119;87m";
                Focus = "\u001b[38;2;177;185;249m";
                Shine = "\u001b[38;2;255;176;148m";
                break;
        }
    }

    // Clawd, the Claude Code mascot: head with eyes, body with arms, feet. Every frame is 9 columns.
    const string EyesAhead = " ▐▛███▜▌ ";
    const string EyesLeft = " ▐▜███▜▌ ";
    const string EyesRight = " ▐▛███▛▌ ";
    const string EyesClosed = " ▐█████▌ ";
    const string EyesSad = " ▐▀███▀▌ ";
    const string ArmsDown = "▝▜█████▛▘";
    const string ArmsUp = "▗▜█████▛▖";
    const string FeetApart = "  ▘▘ ▝▝  ";
    const string FeetStep = "  ▝▘ ▝▘  ";
    const int MascotCycle = 200;

    // ---- animation ------------------------------------------------------

    // Claude Code's spinner glyphs, played forward and back.
    static readonly string[] Spinner = { "·", "✢", "*", "✶", "✻", "✽", "✻", "✶", "*", "✢" };
    const int TickMs = 40;
    const int LinesPerTick = 3;

    // Advances once per tick while the menu is open.
    static int frame;

    static string Spin()
    {
        return Spinner[(frame / 3) % Spinner.Length];
    }

    // A highlight that sweeps across the text, as on Claude Code's "Thinking…".
    static string Shimmer(string text, string color)
    {
        int at = (frame / 2) % (text.Length + 12) - 6;
        var result = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            int distance = Math.Abs(i - at);
            result.Append(distance == 0 ? Shine + Bold : distance <= 1 ? Shine : color).Append(text[i]).Append(Reset);
        }
        return result.ToString();
    }

    // Ticks without a key press before the mascot dozes off (about 40 seconds).
    const int SleepAfter = 1000;
    // Ticks the mascot reacts to a key press.
    const int ReactFor = 8;

    // The mascot dances until this frame (Space on the account screen).
    static int danceUntil;
    // Frame the current screen appeared on; the limit bars fill up from it.
    static int screenStart;

    // A one-line mascot for the session and project screens; blinks with the big one.
    static string MiniMascot()
    {
        int t = frame % MascotCycle;
        if (t >= 185 && t < 189) return "▐███▌";
        if (t >= 40 && t < 60) return "▐▜█▜▌";
        if (t >= 60 && t < 80) return "▐▛█▛▌";
        return "▐▛█▜▌";
    }

    // The account under the cursor is out of limit: the mascot is sad.
    static bool sad;

    // Row (1-5) where the little mascot on the account list is; it runs toward the cursor.
    static double cursorPos = 1;

    static double Approach(double position, int target)
    {
        const double step = 0.34;
        if (Math.Abs(target - position) <= step) return target;
        return position + (target > position ? step : -step);
    }

    // A hat for the New Year, a star on the account's anniversary, a pumpkin for Halloween.
    static string[] Decorate(string[] rows, bool anniversary)
    {
        DateTime today = DateTime.Today;
        bool newYear = (today.Month == 12 && today.Day >= 20) || (today.Month == 1 && today.Day <= 7);
        bool halloween = (today.Month == 10 && today.Day >= 25) || (today.Month == 11 && today.Day == 1);
        var result = (string[])rows.Clone();
        if (result[0].Length == 0)
        {
            if (anniversary) result[0] = "    ★    ";
            else if (newYear) result[0] = "   ▗█▖   ";
        }
        if (halloween)
        {
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] == FeetApart || result[i] == FeetStep) result[i] = result[i].Substring(0, 8) + "●";
            }
        }
        return result;
    }

    // ---- toasts ---------------------------------------------------------

    // A short message shown for a few seconds in place of the tip.
    static string toast;
    static int toastUntil;

    static void ShowToast(string message)
    {
        toast = message;
        toastUntil = frame + 75;
    }

    // Frame of the last key press, and whether it moved the cursor up.
    static int lastKey;
    static bool lastKeyUp;

    // Five rows: the mascot with a blank row above and below, so it has room to hop.
    // One cycle: stand, look left and right, wave, hop, blink. It glances where the
    // cursor moves and falls asleep when left alone.
    static string[] MascotRows()
    {
        int t = frame % MascotCycle;
        string eyes = EyesAhead, arms = ArmsDown, feet = FeetApart;
        bool up = false;

        if (frame < danceUntil)
        {
            switch ((frame / 3) % 4)
            {
                case 0: return new[] { EyesAhead, ArmsUp, FeetStep, "", "" };
                case 1: return new[] { "", EyesLeft, ArmsDown, FeetApart, "" };
                case 2: return new[] { EyesAhead, ArmsUp, FeetStep, "", "" };
                default: return new[] { "", EyesRight, ArmsDown, FeetApart, "" };
            }
        }
        if (sad) return new[] { "", EyesSad, ArmsUp, FeetApart, "" };
        if (frame - lastKey > SleepAfter)
        {
            int z = (frame / 10) % 4;
            string above = z == 1 ? "      z  " : z == 2 ? "       z " : z == 3 ? "        Z" : "";
            return new[] { above, EyesClosed, ArmsDown, FeetApart, "" };
        }
        if (frame - lastKey < ReactFor)
        {
            if (lastKeyUp) up = frame - lastKey < 4;
            else eyes = EyesRight;
            return up ? new[] { eyes, ArmsUp, FeetStep, "", "" } : new[] { "", eyes, arms, feet, "" };
        }

        if (t >= 40 && t < 60) eyes = EyesLeft;
        else if (t >= 60 && t < 80) eyes = EyesRight;
        else if (t >= 95 && t < 125) arms = (t / 4) % 2 == 0 ? ArmsUp : ArmsDown;
        else if (t >= 140 && t < 160)
        {
            // Crouch, jump, land.
            up = t >= 144 && t < 154;
            arms = up ? ArmsUp : ArmsDown;
            feet = up ? FeetStep : FeetApart;
        }
        if (t >= 185 && t < 189) eyes = EyesClosed;

        return up ? new[] { eyes, arms, feet, "", "" } : new[] { "", eyes, arms, feet, "" };
    }

    // Walking in place, for the launch screen.
    static string[] MascotWalking()
    {
        bool step = (frame / 3) % 2 == 0;
        return new[] { (frame / 12) % 2 == 0 ? EyesRight : EyesAhead, step ? ArmsUp : ArmsDown, step ? FeetStep : FeetApart };
    }
    static readonly string[] SharedFolders = { "projects", "skills", "agents", "commands", "plugins" };

    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly string MainConfigDir = Path.Combine(Home, ".claude");
    static readonly string LastAccountFile = Path.Combine(Home, ".claude-last-account");
    static readonly string SharedProjects = Path.Combine(MainConfigDir, "projects");
    static readonly string PrefsFile = Path.Combine(Home, ".claude-accounts.json");
    static readonly string TrashDir = Path.Combine(Home, ".claude-accounts-trash");

    // What the user set up in the menu: theme, account labels, favorite projects, session titles.
    class Prefs
    {
        public string Theme = "orange";
        public Dictionary<string, string> Labels = new Dictionary<string, string>();
        public List<string> Favorites = new List<string>();
        public Dictionary<string, string> Titles = new Dictionary<string, string>();
    }

    static Prefs prefs = LoadPrefs();

    static Prefs LoadPrefs()
    {
        var result = new Prefs();
        Dictionary<string, object> root = ReadJson(PrefsFile);
        if (root == null) return result;

        result.Theme = Text(root, "theme") ?? result.Theme;
        CopyTexts(Child(root, "labels"), result.Labels);
        CopyTexts(Child(root, "titles"), result.Titles);
        object favorites;
        if (root.TryGetValue("favorites", out favorites) && favorites is object[])
        {
            foreach (object item in (object[])favorites)
            {
                if (item is string) result.Favorites.Add((string)item);
            }
        }
        return result;
    }

    static void CopyTexts(Dictionary<string, object> source, Dictionary<string, string> target)
    {
        if (source == null) return;
        foreach (KeyValuePair<string, object> pair in source)
        {
            if (pair.Value is string) target[pair.Key] = (string)pair.Value;
        }
    }

    static void SavePrefs()
    {
        var root = new Dictionary<string, object>
        {
            { "theme", prefs.Theme },
            { "labels", prefs.Labels },
            { "favorites", prefs.Favorites },
            { "titles", prefs.Titles },
        };
        try
        {
            File.WriteAllText(PrefsFile, Json.Write(root, true));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static bool IsFavorite(string project)
    {
        foreach (string favorite in prefs.Favorites)
        {
            if (string.Equals(favorite, project, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // One rate-limit window as Claude Code reports it.
    class Window
    {
        public double Used;
        public DateTime Resets;
    }

    // Email is null when nobody is logged in; windows are null when unknown or already reset.
    class Account
    {
        public string Email;
        public string Name;
        public string Plan;
        public Window FiveHour;
        public Window SevenDay;
        public string Label;
        public DateTime? Created;
    }

    // A menu row. Id is a session id (null = new session) or a project path.
    class Row
    {
        public string Id;
        public string Title;
        public string Detail;
    }

    class View
    {
        public Account[] Accounts;
        public List<Row> Sessions;
        public List<Row> Projects;
        public string Project;
        public int Last;
        public bool Returned;
        public int Step;
        public int Account;
        public int Row;
        public int Scroll;
        public string Filter = "";
        // An inline prompt at the bottom: EditLabel, EditTitle or EditDelete; null when none.
        public string Editing;
        public string EditText = "";
        public Row EditTarget;
        public string Today;
    }

    const string EditLabel = "label";
    const string EditTitle = "title";
    const string EditDelete = "delete";

    const int StepAccount = 0;
    const int StepSession = 1;
    const int StepProject = 2;

    static int Main(string[] args)
    {
#if TRAY
        return RunTray();
#endif
        ApplyTheme(prefs.Theme);
        if (args.Length == 1 && args[0] == "--statusline") return StatusLine();
        if (args.Length == 1 && args[0] == "--install") return Install();
        if (args.Length == 1 && args[0] == "--uninstall") return Uninstall();
        if (args.Length == 1 && args[0] == "--about")
        {
            Console.WriteLine("Multi_calude " + Version());
            return 0;
        }
        if (args.Length == 2 && args[0] == "--request") return SendRequest(args[1]);

        // Ctrl+C belongs to Claude Code while it runs; in the menu it quits.
        Console.CancelKeyPress += (sender, e) =>
        {
            if (inMenu) LeaveScreen();
            else e.Cancel = true;
        };

        string project = Environment.CurrentDirectory;
        var rest = new List<string>(args);
        bool interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
        int number = 0;
        int exitCode = 0;
        // What the running session asked for through /switch-project or /switch-account, if anything.
        string request = null;

        bool direct = rest.Count > 0 && TryParseAccount(rest[0], out number);
        if (direct) rest.RemoveAt(0);
        else number = ReadLastAccount();
        if (direct || !interactive)
        {
            exitCode = Launch(number, rest, project, out request);
            if (request == null || !interactive) return exitCode;
            rest.Clear();
        }

        bool returned = request != null;
        while (true)
        {
            bool chooseSession = rest.Count == 0;
            string sessionId;
            int startStep = request == RequestProject ? StepProject : StepAccount;
            if (!Menu(returned, chooseSession, startStep, ref project, out number, out sessionId)) return exitCode;

            var arguments = new List<string>(rest);
            if (sessionId != null)
            {
                arguments.Add("--resume");
                arguments.Add(sessionId);
            }
            exitCode = Launch(number, arguments, project, out request);
            if (!chooseSession && request == null) return exitCode;
            rest.Clear();
            returned = true;
        }
    }

    // ---- requests from a running session --------------------------------

    const string RequestVariable = "CLAUDE_ACCOUNTS_REQUEST";
    const string RequestProject = "project";
    const string RequestAccount = "account";

    // Runs inside Claude Code (the /switch-project and /switch-account commands): asks the launcher
    // that started this session to close it and open the menu.
    static int SendRequest(string action)
    {
        string file = Environment.GetEnvironmentVariable(RequestVariable);
        string message;
        if (action != RequestProject && action != RequestAccount) message = "Неизвестное действие: " + action;
        else if (string.IsNullOrEmpty(file)) message = "Эта сессия запущена не через cli, поэтому меню открыть нельзя.";
        else
        {
            File.WriteAllText(file, action);
            message = "Открываю меню…";
        }

        byte[] bytes = new UTF8Encoding(false).GetBytes(message);
        using (Stream output = Console.OpenStandardOutput()) output.Write(bytes, 0, bytes.Length);
        return message.StartsWith("Открываю") ? 0 : 1;
    }

    static bool TryParseAccount(string text, out int number)
    {
        number = 0;
        if (text.Length != 1 || text[0] < '1' || text[0] >= '1' + AccountCount) return false;
        number = text[0] - '0';
        return true;
    }

    static int ReadLastAccount()
    {
        int number;
        try
        {
            if (TryParseAccount(File.ReadAllText(LastAccountFile).Trim(), out number)) return number;
        }
        catch (IOException) { }
        return 1;
    }

    static string ConfigDir(int number)
    {
        return number == 1 ? MainConfigDir : Path.Combine(Home, ".claude-acc" + number);
    }

    // Where the account keeps .claude.json: next to ~/.claude for account 1, inside the folder otherwise.
    static string StateFile(int number)
    {
        return Path.Combine(number == 1 ? Home : ConfigDir(number), ".claude.json");
    }

    // ---- json -----------------------------------------------------------

    static Dictionary<string, object> ReadJson(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return Json.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static Dictionary<string, object> Child(Dictionary<string, object> entry, string key)
    {
        object value;
        return entry != null && entry.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
    }

    static string Text(Dictionary<string, object> entry, string key)
    {
        object value;
        return entry != null && entry.TryGetValue(key, out value) ? value as string : null;
    }

    static bool Flag(Dictionary<string, object> entry, string key)
    {
        object value;
        return entry.TryGetValue(key, out value) && value is bool && (bool)value;
    }

    static double? Number(Dictionary<string, object> entry, string key)
    {
        object value;
        if (entry == null || !entry.TryGetValue(key, out value) || value == null || value is string || value is bool) return null;
        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---- accounts and limits --------------------------------------------

    static Account LoadAccount(int number)
    {
        var account = new Account();
        string label;
        if (prefs.Labels.TryGetValue(number.ToString(), out label)) account.Label = label;
        Dictionary<string, object> oauth = Child(ReadJson(StateFile(number)), "oauthAccount");
        if (oauth == null) return account;

        DateTime created;
        if (DateTime.TryParse(Text(oauth, "accountCreatedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out created))
            account.Created = created;
        account.Email = Text(oauth, "emailAddress");
        account.Name = Text(oauth, "displayName");
        account.Plan = PlanName(Text(oauth, "organizationType"));

        Dictionary<string, object> limits = ReadJson(Path.Combine(ConfigDir(number), LimitsFile));
        account.FiveHour = ReadWindow(limits, "five_hour");
        account.SevenDay = ReadWindow(limits, "seven_day");
        return account;
    }

    static string PlanName(string organizationType)
    {
        switch (organizationType)
        {
            case "claude_pro": return "Pro";
            case "claude_max": return "Max";
            case "claude_team": return "Team";
            case "claude_enterprise": return "Enterprise";
            default: return null;
        }
    }

    // Null when the window is absent or its reset time has passed.
    static Window ReadWindow(Dictionary<string, object> limits, string key)
    {
        Dictionary<string, object> window = Child(limits, key);
        double? used = Number(window, "used_percentage");
        double? resets = Number(window, "resets_at");
        if (used == null || resets == null) return null;

        DateTime time = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(resets.Value).ToLocalTime();
        return time > DateTime.Now ? new Window { Used = used.Value, Resets = time } : null;
    }

    static Window ExhaustedWindow(Account account)
    {
        if (account.FiveHour != null && account.FiveHour.Used >= 100) return account.FiveHour;
        if (account.SevenDay != null && account.SevenDay.Used >= 100) return account.SevenDay;
        return null;
    }

    static string ResetTime(DateTime time)
    {
        return time.ToString(time.Date == DateTime.Today ? "HH:mm" : "dd.MM HH:mm");
    }

    static string UsageColor(double used)
    {
        return used >= 100 ? Error : used >= 80 ? Warning : Accent;
    }

    // "████░░░░ 5ч 42% · сброс 18:00 · 7д 12%", or what is exhausted and until when.
    static string LimitsText(Account account)
    {
        Window exhausted = ExhaustedWindow(account);
        if (exhausted != null)
        {
            return Error + (exhausted == account.FiveHour ? "лимит исчерпан" : "недельный лимит исчерпан")
                + " · сброс " + ResetTime(exhausted.Resets) + Reset;
        }

        string text = "";
        if (account.FiveHour != null)
        {
            double grow = Math.Min(1.0, (frame - screenStart) / 20.0);
            double used = account.FiveHour.Used * grow;
            int filled = (int)Math.Round(Math.Min(100, used) / 100 * BarCells);
            text = UsageColor(used) + new string('█', filled) + Reset + Dim + new string('░', BarCells - filled)
                + " 5ч " + Reset + Percent(used) + Dim + " · сброс " + ResetTime(account.FiveHour.Resets) + Reset;
        }
        if (account.SevenDay != null)
            text += Dim + (text.Length > 0 ? " · " : "") + "7д " + Reset + Percent(account.SevenDay.Used);
        return text;
    }

    static string Percent(double used)
    {
        return UsageColor(used) + (int)Math.Floor(used) + "%" + Reset;
    }

    // ---- status line ----------------------------------------------------

    // Runs as the Claude Code status line command: saves the limits it is given and prints one line.
    static int StatusLine()
    {
        string dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (string.IsNullOrEmpty(dir)) dir = MainConfigDir;

        Dictionary<string, object> input = null;
        try
        {
            string json = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)).ReadToEnd();
            input = Json.Parse(json) as Dictionary<string, object>;
        }
        catch (Exception) { }

        // Absent before the first response of a session: keep what the last session saw.
        Dictionary<string, object> limits = Child(input, "rate_limits");
        if (limits != null)
        {
            try
            {
                File.WriteAllText(Path.Combine(dir, LimitsFile), Json.Write(limits, false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        var parts = new List<string>();
        string model = Text(Child(input, "model"), "display_name");
        if (model != null) parts.Add(Accent + model + Reset);

        Match match = Regex.Match(dir.TrimEnd('\\', '/'), @"\.claude-acc(\d)$");
        parts.Add(Dim + "аккаунт " + (match.Success ? match.Groups[1].Value : "1") + Reset);

        double? context = Number(Child(input, "context_window"), "used_percentage");
        if (context != null) parts.Add(Dim + "контекст " + Reset + Percent(context.Value));

        Window fiveHour = ReadWindow(limits, "five_hour");
        if (fiveHour != null)
            parts.Add(Dim + "5ч " + Reset + Percent(fiveHour.Used) + Dim + " до " + ResetTime(fiveHour.Resets) + Reset);
        Window sevenDay = ReadWindow(limits, "seven_day");
        if (sevenDay != null) parts.Add(Dim + "7д " + Reset + Percent(sevenDay.Used));

        byte[] line = new UTF8Encoding(false).GetBytes(string.Join(Dim + " · " + Reset, parts));
        using (Stream output = Console.OpenStandardOutput()) output.Write(line, 0, line.Length);
        return 0;
    }

    // ---- sessions and projects ------------------------------------------

    static string ProjectFolder(string project)
    {
        return Path.Combine(SharedProjects, Regex.Replace(project, "[^a-zA-Z0-9]", "-"));
    }

    static FileInfo[] SessionFiles(string folder)
    {
        FileInfo[] files = new DirectoryInfo(folder).GetFiles("*.jsonl");
        Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
        return files;
    }

    // Sessions started in the project folder, newest first.
    static List<Row> LoadSessions(string project)
    {
        var sessions = new List<Row>();
        string folder = ProjectFolder(project);
        if (!Directory.Exists(folder)) return sessions;

        DateTime now = DateTime.Now;
        foreach (FileInfo file in SessionFiles(folder))
        {
            if (sessions.Count >= MaxSessions) break;
            string title, branch, custom;
            string id = Path.GetFileNameWithoutExtension(file.Name);
            ReadSessionHead(file.FullName, out title, out branch);
            if (prefs.Titles.TryGetValue(id, out custom)) title = custom;
            if (title == null) continue;
            sessions.Add(new Row
            {
                Id = id,
                Title = title,
                Detail = branch == null ? Age(file.LastWriteTime, now) : Age(file.LastWriteTime, now) + " · " + branch,
            });
        }
        return sessions;
    }

    // Title: a stored title or summary if there is one, otherwise the first thing the user typed.
    static void ReadSessionHead(string path, out string title, out string branch)
    {
        title = null;
        branch = null;
        try
        {
            using (StreamReader reader = OpenShared(path))
            {
                string line;
                for (int i = 0; i < TitleScanLines && (line = reader.ReadLine()) != null; i++)
                {
                    if (line.Length == 0 || line.Length > MaxLineLength) continue;
                    if (!line.Contains("\"type\":\"user\"") && !line.Contains("-title\"") && !line.Contains("\"type\":\"summary\""))
                        continue;

                    Dictionary<string, object> entry;
                    try
                    {
                        entry = Json.Parse(line) as Dictionary<string, object>;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (entry == null) continue;

                    string type = Text(entry, "type");
                    string stored = type == "summary" ? Text(entry, "summary")
                        : type == "custom-title" ? Text(entry, "customTitle")
                        : type == "ai-title" ? Text(entry, "aiTitle")
                        : null;
                    if (!string.IsNullOrWhiteSpace(stored))
                    {
                        title = Collapse(stored);
                        return;
                    }
                    if (type == "user" && !Flag(entry, "isMeta") && !Flag(entry, "isSidechain"))
                    {
                        if (title == null) title = UserText(entry);
                        string value = Text(entry, "gitBranch");
                        if (branch == null && !string.IsNullOrEmpty(value) && value != "HEAD") branch = value;
                    }
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // The session file may be open in a running Claude Code.
    static StreamReader OpenShared(string path)
    {
        return new StreamReader(
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), Encoding.UTF8);
    }

    static string UserText(Dictionary<string, object> entry)
    {
        object content;
        Dictionary<string, object> message = Child(entry, "message");
        if (message == null || !message.TryGetValue("content", out content)) return null;

        if (content is string) return TypedText((string)content);
        var blocks = content as object[];
        if (blocks == null) return null;
        foreach (object block in blocks)
        {
            var item = block as Dictionary<string, object>;
            if (item == null || Text(item, "type") != "text") continue;
            string text = TypedText(Text(item, "text"));
            if (text != null) return text;
        }
        return null;
    }

    // Null for text the harness injected rather than the user typed.
    static string TypedText(string text)
    {
        if (text == null) return null;
        text = Collapse(text);
        if (text.Length == 0 || text.StartsWith("<") || text.StartsWith("Caveat:") || text.StartsWith("[Request interrupted"))
            return null;
        return text;
    }

    // Every folder that has sessions and still exists, most recently used first; the current one always listed.
    static List<Row> LoadProjects(string current)
    {
        var projects = new List<Row>();
        var times = new List<DateTime>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DateTime now = DateTime.Now;

        if (Directory.Exists(SharedProjects))
        {
            foreach (string folder in Directory.GetDirectories(SharedProjects))
            {
                FileInfo[] files = SessionFiles(folder);
                string path = null;
                for (int i = 0; i < files.Length && i < 3 && path == null; i++) path = ReadCwd(files[i].FullName);
                if (path == null || !Directory.Exists(path) || !seen.Add(path)) continue;

                projects.Add(new Row
                {
                    Id = path,
                    Title = path,
                    Detail = files.Length + " " + Plural(files.Length, "сессия", "сессии", "сессий")
                        + " · " + Age(files[0].LastWriteTime, now),
                });
                times.Add(files[0].LastWriteTime);
            }
        }

        Row[] sorted = projects.ToArray();
        Array.Sort(times.ToArray(), sorted);
        Array.Reverse(sorted);
        // Favorites first, each group still newest first.
        projects = new List<Row>();
        foreach (Row row in sorted) if (IsFavorite(row.Id)) projects.Add(row);
        foreach (Row row in sorted) if (!IsFavorite(row.Id)) projects.Add(row);
        if (seen.Add(current)) projects.Insert(0, new Row { Id = current, Title = current, Detail = "сессий пока нет" });
        return projects;
    }

    // "Сегодня: 4 сессии в 2 проектах" over every project.
    static string TodayText()
    {
        int sessions = 0, projects = 0;
        DateTime today = DateTime.Today;
        try
        {
            foreach (string folder in Directory.GetDirectories(SharedProjects))
            {
                int count = 0;
                foreach (FileInfo file in new DirectoryInfo(folder).GetFiles("*.jsonl"))
                {
                    if (file.LastWriteTime >= today) count++;
                }
                if (count == 0) continue;
                sessions += count;
                projects++;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        if (sessions == 0) return "Сегодня сессий ещё не было";
        return "Сегодня: " + sessions + " " + Plural(sessions, "сессия", "сессии", "сессий")
            + " в " + projects + " " + Plural(projects, "проекте", "проектах", "проектах");
    }

    // Moves the session (and its folder of tool results) to ~/.claude-accounts-trash.
    static void DeleteSession(View view, Row row)
    {
        string folder = ProjectFolder(view.Project);
        string trash = Path.Combine(TrashDir, Path.GetFileName(folder));
        try
        {
            Directory.CreateDirectory(trash);
            string name = row.Id;
            if (File.Exists(Path.Combine(trash, name + ".jsonl"))) name += "-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Move(Path.Combine(folder, row.Id + ".jsonl"), Path.Combine(trash, name + ".jsonl"));
            string extra = Path.Combine(folder, row.Id);
            if (Directory.Exists(extra)) Directory.Move(extra, Path.Combine(trash, name));

            view.Sessions.Remove(row);
            view.Row = Math.Max(0, Math.Min(view.Row, Rows(view).Count - 1));
            ShowToast("Сессия перенесена в корзину: " + trash.Replace(Home, "~"));
        }
        catch (IOException)
        {
            ShowToast(Error + "Не удалось удалить: файл занят — сессия сейчас открыта?" + Reset);
        }
        catch (UnauthorizedAccessException)
        {
            ShowToast(Error + "Не удалось удалить: нет доступа к файлу" + Reset);
        }
    }

    static void ToggleFavorite(View view, string project)
    {
        bool add = !IsFavorite(project);
        prefs.Favorites.RemoveAll(item => string.Equals(item, project, StringComparison.OrdinalIgnoreCase));
        if (add) prefs.Favorites.Add(project);
        SavePrefs();

        view.Projects = LoadProjects(view.Project);
        List<Row> rows = Rows(view);
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id == project) view.Row = i;
        }
        ShowToast(add ? "★ В избранном — проект теперь всегда сверху" : "Убрано из избранного");
    }

    // The folder a session was started in.
    static string ReadCwd(string path)
    {
        try
        {
            using (StreamReader reader = OpenShared(path))
            {
                string line;
                for (int i = 0; i < CwdScanLines && (line = reader.ReadLine()) != null; i++)
                {
                    if (line.Length > MaxLineLength) continue;
                    Match match = Regex.Match(line, "\"cwd\":(\"(?:[^\"\\\\]|\\\\.)*\")");
                    if (match.Success) return Json.Parse(match.Groups[1].Value) as string;
                }
            }
        }
        catch (Exception) { }
        return null;
    }

    static string Collapse(string text)
    {
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    static string Age(DateTime time, DateTime now)
    {
        TimeSpan span = now - time;
        if (span.TotalMinutes < 1) return "только что";
        if (span.TotalHours < 1) return (int)span.TotalMinutes + " мин назад";
        if (span.TotalDays < 1) return (int)span.TotalHours + " ч назад";
        if (span.TotalDays < 7) return (int)span.TotalDays + " дн назад";
        return time.ToString("dd.MM.yyyy");
    }

    static string Plural(int count, string one, string few, string many)
    {
        int tens = count % 100, units = count % 10;
        if (tens >= 11 && tens <= 14) return many;
        return units == 1 ? one : units >= 2 && units <= 4 ? few : many;
    }

    // ---- text metrics ---------------------------------------------------

    static int CodePointAt(string text, int index, out int length)
    {
        if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            length = 2;
            return char.ConvertToUtf32(text[index], text[index + 1]);
        }
        length = 1;
        return text[index];
    }

    // Terminal columns a code point takes.
    static int CharWidth(int cp)
    {
        if (cp < 0x20 || (cp >= 0x7F && cp < 0xA0)) return 0;
        if ((cp >= 0x0300 && cp <= 0x036F) || (cp >= 0x200B && cp <= 0x200F) || (cp >= 0xFE00 && cp <= 0xFE0F) || cp == 0xFEFF)
            return 0;
        if ((cp >= 0x1100 && cp <= 0x115F) || (cp >= 0x2E80 && cp <= 0xA4CF) || (cp >= 0xAC00 && cp <= 0xD7A3)
            || (cp >= 0xF900 && cp <= 0xFAFF) || (cp >= 0xFE30 && cp <= 0xFE4F) || (cp >= 0xFF00 && cp <= 0xFF60)
            || (cp >= 0xFFE0 && cp <= 0xFFE6) || (cp >= 0x1F300 && cp <= 0x1FAFF) || (cp >= 0x20000 && cp <= 0x3FFFD))
            return 2;
        return 1;
    }

    // Width of text that may contain color escapes.
    static int Width(string text)
    {
        int width = 0;
        for (int i = 0; i < text.Length;)
        {
            if (text[i] == '\u001b')
            {
                i = EscapeEnd(text, i);
                continue;
            }
            int length;
            width += CharWidth(CodePointAt(text, i, out length));
            i += length;
        }
        return width;
    }

    static int EscapeEnd(string text, int index)
    {
        int end = index + 1;
        while (end < text.Length && !char.IsLetter(text[end])) end++;
        return Math.Min(end + 1, text.Length);
    }

    // Cuts text (plain or colored) to the given width, ending it with the ellipsis.
    static string Clip(string text, int width, string ellipsis)
    {
        if (Width(text) <= width) return text;
        int room = Math.Max(0, width - ellipsis.Length);
        var result = new StringBuilder();
        int used = 0;
        for (int i = 0; i < text.Length;)
        {
            if (text[i] == '\u001b')
            {
                int end = EscapeEnd(text, i);
                result.Append(text, i, end - i);
                i = end;
                continue;
            }
            int length;
            int charWidth = CharWidth(CodePointAt(text, i, out length));
            if (used + charWidth > room) break;
            result.Append(text, i, length);
            used += charWidth;
            i += length;
        }
        return result + ellipsis;
    }

    static string Fit(string text, int width)
    {
        return Clip(text, width, "…");
    }

    static string FitLeft(string text, int width)
    {
        return text.Length <= width ? text : "…" + text.Substring(text.Length - width + 1);
    }

    static string Pad(string text, int width)
    {
        return text + new string(' ', Math.Max(0, width - Width(text)));
    }

    static string Center(string text, int width)
    {
        int space = Math.Max(0, width - Width(text));
        return new string(' ', space / 2) + text + new string(' ', space - space / 2);
    }

    // ---- layout ---------------------------------------------------------

    static List<string> Compose(View view, int width, int height)
    {
        var lines = new List<string>();
        if (view.Step == StepAccount)
        {
            List<string> body = AccountLines(view, width);
            if (width >= 50 && height >= body.Count + 13) lines.AddRange(WelcomeBox(view, width));
            else lines.Add(CompactHeader(view, width));
            lines.Add("");
            lines.AddRange(body);
        }
        else
        {
            lines.AddRange(ListLines(view, width, height));
        }

        if (lines.Count > height) lines.RemoveRange(height, lines.Count - height);
        for (int i = 0; i < lines.Count; i++)
        {
            if (Width(lines[i]) > width) lines[i] = Clip(lines[i], width, "") + Reset;
        }
        return lines;
    }

    static string CompactHeader(View view, int width)
    {
        return " " + Accent + Spin() + " " + Reset + Shimmer("Multi_calude", Bold) + Dim + " · "
            + FitLeft(view.Project, Math.Max(10, width - 30)) + Reset;
    }

    // The Claude Code welcome box: mascot on the left, hints on the right.
    static List<string> WelcomeBox(View view, int width)
    {
        int inner = Math.Min(width - 2, 88);
        bool twoColumns = inner >= 78;
        int left = twoColumns ? 44 : inner;
        int right = inner - left - 1;

        Account last = view.Accounts[view.Last - 1];
        int connected = 0, exhausted = 0;
        foreach (Account account in view.Accounts)
        {
            if (account.Email != null) connected++;
            if (ExhaustedWindow(account) != null) exhausted++;
        }

        bool anniversary = last.Created != null && last.Created.Value.Month == DateTime.Today.Month
            && last.Created.Value.Day == DateTime.Today.Day && last.Created.Value.Year < DateTime.Today.Year;
        string greeting = (anniversary ? "С годовщиной" : Greeting()) + (last.Name != null ? ", " + last.Name + "!" : "!");
        sad = ExhaustedWindow(view.Accounts[view.Account - 1]) != null;
        string[] mascot = Decorate(MascotRows(), anniversary);
        string info = last.Email == null ? "вход не выполнен" : last.Plan == null ? last.Email : last.Email + " · " + last.Plan;
        var leftRows = new[]
        {
            "",
            Bold + Fit(greeting, left - 2) + Reset,
            Accent + mascot[0] + Reset,
            Accent + mascot[1] + Reset,
            Accent + mascot[2] + Reset,
            Accent + mascot[3] + Reset,
            Accent + mascot[4] + Reset,
            Dim + Fit(info, left - 2) + Reset,
            Dim + FitLeft(view.Project, left - 2) + Reset,
        };
        var rightRows = new[]
        {
            Accent + Bold + "Как пользоваться" + Reset,
            "Выбери аккаунт, затем сессию",
            "Кончился лимит — выйди (/exit)",
            "и продолжи под другим аккаунтом",
            Accent + new string('─', Math.Max(0, right - 2)) + Reset,
            Accent + Bold + "Аккаунты" + Reset,
            "Подключено: " + connected + " из " + AccountCount,
            exhausted > 0 ? Error + "Лимит исчерпан: " + exhausted + Reset : Dim + "История и настройки — общие" + Reset,
            Dim + view.Today + Reset,
        };

        string edge = Accent + "│" + Reset;
        var lines = new List<string>();
        string title = Accent + "╭─ " + Spin() + " " + Reset + Shimmer("Multi_calude", Bold) + Dim + " v" + Version() + " " + Reset;
        lines.Add(title + Accent + new string('─', Math.Max(0, inner - Width(title) + 1)) + "╮" + Reset);
        int month = DateTime.Now.Month;
        if (month == 12 || month <= 2) AddSnow(leftRows, left);
        for (int i = 0; i < leftRows.Length; i++)
        {
            string row = edge + Center(leftRows[i], left);
            if (twoColumns) row += edge + " " + Pad(Fit(rightRows[i], right - 2), right - 1);
            lines.Add(row + edge);
        }
        lines.Add(Accent + "╰" + new string('─', inner) + "╯" + Reset);
        return lines;
    }

    static List<string> AccountLines(View view, int width)
    {
        var lines = new List<string>();
        if (view.Returned)
        {
            Window exhausted = ExhaustedWindow(view.Accounts[view.Last - 1]);
            lines.Add(exhausted != null
                ? " " + Error + Spin() + " Лимит аккаунта " + view.Last + " исчерпан до " + ResetTime(exhausted.Resets) + "." + Reset
                    + " Продолжи сессию под другим аккаунтом."
                : " " + Accent + Spin() + " " + Reset + "Сессия закрыта. Её можно продолжить под другим аккаунтом.");
            lines.Add("");
        }
        lines.Add(" " + Bold + "Выбери аккаунт" + Reset);
        lines.Add(" " + Dim + "Под каким аккаунтом запустить Claude Code." + Reset);
        lines.Add("");

        var labels = new string[AccountCount];
        var owners = new string[AccountCount];
        int labelWidth = 0, ownerWidth = 0;
        for (int i = 0; i < AccountCount; i++)
        {
            Account account = view.Accounts[i];
            string name = account.Email ?? "не подключён";
            if (account.Label != null) name = account.Label + " · " + name;
            labels[i] = Fit(name, Math.Max(12, width - 38));
            if (account.Email == null) owners[i] = "Enter, затем /login";
            else if (account.Name != null && account.Plan != null) owners[i] = account.Name + " · " + account.Plan;
            else owners[i] = account.Name ?? account.Plan ?? "";
            labelWidth = Math.Max(labelWidth, Width(labels[i]));
            ownerWidth = Math.Max(ownerWidth, Width(owners[i]));
        }

        // The little mascot stands at the cursor and runs to it when it moves.
        int mascotRow = (int)Math.Round(cursorPos);
        bool running = cursorPos != view.Account;
        for (int i = 0; i < AccountCount; i++)
        {
            Account account = view.Accounts[i];
            bool connected = account.Email != null;

            string runner = running ? (view.Account > cursorPos ? "▐▛█▛▌" : "▐▜█▜▌")
                : ExhaustedWindow(account) != null ? "▐▀█▀▌" : MiniMascot();
            string prefix = i + 1 == mascotRow ? Accent + runner + Reset + " " : "      ";
            string label = (i + 1) + ". " + labels[i];
            if (i + 1 == view.Account) label = Focus + Bold + label + Reset;
            else if (!connected || ExhaustedWindow(account) != null) label = Dim + label + Reset;
            if (i + 1 == view.Last) label += Success + " ✔" + Reset;

            lines.Add(" " + prefix + Pad(label, labelWidth + 7) + Dim + Pad(owners[i], ownerWidth + 3) + Reset + LimitsText(account));
        }

        lines.Add("");
        if (view.Editing == EditLabel) lines.Add(EditLine("Подпись аккаунта " + view.Account, view.EditText));
        else lines.Add(" " + Dim + "Enter — выбрать · ↑↓ или 1-5 — перейти · R — подпись · T — тема · Esc — выход" + Reset);
        lines.Add("");
        lines.Add(ToastOrTip());
        return lines;
    }

    static string EditLine(string prompt, string text)
    {
        return " " + Accent + prompt + ": " + Reset + text + Focus + "▏" + Reset + Dim + "   Enter — сохранить · Esc — отмена" + Reset;
    }

    static string ToastOrTip()
    {
        if (frame < toastUntil) return " " + Accent + "✻ " + Reset + toast;
        return " " + Accent + "※ " + Reset + Dim + Tips[(frame / 150) % Tips.Length] + Reset;
    }

    // Rows of the current list screen that match the filter.
    static List<Row> Rows(View view)
    {
        var rows = new List<Row>();
        if (view.Step == StepSession && view.Filter.Length == 0)
            rows.Add(new Row { Title = "+ Новая сессия", Detail = "начать с чистого листа" });
        foreach (Row row in view.Step == StepSession ? view.Sessions : view.Projects)
        {
            if (view.Filter.Length == 0
                || row.Title.IndexOf(view.Filter, StringComparison.OrdinalIgnoreCase) >= 0
                || row.Detail.IndexOf(view.Filter, StringComparison.OrdinalIgnoreCase) >= 0)
                rows.Add(row);
        }
        return rows;
    }

    // Two lines per row; twelve lines go to the heading, search box, scroll marks and footer.
    static int PageSize(int height)
    {
        return Math.Max(1, (height - 12) / 2);
    }

    // The session and project screens: heading, search box, scrolling two-line rows.
    static List<string> ListLines(View view, int width, int height)
    {
        bool sessions = view.Step == StepSession;
        List<Row> rows = Rows(view);
        int page = PageSize(height);
        if (view.Row < view.Scroll) view.Scroll = view.Row;
        if (view.Row >= view.Scroll + page) view.Scroll = view.Row - page + 1;
        view.Scroll = Math.Max(0, Math.Min(view.Scroll, Math.Max(0, rows.Count - page)));

        var lines = new List<string>();
        if (sessions)
        {
            lines.Add(" " + Accent + MiniMascot() + Reset + " " + Bold + "Выбери сессию" + Reset + Dim + " · аккаунт " + view.Account + " · "
                + (view.Accounts[view.Account - 1].Email ?? "не подключён") + Reset);
            lines.Add(" " + Dim + "Проект: " + Reset + FitLeft(view.Project, Math.Max(10, width - 11)));
        }
        else
        {
            lines.Add(" " + Accent + MiniMascot() + Reset + " " + Bold + "Выбери проект" + Reset);
            lines.Add(" " + Dim + "Папки, в которых уже есть сессии Claude Code." + Reset);
        }

        int box = Math.Max(20, Math.Min(width - 3, 70));
        string query = view.Filter.Length == 0
            ? Dim + "Поиск…" + Reset
            : Fit(view.Filter, box - 6) + Focus + "▏" + Reset;
        lines.Add(" " + Dim + "╭" + new string('─', box) + "╮" + Reset);
        lines.Add(" " + Dim + "│ ⌕ " + Reset + Pad(query, box - 3) + Dim + "│" + Reset);
        lines.Add(" " + Dim + "╰" + new string('─', box) + "╯" + Reset);

        lines.Add(view.Scroll > 0 ? "   " + Dim + "↑ ещё " + view.Scroll + Reset : "");
        int textWidth = Math.Max(10, width - 4);
        for (int i = view.Scroll; i < Math.Min(rows.Count, view.Scroll + page); i++)
        {
            bool favorite = !sessions && IsFavorite(rows[i].Id);
            string star = favorite ? "★ " : "";
            string title = sessions ? Fit(rows[i].Title, textWidth) : FitLeft(rows[i].Title, textWidth - star.Length);
            string detail = rows[i].Detail;
            if (!sessions && string.Equals(rows[i].Id, view.Project, StringComparison.OrdinalIgnoreCase)) detail += " · текущий";
            lines.Add(i == view.Row ? " " + Focus + "❯ " + star + title + Reset
                : "   " + (favorite ? Accent + star + Reset : "") + title);
            lines.Add("   " + Dim + Fit(detail, textWidth) + Reset);
        }
        if (rows.Count == 0)
            lines.Add("   " + Accent + ((frame / 8) % 2 == 0 ? "▐▜█▜▌" : "▐▛█▛▌") + Reset + Dim + " ничего не нашлось — попробуй другое слово" + Reset);

        int below = rows.Count - view.Scroll - page;
        lines.Add(below > 0 ? "   " + Dim + "↓ ещё " + below + Reset : "");

        string enter = sessions ? "Enter — запустить" : "Enter — открыть";
        string tab = sessions ? " · Tab — другой проект" : "";
        string escape = " · Esc — " + (view.Filter.Length == 0 ? "назад" : "сбросить поиск");
        lines.Add(" " + Dim + enter + (width >= 90 ? " · ↑↓ — выбор" + tab + " · печатай для поиска" : tab) + escape + Reset);

        if (view.Editing == EditTitle) lines.Add(EditLine("Новое название", view.EditText));
        else if (view.Editing == EditDelete)
            lines.Add(" " + Error + "Удалить «" + Fit(view.EditTarget.Title, 40) + "»?" + Reset + Dim + "   Y — да · любая другая клавиша — нет" + Reset);
        else if (frame < toastUntil) lines.Add(" " + Accent + "✻ " + Reset + toast);
        else lines.Add(" " + Dim + (sessions ? "F2 — переименовать · Del — удалить в корзину" : "Ctrl+F — избранное") + Reset);
        return lines;
    }

    // ---- menu -----------------------------------------------------------

    // False if the user quit. sessionId is null for a new session; project is where to run.
    static bool Menu(bool returned, bool chooseSession, int startStep, ref string project, out int number, out string sessionId)
    {
        number = 0;
        sessionId = null;

        var view = new View { Returned = returned, Last = ReadLastAccount(), Project = project };
        view.Accounts = new Account[AccountCount];
        for (int i = 0; i < AccountCount; i++) view.Accounts[i] = LoadAccount(i + 1);
        view.Sessions = chooseSession ? LoadSessions(project) : new List<Row>();
        view.Account = returned ? SuggestedAccount(view) : view.Last;
        view.Today = TodayText();
        cursorPos = view.Account;
        // After a run the newest session is the one just closed.
        view.Row = returned && view.Sessions.Count > 0 ? 1 : 0;
        if (startStep == StepProject && chooseSession)
        {
            view.Projects = LoadProjects(project);
            view.Step = StepProject;
            view.Row = 0;
        }
        while (Console.KeyAvailable) Console.ReadKey(true);

        int width = 0, height = 0;
        int shownStep = -1, revealStart = 0;
        EnterScreen();
        try
        {
            while (true)
            {
                ConsoleKeyInfo key;
                if (!WaitKey(ref width, ref height, out key))
                {
                    // A new screen rolls in from the top, a few lines per tick.
                    if (view.Step != shownStep)
                    {
                        shownStep = view.Step;
                        revealStart = frame;
                        screenStart = frame;
                    }
                    cursorPos = Approach(cursorPos, view.Account);
                    List<string> lines = Compose(view, width, height);
                    int shown = (frame - revealStart + 1) * LinesPerTick;
                    if (shown < lines.Count) lines.RemoveRange(shown, lines.Count - shown);
                    Draw(lines);
                    continue;
                }

                lastKey = frame;
                lastKeyUp = key.Key == ConsoleKey.UpArrow;
                if (view.Editing != null)
                {
                    HandleEdit(view, key);
                    continue;
                }
                if (view.Step == StepAccount && key.Key == ConsoleKey.Spacebar)
                {
                    danceUntil = frame + 50;
                    continue;
                }
                if (view.Step == StepAccount && key.Key == ConsoleKey.T)
                {
                    int next = (Array.IndexOf(ThemeNames, prefs.Theme) + 1) % ThemeNames.Length;
                    prefs.Theme = ThemeNames[next];
                    ApplyTheme(prefs.Theme);
                    SavePrefs();
                    ShowToast("Тема: " + ThemeTitles[next]);
                    continue;
                }
                if (view.Step == StepAccount && (key.Key == ConsoleKey.R || key.Key == ConsoleKey.F2))
                {
                    view.Editing = EditLabel;
                    view.EditText = view.Accounts[view.Account - 1].Label ?? "";
                    continue;
                }
                if (view.Step != StepAccount)
                {
                    List<Row> current = Rows(view);
                    Row selected = current.Count > 0 ? current[view.Row] : null;
                    bool session = view.Step == StepSession && selected != null && selected.Id != null;
                    if (session && key.Key == ConsoleKey.F2)
                    {
                        view.Editing = EditTitle;
                        view.EditText = selected.Title;
                        view.EditTarget = selected;
                        continue;
                    }
                    if (session && key.Key == ConsoleKey.Delete)
                    {
                        view.Editing = EditDelete;
                        view.EditTarget = selected;
                        continue;
                    }
                    if (view.Step == StepProject && selected != null && key.Key == ConsoleKey.F
                        && (key.Modifiers & ConsoleModifiers.Control) != 0)
                    {
                        ToggleFavorite(view, selected.Id);
                        continue;
                    }
                }

                if (view.Step == StepAccount)
                {
                    int typed;
                    bool chosen = false;
                    if (key.Key == ConsoleKey.UpArrow) view.Account = view.Account == 1 ? AccountCount : view.Account - 1;
                    else if (key.Key == ConsoleKey.DownArrow) view.Account = view.Account == AccountCount ? 1 : view.Account + 1;
                    else if (key.Key == ConsoleKey.Home) view.Account = 1;
                    else if (key.Key == ConsoleKey.End) view.Account = AccountCount;
                    else if (key.Key == ConsoleKey.Escape)
                    {
                        PlayGoodbye(view);
                        return false;
                    }
                    else if (key.Key == ConsoleKey.Enter) chosen = true;
                    else if (TryParseAccount(key.KeyChar.ToString(), out typed))
                    {
                        view.Account = typed;
                        chosen = true;
                    }

                    if (chosen && !chooseSession)
                    {
                        number = view.Account;
                        PlayLaunch(view, null);
                        return true;
                    }
                    if (chosen) view.Step = StepSession;
                }
                else
                {
                    List<Row> rows = Rows(view);
                    int page = PageSize(height);
                    int end = Math.Max(0, rows.Count - 1);
                    if (key.Key == ConsoleKey.UpArrow) view.Row = view.Row <= 0 ? end : view.Row - 1;
                    else if (key.Key == ConsoleKey.DownArrow) view.Row = view.Row >= end ? 0 : view.Row + 1;
                    else if (key.Key == ConsoleKey.PageUp) view.Row = Math.Max(0, view.Row - page);
                    else if (key.Key == ConsoleKey.PageDown) view.Row = Math.Min(end, view.Row + page);
                    else if (key.Key == ConsoleKey.Home) view.Row = 0;
                    else if (key.Key == ConsoleKey.End) view.Row = end;
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        if (rows.Count > 0 && view.Step == StepSession)
                        {
                            number = view.Account;
                            sessionId = rows[view.Row].Id;
                            project = view.Project;
                            PlayLaunch(view, rows[view.Row]);
                            return true;
                        }
                        if (rows.Count > 0)
                        {
                            view.Project = rows[view.Row].Id;
                            view.Sessions = LoadSessions(view.Project);
                            OpenList(view, StepSession);
                        }
                    }
                    else if (key.Key == ConsoleKey.Tab && view.Step == StepSession)
                    {
                        if (view.Projects == null) view.Projects = LoadProjects(view.Project);
                        OpenList(view, StepProject);
                    }
                    else if (key.Key == ConsoleKey.Escape)
                    {
                        if (view.Filter.Length > 0) SetFilter(view, "");
                        else OpenList(view, view.Step == StepProject ? StepSession : StepAccount);
                    }
                    else if (key.Key == ConsoleKey.Backspace)
                    {
                        if (view.Filter.Length > 0) SetFilter(view, view.Filter.Substring(0, view.Filter.Length - 1));
                    }
                    else if (!char.IsControl(key.KeyChar))
                    {
                        SetFilter(view, view.Filter + key.KeyChar);
                    }
                }
            }
        }
        finally
        {
            LeaveScreen();
        }
    }

    // A short "starting" screen between the menu and Claude Code.
    static void PlayLaunch(View view, Row session)
    {
        Account account = view.Accounts[view.Account - 1];
        string what = session == null || session.Id == null ? "Новая сессия" : Fit(session.Title, 60);
        int width = Console.WindowWidth, height = Console.WindowHeight;
        string verb = LaunchVerbs[new Random().Next(LaunchVerbs.Length)];
        for (int i = 0; i < 20; i++)
        {
            var lines = new List<string>();
            for (int pad = 0; pad < Math.Max(0, height / 2 - 7); pad++) lines.Add("");
            foreach (string row in MascotWalking()) lines.Add(Center(Accent + row + Reset, width));
            lines.Add("");
            lines.Add(Center(Accent + Spin() + " " + Reset + Shimmer(verb, Bold) + Dim + "…" + Reset, width));
            lines.Add("");
            lines.Add(Center(Dim + "аккаунт " + view.Account + " · " + (account.Email ?? "не подключён") + Reset, width));
            lines.Add(Center(Dim + what + Reset, width));
            Draw(lines);
            Thread.Sleep(TickMs);
            frame++;
        }
    }

    // The mascot waves goodbye when the menu is closed with Esc.
    static void PlayGoodbye(View view)
    {
        int width = Console.WindowWidth, height = Console.WindowHeight;
        Account last = view.Accounts[view.Last - 1];
        for (int i = 0; i < 16; i++)
        {
            var lines = new List<string>();
            for (int pad = 0; pad < Math.Max(0, height / 2 - 4); pad++) lines.Add("");
            lines.Add(Center(Accent + (i < 12 ? EyesAhead : EyesClosed) + Reset, width));
            lines.Add(Center(Accent + ((i / 3) % 2 == 0 ? ArmsUp : ArmsDown) + Reset, width));
            lines.Add(Center(Accent + FeetApart + Reset, width));
            lines.Add("");
            lines.Add(Center(Bold + (last.Name != null ? "До встречи, " + last.Name + "!" : "До встречи!") + Reset, width));
            Draw(lines);
            Thread.Sleep(TickMs);
            frame++;
        }
    }

    // Flakes drifting down the empty rows of the welcome box.
    static void AddSnow(string[] rows, int width)
    {
        int[] columns = { 3, 9, 15, 29, 35, 41 };
        for (int k = 0; k < columns.Length; k++)
        {
            if (columns[k] >= width) continue;
            int row = (frame / 6 + k * 4) % rows.Length;
            string plain = StripColor(rows[row]);
            if (plain.Trim(' ', '*', '·').Length != 0) continue;
            char[] cells = plain.PadRight(width).Substring(0, width).ToCharArray();
            cells[columns[k]] = k % 2 == 0 ? '*' : '·';
            rows[row] = Dim + " " + new string(cells).Substring(1) + Reset;
        }
    }

    static string StripColor(string text)
    {
        return Regex.Replace(text, "\u001b\\[[0-9;?]*[a-zA-Z]", "");
    }

    static string Greeting()
    {
        DateTime today = DateTime.Today;
        if ((today.Month == 12 && today.Day == 31) || (today.Month == 1 && today.Day <= 7)) return "С Новым годом";
        if (today.Month == 12 && today.Day >= 20) return "С наступающим";
        int hour = DateTime.Now.Hour;
        if (hour >= 5 && hour < 12) return "Доброе утро";
        if (hour >= 12 && hour < 18) return "Добрый день";
        if (hour >= 18 && hour < 23) return "Добрый вечер";
        return "Доброй ночи";
    }

    static readonly string[] Tips =
    {
        "/switch-account в чате — продолжить сессию под другим аккаунтом",
        "/switch-project в чате — перейти в другой проект",
        "Tab на экране сессий — список всех проектов",
        "На экране сессий просто печатай — это поиск",
        "Лимиты аккаунтов видно здесь и в строке внизу Claude Code",
        "cli 3 -c — сразу продолжить последнюю сессию под аккаунтом 3",
        "Нажми пробел — и кое-кто станцует",
        "Оставь меню в покое — и кое-кто задремлет",
        "R — подписать аккаунт, например «рабочий»",
        "T — сменить тему: оранжевая, ночная, монохром",
        "F2 на сессии — переименовать, Del — убрать в корзину",
        "Ctrl+F на проекте — в избранное, он будет всегда сверху",
        "Значок в трее покажет лимиты и скажет, когда аккаунт снова доступен",
    };

    static readonly string[] LaunchVerbs =
    {
        "Запускаю Claude Code", "Разминаю лапки", "Завариваю чай", "Точу карандаши",
        "Раскладываю мысли по полочкам", "Протираю очки", "Собираю контекст", "Включаю лампу",
    };

    // The last account, unless its limit is exhausted: then the connected account with the most room.
    static int SuggestedAccount(View view)
    {
        if (ExhaustedWindow(view.Accounts[view.Last - 1]) == null) return view.Last;

        int best = view.Last;
        double bestUsed = double.MaxValue;
        for (int i = 0; i < AccountCount; i++)
        {
            Account account = view.Accounts[i];
            if (account.Email == null || ExhaustedWindow(account) != null) continue;
            double used = account.FiveHour == null ? 0 : account.FiveHour.Used;
            if (used < bestUsed)
            {
                best = i + 1;
                bestUsed = used;
            }
        }
        return best;
    }

    // Keys while an inline prompt is open.
    static void HandleEdit(View view, ConsoleKeyInfo key)
    {
        if (view.Editing == EditDelete)
        {
            if (key.Key == ConsoleKey.Y) DeleteSession(view, view.EditTarget);
            view.Editing = null;
            return;
        }

        if (key.Key == ConsoleKey.Escape) view.Editing = null;
        else if (key.Key == ConsoleKey.Backspace)
        {
            if (view.EditText.Length > 0) view.EditText = view.EditText.Substring(0, view.EditText.Length - 1);
        }
        else if (key.Key == ConsoleKey.Enter)
        {
            string text = Collapse(view.EditText);
            if (view.Editing == EditLabel)
            {
                string number = view.Account.ToString();
                if (text.Length == 0) prefs.Labels.Remove(number);
                else prefs.Labels[number] = text;
                view.Accounts[view.Account - 1].Label = text.Length == 0 ? null : text;
                ShowToast(text.Length == 0 ? "Подпись убрана" : "Подпись сохранена");
            }
            else
            {
                if (text.Length == 0) prefs.Titles.Remove(view.EditTarget.Id);
                else prefs.Titles[view.EditTarget.Id] = text;
                ShowToast(text.Length == 0 ? "Вернул исходное название" : "Сессия переименована");
            }
            SavePrefs();
            if (view.Editing == EditTitle) view.Sessions = LoadSessions(view.Project);
            view.Editing = null;
        }
        else if (!char.IsControl(key.KeyChar) && view.EditText.Length < 60)
        {
            view.EditText += key.KeyChar;
        }
    }

    static void OpenList(View view, int step)
    {
        view.Step = step;
        SetFilter(view, "");
    }

    static void SetFilter(View view, string filter)
    {
        view.Filter = filter;
        view.Row = 0;
        view.Scroll = 0;
    }

    // False when it is time to redraw instead: the window was resized or an animation tick passed.
    static bool WaitKey(ref int width, ref int height, out ConsoleKeyInfo key)
    {
        key = default(ConsoleKeyInfo);
        if (Console.KeyAvailable)
        {
            key = Console.ReadKey(true);
            return true;
        }
        if (Console.WindowWidth != width || Console.WindowHeight != height)
        {
            width = Console.WindowWidth;
            height = Console.WindowHeight;
            lastScreen = null;
            return false;
        }
        Thread.Sleep(TickMs);
        frame++;
        return false;
    }

    // Overwrites in place (no full clear), so the screen does not flicker.
    static void Draw(List<string> lines)
    {
        var screen = new StringBuilder("\u001b[H");
        for (int i = 0; i < lines.Count; i++)
        {
            screen.Append(lines[i]).Append("\u001b[K");
            if (i < lines.Count - 1) screen.Append("\r\n");
        }
        screen.Append("\u001b[J");
        string text = screen.ToString();
        if (text == lastScreen) return;
        lastScreen = text;
        Console.Write(text);
    }

    // ---- console --------------------------------------------------------

    const int StdInput = -10;
    const int StdOutput = -11;
    const uint VirtualTerminalProcessing = 0x0004;

    static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    static string Version()
    {
        Version version = typeof(Program).Assembly.GetName().Version;
        return version.Major + "." + version.Minor + "." + version.Build;
    }

    static bool inMenu;
    static string lastScreen;
    static uint savedMode;
    static Encoding savedEncoding;

    [DllImport("kernel32.dll")]
    static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll")]
    static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll")]
    static extern bool SetConsoleMode(IntPtr handle, uint mode);

    // Switches to the alternate screen so the menu leaves nothing behind.
    static void EnterScreen()
    {
        if (IsWindows)
        {
            IntPtr handle = GetStdHandle(StdOutput);
            GetConsoleMode(handle, out savedMode);
            SetConsoleMode(handle, savedMode | VirtualTerminalProcessing);
            savedEncoding = Console.OutputEncoding;
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        Console.Write("\u001b]0;✻ Multi_calude\u0007\u001b[?1049h\u001b[?25l");
        lastScreen = null;
        inMenu = true;
    }

    static void LeaveScreen()
    {
        if (!inMenu) return;
        inMenu = false;
        Console.Write("\u001b[?25h\u001b[?1049l");
        if (!IsWindows) return;
        SetConsoleMode(GetStdHandle(StdOutput), savedMode);
        Console.OutputEncoding = savedEncoding;
    }

    // ---- launch ---------------------------------------------------------

    static int Launch(int number, List<string> arguments, string project, out string request)
    {
        File.WriteAllText(LastAccountFile, number.ToString());

        Encoding previousEncoding = Console.OutputEncoding;
        if (IsWindows) Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            if (number != 1)
            {
                foreach (string folder in SharedFolders) LinkSharedFolder(number, folder);
                AddSharedConfig(number, arguments);
            }
        }
        finally
        {
            if (IsWindows) Console.OutputEncoding = previousEncoding;
        }
        return RunClaude(number, arguments, project, out request);
    }

    // Makes the account's folder a junction (Windows) or symlink to the same folder of account 1.
    static void LinkSharedFolder(int number, string name)
    {
        string shared = Path.Combine(MainConfigDir, name);
        string link = Path.Combine(ConfigDir(number), name);
        Directory.CreateDirectory(ConfigDir(number));
        Directory.CreateDirectory(shared);

        if (Directory.Exists(link))
        {
            if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) return;
            if (Directory.GetFileSystemEntries(link).Length > 0)
            {
                Console.Error.WriteLine("Внимание: у аккаунта " + number + " своя папка " + link
                    + " — она не общая. Перенеси её содержимое в " + shared + " и удали папку.");
                return;
            }
            Directory.Delete(link);
        }

#if !NETFRAMEWORK
        if (!IsWindows)
        {
            try
            {
                Directory.CreateSymbolicLink(link, shared);
            }
            catch (IOException)
            {
                Console.Error.WriteLine("Внимание: не удалось связать " + link + " с " + shared + " — папка не общая.");
            }
            return;
        }
#endif
        var info = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + shared + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        using (Process process = Process.Start(info))
        {
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                Console.Error.WriteLine("Внимание: не удалось связать " + link + " с " + shared + " — папка не общая.");
        }
    }

    // Passes account 1's settings and user MCP servers to another account, unless the caller passed their own.
    // Appended last: --mcp-config takes every argument that follows it.
    static void AddSharedConfig(int number, List<string> arguments)
    {
        // A bare word first ("mcp", "update") is a claude subcommand, and those reject both flags.
        if (arguments.Count > 0 && Regex.IsMatch(arguments[0], "^[a-z][a-z-]*$")) return;

        string settings = Path.Combine(MainConfigDir, "settings.json");
        if (File.Exists(settings) && !arguments.Contains("--settings"))
        {
            arguments.Add("--settings");
            arguments.Add(settings);
        }

        if (arguments.Contains("--mcp-config")) return;
        Dictionary<string, object> servers = Child(ReadJson(StateFile(1)), "mcpServers");
        if (servers == null || servers.Count == 0) return;
        try
        {
            string file = Path.Combine(ConfigDir(number), SharedMcpFile);
            var config = new Dictionary<string, object> { { "mcpServers", servers } };
            File.WriteAllText(file, Json.Write(config, false));
            arguments.Add("--mcp-config");
            arguments.Add(file);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // request is what the session asked for through SendRequest (it is closed for that), or null.
    static int RunClaude(int number, List<string> arguments, string project, out string request)
    {
        request = null;
        ProcessStartInfo info = ClaudeStartInfo(arguments);
        if (info == null)
        {
            Console.Error.WriteLine("Claude Code не найден. Установи его: https://claude.com/claude-code");
            return 127;
        }
        info.UseShellExecute = false;
        info.WorkingDirectory = project;

        string requestFile = Path.Combine(Path.GetTempPath(), "claude-accounts-" + Process.GetCurrentProcess().Id + ".request");
        File.Delete(requestFile);
        if (number == 1) info.EnvironmentVariables.Remove("CLAUDE_CONFIG_DIR");
        else info.EnvironmentVariables["CLAUDE_CONFIG_DIR"] = ConfigDir(number);
        info.EnvironmentVariables[RequestVariable] = requestFile;

        uint inputMode = 0, outputMode = 0;
        if (IsWindows)
        {
            GetConsoleMode(GetStdHandle(StdInput), out inputMode);
            GetConsoleMode(GetStdHandle(StdOutput), out outputMode);
        }
        try
        {
            using (Process process = Process.Start(info))
            {
                while (!process.WaitForExit(200))
                {
                    if (!File.Exists(requestFile)) continue;
                    request = File.ReadAllText(requestFile).Trim();
                    KillTree(process);
                    RestoreTerminal(inputMode, outputMode);
                }
                return process.ExitCode;
            }
        }
        catch (Win32Exception error)
        {
            Console.Error.WriteLine("Не удалось запустить Claude Code: " + error.Message);
            return 127;
        }
        finally
        {
            File.Delete(requestFile);
        }
    }

    // Claude Code from PATH: claude.exe or the npm claude.cmd on Windows, claude elsewhere.
    static ProcessStartInfo ClaudeStartInfo(List<string> arguments)
    {
        string path = IsWindows ? FindOnPath("claude.exe") ?? FindOnPath("claude.cmd") : FindOnPath("claude");
        if (path == null) return null;

        var all = new List<string>(arguments);
        string program = path;
        if (path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            program = "cmd.exe";
            all.InsertRange(0, new[] { "/c", path });
        }

        var info = new ProcessStartInfo(program);
#if NETFRAMEWORK
        var quoted = new List<string>();
        foreach (string argument in all) quoted.Add(QuoteArgument(argument));
        info.Arguments = string.Join(" ", quoted);
#else
        foreach (string argument in all) info.ArgumentList.Add(argument);
#endif
        return info;
    }

    static string FindOnPath(string name)
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in path.Split(Path.PathSeparator))
        {
            if (dir.Trim().Length == 0) continue;
            try
            {
                string candidate = Path.Combine(dir.Trim().Trim('"'), name);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    static void KillTree(Process process)
    {
#if !NETFRAMEWORK
        process.Kill(true);
        process.WaitForExit();
        return;
#endif
        var info = new ProcessStartInfo("taskkill.exe", "/PID " + process.Id + " /T /F")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using (Process taskkill = Process.Start(info)) taskkill.WaitForExit();
        process.WaitForExit();
    }

    // Claude Code was killed, so it never undid its terminal setup: cursor, bracketed paste,
    // focus and mouse reporting, extended keyboard modes, colors.
    static void RestoreTerminal(uint inputMode, uint outputMode)
    {
        if (!IsWindows)
        {
            Console.Write(TerminalReset);
            try
            {
                using (Process stty = Process.Start(new ProcessStartInfo("stty", "sane") { UseShellExecute = false })) stty.WaitForExit();
            }
            catch (Win32Exception) { }
            return;
        }
        IntPtr output = GetStdHandle(StdOutput);
        SetConsoleMode(output, outputMode | VirtualTerminalProcessing);
        Console.Write(TerminalReset);
        SetConsoleMode(output, outputMode);
        SetConsoleMode(GetStdHandle(StdInput), inputMode);
    }

    const string TerminalReset = "\u001b[?25h\u001b[?2004l\u001b[?1004l\u001b[?1000l\u001b[?1002l\u001b[?1003l\u001b[?1006l\u001b[<u\u001b[>4;0m\u001b[0m\r\n";

    // Quotes one argument by the Windows command-line rules.
    static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return argument;

        var result = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"') result.Append('\\', backslashes * 2 + 1);
            else result.Append('\\', backslashes);
            result.Append(c);
            backslashes = 0;
        }
        result.Append('\\', backslashes * 2);
        result.Append('"');
        return result.ToString();
    }
}
