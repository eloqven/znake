[CmdletBinding()]
param(
    [ValidateSet('Direct', 'Corner', 'Idle', 'Modes', 'InvalidState')]
    [string]$Mode = 'Direct',
    [ValidateRange(1, 600)]
    [int]$IdleSeconds = 15
)

$ErrorActionPreference = 'Stop'
$out = Join-Path (Split-Path -Parent $PSScriptRoot) 'out'
$saver = Join-Path $out 'ZnakeSmoke.scr'
if (!(Test-Path -LiteralPath $saver -PathType Leaf)) {
    throw 'Build the Windows smoke saver with build.ps1 -Smoke before running this test.'
}

Add-Type -Path (Join-Path $PSScriptRoot 'WindowsSmokeFixture.cs') -ReferencedAssemblies System.Windows.Forms, System.Drawing
if (![ZnakeSmoke.Fixture]::OnDefaultDesktop()) {
    throw 'The smoke test requires the current interactive Windows desktop.'
}

$profileName = [Guid]::NewGuid().ToString('N')
$profile = Join-Path $out "smoke-profiles\$profileName"
[IO.Directory]::CreateDirectory($profile) | Out-Null
[IO.File]::WriteAllText((Join-Path $profile 'settings.json'),
    '{"genes":{"Length3":0,"Length5":1,"Length8":0,"Length13":0,"Speed":0.35,"Sprint":0}}')
[IO.File]::WriteAllText((Join-Path $out 'windows-smoke-profile.txt'), $profileName)

$tracePath = Join-Path $out 'windows-smoke.log'
[IO.File]::WriteAllText($tracePath, '')
$trace = {
    $open = [IO.File]::Open($tracePath, 'Open', 'Read', 'ReadWrite')
    $reader = New-Object IO.StreamReader($open)
    try { $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Wait-Smoke([scriptblock]$Condition, [int]$Seconds, [string]$Failure) {
    $limit = [Diagnostics.Stopwatch]::StartNew()
    do {
        [Windows.Forms.Application]::DoEvents()
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 50
    } while ($limit.Elapsed.TotalSeconds -lt $Seconds)
    throw ($Failure + [Environment]::NewLine + (& $trace))
}

function Start-ZnakeSaver([string]$Arguments) {
    $info = New-Object Diagnostics.ProcessStartInfo($saver, $Arguments)
    $info.UseShellExecute = $false
    if ($Mode -eq 'Corner') {
        $info.EnvironmentVariables['ZNAKE_SMOKE_BOUNDS'] = @($fixture.Left, $fixture.Top, $fixture.Width, $fixture.Height) -join ','
    }
    [Diagnostics.Process]::Start($info)
}

function Wait-ZnakeLaunch([int]$Count) {
    $launchLimit = if ($Mode -eq 'Idle') { [Math]::Max(45, $IdleSeconds + 20) } else { 8 }
    $script:maximumIdle = [uint32]0
    try {
        Wait-Smoke {
            $script:maximumIdle = [Math]::Max($script:maximumIdle, [ZnakeSmoke.Fixture]::IdleMilliseconds())
            [regex]::Matches((& $trace), 'started desktop=Default pid=\d+').Count -ge $Count
        } $launchLimit 'The full-screen saver did not start.'
    } catch {
        throw ("Longest measured Windows idle interval was " + $script:maximumIdle + " ms." +
            [Environment]::NewLine + $_.Exception.Message)
    }
    $entry = [regex]::Matches((& $trace), 'started desktop=Default pid=(\d+)')[$Count - 1]
    Get-Process -Id ([int]$entry.Groups[1].Value)
}

function Close-CornerSaver($ActiveSaver) {
    $match = [regex]::Match((& $trace), "started desktop=Default pid=$($ActiveSaver.Id) hwnd=(\d+)")
    if (!$match.Success) { return $false }
    [ZnakeSmoke.Fixture]::RequestClose([IntPtr][long]$match.Groups[1].Value, [uint32]$ActiveSaver.Id)
}

function Confirm-IdleParent($ActiveSaver) {
    $record = Get-CimInstance Win32_Process -Filter "ProcessId = $($ActiveSaver.Id)"
    $launcher = (Get-Process -Id $record.ParentProcessId -ErrorAction SilentlyContinue).ProcessName
    if ($launcher -ne 'winlogon') {
        throw "The idle launch had unexpected parent process '$launcher'."
    }
    Write-Output 'Windows idle started the saver through winlogon.'
}

$desktop = $null
$snapshot = @{}
$fixture = $null
$saverProcess = $null
try {
    if ($Mode -eq 'InvalidState') {
        $settingsPath = Join-Path $profile 'settings.json'
        $worldPath = Join-Path $profile 'world.json'
        $backupPath = Join-Path $profile 'world.previous.json'
        [IO.File]::WriteAllText($settingsPath, '{"genes":{"UnknownGene":1}}')
        $saverProcess = Start-ZnakeSaver '/s'
        Wait-Smoke { $saverProcess.HasExited } 5 'The saver did not exit after reading invalid settings.'
        if ($saverProcess.ExitCode -ne 0 -or (& $trace) -notmatch 'error The settings file is invalid' -or
            (Test-Path -LiteralPath $worldPath)) {
            throw 'Invalid settings did not exit cleanly with the world untouched.'
        }

        $saverProcess = Start-ZnakeSaver '/c'
        Wait-Smoke {
            $saverProcess.Refresh()
            $saverProcess.MainWindowTitle -eq 'Znake Settings'
        } 8 'The configuration dialog could not open to repair invalid settings.'
        $saverProcess.CloseMainWindow() | Out-Null
        Wait-Smoke { $saverProcess.HasExited } 5 'The configuration dialog did not close.'

        [IO.File]::WriteAllText($settingsPath, '{}')
        $badWorld = '{"version":'
        [IO.File]::WriteAllText($worldPath, $badWorld)
        [IO.File]::WriteAllText($backupPath, $badWorld)
        $saverProcess = Start-ZnakeSaver '/s'
        Wait-Smoke { $saverProcess.HasExited } 5 'The saver did not exit with unreadable checkpoints.'
        if ($saverProcess.ExitCode -ne 0 -or (& $trace) -notmatch 'error No complete Znake checkpoint' -or
            [IO.File]::ReadAllText($worldPath) -ne $badWorld -or
            [IO.File]::ReadAllText($backupPath) -ne $badWorld) {
            throw 'Unreadable checkpoints did not exit cleanly with both files intact.'
        }
        Write-Output 'PASS Bad settings can be repaired and unreadable checkpoints are preserved'
        return
    }

    $fixture = New-Object ZnakeSmoke.Fixture -ArgumentList ($Mode -eq 'Corner')
    $fixture.Show()
    [Windows.Forms.Application]::DoEvents()

    if ($Mode -eq 'Modes') {
        $parentHex = ('0x{0:x}' -f $fixture.Handle.ToInt64())
        $saverProcess = Start-ZnakeSaver "/p:$parentHex"
        Wait-Smoke { (& $trace) -match 'preview parent=1' } 8 'Windows preview did not attach to its parent.'
        if ($saverProcess.HasExited) { throw 'The embedded preview exited while its parent remained open.' }
        $fixture.Close()
        Wait-Smoke { $saverProcess.HasExited } 5 'The preview did not exit when its parent closed.'

        $saverProcess = Start-ZnakeSaver '/c'
        Wait-Smoke {
            $saverProcess.Refresh()
            $saverProcess.MainWindowTitle -eq 'Znake Settings'
        } 8 'Windows settings did not show its configuration dialog.'
        $saverProcess.CloseMainWindow() | Out-Null
        Wait-Smoke { $saverProcess.HasExited } 5 'The settings dialog did not close.'
        if (Test-Path -LiteralPath (Join-Path $profile 'world.json')) {
            throw 'Settings or preview wrote a world checkpoint.'
        }
        Write-Output 'PASS Configuration dialog and embedded preview lifecycle'
        return
    }

    if ($Mode -eq 'Idle') {
        $desktop = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\Desktop', $true)
        if ($null -eq $desktop) { throw 'The screensaver settings key is unavailable.' }
        if ($desktop.GetValue('ScreenSaverIsSecure', '0') -ne '0') {
            throw 'The current Windows protection setting requires the secure desktop.'
        }
        $policy = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Policies\Microsoft\Windows\Control Panel\Desktop')
        if ($policy) {
            try {
                if ($policy.GetValueNames() | Where-Object { $_ -in @('ScreenSaveActive', 'ScreenSaveTimeOut', 'SCRNSAVE.EXE', 'ScreenSaverIsSecure') }) {
                    throw 'Windows policy manages the screensaver. Temporary smoke settings are unavailable.'
                }
            } finally { $policy.Dispose() }
        }

        $priorTimeout = [ZnakeSmoke.Fixture]::GetSetting(0x000E)
        $priorActive = [ZnakeSmoke.Fixture]::GetSetting(0x0010)
        foreach ($name in @('SCRNSAVE.EXE', 'ScreenSaveTimeOut', 'ScreenSaveActive')) {
            if ($desktop.GetValueNames() -contains $name) {
                $snapshot[$name] = @($desktop.GetValue($name), $desktop.GetValueKind($name))
            } else {
                $snapshot[$name] = $null
            }
        }
        [ZnakeSmoke.Fixture]::SetSetting(0x0011, 0)
        $desktop.SetValue('SCRNSAVE.EXE', $saver, [Microsoft.Win32.RegistryValueKind]::String)
        [ZnakeSmoke.Fixture]::MovePointer()
        [ZnakeSmoke.Fixture]::SetSetting(0x000F, $IdleSeconds)
        [ZnakeSmoke.Fixture]::SetSetting(0x0011, 1)
    } else {
        $saverProcess = Start-ZnakeSaver '/s'
    }

    $saverProcess = Wait-ZnakeLaunch 1
    Wait-Smoke {
        $evidence = & $trace
        ($evidence -match 'covered=blue') -and
        ($evidence -match 'covered=red') -and
        ($evidence -match 'layered=visible') -and
        ($evidence -match 'draw=changed') -and
        ($evidence -match 'shape=opaque, surrounding=transparent') -and
        ($evidence -notmatch 'covered=self') -and
        ($evidence -match 'debug=on') -and
        (($Mode -ne 'Corner') -or ($evidence -match 'corner=nonactivating bounds='))
    } 8 'The pixels beneath the moving overlay did not match the live fixture.'
    if ($Mode -eq 'Idle') { Confirm-IdleParent $saverProcess }
    if ($Mode -eq 'Corner') {
        if (!(Close-CornerSaver $saverProcess)) { throw 'The corner saver did not accept its close request.' }
    } else {
        [ZnakeSmoke.Fixture]::PressDismissKey()
    }
    Wait-Smoke { $saverProcess.HasExited } 5 'The saver did not close.'

    $firstSave = [regex]::Matches((& $trace), 'saved (tick=\d+ resident=\d+ rng=\d+)')
    if ($firstSave.Count -eq 0) { throw 'The saver did not commit a checkpoint on dismissal.' }
    $expected = $firstSave[$firstSave.Count - 1].Groups[1].Value
    $worldPath = Join-Path $profile 'world.json'
    $checkpoint = Get-Content -Raw -LiteralPath $worldPath | ConvertFrom-Json
    if ($checkpoint.world.Ticks -le 0 -or $checkpoint.world.Snakes[0].Body.Count -ne 5) {
        throw 'The first checkpoint does not contain the configured live snake.'
    }

    [IO.File]::WriteAllText((Join-Path $profile 'settings.json'), '{"debug":false}')
    if ($Mode -ne 'Idle') { $saverProcess = Start-ZnakeSaver '/s' }
    $saverProcess = Wait-ZnakeLaunch 2
    Wait-Smoke {
        $evidence = & $trace
        $evidence.Contains("resumed $expected") -and ($evidence -match 'debug=off')
    } 5 'The next launch did not restore the exact saved resident and random state.'
    if ($Mode -eq 'Idle') { Confirm-IdleParent $saverProcess }
    if ($Mode -eq 'Corner') {
        if (!(Close-CornerSaver $saverProcess)) { throw 'The corner saver did not accept its close request.' }
    } else {
        [ZnakeSmoke.Fixture]::MovePointer()
    }
    Wait-Smoke { $saverProcess.HasExited } 5 'The saver did not close after resume.'

    Write-Output "PASS $Mode launch, live underlying colors, both views, close, and saved-state resume"
    & $trace
} finally {
    try {
        if ($snapshot.Count -eq 3) {
            $priorSaver = $snapshot['SCRNSAVE.EXE']
            if ($null -eq $priorSaver) {
                $desktop.DeleteValue('SCRNSAVE.EXE', $false)
            } else {
                $desktop.SetValue('SCRNSAVE.EXE', $priorSaver[0], $priorSaver[1])
            }
            try {
                [ZnakeSmoke.Fixture]::SetSetting(0x000F, $priorTimeout)
                [ZnakeSmoke.Fixture]::SetSetting(0x0011, $priorActive)
            } finally {
                foreach ($name in @('ScreenSaveTimeOut', 'ScreenSaveActive')) {
                    $entry = $snapshot[$name]
                    if ($null -eq $entry) {
                        $desktop.DeleteValue($name, $false)
                    } else {
                        $desktop.SetValue($name, $entry[0], $entry[1])
                    }
                }
            }
        }
    } finally {
        if ($desktop) { $desktop.Dispose() }
        if ($saverProcess -and !$saverProcess.HasExited) {
            if ($Mode -eq 'Corner') {
                Close-CornerSaver $saverProcess | Out-Null
                if (!$saverProcess.WaitForExit(1500)) { $saverProcess.Kill() }
            } else {
                $saverProcess.CloseMainWindow() | Out-Null
            }
        }
        if ($fixture) { $fixture.Dispose() }
    }
}
