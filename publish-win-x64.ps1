$output = Join-Path $PSScriptRoot 'publish\win-x64'
$exe = Join-Path $output 'volume-key-router.exe'
New-Item -ItemType Directory -Path $output -Force | Out-Null

if (Test-Path -LiteralPath $exe) {
    & $exe --shutdown-existing | Out-Null

    $deadline = (Get-Date).AddSeconds(6)
    do {
        $running = Get-Process -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $exe }

        if (-not $running) {
            break
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    $running = Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $exe }

    foreach ($process in $running) {
        if ($process.MainWindowHandle -ne 0) {
            $null = $process.CloseMainWindow()
        }
    }

    $deadline = (Get-Date).AddSeconds(4)
    do {
        $running = Get-Process -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $exe }

        if (-not $running) {
            break
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $exe } |
        Stop-Process -Force
}

Get-ChildItem -LiteralPath $output -Filter 'volume-key-router*' -ErrorAction SilentlyContinue | Remove-Item -Force
dotnet publish .\VolumeKeyRouter.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64
