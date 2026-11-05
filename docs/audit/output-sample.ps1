param(
    [Parameter(Mandatory)][string]$Cli,
    [Parameter(Mandatory)][string]$OutputRoot
)

# Run from the repository root. The caller supplies baseline or candidate binaries.
# Compare output files byte-for-byte. ZIP entry payloads are extracted because archive
# timestamps are packaging metadata, not deterministic generation output.
$ErrorActionPreference = 'Stop'
$Cli = (Resolve-Path -LiteralPath $Cli).Path
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw "Output already exists: $OutputRoot" }
New-Item -ItemType Directory -Path $OutputRoot | Out-Null

function Invoke-Nfty([string[]]$Arguments) {
    & dotnet $Cli @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "nfty failed: $($Arguments -join ' ')" }
}

$books = @{
    demo = 'src/Nfty.Core/Demo/ChestDemo.cbk'
    fixture = 'tests/fixtures/VaporPets.cbk'
}
foreach ($name in @('demo', 'fixture')) {
    $book = $books[$name]
    $initialCount = if ($name -eq 'fixture') { '8' } else { '24' }
    $finalCount = if ($name -eq 'fixture') { '12' } else { '32' }
    foreach ($threads in @(1, 4)) {
        $set = Join-Path $OutputRoot "$name-$threads"
        Invoke-Nfty @('generate', $book, '--count', $initialCount, '--seed', 'audit-output-2026',
            '--out', $set, '--threads', "$threads")
        Invoke-Nfty @('extend', $book, $set, '--to', $finalCount, '--seed', 'audit-extend-2026',
            '--threads', "$threads")
    }
    foreach ($mode in @('atlas', 'loose')) {
        $menu = Join-Path $OutputRoot "$name-$mode.mnu"
        Invoke-Nfty @('menu', $book, '--out', $menu, '--art', $mode)
        foreach ($seed in @('audit-one', 'audit-two', 'audit-three')) {
            Invoke-Nfty @('serve', $menu, '--seed', $seed,
                '--out', (Join-Path $OutputRoot "$name-$mode-$seed.png"))
        }
        [System.IO.Compression.ZipFile]::ExtractToDirectory($menu, "$menu.contents")
    }
}
Invoke-Nfty @('generate', $books.demo, '--count', '8', '--seed', 'audit-models',
    '--models', '--out', (Join-Path $OutputRoot 'models'), '--threads', '4')
