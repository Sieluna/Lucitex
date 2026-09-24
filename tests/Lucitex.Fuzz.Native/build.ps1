param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw "vswhere.exe not found at '$vswhere'. Install Visual Studio with the C++ desktop workload."
}

$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) {
    throw 'MSBuild.exe not found. Install the "Desktop development with C++" workload in Visual Studio.'
}

$project = Join-Path $PSScriptRoot 'Lucitex.Fuzz.Native.vcxproj'
& $msbuild $project /p:Configuration=$Configuration /p:Platform=x64 /nologo
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}

$library = Join-Path $PSScriptRoot "artifacts\Lucitex.Fuzz.Native\$Configuration\lucitex_fuzz_native.dll"
Write-Host "Built $library"
