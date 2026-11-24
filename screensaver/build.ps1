[CmdletBinding()]
param(
    [switch]$Smoke,
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$framework = if ([Environment]::Is64BitOperatingSystem) { 'Framework64' } else { 'Framework' }
$compiler = Join-Path $env:SystemRoot "Microsoft.NET\$framework\v4.0.30319\csc.exe"
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The Windows .NET Framework C# compiler is required.'
}

$outDir = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object FullName)
$options = @('/nologo', '/warnaserror+', '/optimize+', '/target:winexe',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll', "/win32manifest:$manifest")
$fileName = 'Znake.scr'
if ($Smoke) {
    $fileName = 'ZnakeSmoke.scr'
    $options += '/define:ZNAKE_SMOKE'
    $sources += Join-Path $PSScriptRoot 'tests\SmokeTrace.cs'
}

if ($Test) {
    $fileName = 'ZnakeTests.exe'
    $options += '/target:exe', '/main:Znake.Tests.TestRunner'
    $sources += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*Tests.cs' -File | ForEach-Object FullName)
    $sources += Join-Path $PSScriptRoot 'tests\TestRunner.cs'
}
$target = Join-Path $outDir $fileName
& $compiler @options "/out:$target" @sources
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }
Write-Output $target

if ($Test) { & $target; if ($LASTEXITCODE -ne 0) { throw "Screensaver tests failed." } }
