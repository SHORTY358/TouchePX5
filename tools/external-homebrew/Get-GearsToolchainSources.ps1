# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
[CmdletBinding()]
param([string[]]$Names=@())
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$destination=Join-Path $root 'artifacts/external-homebrew/gears-toolchain'
$lock=Get-Content "$PSScriptRoot/GearsToolchainDependencies.json" -Raw | ConvertFrom-Json
if($Names.Count -and @($Names | Where-Object { $_ -notin $lock.Name }).Count) { throw 'Unknown dependency name' }
foreach($dependency in $lock) {
    if($Names.Count -and $dependency.Name -notin $Names) { continue }
    $path=[IO.Path]::GetFullPath((Join-Path $destination $dependency.Name))
    if(-not $path.StartsWith($destination+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Dependency path escapes isolated toolchain directory' }
    if($dependency.Pin -notmatch '^[0-9a-f]{40}$') { throw 'Exact commit required' }
    if(-not (Test-Path "$path/.git")) {
        if((Test-Path $path) -and (Get-ChildItem -LiteralPath $path -Force)) { throw "Refusing nonempty unmanaged directory: $path" }
        & git init $path
        if($LASTEXITCODE) { throw 'Git init failed' }
        & git -C $path remote add origin ("https://github.com/"+$dependency.Repo+'.git')
        if($LASTEXITCODE) { throw 'Git remote failed' }
        & git -C $path config core.longpaths true
        if($dependency.Name -eq 'llvm-project') {
            & git -C $path sparse-checkout init --cone
            & git -C $path sparse-checkout set llvm cmake third-party
            if($LASTEXITCODE) { throw 'LLVM sparse checkout setup failed' }
        }
    }
    $remote=& git -C $path remote get-url origin
    if($LASTEXITCODE -or $remote -ne ("https://github.com/"+$dependency.Repo+'.git')) { throw "Dependency remote mismatch: $path" }
    $head=& git -C $path rev-parse --verify HEAD 2>$null
    if($LASTEXITCODE -eq 0) {
        if($head -ne $dependency.Pin -or (& git -C $path status --porcelain)) { throw "Pinned clean checkout required: $path" }
        Write-Output ("Verified: "+$dependency.Name+' '+$head)
        continue
    }
    & git -C $path fetch --depth 1 --filter=blob:none origin $dependency.Pin
    if($LASTEXITCODE) { throw "Dependency fetch failed: $path" }
    & git -C $path checkout --detach $dependency.Pin
    if($LASTEXITCODE) { throw "Dependency checkout failed: $path" }
    if((& git -C $path rev-parse HEAD) -ne $dependency.Pin) { throw 'Dependency pin mismatch' }
}
