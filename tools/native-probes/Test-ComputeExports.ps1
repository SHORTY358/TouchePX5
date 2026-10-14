param([string]$Dotnet = 'C:\Dev\dotnet10\dotnet.exe')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$run = Join-Path $repo ('artifacts/compute-export-gpu/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$generator = Join-Path $repo 'tools/SharpEmu.Tools.ComputeExportDump/SharpEmu.Tools.ComputeExportDump.csproj'
$checker = Join-Path $repo 'tools/SharpEmu.Tools.GpuConformance/SharpEmu.Tools.GpuConformance.csproj'
$dumper = Join-Path $repo 'tools/SharpEmu.Tools.ShaderDump/SharpEmu.Tools.ShaderDump.csproj'
Push-Location $repo
try {
    $cases = @()
    $variants = @(
        @{ Name = 'positive'; Flags = @(); Exit = 0; Mismatches = 0 },
        @{ Name = 'negative-capacity'; Flags = @('--negative-capacity'); Exit = 1; Mismatches = 6 },
        @{ Name = 'triangle'; Flags = @('--triangle'); Exit = 0; Mismatches = 0 },
        @{ Name = 'triangle-null'; Flags = @('--null-triangle'); Exit = 0; Mismatches = 0 },
        @{ Name = 'triangle-invalid'; Flags = @('--invalid-triangle'); Exit = 0; Mismatches = 0 },
        @{ Name = 'triangle-gap'; Flags = @('--gap-triangle'); Exit = 0; Mismatches = 0 },
        @{ Name = 'triangle-capacity-negative'; Flags = @('--triangle', '--negative-capacity'); Exit = 1; Mismatches = 5 },
        @{ Name = 'triangle-decoded'; Flags = @('--triangle', '--decode-triangle'); Exit = 0; Mismatches = 0 },
        @{ Name = 'triangle-null-decoded'; Flags = @('--null-triangle', '--decode-triangle'); Exit = 0; Mismatches = 0 },
        @{ Name = 'triangle-multi-group'; Flags = @('--triangle', '--multi-group'); Exit = 0; Mismatches = 0 }
    )
    foreach ($variant in $variants) {
        $name = $variant.Name
        $directory = Join-Path $run $name
        $arguments = @('run', '--project', $generator, '-c', 'Release', '--', $directory)
        $arguments += $variant.Flags
        & $Dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "Export generation failed: $name" }
        $log = Join-Path $directory 'gpu.log'
        & $Dotnet run --project $checker -c Release -- (Join-Path $directory 'exports.spv') (Join-Path $directory 'expected.json') > $log 2>&1
        $actualExit = $LASTEXITCODE
        $expectedExit = $variant.Exit
        $text = Get-Content -LiteralPath $log -Raw
        $expectedResult = if ($expectedExit -ne 0) { "RESULT: $($variant.Mismatches) mismatch(es)" } else { 'RESULT: all values match' }
        if ($actualExit -ne $expectedExit -or !$text.Contains($expectedResult)) {
            throw "Unexpected GPU result: $name; see $log"
        }
        $cases += [ordered]@{
            Name = $name; ExitCode = $actualExit; ExpectedExitCode = $expectedExit
            Device = ($text -split '\r?\n' | Where-Object { $_ -like 'executing on:*' } | Select-Object -First 1)
            SpirvSha256 = (Get-FileHash -LiteralPath (Join-Path $directory 'exports.spv')).Hash
            ExpectedSha256 = (Get-FileHash -LiteralPath (Join-Path $directory 'expected.json')).Hash
            LogSha256 = (Get-FileHash -LiteralPath $log).Hash
            GuestShaderSha256 = if (Test-Path -LiteralPath (Join-Path $directory 'guest-shader.bin')) {
                (Get-FileHash -LiteralPath (Join-Path $directory 'guest-shader.bin')).Hash
            } else { $null }
        }
    }
    $baseline = Join-Path $run 'arithmetic-baseline'
    & $Dotnet run --project $dumper -c Release -- $baseline > (Join-Path $run 'dump.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Shader dumper failed' }
    & $Dotnet run --project $checker -c Release -- (Join-Path $baseline 'exec-cs.spv') > (Join-Path $baseline 'gpu.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Original arithmetic GPU baseline failed' }
    $sourceFiles = @('tools/SharpEmu.Tools.ComputeExportDump/Program.cs',
        'tools/SharpEmu.Tools.GpuConformance/Program.cs', 'tools/SharpEmu.Tools.ShaderDump/Program.cs',
        'src/SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs',
        'src/SharpEmu.ShaderCompiler.Vulkan/Gen5ComputeExportOutput.cs',
        'src/SharpEmu.ShaderCompiler.Vulkan/Gen5ComputeTriangleOutput.cs',
        'src/SharpEmu.ShaderCompiler/Gen5TriangleConnectivity.cs')
    $hashes = @($sourceFiles | ForEach-Object {
        [ordered]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $repo $_)).Hash }
    })
    [ordered]@{
        Scope = 'Synthetic IR plus two hand-assembled guest decode cases through real Vulkan GPU; not Gears or NGG system inputs'
        GpuExecuted = $true; Cases = $cases; ArithmeticBaselinePassed = $true
        Sources = $hashes; DotnetSha256 = (Get-FileHash -LiteralPath $Dotnet).Hash
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $run 'execution-report.json') -Encoding UTF8
    Write-Output "PASS compute EXP hardware checks and negative control: $run"
} finally { Pop-Location }
