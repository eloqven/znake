[CmdletBinding()]
param(
    [switch]$Smoke,
    [switch]$Test,
    [switch]$Widget,
    [switch]$Headless,
    [switch]$Replay,
    [switch]$ControlPanel
)

$ErrorActionPreference = 'Stop'
$modes = @($Widget, $Test, $Headless, $Replay, $ControlPanel) | Where-Object { $_ }
if ($modes.Count -gt 1) {
    throw 'Specify at most one target switch (-Widget, -Test, -Headless, -Replay, or -ControlPanel).'
}
if ($Smoke -and ($Test -or $Headless -or $Replay -or $ControlPanel)) {
    throw '-Smoke is supported only for the screensaver or -Widget targets.'
}

$framework = if ([Environment]::Is64BitOperatingSystem) { 'Framework64' } else { 'Framework' }
$frameworkDir = Join-Path $env:SystemRoot "Microsoft.NET\$framework\v4.0.30319"
$compiler = Join-Path $frameworkDir 'csc.exe'
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The Windows .NET Framework C# compiler is required.'
}

$outDir = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object FullName)
$options = @('/nologo', '/warnaserror+', '/optimize+', '/target:winexe',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll', "/win32manifest:$manifest")
$wpfRefs = @(
    ("/reference:" + (Join-Path $frameworkDir 'WPF\WindowsFormsIntegration.dll')),
    '/reference:System.Xaml.dll',
    ("/reference:" + (Join-Path $frameworkDir 'WPF\PresentationCore.dll')),
    ("/reference:" + (Join-Path $frameworkDir 'WPF\PresentationFramework.dll')),
    ("/reference:" + (Join-Path $frameworkDir 'WPF\WindowsBase.dll'))
)

$fileName = 'Znake.scr'
if ($Smoke) {
    $fileName = 'ZnakeSmoke.scr'
    $options += '/define:ZNAKE_SMOKE'
    $sources += Join-Path $PSScriptRoot 'tests\SmokeTrace.cs'
}

if ($Widget) {
    $fileName = if ($Smoke) { 'ZnakeWidgetSmoke.exe' } else { 'ZnakeWidget.exe' }
    $options += '/main:Znake.Widget'
    $sources += Join-Path $PSScriptRoot 'widget\Widget.cs'
}

$controlPanelDir = Join-Path $PSScriptRoot 'controlpanel'

if ($Test) {
    $fileName = 'ZnakeTests.exe'
    $options += '/target:exe', '/main:Znake.Tests.TestRunner'
    $options += $wpfRefs
    $sources += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*Tests.cs' -File | ForEach-Object FullName)
    $sources += Join-Path $PSScriptRoot 'tools\HeadlessShow.cs'
    $sources += Join-Path $PSScriptRoot 'tools\ShowReplay.cs'
    if (Test-Path -LiteralPath $controlPanelDir -PathType Container) {
        $sources += @(Get-ChildItem -LiteralPath $controlPanelDir -Filter '*.cs' -File | ForEach-Object FullName)
    }
    $sources += Join-Path $PSScriptRoot 'tests\TestRunner.cs'
}

if ($Headless) {
    $fileName = 'ZnakeHeadless.exe'
    $options += '/target:exe', '/main:Znake.Headless.Program'
    $sources += Join-Path $PSScriptRoot 'tools\HeadlessShow.cs'
}

if ($Replay) {
    $fileName = 'ShowReplay.exe'
    $options += '/target:exe', '/main:Znake.Replay.Program'
    $sources += Join-Path $PSScriptRoot 'tools\ShowReplay.cs'
}

if ($ControlPanel) {
    $fileName = 'ZnakeControlPanel.exe'
    $options += '/target:winexe', '/main:Znake.ControlPanel.Program'
    $options += $wpfRefs
    $sources += Join-Path $PSScriptRoot 'tools\HeadlessShow.cs'
    $sources += Join-Path $PSScriptRoot 'tools\ShowReplay.cs'
    $sources += @(Get-ChildItem -LiteralPath $controlPanelDir -Filter '*.cs' -File | ForEach-Object FullName)
}

$target = Join-Path $outDir $fileName
& $compiler @options "/out:$target" @sources
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }
Write-Output $target

if ($Test) { & $target; if ($LASTEXITCODE -ne 0) { throw "Screensaver tests failed." } }
