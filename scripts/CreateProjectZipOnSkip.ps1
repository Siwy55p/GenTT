param(
    [string]$TargetDirectory = "C:\Users\Piotr\Desktop\ChatGPTGeneratortTikTok"
)

$ErrorActionPreference = "Stop"

function Invoke-Git {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    $output = & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }

    return $output
}

$repositoryRoot = (Invoke-Git rev-parse --show-toplevel).Trim()
Set-Location -LiteralPath $repositoryRoot

$commitMessage = (Invoke-Git log -1 --pretty=%B) -join "`n"
if ($commitMessage -notmatch "(?i)(^|\W)skip($|\W)") {
    exit 0
}

New-Item -ItemType Directory -Path $TargetDirectory -Force | Out-Null

$shortSha = (Invoke-Git rev-parse --short HEAD).Trim()
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$zipPath = Join-Path $TargetDirectory "ChatGPTGeneratortTikTok-$timestamp-$shortSha.zip"

Invoke-Git archive --format=zip "--output=$zipPath" HEAD | Out-Null

Write-Host "Created project ZIP: $zipPath"
