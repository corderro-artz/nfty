param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$Candidate,
    [string[]]$ExcludeExtensions = @()
)

# Compare relative membership and SHA-256 of every included file. For ZIP archives,
# compare extracted entry payloads using output-sample.ps1 and exclude .mnu here.
$ErrorActionPreference = 'Stop'
function Get-Inventory([string]$Root) {
    $rootPath = (Resolve-Path -LiteralPath $Root).Path
    $result = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($file in Get-ChildItem -LiteralPath $rootPath -File -Recurse) {
        if ($file.Extension -in $ExcludeExtensions) { continue }
        $relative = [System.IO.Path]::GetRelativePath($rootPath, $file.FullName)
        $result[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    return ,$result
}

$expected = Get-Inventory $Baseline
$actual = Get-Inventory $Candidate
$missing = @($expected.Keys | Where-Object { -not $actual.ContainsKey($_) })
$extra = @($actual.Keys | Where-Object { -not $expected.ContainsKey($_) })
$changed = @($expected.Keys | Where-Object { $actual.ContainsKey($_) -and $expected[$_] -cne $actual[$_] })
[ordered]@{
    baselineFiles = $expected.Count
    candidateFiles = $actual.Count
    identicalFiles = $expected.Count - $missing.Count - $changed.Count
    missing = $missing
    extra = $extra
    changed = $changed
} | ConvertTo-Json -Depth 3
if ($missing.Count -or $extra.Count -or $changed.Count) { exit 1 }
