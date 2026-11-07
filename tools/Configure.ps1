<#
.SYNOPSIS
Sets up building Https360: its submodules, xsdk and its toolchain in WSL, and Silverlight360 for the sample. Run it
first, after cloning.

.DESCRIPTION
1. curl-xenon and mbedtls-xenon are submodules in deps\, at the commits Https360 is built from: it checks them out
   (git submodule update --init --recursive), Mbed TLS's framework with them.
2. WSL, and make, cmake, ninja, python3 and git in it.
3. xsdk: the dll-modules build XboxNGen uses (https://github.com/jessenic/xsdk, branch dll-modules), as its
   elf2xex.py has --dll. Found from -Xsdk, the XSDK environment variable, the last configuration, XboxNGen's
   configuration beside this repository, or an xsdk checkout two folders up (C:\Proj\xsdk) or beside it. With none,
   it offers to clone it two folders up (-CloneXsdk without asking). Its build\libxsdk.a is built if it isn't.
4. The toolchain, xsdk's GCC for powerpc-xsdk-elf, in WSL: -Toolchain's prefix, else the last one, else
   ~/xsdk-gcc/bin/powerpc-xsdk-elf-. With none, it offers to build it with xsdk's tools/toolchain/build.sh into
   ~/xsdk-gcc (-BuildToolchain without asking): about 10 minutes, and a 136 MB download of GCC's, binutils' and
   newlib's sources.
5. Silverlight360, for the sample only: -Silverlight360, its environment variable, the last configuration, or a
   checkout beside this repository (XboxSilverlightSdk or Silverlight360). With none, it offers to clone
   https://github.com/Digiex/Silverlight360 beside it (-CloneSilverlight360 without asking). Building the DLL
   doesn't need it.

It writes, at the repository's root (git ignores them):
  config.mk     XSDK and TOOLCHAIN, as WSL sees them, which the Makefile reads
  config.props  XboxSilverlightSdk, for sample\HttpsSample.csproj
  config.json   all of it, for tools\Release.ps1 (tools\Config.ps1 reads it)
Run it again after changing something; -Show prints what's configured.

.EXAMPLE
.\tools\Configure.ps1

.EXAMPLE
.\tools\Configure.ps1 -Xsdk D:\src\xsdk -NonInteractive
#>
param(
    # xsdk's dll-modules checkout, as a Windows path.
    [string]$Xsdk = '',
    # The toolchain's prefix in WSL, as /home/me/xsdk-gcc/bin/powerpc-xsdk-elf-.
    [string]$Toolchain = '',
    # Silverlight360's checkout, for the sample.
    [string]$Silverlight360 = '',
    [switch]$CloneXsdk,
    [switch]$BuildToolchain,
    [switch]$CloneSilverlight360,
    # Asks nothing: clones and builds only what the switches above say.
    [switch]$NonInteractive,
    [switch]$Show
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$parent = Split-Path $root -Parent
$grandparent = Split-Path $parent -Parent
$jsonFile = Join-Path $root 'config.json'
$xsdkRepository = 'https://github.com/jessenic/xsdk.git'
$xsdkBranch = 'dll-modules'
$silverlight360Repository = 'https://github.com/Digiex/Silverlight360.git'

if ($Show) {
    foreach ($name in 'Xsdk', 'Toolchain', 'Silverlight360') {
        '{0,-15} {1}' -f $name, $(if ($value = & (Join-Path $PSScriptRoot 'Config.ps1') $name) { $value } else { '(not set)' })
    }
    return
}

$previous = if (Test-Path -LiteralPath $jsonFile) { Get-Content -LiteralPath $jsonFile -Raw | ConvertFrom-Json } else { $null }

# Prints one line of the report.
function Report([string]$name, [string]$value, [string]$fix, [switch]$Optional) {
    if ($value) {
        Write-Host ('{0,-15} {1}' -f $name, $value)
    } else {
        Write-Host ('{0,-15} {1}' -f $name, "missing: $fix") -ForegroundColor $(if ($Optional) { 'Yellow' } else { 'Red' })
    }
}
# Asks a yes-or-no question whose answer is yes unless it's no; with -NonInteractive, the switch's answer.
function Confirm([string]$question, [bool]$given) {
    if ($given) { return $true }
    if ($NonInteractive) { return $false }
    (Read-Host "$question (Y/n)") -notmatch '^n'
}
# A command in WSL, through bash so ~ and $HOME are its own; its output, and whether it succeeded.
function Invoke-Wsl([string]$command) {
    $output = wsl -e bash -lc $command 2>&1
    [pscustomobject]@{ Ok = ($LASTEXITCODE -eq 0); Output = $output }
}
# A Windows path as WSL sees it: C:\x\y is /mnt/c/x/y.
function ConvertTo-Wsl([string]$path) {
    '/mnt/{0}{1}' -f $path.Substring(0, 1).ToLowerInvariant(), ($path.Substring(2) -replace '\\', '/')
}

# 1. The submodules.
Write-Host 'Submodules:'
& git.exe -C $root submodule update --init --recursive
if ($LASTEXITCODE) { throw 'git submodule update failed.' }
foreach ($port in 'curl-xenon', 'mbedtls-xenon') {
    $folder = Join-Path $root "deps\$port"
    Report $port $(if (Test-Path -LiteralPath (Join-Path $folder 'CMakeLists.txt')) { "deps\$port, " + (& git.exe -C $folder rev-parse --short=12 HEAD) }) 'git submodule update --init --recursive'
}

# 2. WSL and its tools.
Write-Host ''
Write-Host 'WSL:'
if (-not (Get-Command wsl -ErrorAction SilentlyContinue) -or -not (Invoke-Wsl 'true').Ok) {
    throw 'WSL is needed to build the DLL: install it (wsl --install -d Debian), then run this again.'
}
$tools = Invoke-Wsl 'for t in make cmake ninja python3 git; do command -v $t >/dev/null || echo $t; done'
$missingTools = @($tools.Output | Where-Object { $_ })
Report 'build tools' $(if (-not $missingTools) { 'make, cmake, ninja, python3, git' }) `
    "$($missingTools -join ', '): sudo apt install build-essential cmake ninja-build python3 git"

# 3. xsdk.
$test = { param($p) $p -and (Test-Path -LiteralPath (Join-Path $p 'cmake\xsdk.cmake')) -and
    (Test-Path -LiteralPath (Join-Path $p 'tools\elf2xex.py')) -and
    (Select-String -LiteralPath (Join-Path $p 'tools\elf2xex.py') -Pattern '--dll' -SimpleMatch -Quiet) }
$ngenConfig = Join-Path $parent 'XboxNGen\config.json'
$ngenXsdk = if (Test-Path -LiteralPath $ngenConfig) { (Get-Content -LiteralPath $ngenConfig -Raw | ConvertFrom-Json).Xsdk } else { '' }
$candidates = @($Xsdk, $env:XSDK, $(if ($previous) { $previous.Xsdk }), $ngenXsdk, (Join-Path $grandparent 'xsdk'), (Join-Path $parent 'xsdk'))
if ($Xsdk -and -not (& $test $Xsdk)) { throw "$Xsdk isn't xsdk's dll-modules build: it needs cmake\xsdk.cmake and tools\elf2xex.py with --dll." }
$foundXsdk = $candidates | Where-Object { & $test $_ } | Select-Object -First 1
$cloneTo = Join-Path $grandparent 'xsdk'
if (-not $foundXsdk -and -not (Test-Path -LiteralPath $cloneTo) -and
        (Confirm "xsdk's $xsdkBranch build not found. Clone $xsdkRepository ($xsdkBranch) to ${cloneTo}?" $CloneXsdk)) {
    & git.exe clone --branch $xsdkBranch $xsdkRepository $cloneTo
    if ($LASTEXITCODE) { throw 'git clone failed.' }
    $foundXsdk = $cloneTo
}

# 4. The toolchain, which xsdk's library needs too.
$prefix = @($Toolchain, $(if ($previous) { $previous.Toolchain }), '$HOME/xsdk-gcc/bin/powerpc-xsdk-elf-') |
    Where-Object { $_ } | Where-Object { (Invoke-Wsl "test -x ${_}gcc").Ok } | Select-Object -First 1
if (-not $prefix -and $foundXsdk -and
        (Confirm 'xsdk''s toolchain not found. Build it into ~/xsdk-gcc (about 10 minutes, and a 136 MB download)?' $BuildToolchain)) {
    $build = "cd '$(ConvertTo-Wsl $foundXsdk)' && PREFIX=`$HOME/xsdk-gcc WORK=`$HOME/xsdk-gcc-work JOBS=`$(nproc) tools/toolchain/build.sh"
    wsl -e bash -lc $build
    if ($LASTEXITCODE) { throw "xsdk's toolchain build failed: its logs are in ~/xsdk-gcc-work/logs in WSL." }
    $prefix = '$HOME/xsdk-gcc/bin/powerpc-xsdk-elf-'
}
# The prefix as WSL spells it out, so make and CMake get no $HOME to expand.
$toolchainPath = if ($prefix) { (Invoke-Wsl "echo $prefix").Output | Select-Object -Last 1 } else { '' }

# xsdk's library, which the DLL links.
if ($foundXsdk -and $toolchainPath -and -not (Test-Path -LiteralPath (Join-Path $foundXsdk 'build\libxsdk.a'))) {
    Write-Host "Building xsdk's libxsdk.a in $foundXsdk..."
    wsl --cd $foundXsdk -e bash -lc "make -j`$(nproc) build/libxsdk.a TOOLCHAIN=$toolchainPath"
    if ($LASTEXITCODE) { throw "xsdk's libxsdk.a didn't build." }
}
$xsdkReady = $foundXsdk -and (Test-Path -LiteralPath (Join-Path $foundXsdk 'build\libxsdk.a'))

# 5. Silverlight360, for the sample.
$sdkTest = { param($p) $p -and (Test-Path -LiteralPath (Join-Path $p 'msbuild\XboxSilverlight.targets')) }
$sdkCandidates = @($Silverlight360, $env:Silverlight360Dir, $env:SILVERLIGHT360, $(if ($previous) { $previous.Silverlight360 }),
    (Join-Path $parent 'XboxSilverlightSdk'), (Join-Path $parent 'Silverlight360'))
$foundSdk = $sdkCandidates | Where-Object { & $sdkTest $_ } | Select-Object -First 1
$sdkTo = Join-Path $parent 'Silverlight360'
if (-not $foundSdk -and -not (Test-Path -LiteralPath $sdkTo) -and
        (Confirm "Silverlight360 not found, which the sample needs. Clone $silverlight360Repository to ${sdkTo}?" $CloneSilverlight360)) {
    & git.exe clone $silverlight360Repository $sdkTo
    if ($LASTEXITCODE) { throw 'git clone failed.' }
    $foundSdk = $sdkTo
}

$config = [ordered]@{
    Xsdk = if ($foundXsdk) { [IO.Path]::GetFullPath($foundXsdk) } else { '' }
    Toolchain = $toolchainPath
    Silverlight360 = if ($foundSdk) { [IO.Path]::GetFullPath($foundSdk) } else { '' }
}
Write-Host ''
Write-Host 'Https360:'
Report 'xsdk' $(if ($xsdkReady) { "$($config.Xsdk), " + (& git.exe -C $config.Xsdk rev-parse --short=12 HEAD) + " ($(& git.exe -C $config.Xsdk rev-parse --abbrev-ref HEAD))" }) `
    $(if ($config.Xsdk) { 'libxsdk.a isn''t built: it needs the toolchain' } else { "clone $xsdkRepository ($xsdkBranch), or pass -Xsdk" })
Report 'toolchain' $(if ($toolchainPath) { ((Invoke-Wsl "${toolchainPath}gcc --version").Output | Select-Object -First 1) + ", $toolchainPath" }) `
    "run .\tools\Configure.ps1 -BuildToolchain, or pass -Toolchain"
Report 'Silverlight360' $config.Silverlight360 "optional, for the sample: clone $silverlight360Repository beside this repository, or pass -Silverlight360" -Optional

$config | ConvertTo-Json | Set-Content -LiteralPath $jsonFile -Encoding utf8
$mk = "# Written by tools\Configure.ps1.`n"
if ($config.Xsdk) { $mk += "XSDK ?= $(ConvertTo-Wsl $config.Xsdk)`n" }
if ($toolchainPath) { $mk += "TOOLCHAIN ?= $toolchainPath`n" }
Set-Content -LiteralPath (Join-Path $root 'config.mk') $mk -Encoding ascii -NoNewline
$sdk = if ($config.Silverlight360) { $config.Silverlight360.TrimEnd('\') } else { '' }
$props = @"
<!-- Written by tools\Configure.ps1. -->
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <XboxSilverlightSdk Condition=" '`$(XboxSilverlightSdk)' == '' and '$sdk' != '' ">$sdk</XboxSilverlightSdk>
  </PropertyGroup>
</Project>
"@
Set-Content -LiteralPath (Join-Path $root 'config.props') $props -Encoding utf8

Write-Host ''
if (-not $xsdkReady -or -not $toolchainPath -or $missingTools) {
    Write-Warning 'The DLL can''t be built until what''s missing above is there: run .\tools\Configure.ps1 again then.'
} else {
    Write-Host 'Ready. In WSL, in this folder: make -j$(nproc)'
}
