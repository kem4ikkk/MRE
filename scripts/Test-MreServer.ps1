[CmdletBinding()]
param([ValidateRange(1, 65535)][int]$Port = 27015)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $projectRoot 'runtime\mre-process.json'
if (-not (Test-Path -LiteralPath $statePath)) { throw 'MRE еще не запускался.' }
$state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
$expectedExe = Join-Path $projectRoot 'runtime\server\Unturned.exe'
$processInfo = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + [int]$state.ProcessId)
if (-not $processInfo -or -not $processInfo.ExecutablePath -or
    [IO.Path]::GetFullPath($processInfo.ExecutablePath) -ne [IO.Path]::GetFullPath($expectedExe)) {
    throw 'Процесс MRE не работает или путь не совпадает.'
}

function Read-QueryString([IO.BinaryReader]$Reader) {
    $bytes = New-Object 'System.Collections.Generic.List[byte]'
    while ($true) {
        $nextByte = $Reader.ReadByte()
        if ($nextByte -eq 0) { break }
        $bytes.Add($nextByte)
        if ($bytes.Count -gt 4096) { throw 'Слишком длинная строка A2S.' }
    }
    [Text.Encoding]::UTF8.GetString($bytes.ToArray())
}

$udp = New-Object Net.Sockets.UdpClient
$udp.Client.ReceiveTimeout = 5000
$remoteEndPoint = New-Object Net.IPEndPoint([Net.IPAddress]::Loopback, $Port)
$packet = [byte[]](@(255,255,255,255,84) + [Text.Encoding]::ASCII.GetBytes('Source Engine Query') + @(0))
try {
    $udp.Connect('127.0.0.1', $Port)
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    [void]$udp.Send($packet, $packet.Length)
    $response = $udp.Receive([ref]$remoteEndPoint)
    if ($response.Length -ge 9 -and $response[4] -eq 65) {
        $packet = [byte[]]($packet + $response[5..8])
        [void]$udp.Send($packet, $packet.Length)
        $response = $udp.Receive([ref]$remoteEndPoint)
    }
    $stopwatch.Stop()
    if ($response.Length -lt 6 -or [BitConverter]::ToInt32($response,0) -ne -1 -or $response[4] -ne 73) {
        throw 'Сервер вернул неподдерживаемый ответ A2S_INFO.'
    }
    $stream = New-Object IO.MemoryStream(,$response)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        [void]$reader.ReadBytes(5)
        $protocol = $reader.ReadByte()
        $serverName = Read-QueryString $reader
        $mapName = Read-QueryString $reader
        $folderName = Read-QueryString $reader
        $gameName = Read-QueryString $reader
        [void]$reader.ReadUInt16()
        $players = $reader.ReadByte()
        $maxPlayers = $reader.ReadByte()
        $bots = $reader.ReadByte()
        $serverType = [char]$reader.ReadByte()
        $environment = [char]$reader.ReadByte()
        $passwordProtected = $reader.ReadByte() -eq 1
        $vacSecure = $reader.ReadByte() -eq 1
        $gameVersion = Read-QueryString $reader
    }
    finally { $reader.Dispose(); $stream.Dispose() }
    if ($mapName -ne 'PEI' -or $maxPlayers -ne 24) {
        throw "Неожиданная конфигурация: карта $mapName, слоты $maxPlayers."
    }
    [pscustomobject]@{
        ProcessId = $state.ProcessId
        Query = 'A2S_INFO'
        Address = '127.0.0.1'
        Port = $Port
        ServerName = $serverName
        Map = $mapName
        Players = $players
        MaxPlayers = $maxPlayers
        PasswordProtected = $passwordProtected
        VAC = $vacSecure
        Version = $gameVersion
        QueryMilliseconds = [math]::Round($stopwatch.Elapsed.TotalMilliseconds,1)
        PlayerJoinTested = $false
    }
}
finally { $udp.Dispose() }
