function Get-ToucheRuntimeManifest {
    param([Parameter(Mandatory)][string]$Directory)
    $runtimeRoot = (Resolve-Path -LiteralPath $Directory -ErrorAction Stop).Path
    Get-ChildItem -LiteralPath $runtimeRoot -File -Recurse -ErrorAction Stop |
        Where-Object { $_.Extension -in '.dll','.exe' -or $_.Name -like '*.deps.json' -or $_.Name -like '*.runtimeconfig.json' } |
        Sort-Object FullName |
        ForEach-Object {
            [pscustomobject][ordered]@{
                Path = [IO.Path]::GetRelativePath($runtimeRoot, $_.FullName)
                Length = $_.Length
                Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256 -ErrorAction Stop).Hash
            }
        }
}
