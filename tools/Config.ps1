<#
.SYNOPSIS
Gives one of the settings tools\Configure.ps1 saved in config.json at the repository's root, or '' when it isn't
there.

Settings: Xsdk (xsdk's dll-modules checkout, a Windows path), Toolchain (its compiler's prefix in WSL),
Silverlight360 (the SDK's checkout).

.EXAMPLE
$xsdk = & (Join-Path $PSScriptRoot 'Config.ps1') Xsdk
#>
param(
    [Parameter(Mandatory)]
    [ValidateSet('Xsdk', 'Toolchain', 'Silverlight360')]
    [string]$Name
)

$file = Join-Path (Split-Path -Parent $PSScriptRoot) 'config.json'
if (Test-Path -LiteralPath $file) {
    $value = (Get-Content -LiteralPath $file -Raw | ConvertFrom-Json).$Name
    if ($value) {
        return $value
    }
}
return ''
