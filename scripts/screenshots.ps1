# Renders the README images in docs/images from the real menu code with demo data
# (no real accounts or paths). Windows only: needs a built tray (scripts\build.ps1)
# for the shared code and cli.exe for the status line.

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$out = Join-Path $root 'docs\images'
New-Item -ItemType Directory -Force $out | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'TerminalShot.cs') -ReferencedAssemblies System.Drawing

# Load a copy so the build output stays unlocked.
$copy = Join-Path ([IO.Path]::GetTempPath()) "multi_calude-shots-$(Get-Random).exe"
Copy-Item (Join-Path $root 'dist\tray\cli-tray.exe') $copy
$asm = [Reflection.Assembly]::LoadFile($copy)
$P = $asm.GetType('Program')
$flags = [Reflection.BindingFlags]'NonPublic,Static'
function Field($name, $value) { $P.GetField($name, $flags).SetValue($null, $value) }
function Call($name, $arguments) { $P.GetMethod($name, $flags).Invoke($null, $arguments) }
function New-Object2($type) { [Activator]::CreateInstance($asm.GetType("Program+$type"), $true) }

$scale = 2
function Shot($lines, $columns, $title, $file) {
    [TerminalShot]::Render([string[]]$lines, $columns, $title, (Join-Path $out $file), $scale)
    Write-Host "-> docs/images/$file"
}

# ---- demo data ----------------------------------------------------------

function Window($used, $hours) {
    $w = New-Object2 'Window'
    $w.Used = $used
    $w.Resets = (Get-Date).AddHours($hours)
    $w
}
function Account($email, $name, $plan, $label) {
    $a = New-Object2 'Account'
    # PowerShell turns $null into "" for string fields; [NullString] keeps it null.
    $a.Email = if ($email) { $email } else { [NullString]::Value }
    $a.Name = if ($name) { $name } else { [NullString]::Value }
    $a.Plan = if ($plan) { $plan } else { [NullString]::Value }
    $a.Label = if ($label) { $label } else { [NullString]::Value }
    $a
}
$accounts = [Array]::CreateInstance($asm.GetType('Program+Account'), 5)
$accounts[0] = Account 'work@example.com' 'Alex' 'Max' 'рабочий'
$accounts[0].FiveHour = Window 100 1.4
$accounts[1] = Account 'me@example.com' 'Alex' 'Pro' 'личный'
$accounts[1].FiveHour = Window 42 2.6
$accounts[1].SevenDay = Window 12 70
$accounts[2] = Account 'side@example.com' 'Alex' 'Pro' $null
$accounts[2].FiveHour = Window 86 0.7
$accounts[3] = Account $null $null $null $null
$accounts[4] = Account $null $null $null $null

function Rows($items) {
    $list = [Activator]::CreateInstance([Collections.Generic.List`1].MakeGenericType($asm.GetType('Program+Row')))
    foreach ($item in $items) {
        $row = New-Object2 'Row'
        $row.Id = $item[0]; $row.Title = $item[1]; $row.Detail = $item[2]
        $list.Add($row)
    }
    , $list
}
$sessions = Rows @(
    @('a1', 'Рефакторинг авторизации и тесты для API', 'только что · feature/auth'),
    @('a2', 'Миграция базы на PostgreSQL 16', '2 ч назад · main'),
    @('a3', 'Почему падает сборка на CI', 'вчера'),
    @('a4', 'Новый дизайн страницы тарифов', '3 дн назад · design/pricing')
)
$projects = Rows @(
    @('~/projects/web-app', '~/projects/web-app', '24 сессии · только что'),
    @('~/projects/api', '~/projects/api', '11 сессий · 2 ч назад'),
    @('~/projects/mobile', '~/projects/mobile', '6 сессий · вчера'),
    @('~/notes', '~/notes', '2 сессии · 12.10.2026')
)

function View($step) {
    $v = New-Object2 'View'
    $v.Accounts = $accounts; $v.Sessions = $sessions; $v.Projects = $projects
    $v.Project = '~/projects/web-app'; $v.Last = 2; $v.Account = 2; $v.Step = $step
    $v.Today = 'Сегодня: 4 сессии в 2 проектах'
    $v
}

# A calm frame: spinner on ✻, mascot standing, limit bars full, the first tip.
function Calm {
    Field 'frame' 12; Field 'lastKey' 0; Field 'danceUntil' 0; Field 'sad' $false
    Field 'screenStart' (-1000); Field 'cursorPos' ([double]2); Field 'toastUntil' 0
}

# ---- screens ------------------------------------------------------------

Call 'ApplyTheme' @('orange')
Calm
$view = View 0
Shot (Call 'Compose' @($view, 104, 30)) 104 'cli — Multi_calude' 'accounts.png'

Calm
$view = View 1
$view.Row = 1
Shot (Call 'Compose' @($view, 92, 21)) 92 'cli — выбор сессии' 'sessions.png'

Calm
$view = View 2
$view.Row = 0
$prefs = $P.GetField('prefs', $flags).GetValue($null)
$prefs.Favorites.Clear(); $prefs.Favorites.Add('~/projects/web-app')
Shot (Call 'Compose' @($view, 92, 21)) 92 'cli — выбор проекта' 'projects.png'
$prefs.Favorites.Clear()

# ---- themes: the welcome box in each ------------------------------------

$parts = foreach ($theme in 'orange', 'night', 'mono') {
    Call 'ApplyTheme' @($theme)
    Calm
    $lines = Call 'Compose' @((View 0), 92, 30)
    $file = "theme-$theme.png"
    Shot ($lines | Select-Object -First 11) 92 "тема: $theme" $file
    Join-Path $out $file
}
Call 'ApplyTheme' @('orange')

$parts = @($parts | Where-Object { $_ })
$images = $parts | ForEach-Object { [Drawing.Image]::FromFile($_) }
$gap = 24 * $scale
$sheet = New-Object Drawing.Bitmap ($images[0].Width), (($images | Measure-Object Height -Sum).Sum + $gap * 2)
$g = [Drawing.Graphics]::FromImage($sheet)
$y = 0
foreach ($image in $images) { $g.DrawImage($image, 0, $y, $image.Width, $image.Height); $y += $image.Height + $gap }
$g.Dispose()
$sheet.Save((Join-Path $out 'themes.png'), [Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
$images | ForEach-Object { $_.Dispose() }
$parts | Remove-Item
Write-Host '-> docs/images/themes.png'

# ---- the mascot's moods -------------------------------------------------

$accent = "$([char]27)[38;2;215;119;87m"
$dim = "$([char]27)[38;2;153;153;153m"
$reset = "$([char]27)[0m"
$moods = @(
    @{ Caption = 'стоит'; Frame = 12 },
    @{ Caption = 'смотрит'; Frame = 45 },
    @{ Caption = 'машет'; Frame = 96 },
    @{ Caption = 'прыгает'; Frame = 148 },
    @{ Caption = 'грустит'; Frame = 12; Sad = $true },
    @{ Caption = 'спит'; Frame = 1020 },
    @{ Caption = 'годовщина'; Frame = 12; Star = $true }
)
$columns = New-Object 'System.Collections.Generic.List[string[]]'
foreach ($mood in $moods) {
    Field 'frame' $mood.Frame; Field 'lastKey' 0; Field 'danceUntil' 0; Field 'sad' ([bool]$mood.Sad)
    $rows = Call 'MascotRows' @()
    if ($mood.Star) { $rows = Call 'Decorate' @([string[]]$rows, $true) }
    $columns.Add([string[]]$rows)
}
$width = 13
$lines = for ($r = 0; $r -lt 5; $r++) {
    ($columns | ForEach-Object { '  ' + $accent + $_[$r].PadRight(9) + $reset + '  ' }) -join ''
}
$lines += ''
$lines += ($moods | ForEach-Object { $dim + $_.Caption.PadLeft([int](($width + $_.Caption.Length) / 2)).PadRight($width) + $reset }) -join ''
Shot $lines ($width * $moods.Count) 'маскот' 'mascot.png'
Field 'sad' $false

# ---- the status line ----------------------------------------------------

$cli = Join-Path $root 'dist\win-x64\cli.exe'
if (Test-Path $cli) {
    $config = Join-Path ([IO.Path]::GetTempPath()) "multi_calude-shots\.claude-acc2"
    New-Item -ItemType Directory -Force $config | Out-Null
    $reset5 = [DateTimeOffset]::Now.AddHours(2.6).ToUnixTimeSeconds()
    $reset7 = [DateTimeOffset]::Now.AddDays(3).ToUnixTimeSeconds()
    $json = '{"model":{"display_name":"Opus 5.5"},"context_window":{"used_percentage":23},"rate_limits":{"five_hour":{"used_percentage":42,"resets_at":' + $reset5 + '},"seven_day":{"used_percentage":12,"resets_at":' + $reset7 + '}}}'
    [IO.File]::WriteAllText("$config\in.json", $json)
    $previous = $env:CLAUDE_CONFIG_DIR
    $env:CLAUDE_CONFIG_DIR = $config
    cmd /c "`"$cli`" --statusline < `"$config\in.json`" > `"$config\out.txt`""
    $env:CLAUDE_CONFIG_DIR = $previous
    $status = [IO.File]::ReadAllText("$config\out.txt", [Text.Encoding]::UTF8)
    $border = "$dim" + ('─' * 76) + $reset
    Shot @($border, " $([char]0x276F) Продолжим с тестами", $border, "  $status") 78 'Claude Code — строка состояния' 'statusline.png'
    Remove-Item (Split-Path $config) -Recurse -Force
}

# ---- the icon -----------------------------------------------------------

Add-Type -AssemblyName PresentationCore
$decoder = New-Object Windows.Media.Imaging.IconBitmapDecoder ([Uri](Join-Path $root 'assets\icon.ico')), 'None', 'Default'
$frame = $decoder.Frames | Sort-Object PixelWidth -Descending | Select-Object -First 1
$encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
$encoder.Frames.Add($frame)
$stream = [IO.File]::Create((Join-Path $out 'icon.png'))
$encoder.Save($stream)
$stream.Dispose()
Write-Host '-> docs/images/icon.png'
