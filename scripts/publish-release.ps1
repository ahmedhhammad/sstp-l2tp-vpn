param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0-preview.1',

    [Parameter()]
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $repositoryRoot 'SarmadiVpn\SarmadiVpn.csproj'
$solution = Join-Path $repositoryRoot 'SarmadiVpn.sln'
$releaseDirectory = Join-Path $repositoryRoot "dist\$Version"
$publishDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "sarmadi-vpn-publish-$PID-$([guid]::NewGuid().ToString('N'))"

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

try {
    if (-not $SkipTests) {
        $testArguments = @($solution, '--configuration', 'Release', '--verbosity', 'minimal', '-m:1')
        if (Test-Path -LiteralPath (Join-Path $repositoryRoot 'SarmadiVpn.Tests\obj\project.assets.json')) {
            $testArguments += '--no-restore'
        }
        & dotnet test @testArguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }
    }

    dotnet publish $appProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $publishDirectory `
        "-p:Version=$Version" `
        '-p:PublishSingleFile=true' `
        '-p:IncludeNativeLibrariesForSelfExtract=true' `
        '-p:DebugSymbols=false' `
        '-p:DebugType=None'
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    $publishedExecutable = Join-Path $publishDirectory 'SarmadiVpn.exe'
    if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
        throw "Publish succeeded but did not produce $publishedExecutable."
    }

    $releaseExecutable = Join-Path $releaseDirectory 'SarmadiVpn-win-x64.exe'
    Copy-Item -LiteralPath $publishedExecutable -Destination $releaseExecutable -Force
    $hash = (Get-FileHash -LiteralPath $releaseExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt') `
        -Value "$hash  SarmadiVpn-win-x64.exe" -Encoding ascii

    Get-Item -LiteralPath $releaseExecutable | Select-Object FullName, Length
    Get-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt')
}
finally {
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (Test-Path -LiteralPath $publishDirectory) {
        $resolvedPublishDirectory = (Resolve-Path -LiteralPath $publishDirectory).Path
        if (-not $resolvedPublishDirectory.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a publish directory outside the temporary folder: $resolvedPublishDirectory"
        }
        Remove-Item -LiteralPath $resolvedPublishDirectory -Recurse -Force
    }
}
