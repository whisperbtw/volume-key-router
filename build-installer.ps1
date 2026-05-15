$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$projectPath = Join-Path $root 'VolumeKeyRouter.csproj'
$installerScript = Join-Path $root 'installer\VolumeKeyRouter.iss'
$dist = Join-Path $root 'dist'

[xml]$project = Get-Content -LiteralPath $projectPath
$version = $project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    $version = '0.0.0'
}

& (Join-Path $root 'publish-win-x64.ps1')

$candidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($iscc)) {
    throw 'Inno Setup nao encontrado. Instale o Inno Setup 6 e rode este script novamente.'
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null
& $iscc "/DAppVersion=$version" $installerScript

Write-Host "Instalador gerado em: $(Join-Path $dist "VolumeKeyRouterSetup-$version.exe")"
