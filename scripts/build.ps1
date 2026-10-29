param(
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../outputs'),
    [string]$Version = '',
    [string]$InnoSetupCompiler = ''
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$project = Join-Path $PSScriptRoot '../SnapLine.csproj'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $Version) { $Version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号必须采用主版本.次版本.修订版本格式。' }
if (-not $InnoSetupCompiler) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compiler) { $InnoSetupCompiler = $compiler.Source }
    else { $InnoSetupCompiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
}
if (-not (Test-Path -LiteralPath $InnoSetupCompiler)) { throw '找不到 Inno Setup 编译器，请安装 Inno Setup 6 或通过 InnoSetupCompiler 指定路径。' }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$publish = Join-Path ([IO.Path]::GetTempPath()) ('SnapLine-build-' + [guid]::NewGuid().ToString('N'))
try {
    dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:Version=$Version -o $publish --nologo
    if ($LASTEXITCODE -ne 0) { throw '发布构建失败。' }
    Copy-Item (Join-Path $PSScriptRoot '../README.md') $publish
    Copy-Item (Join-Path $PSScriptRoot '../LICENSE') $publish
    Copy-Item (Join-Path $PSScriptRoot '../THIRD-PARTY-NOTICES.md') $publish
    $archive = Join-Path $output "SnapLine-$Version-$Runtime.zip"
    Compress-Archive -Path "$publish/*" -DestinationPath $archive -Force
    & $InnoSetupCompiler "/DAppVersion=$Version" "/DPublishDir=$publish" "/DOutputDir=$output" "/DRuntime=$Runtime" (Join-Path $PSScriptRoot 'installer.iss')
    if ($LASTEXITCODE -ne 0) { throw '安装程序构建失败。' }
    $exe = Join-Path $output "SnapLine-$Version-$Runtime-Setup.exe"
    foreach ($package in @($exe, $archive)) {
        Write-Output $package
    }
} finally {
    if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
}
