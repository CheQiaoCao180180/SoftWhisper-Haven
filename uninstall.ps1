# 大肥鱼桌宠 · 卸载
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File uninstall.ps1            看着清单确认再删
#   powershell -ExecutionPolicy Bypass -File uninstall.ps1 -DryRun    只列清单，一个都不删
#   powershell -ExecutionPolicy Bypass -File uninstall.ps1 -Force     不问直接删
#   powershell -ExecutionPolicy Bypass -File uninstall.ps1 -KeepData  只删程序，留下记忆/好感/语音
#
# 删之前会先停掉桌宠、托盘、渲染器；删完会告诉你还剩什么要手动处理。

param(
    [switch]$DryRun,
    [switch]$Force,
    [switch]$KeepData
)

$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path -LiteralPath $root).Path

function Line($text, $color = 'Gray') { Write-Host $text -ForegroundColor $color }
function Section($text) { Write-Host ''; Write-Host ('── ' + $text + ' ') -ForegroundColor Cyan }

# ───────────────────────────────────── 先算清单

$processes = @()
foreach ($name in @('FishPet', 'FishPet.Mcp', 'FishTray', 'l2d-render', '大肥鱼设置', 'ming-tts')) {
    $found = Get-Process -Name $name -ErrorAction SilentlyContinue
    if ($found) { $processes += [pscustomobject]@{ Name = $name; Count = $found.Count } }
}

$desktop = [Environment]::GetFolderPath('Desktop')
$shortcuts = @()
foreach ($name in @('DeepFish.lnk', '大肥鱼设置.lnk')) {
    $path = Join-Path $desktop $name
    if (Test-Path -LiteralPath $path) { $shortcuts += $path }
}

$tasks = @()
foreach ($task in @('FishTray')) {
    $found = Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
    if ($found) { $tasks += $task }
}

# 插件目录里的大小
function FolderSize($path) {
    if (-not (Test-Path -LiteralPath $path)) { return 0 }
    try { return (Get-ChildItem -LiteralPath $path -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum } catch { return 0 }
}

$totalMb = [math]::Round((FolderSize $root) / 1MB, 1)
$voiceDir = Join-Path $root 'audio\voices'
$voiceMb = [math]::Round((FolderSize $voiceDir) / 1MB, 1)
$modelDir = Join-Path $root 'model'
$modelMb = [math]::Round((FolderSize $modelDir) / 1MB, 1)

# 值得留的东西（删了就没了）
$keepList = @()
foreach ($file in @('memory.json', 'affection.json', 'settings.json')) {
    $path = Join-Path $root $file
    if (Test-Path -LiteralPath $path) { $keepList += [pscustomobject]@{ File = $file; Kb = [math]::Round((Get-Item $path).Length / 1KB, 1) } }
}

# 有没有从 C 盘指过来的 junction（PowerShell 5.1 的 Get-Item 没有 LinkType，只能看 ReparsePoint 标志）
$junctions = @()
foreach ($candidate in @(('C:\Users\' + $env:USERNAME + '\大肥鱼插件'), 'D:\大肥鱼插件')) {
    if (Test-Path -LiteralPath $candidate) {
        $item = Get-Item -LiteralPath $candidate -Force
        if ((($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) -or $item.LinkType -eq 'Junction') {
            $junctions += $candidate
        }
    }
}

# ───────────────────────────────────── 打出来

Write-Host ''
Write-Host '  大肥鱼桌宠 · 卸载' -ForegroundColor Cyan
Line  ('  插件位置：' + $root)
Line  ('  占用大小：' + $totalMb + ' MB' + $(if ($voiceMb -gt 0) { '（其中语音缓存 ' + $voiceMb + ' MB、模型 ' + $modelMb + ' MB）' } else { '' }))

Section '会删掉'
if ($processes.Count -gt 0) { foreach ($p in $processes) { Line ('  · 运行中的进程 ' + $p.Name + ' ×' + $p.Count) } } else { Line '  · （没有在运行的进程）' }
if ((Get-Process -Name 'ming-tts' -ErrorAction SilentlyContinue)) {
    Line '    注意：ming-tts 正在生成语音，卸载会把这一批任务中断。' 'Yellow'
}
foreach ($shortcut in $shortcuts) { Line ('  · 桌面图标 ' + (Split-Path $shortcut -Leaf)) }
foreach ($task in $tasks) { Line ('  · 计划任务 ' + $task) }
foreach ($junction in $junctions) { Line ('  · 目录联接 ' + $junction) }
if ($KeepData) {
    Line ('  · 程序目录 ' + $root + '（保留记忆、好感、语音、模型）') 'Yellow'
} else {
    Line ('  · 整个插件目录 ' + $root) 'Yellow'
}

if ($keepList.Count -gt 0) {
    Section '这些是你自己的东西，删了就没了'
    foreach ($item in $keepList) { Line ('  · ' + $item.File + '（' + $item.Kb + ' KB）') 'Yellow' }
    if ($voiceMb -gt 0) { Line ('  · audio\voices\ 里的语音缓存（' + $voiceMb + ' MB，删了可以重新生成）') 'Yellow' }
    if (-not $KeepData -and -not $DryRun) {
        Line '  想留着的话，先复制出来，或者加 -KeepData 只删程序。' 'Yellow'
    }
}

Section '还要你自己处理'
Line '  · DSH / Claude Code 的 MCP 配置里如果加过 dafeiyu，记得手动删掉那条（本脚本不动别的软件的配置）。'
Line '  · 桌面左下角托盘如果还挂着鲸鱼图标，右键退出一次。'

if ($DryRun) {
    Write-Host ''
    Line '这是 -DryRun：什么都没删。' 'Green'
    return
}

if (-not $Force) {
    Write-Host ''
    $answer = Read-Host '确认卸载吗？输入 yes 继续，其他任何键取消'
    if ($answer -ne 'yes') { Line '取消了，什么都没动。' 'Green'; return }
}

# ───────────────────────────────────── 开始删

Section '停进程'
foreach ($name in @('FishPet', 'FishPet.Mcp', 'FishTray', 'l2d-render', '大肥鱼设置', 'ming-tts')) {
    $found = Get-Process -Name $name -ErrorAction SilentlyContinue
    if ($found) {
        $found | Stop-Process -Force -ErrorAction SilentlyContinue
        Line ('  停掉 ' + $name + ' ×' + $found.Count)
    }
}
Start-Sleep -Seconds 2

Section '删计划任务'
foreach ($task in $tasks) {
    try { Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction Stop; Line ('  删掉 ' + $task) }
    catch { Line ('  删不掉 ' + $task + '：' + $_.Exception.Message) 'Yellow' }
}

Section '删桌面图标'
foreach ($shortcut in $shortcuts) {
    try { Remove-Item -LiteralPath $shortcut -Force; Line ('  删掉 ' + (Split-Path $shortcut -Leaf)) }
    catch { Line ('  删不掉 ' + $shortcut + '：' + $_.Exception.Message) 'Yellow' }
}

Section '删目录'
if ($junctions.Count -gt 0) {
    foreach ($junction in $junctions) {
        try { (Get-Item -LiteralPath $junction -Force).Delete(); Line ('  断开目录联接 ' + $junction) }
        catch { Line ('  断不开 ' + $junction + '：' + $_.Exception.Message) 'Yellow' }
    }
}

if ($KeepData) {
    $keep = @('memory.json', 'affection.json', 'settings.json', 'audio', 'model')
    foreach ($name in $keep) {
        $path = Join-Path $root $name
        if (-not (Test-Path -LiteralPath $path)) { continue }
        # 挪到插件目录外面，免得跟着目录一起删
        $target = Join-Path (Split-Path $root -Parent) ('大肥鱼-留下的-' + $name)
        try {
            if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
            Move-Item -LiteralPath $path -Destination $target -Force
            Line ('  留下 ' + $name + ' → ' + $target)
        } catch { Line ('  留不下 ' + $name + '：' + $_.Exception.Message) 'Yellow' }
    }
}

# 真正删目录前再确认一次路径，别删错东西
if (Test-Path -LiteralPath $root) {
    if ($root -match '\\大肥鱼插件$' -or $root -match '\\DeepFish$') {
        try {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction Stop
            Line ('  删掉 ' + $root)
        } catch {
            Line ('  删不干净（可能有文件被占用）：' + $_.Exception.Message) 'Yellow'
            Line '  关掉资源管理器窗口、重开一次电脑再跑一遍这个脚本，或者手动删这个目录。' 'Yellow'
        }
    } else {
        Line ('  路径看着不像插件目录，为了安全没删：' + $root) 'Red'
    }
}

Write-Host ''
Line '卸载做完了。' 'Green'
Line '如果 DSH 里还留着 dafeiyu 这个 MCP，记得去配置里删掉那一行。'
