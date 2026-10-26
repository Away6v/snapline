param(
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../outputs')
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$project = Join-Path $PSScriptRoot '../SnapLine.csproj'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$publish = Join-Path $output "SnapLine-$Runtime"
dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw '发布构建失败。' }
Copy-Item (Join-Path $PSScriptRoot '../README.md') $publish
Copy-Item (Join-Path $PSScriptRoot '../LICENSE') $publish
Copy-Item (Join-Path $PSScriptRoot '../THIRD-PARTY-NOTICES.md') $publish
$archive = Join-Path $output "SnapLine-0.1.0-$Runtime.zip"
Compress-Archive -Path "$publish/*" -DestinationPath $archive -Force
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -Encoding ascii "$archive.sha256"
Write-Output $archive
