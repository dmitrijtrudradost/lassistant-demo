param(
    [Parameter(Mandatory = $true)]
    [string] $RepoRoot,
    [Parameter(Mandatory = $true)]
    [string] $OutDir
)

$ErrorActionPreference = 'Stop'

$repo = [System.IO.Path]::GetFullPath($RepoRoot)
$out = [System.IO.Path]::GetFullPath($OutDir)
$docsOut = Join-Path $out 'docs'

if (Test-Path -LiteralPath $docsOut) {
    Remove-Item -LiteralPath $docsOut -Recurse -Force
}

foreach ($name in @('руководства', 'правила')) {
    $src = Join-Path $repo "docs\$name"
    if (-not (Test-Path -LiteralPath $src)) {
        Write-Host "SKIP docs: $name (folder not found)"
        continue
    }

    $dst = Join-Path $docsOut $name
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    Copy-Item -Path (Join-Path $src '*') -Destination $dst -Recurse -Force
    Write-Host "Copied docs\$name"
}