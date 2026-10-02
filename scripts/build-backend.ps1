<#
.SYNOPSIS
    Compila el backend para PRODUCCION: publish Release -> ofuscacion con
    Obfuscar -> sin .pdb. Lo usa el CI (y se puede correr en la maquina de
    desarrollo para probar el resultado).

.DESCRIPTION
    El output en -OutDir es lo que se despliega. Los .pdb y el mapping de
    nombres NO van ahi: el mapping (Mapping.txt) queda en -MappingDir y se
    guarda aparte, es lo que permite des-ofuscar un stack trace.

.EXAMPLE
    .\scripts\build-backend.ps1 -OutDir .\artifacts\api -MappingDir .\artifacts\mapping
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutDir,
    [string]$MappingDir,
    [switch]$SkipObfuscation
)

$ErrorActionPreference = 'Stop'

function Invoke-Checked {
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][string[]]$ArgumentList)
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath $($ArgumentList -join ' ')' fallo con exit code $LASTEXITCODE"
    }
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ApiProject = Join-Path $RepoRoot 'backend\TaskManager\TaskManager.Api\TaskManager.Api.csproj'
$Work = Join-Path ([IO.Path]::GetTempPath()) "apptareas-build-$([guid]::NewGuid().ToString('N'))"
$InDir = Join-Path $Work 'in'
$ObfOut = Join-Path $Work 'out'
New-Item -ItemType Directory -Path $InDir, $ObfOut | Out-Null

try {
    Write-Host "==> dotnet publish (Release)" -ForegroundColor Cyan
    Invoke-Checked dotnet @('publish', $ApiProject, '-c', 'Release', '-o', $InDir, '--nologo', '-p:IncludeSourceRevisionInInformationalVersion=false')

    if ($SkipObfuscation) {
        Write-Host "Ofuscacion saltada (-SkipObfuscation)." -ForegroundColor DarkYellow
        $final = $InDir
    } else {
        Write-Host "==> Obfuscar" -ForegroundColor Cyan
        # Obfuscar necesita resolver las referencias al framework compartido
        # (Microsoft.AspNetCore.App / NETCore.App) para ver la jerarquia de
        # herencia; se toma la version del runtime mas nueva instalada.
        $frameworks = foreach ($fw in 'Microsoft.NETCore.App', 'Microsoft.AspNetCore.App') {
            $line = (& dotnet --list-runtimes) | Where-Object { $_ -like "$fw *" } | Select-Object -Last 1
            if ($line -match '^\S+ (\S+) \[(.+)\]$') { Join-Path $Matches[2] $Matches[1] }
        }
        $cfg = (Get-Content (Join-Path $PSScriptRoot 'obfuscar.xml') -Raw).
            Replace('@IN@', $InDir).Replace('@OUT@', $ObfOut).
            Replace('@FRAMEWORKS@', ($frameworks -join ';'))
        $cfgPath = Join-Path $Work 'obfuscar.xml'
        Set-Content -Path $cfgPath -Value $cfg -Encoding utf8

        Push-Location $RepoRoot   # donde esta dotnet-tools.json
        try {
            Invoke-Checked dotnet @('tool', 'restore')
            Invoke-Checked dotnet @('tool', 'run', 'obfuscar.console', $cfgPath)
        } finally {
            Pop-Location
        }

        # Obfuscar solo escribe los 3 dll propios en OutPath: el resto del
        # publish (dependencias, runtimeconfig, wwwroot...) se copia tal cual.
        Get-ChildItem $InDir -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($InDir.Length).TrimStart('\')
            $dest = Join-Path $ObfOut $rel
            if (-not (Test-Path $dest)) {
                New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
                Copy-Item $_.FullName $dest
            }
        }

        if ($MappingDir) {
            New-Item -ItemType Directory -Path $MappingDir -Force | Out-Null
            Copy-Item (Join-Path $ObfOut 'Mapping.txt') $MappingDir -Force
        }
        Remove-Item (Join-Path $ObfOut 'Mapping.txt') -Force -ErrorAction SilentlyContinue
        $final = $ObfOut
    }

    Write-Host "==> Quitando simbolos de depuracion (.pdb)" -ForegroundColor Cyan
    Get-ChildItem $final -Recurse -Filter *.pdb | Remove-Item -Force

    if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
    New-Item -ItemType Directory -Path (Split-Path -Parent ([IO.Path]::GetFullPath($OutDir))) -Force | Out-Null
    Copy-Item $final $OutDir -Recurse
    Write-Host "Backend listo en $OutDir" -ForegroundColor Green
}
finally {
    Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
}
