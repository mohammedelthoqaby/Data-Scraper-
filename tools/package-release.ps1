<#
.SYNOPSIS
    Builds the two release artifacts for LazyBean Collector: the server package and the desktop app.

.DESCRIPTION
    1. Publishes the desktop app as one self-contained win-x64 exe.
    2. Copies that exe into backend/src/LazyBean.Api/wwwroot/downloads so the server package offers it
       at /downloads/LazyBeanCollector.exe. wwwroot is git-ignored, so the exe is never committed.
    3. Publishes the API with the same revision stamp, drops appsettings.Development.json, and zips it.
    4. Removes the temporary copy from wwwroot, checks the zip contents, and prints both SHA-256 hashes.

    Run it after the release commit is made, so the revision names that commit.

.PARAMETER Revision
    Short commit hash stamped into both builds. Defaults to the current HEAD.

.PARAMETER OutDir
    Folder for the artifacts, relative to the repository root. Git-ignored.

.PARAMETER ApiRuntime
    Runtime identifier for the server package. The server runs framework-dependent on win-x86.
#>
param(
    [string]$Revision = (git rev-parse --short HEAD).Trim(),
    [string]$OutDir = "artifacts",
    [string]$ApiRuntime = "win-x86"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$desktopProject = "desktop/LazyBeanCollector/LazyBeanCollector.csproj"
$apiProject = "backend/src/LazyBean.Api/LazyBean.Api.csproj"
$downloads = Join-Path $repo "backend/src/LazyBean.Api/wwwroot/downloads"
$out = Join-Path $repo $OutDir
New-Item -ItemType Directory -Force -Path $out | Out-Null

# 1. Desktop app: one self-contained exe.
$desktopOut = Join-Path $out "desktop"
dotnet publish $desktopProject -c Release -r win-x64 --self-contained true -p:SourceRevisionId=$Revision -o $desktopOut
if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed." }
$exe = Join-Path $desktopOut "LazyBeanCollector.exe"
$exeArtifact = Join-Path $out "LazyBeanCollector-$Revision.exe"
Copy-Item $exe $exeArtifact -Force

# 2. Place the exe where the server package expects it. The copy is removed again in the finally block.
New-Item -ItemType Directory -Force -Path $downloads | Out-Null
$served = Join-Path $downloads "LazyBeanCollector.exe"
Copy-Item $exe $served -Force

try {
    # 3. Server package.
    $apiOut = Join-Path $out "api"
    if (Test-Path $apiOut) { Remove-Item $apiOut -Recurse -Force }
    # Build before publish. The precompressed web assets (.br and .gz) are produced by the Release build, and a
    # publish from a clean checkout alone leaves them out of the package.
    dotnet build $apiProject -c Release -r $ApiRuntime --self-contained false -p:SourceRevisionId=$Revision
    if ($LASTEXITCODE -ne 0) { throw "API build failed." }
    dotnet publish $apiProject -c Release -r $ApiRuntime --self-contained false -p:SourceRevisionId=$Revision -o $apiOut
    if ($LASTEXITCODE -ne 0) { throw "API publish failed." }
    Remove-Item (Join-Path $apiOut "appsettings.Development.json") -ErrorAction SilentlyContinue

    $zip = Join-Path $out "lazybean-api-$Revision.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $apiOut "*") -DestinationPath $zip
}
finally {
    # 4. Never leave the exe inside wwwroot.
    Remove-Item $served -Force -ErrorAction SilentlyContinue
}

# Check the server package: it must carry the installer and must not carry the development settings.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = $archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') }
    if ($names -notcontains "wwwroot/downloads/LazyBeanCollector.exe") {
        throw "Server package is missing wwwroot/downloads/LazyBeanCollector.exe."
    }
    if ($names -contains "appsettings.Development.json") {
        throw "Server package contains appsettings.Development.json."
    }
}
finally {
    $archive.Dispose()
}

foreach ($artifact in @($exeArtifact, $zip)) {
    $hash = (Get-FileHash $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item $artifact).Length
    "{0}  {1:N0} bytes  sha256 {2}" -f (Split-Path $artifact -Leaf), $size, $hash
}
