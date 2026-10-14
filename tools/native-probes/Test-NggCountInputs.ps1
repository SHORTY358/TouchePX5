param([string]$Dotnet = 'C:\Dev\dotnet10\dotnet.exe')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$run = Join-Path $repo ('artifacts/ngg-count-inputs/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$generator = Join-Path $repo 'tools/SharpEmu.Tools.NggCountsDump/SharpEmu.Tools.NggCountsDump.csproj'
$checker = Join-Path $repo 'tools/SharpEmu.Tools.GpuConformance/SharpEmu.Tools.GpuConformance.csproj'
Push-Location $repo
try {
    $results = @()
    foreach ($negative in @($false, $true)) {
        $directory = Join-Path $run $(if ($negative) { 'omitted-inputs-negative' } else { 'positive' })
        $arguments = @('run', '--project', $generator, '-c', 'Release', '--', $directory)
        if ($negative) { $arguments += '--omit-count-inputs' }
        & $Dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Count shader generation failed' }
        foreach ($case in Get-ChildItem -LiteralPath $directory -Directory) {
            $log = Join-Path $case.FullName 'gpu.log'
            & $Dotnet run --project $checker -c Release -- (Join-Path $case.FullName 'counts.spv') (Join-Path $case.FullName 'expected.json') > $log 2>&1
            $code = $LASTEXITCODE
            $text = Get-Content -LiteralPath $log -Raw
            $expectedExit = if ($negative) { 1 } else { 0 }
            $matches = if ($negative) { $text.Contains('RESULT: 128 mismatch(es)') } else { $text.Contains('RESULT: all values match') }
            if ($code -ne $expectedExit -or !$matches) { throw "Unexpected count GPU result: $($case.Name); see $log" }
            $results += [ordered]@{
                Name = $case.Name; Negative = $negative; ExitCode = $code
                Device = ($text -split '\r?\n' | Where-Object { $_ -like 'executing on:*' } | Select-Object -First 1)
                SpirvSha256 = (Get-FileHash -LiteralPath (Join-Path $case.FullName 'counts.spv')).Hash
                GuestSha256 = (Get-FileHash -LiteralPath (Join-Path $case.FullName 'guest-shader.bin')).Hash
                ExpectedSha256 = (Get-FileHash -LiteralPath (Join-Path $case.FullName 'expected.json')).Hash
                LogSha256 = (Get-FileHash -LiteralPath $log).Hash
            }
        }
    }
    $sources = @('src/SharpEmu.ShaderCompiler/Gen5NggMergedGroup.cs',
        'src/SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs',
        'tools/SharpEmu.Tools.NggCountsDump/Program.cs', 'tools/SharpEmu.Tools.GpuConformance/Program.cs')
    [ordered]@{
        Scope = 'Explicit uniform count fields only; guest decode and GPU reads; not Gears, source IDs or a scheduler'
        GpuExecuted = $true; Cases = $results
        Sources = @($sources | ForEach-Object { @{ Path = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $repo $_)).Hash } })
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $run 'execution-report.json') -Encoding UTF8
    Write-Output "PASS count field GPU reads and omitted-input negative control: $run"
} finally { Pop-Location }
