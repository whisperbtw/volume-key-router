$output = Join-Path $PSScriptRoot 'publish\win-x64'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Get-ChildItem -LiteralPath $output -Filter 'volume-key-router*' -ErrorAction SilentlyContinue | Remove-Item -Force
dotnet publish .\VolumeKeyRouter.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64
