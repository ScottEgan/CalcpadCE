param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath
)

$ErrorActionPreference = 'Stop'

if (-not $env:CALCPAD_SIGN_THUMBPRINT) {
    exit 0
}

$SignTool = if ($env:CALCPAD_SIGNTOOL) { $env:CALCPAD_SIGNTOOL } else { 'signtool' }
& $SignTool sign /sha1 $env:CALCPAD_SIGN_THUMBPRINT /fd sha256 /tr http://timestamp.digicert.com /td sha256 $FilePath
exit $LASTEXITCODE
