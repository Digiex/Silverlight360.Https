<#
.SYNOPSIS
Builds https360.xex from scratch and publishes it on GitHub Releases, with what an app needs beside it.

.DESCRIPTION
As Silverdash360's and XboxNGen's tools\Release.ps1 do:
1. The release is made from a commit as it is, which GitHub has: nothing may be uncommitted, and the commit must be
   pushed already. Nothing is pushed.
2. The version is -Version if given, else the last release's (the highest v<version> tag) with its patch (or -Bump's
   part) raised; the first release is 0.1.0. It is tagged v<version> on the commit, by gh.
3. The DLL is built from scratch in WSL (make clean, then make). -Elf2xex, -Xsdk, -Toolchain, -MbedtlsXenon and
   -CurlXenon go to the Makefile, as WSL paths, when its defaults don't fit; -Elf2xex is xsdk's dll-modules checkout
   two folders up (C:\Proj\xsdk) unless given.
4. build\release\Https360-<version>.zip has, in an Https360 folder, https360.xex, cacert.pem, csharp\HttpsClient.cs,
   README.md, LICENSE.md, THIRD-PARTY-NOTICES.md, licenses\ and BUILD.txt: what was built from what.
5. The release's notes are the commits since the last release, and the zip is its file.
The build isn't run: test it first (docs\testing.md). Needs WSL with xsdk's toolchain, and the GitHub CLI (gh),
signed in, unless -DryRun.

.EXAMPLE
.\tools\Release.ps1 -DryRun

Shows the version, builds and zips, and shows the notes, without publishing.

.EXAMPLE
.\tools\Release.ps1 -Bump Minor
#>
param(
    # The version to release, when it isn't the last one's raised.
    [ValidatePattern('^\d+\.\d+\.\d+$')] [string]$Version = '',
    # The part of the last release's version to raise.
    [ValidateSet('Patch', 'Minor', 'Major')] [string]$Bump = 'Patch',
    # For the Makefile, as WSL paths: elf2xex.py with --dll, xsdk, its toolchain's prefix and the two ports.
    [string]$Elf2xex = '',
    [string]$Xsdk = '',
    [string]$Toolchain = '',
    [string]$MbedtlsXenon = '',
    [string]$CurlXenon = '',
    # Shows what would be released, without publishing.
    [switch]$DryRun,
    [switch]$Prerelease
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repository = 'Digiex/Https360'
$parent = [IO.Path]::GetFullPath((Join-Path $root '..\..'))

# UTF-8 without a byte order mark, which GitHub would show in the notes.
function Write-Utf8([string]$path, [string[]]$lines) {
    [IO.File]::WriteAllLines($path, $lines, (New-Object Text.UTF8Encoding $false))
}
function Invoke-Git { $output = & git.exe -C $root @args; if ($LASTEXITCODE) { throw "git $args failed" }; $output }
# A Windows path as WSL sees it.
function ConvertTo-Wsl([string]$path) {
    $converted = wsl -e wslpath -a ($path -replace '\\', '/')
    if ($LASTEXITCODE) { throw "WSL can't see $path." }
    $converted
}
# The commit a checkout is at, and its branch, or '' when it isn't one.
function Get-Commit([string]$folder) {
    if (-not (Test-Path -LiteralPath (Join-Path $folder '.git'))) { return '' }
    '{0} ({1})' -f (& git.exe -C $folder rev-parse --short=12 HEAD), (& git.exe -C $folder rev-parse --abbrev-ref HEAD)
}

# 1. The commit.
if (Invoke-Git status --porcelain --untracked-files=no) { throw 'There are changes not committed. Commit them, or stash them, first.' }
Invoke-Git fetch --quiet --tags origin
$commit = Invoke-Git rev-parse HEAD
if (-not $DryRun) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'The GitHub CLI (gh) is needed to publish: https://cli.github.com' }
    if (-not (Invoke-Git branch -r --contains $commit)) { throw "Commit $commit isn't on GitHub yet. Push it first." }
}

# 2. The version.
$released = @(foreach ($t in (Invoke-Git tag --list 'v*')) {
    $parsed = $null
    if ([version]::TryParse($t.Substring(1), [ref]$parsed)) { [pscustomobject]@{ Tag = $t; Version = $parsed } }
})
$last = $released | Sort-Object Version -Descending | Select-Object -First 1
if (-not $Version) {
    $Version = if (-not $last) { '0.1.0' } else {
        $v = $last.Version
        switch ($Bump) {
            'Major' { "$($v.Major + 1).0.0" }
            'Minor' { "$($v.Major).$($v.Minor + 1).0" }
            default { "$($v.Major).$($v.Minor).$([Math]::Max($v.Build, 0) + 1)" }
        }
    }
} elseif ($last -and [version]$Version -le $last.Version) { throw "$Version isn't higher than $($last.Tag)." }
$tag = "v$Version"
if ($released | Where-Object { $_.Tag -eq $tag }) { throw "$tag is released already." }
Write-Host ("Version $Version" + $(if ($last) { ", after $($last.Tag)" } else { ', the first release' }))

# 3. The DLL, from scratch.
if (-not $Elf2xex) {
    $beside = Join-Path $parent 'xsdk\tools\elf2xex.py'
    if (-not (Test-Path -LiteralPath $beside)) { throw "No elf2xex.py at ${beside}: pass -Elf2xex, xsdk's dll-modules one, as a WSL path." }
    $Elf2xex = ConvertTo-Wsl $beside
}
$makeArgs = @("ELF2XEX=$Elf2xex")
foreach ($pair in @(@('XSDK', $Xsdk), @('TOOLCHAIN', $Toolchain), @('MBEDTLS_XENON', $MbedtlsXenon), @('CURL_XENON', $CurlXenon))) {
    if ($pair[1]) { $makeArgs += "$($pair[0])=$($pair[1])" }
}
$make = $makeArgs -join ' '
wsl --cd $root -e bash -lc "set -e; make clean; make -j`$(nproc) $make"
if ($LASTEXITCODE) { throw 'The build failed.' }
foreach ($built in 'https360.xex', 'cacert.pem') {
    if (-not (Test-Path -LiteralPath (Join-Path $root $built))) { throw "The build made no $built." }
}
$toolchainName = wsl -e bash -lc "`${TOOLCHAIN:-$(if ($Toolchain) { $Toolchain } else { '$HOME/xsdk-gcc/bin/powerpc-xsdk-elf-' })}gcc --version | head -n 1"
$xsdkCommit = wsl -e bash -lc "cd $(if ($Xsdk) { $Xsdk } else { '$HOME/xsdk' }) && echo `$(git rev-parse --short=12 HEAD) \(`$(git rev-parse --abbrev-ref HEAD)\)"
$elf2xexCommit = wsl -e bash -lc "cd `$(dirname $Elf2xex) && echo `$(git rev-parse --short=12 HEAD) \(`$(git rev-parse --abbrev-ref HEAD)\)"
$ports = foreach ($port in @(@('curl-xenon', $CurlXenon), @('mbedtls-xenon', $MbedtlsXenon))) {
    $at = if ($port[1]) { '' } else { Get-Commit (Join-Path $parent $port[0]) }
    '{0} {1}' -f $port[0], $(if ($at) { $at } else { 'at ' + $port[1] })
}

# 4. The zip.
$name = "Https360 $Version"
$out = Join-Path $root 'build\release'
$staging = Join-Path $out 'Https360'
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
$files = @(Invoke-Git ls-files README.md LICENSE.md THIRD-PARTY-NOTICES.md licenses csharp) + 'https360.xex', 'cacert.pem'
foreach ($f in $files) {
    $to = Join-Path $staging $f
    $null = New-Item -ItemType Directory (Split-Path $to -Parent) -Force
    Copy-Item -LiteralPath (Join-Path $root $f) -Destination $to
}
$build = @"
$name
https://github.com/$repository

Built from commit $($commit.Substring(0, 12)), with:
- $($ports -join "`n- ")
- xsdk $xsdkCommit, and its elf2xex.py from $elf2xexCommit
- $toolchainName

https360.xex is the DLL, at base 0x8D000000, and cacert.pem Mozilla's root certificates, which it checks
servers against: both go beside an app's default.xex. csharp\HttpsClient.cs is the C# side, which needs
Silverlight360's samples\Common\NativeBuffer.cs. See https://digiex.github.io/Https360/using/.

The licenses in licenses\ and THIRD-PARTY-NOTICES.md go with https360.xex and cacert.pem.
"@
Write-Utf8 (Join-Path $staging 'BUILD.txt') ($build -split "`r?`n")
$zip = Join-Path $out "Https360-$Version.zip"
Compress-Archive -Path $staging -DestinationPath $zip

# 5. The notes: the commits since the last release before this commit.
$previous = $null
foreach ($candidate in ($released | Sort-Object Version -Descending)) {
    & git.exe -C $root merge-base --is-ancestor $candidate.Tag $commit
    if ($LASTEXITCODE -eq 0) { $previous = $candidate.Tag; break }
}
$range = if ($previous) { "$previous..$commit" } else { $commit }
$notes = @(if ($previous) { "## Changes since $previous" } else { '## Changes' }) + ''
$notes += Invoke-Git log $range --no-merges --format='- %s (%h)'
$notes += '', ("From $($commit.Substring(0, 7)). The zip has https360.xex and cacert.pem, to go beside an app's " +
    'default.xex, HttpsClient.cs and the licenses that go with them: see BUILD.txt in it.')
$notesFile = Join-Path $out 'notes.md'
Write-Utf8 $notesFile $notes

Write-Host "$name ($tag)"
Write-Host "Zip: $zip"
Write-Host ''
Get-Content -LiteralPath $notesFile | Write-Host
if ($DryRun) { Write-Host ''; Write-Host 'Dry run: nothing published.'; return }

$arguments = @('release', 'create', $tag, $zip, '--repo', $repository, '--target', $commit, '--title', $name,
    '--notes-file', $notesFile)
if ($Prerelease) { $arguments += '--prerelease' }
& gh @arguments
if ($LASTEXITCODE) { throw 'gh release create failed.' }
Invoke-Git fetch --quiet --tags origin
