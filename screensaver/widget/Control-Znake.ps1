[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('help', 'spawn', 'sense', 'move', 'release')]
    [string]$Action = 'help',
    [string]$Token,
    [ValidateSet('forward', 'left', 'right')]
    [string]$Direction = 'forward',
    [switch]$Bypass
)

$ErrorActionPreference = 'Stop'
if ($Action -eq 'help') {
    "Znake agent control"
    "  & '$PSCommandPath' spawn"
    "  & '$PSCommandPath' sense -Token <token>"
    "  & '$PSCommandPath' move -Token <token> -Direction <forward|left|right> [-Bypass]"
    "  & '$PSCommandPath' release -Token <token>"
    return
}
$folder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Znake\Widget\agent-control'
if ($Action -ne 'spawn' -and $Token -notmatch '^[0-9a-f]{32}$') {
    throw 'Use the Token returned by spawn.'
}
New-Item -Path $folder -ItemType Directory -Force | Out-Null
$id = [Guid]::NewGuid().ToString('N')
$temporary = Join-Path $folder ($id + '.writing')
$requestPath = Join-Path $folder ($id + '.request')
$replyPath = Join-Path $folder ($id + '.reply')
$request = @{
    Command = $Action
    Token = $Token
    Direction = $Direction
    Bypass = [bool]$Bypass
} | ConvertTo-Json -Compress
[IO.File]::WriteAllText($temporary, $request, [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $requestPath

$wait = [Diagnostics.Stopwatch]::StartNew()
while ($wait.Elapsed.TotalSeconds -lt 5) {
    if (Test-Path -LiteralPath $replyPath -PathType Leaf) {
        $json = Get-Content -Encoding UTF8 -Raw -LiteralPath $replyPath
        Remove-Item -LiteralPath $replyPath
        $result = $json | ConvertFrom-Json
        if ($result.Error) { throw $result.Error }
        $json
        return
    }
    Start-Sleep -Milliseconds 70
}
if (Test-Path -LiteralPath $requestPath) { Remove-Item -LiteralPath $requestPath }
throw 'Znake did not answer within five seconds.'
