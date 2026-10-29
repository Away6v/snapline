param(
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../outputs'),
    [string]$Version = ''
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$project = Join-Path $PSScriptRoot '../SnapLine.csproj'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$publish = Join-Path $output "SnapLine-$Runtime"
if (-not $Version) { $Version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号必须采用主版本.次版本.修订版本格式。' }
dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -p:Version=$Version -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw '发布构建失败。' }
Copy-Item (Join-Path $PSScriptRoot '../README.md') $publish
Copy-Item (Join-Path $PSScriptRoot '../LICENSE') $publish
Copy-Item (Join-Path $PSScriptRoot '../THIRD-PARTY-NOTICES.md') $publish
Copy-Item (Join-Path $PSScriptRoot '../Assets') $publish -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot '../docs') $publish -Recurse -Force
$archive = Join-Path $output "SnapLine-$Version-$Runtime.zip"
Compress-Archive -Path "$publish/*" -DestinationPath $archive -Force
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -Encoding ascii "$archive.sha256"
Write-Output $archive
