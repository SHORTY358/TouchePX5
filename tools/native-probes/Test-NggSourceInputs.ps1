param([string]$Dotnet = 'C:\Dev\dotnet10\dotnet.exe')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$run = Join-Path $repo ('artifacts/ngg-source-inputs/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$generator = Join-Path $repo 'tools/SharpEmu.Tools.NggSourcesDump/SharpEmu.Tools.NggSourcesDump.csproj'
$checker = Join-Path $repo 'tools/SharpEmu.Tools.GpuConformance/SharpEmu.Tools.GpuConformance.csproj'
Push-Location $repo
try {
    $cases = @()
    foreach ($variant in @('positive', 'omitted', 'truncated')) {
        $directory = Join-Path $run $variant
        $arguments = @('run', '--project', $generator, '-c', 'Release', '--', $directory)
        if ($variant -eq 'omitted') { $arguments += '--omit-inputs' }
        if ($variant -eq 'truncated') { $arguments += '--truncate' }
        & $Dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Source generation failed' }
        $log = Join-Path $directory 'gpu.log'
        & $Dotnet run --project $checker -c Release -- (Join-Path $directory 'sources.spv') (Join-Path $directory 'expected.json') (Join-Path $directory 'actual.json') (Join-Path $directory 'initial.json') > $log 2>&1
        $code = $LASTEXITCODE
        $expectedExit = if ($variant -eq 'omitted') { 1 } else { 0 }
        $expectedResult = if ($variant -eq 'omitted') { 'RESULT: 853 mismatch(es)' } else { 'RESULT: all values match' }
        $text = Get-Content -LiteralPath $log -Raw
        if ($code -ne $expectedExit -or !$text.Contains($expectedResult)) { throw "Unexpected source result: $variant; see $log" }
        $cases += @{ Name = $variant; ExitCode = $code; Device = ($text -split '\r?\n' | Where-Object { $_ -like 'executing on:*' } | Select-Object -First 1)
            Hashes = @('sources.spv','guest-shader.bin','initial.json','expected.json','actual.json','gpu.log') | ForEach-Object { @{ File = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $directory $_)).Hash } } }
    }
    @{ Scope = 'Independent single-subgroup source reads/barrier on GPU; not Gears or a scheduler'; GpuExecuted = $true; Cases = $cases
        Sources = @('src/SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs', 'src/SharpEmu.ShaderCompiler/Gen5NggPassthroughSources.cs',
            'tools/SharpEmu.Tools.NggSourcesDump/Program.cs', 'tools/SharpEmu.Tools.GpuConformance/Program.cs') | ForEach-Object { @{ File = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $repo $_)).Hash } }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $run 'execution-report.json') -Encoding UTF8
    Write-Output "PASS actual source reads, missing-input control and uniform truncated-group rejection: $run"
} finally { Pop-Location }
