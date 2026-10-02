#Requires -Version 7.0
<#
.SYNOPSIS
    Despliegue por PULL en el servidor de produccion: consulta el ultimo release
    publicado en el repo PRIVADO de releases, verifica su firma y hashes, aplica
    migraciones, cambia la version activa y hace rollback si el health check falla.

.DESCRIPTION
    Pensado para correr como tarea programada (cada ~5 min). Si no hay version
    nueva no hace nada. El servidor NO tiene codigo fuente ni credenciales que
    lean el repo de codigo: solo un token de solo-lectura del repo de releases.

    Layout (parametro -Root):
      releases\vX.Y.Z\api, frontend    cada release descomprimido
      current                          junction al release activo
                                       (Apache -> current\frontend ; servicio -> current\api)
      shared\appsettings.Local.json    config/secretos reales (nunca en un release)
      shared\App_Data                  adjuntos de usuarios (junction desde cada release)
      shared\signing-public.pem        llave publica con la que se verifica la firma
      shared\releases-token.xml        PSCredential (Export-Clixml) con el token read-only
      shared\migrations-connection.txt (opcional) connection string con permisos DDL;
                                       si no existe se usa ConnectionStrings:Default
      shared\hooks\pre-migrate.ps1     (opcional) backup de BD antes de migrar; si
                                       falla, aborta el despliegue
      shared\state.json                version activa y versiones que fallaron
      logs\                            bitacora

.PARAMETER DryRun
    Descarga a una carpeta temporal y verifica firma y hashes, pero no toca nada
    (ni migra, ni cambia current, ni reinicia el servicio).

.PARAMETER Version
    Fuerza un tag concreto (p. ej. v1.2.0) en vez de "latest". Permite reintentar
    una version marcada como fallida y es la via para un rollback manual a una
    version anterior (-AllowDowngrade).
#>
[CmdletBinding()]
param(
    [string]$Root = 'C:\apps\apptareas',
    [string]$Repo = 'LQHsu/AppTareas-releases',
    [string]$ServiceName = 'TaskManagerApi',
    [string]$HealthUrl = 'http://127.0.0.1:5168/health',
    [int]$KeepReleases = 3,
    [string]$Version,
    [switch]$AllowDowngrade,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$Shared = Join-Path $Root 'shared'
$ReleasesDir = Join-Path $Root 'releases'
$Current = Join-Path $Root 'current'
$LogDir = Join-Path $Root 'logs'
$StatePath = Join-Path $Shared 'state.json'
foreach ($d in $ReleasesDir, $LogDir) { New-Item -ItemType Directory -Path $d -Force | Out-Null }

Start-Transcript -Path (Join-Path $LogDir "deploy-$(Get-Date -Format 'yyyy-MM').log") -Append | Out-Null

# --- Un solo despliegue a la vez ---------------------------------------------
$lock = $null
try {
    $lock = [IO.File]::Open((Join-Path $Shared 'deploy.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
} catch {
    Write-Host "Otro despliegue esta en curso; salgo."
    Stop-Transcript | Out-Null
    exit 0
}

function Get-State {
    if (Test-Path $StatePath) { return Get-Content $StatePath -Raw | ConvertFrom-Json }
    return [pscustomobject]@{ current = $null; failed = @() }
}
function Save-State($s) { $s | ConvertTo-Json -Depth 4 | Set-Content $StatePath -Encoding utf8NoBOM }

function ConvertTo-Ver([string]$tag) {
    if ($tag -notmatch '^v(\d+\.\d+\.\d+)$') { throw "Tag con formato invalido: '$tag'" }
    return [version]$Matches[1]
}

# --- Acceso a GitHub (repo de releases, token read-only) ----------------------
$token = (Import-Clixml (Join-Path $Shared 'releases-token.xml')).GetNetworkCredential().Password
$ApiHeaders = @{ Authorization = "Bearer $token"; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'apptareas-pull-deploy' }

function Save-Asset($asset, [string]$dest) {
    # El asset de un repo privado se baja con Accept: octet-stream y redirige a
    # una URL prefirmada. Esa segunda peticion NO debe llevar el token.
    $h = $ApiHeaders.Clone(); $h.Accept = 'application/octet-stream'
    $r = Invoke-WebRequest -Uri $asset.url -Headers $h -MaximumRedirection 0 -SkipHttpErrorCheck
    if ($r.StatusCode -in 301, 302, 303, 307, 308) {
        Invoke-WebRequest -Uri ([string]$r.Headers.Location) -OutFile $dest -UseBasicParsing
    } elseif ($r.StatusCode -eq 200) {
        [IO.File]::WriteAllBytes($dest, $r.Content)
    } else { throw "Descarga de '$($asset.name)' fallo: HTTP $($r.StatusCode)" }
}

$tmp = $null
try {
    $state = Get-State

    $url = if ($Version) { "https://api.github.com/repos/$Repo/releases/tags/$Version" }
           else          { "https://api.github.com/repos/$Repo/releases/latest" }
    $rel = Invoke-RestMethod -Uri $url -Headers $ApiHeaders
    if ($rel.draft -or $rel.prerelease) { throw "El release $($rel.tag_name) es draft/prerelease; lo ignoro." }
    $tag = $rel.tag_name
    $newVer = ConvertTo-Ver $tag

    if ($tag -eq $state.current) { Write-Host "Ya esta desplegado $tag."; return }
    if (-not $Version -and $state.failed -contains $tag) { Write-Host "$tag fallo antes; no reintento solo (usa -Version $tag)."; return }
    if ($state.current -and -not $AllowDowngrade -and $newVer -le (ConvertTo-Ver $state.current)) {
        Write-Host "$tag no es mas nuevo que $($state.current); ignoro (anti-downgrade)."; return
    }

    Write-Host "==> Nueva version: $tag (actual: $($state.current ?? 'ninguna'))"
    $tmp = Join-Path ([IO.Path]::GetTempPath()) "apptareas-pull-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory $tmp | Out-Null

    # 1) Primero manifest + firma: no se baja nada mas si no verifican.
    foreach ($n in 'manifest.json', 'manifest.sig') {
        $a = $rel.assets | Where-Object name -eq $n
        if (-not $a) { throw "Falta el asset '$n' en $tag" }
        Save-Asset $a (Join-Path $tmp $n)
    }
    $ecdsa = [System.Security.Cryptography.ECDsa]::Create()
    $ecdsa.ImportFromPem((Get-Content (Join-Path $Shared 'signing-public.pem') -Raw))
    $sig = [Convert]::FromBase64String((Get-Content (Join-Path $tmp 'manifest.sig') -Raw).Trim())
    $ok = $ecdsa.VerifyData([IO.File]::ReadAllBytes((Join-Path $tmp 'manifest.json')), $sig,
                            [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    if (-not $ok) { throw "FIRMA INVALIDA en ${tag}: NO se despliega." }
    $manifest = Get-Content (Join-Path $tmp 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.version -ne $tag) { throw "El manifest dice '$($manifest.version)' pero el release es '$tag'." }
    Write-Host "Firma OK." -ForegroundColor Green

    # 2) Archivos, verificados contra los hashes del manifest firmado.
    foreach ($n in 'api.zip', 'frontend.zip', 'efbundle.exe') {
        $a = $rel.assets | Where-Object name -eq $n
        if (-not $a) { throw "Falta el asset '$n' en $tag" }
        Save-Asset $a (Join-Path $tmp $n)
        $h = (Get-FileHash (Join-Path $tmp $n) -Algorithm SHA256).Hash.ToLower()
        if ($h -ne $manifest.files.$n) { throw "Hash de $n no coincide con el manifest: NO se despliega." }
    }
    Write-Host "Hashes OK." -ForegroundColor Green

    if ($DryRun) { Write-Host "DryRun: verificacion completa, no se toca nada." -ForegroundColor Yellow; return }

    # 3) Preparar el release en su carpeta.
    $target = Join-Path $ReleasesDir $tag
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Expand-Archive (Join-Path $tmp 'api.zip') (Join-Path $target 'api')
    Expand-Archive (Join-Path $tmp 'frontend.zip') (Join-Path $target 'frontend')
    Copy-Item (Join-Path $Shared 'appsettings.Local.json') (Join-Path $target 'api') -Force
    New-Item -ItemType Junction -Path (Join-Path $target 'api\App_Data') -Target (Join-Path $Shared 'App_Data') | Out-Null

    # 4) Backup (hook opcional) y migraciones.
    $hook = Join-Path $Shared 'hooks\pre-migrate.ps1'
    if (Test-Path $hook) { Write-Host "==> Hook pre-migrate"; & $hook; if ($LASTEXITCODE) { throw "pre-migrate.ps1 fallo" } }
    $connFile = Join-Path $Shared 'migrations-connection.txt'
    $conn = if (Test-Path $connFile) { (Get-Content $connFile -Raw).Trim() }
            else { (Get-Content (Join-Path $Shared 'appsettings.Local.json') -Raw | ConvertFrom-Json).ConnectionStrings.Default }
    Write-Host "==> Aplicando migraciones"
    & (Join-Path $tmp 'efbundle.exe') --connection $conn
    if ($LASTEXITCODE -ne 0) { throw "efbundle fallo (exit $LASTEXITCODE); no se cambia la version activa." }

    # 5) Cambiar la version activa con el servicio detenido.
    $previousTarget = if (Test-Path $Current) { (Get-Item $Current).Target } else { $null }
    Write-Host "==> Deteniendo $ServiceName y cambiando 'current' a $tag"
    $svc = Get-Service $ServiceName
    if ($svc.Status -eq 'Running') { Stop-Service $ServiceName; $svc.WaitForStatus('Stopped', '00:00:30') }
    if (Test-Path $Current) { (Get-Item $Current).Delete() }
    New-Item -ItemType Junction -Path $Current -Target $target | Out-Null
    Start-Service $ServiceName

    # 6) Health check, con rollback si falla.
    $healthy = $false
    foreach ($i in 1..30) {
        Start-Sleep 2
        try { if ((Invoke-WebRequest $HealthUrl -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200) { $healthy = $true; break } } catch { }
    }
    if ($healthy) {
        $state.current = $tag; Save-State $state
        Write-Host "==> $tag desplegado y saludable." -ForegroundColor Green
    } else {
        Write-Host "Health check FALLO en $tag. Rollback." -ForegroundColor Red
        Stop-Service $ServiceName -ErrorAction SilentlyContinue
        (Get-Item $Current).Delete()
        if ($previousTarget) {
            New-Item -ItemType Junction -Path $Current -Target $previousTarget | Out-Null
            Start-Service $ServiceName
            Write-Host "Vuelta a $previousTarget. OJO: las migraciones NO se revierten solas; si $tag trajo cambios de esquema, revisa el backup." -ForegroundColor Yellow
        }
        $state.failed = @($state.failed) + $tag; Save-State $state
        throw "Despliegue de $tag fallo y se hizo rollback."
    }

    # 7) Limpieza: conservar solo los ultimos N releases.
    Get-ChildItem $ReleasesDir -Directory | Sort-Object { try { ConvertTo-Ver $_.Name } catch { [version]'0.0.0' } } -Descending |
        Select-Object -Skip $KeepReleases |
        Where-Object { $_.Name -ne $state.current } |
        ForEach-Object { Write-Host "Borrando release viejo $($_.Name)"; Remove-Item $_.FullName -Recurse -Force }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $script:failed = $true
}
finally {
    if ($tmp -and (Test-Path $tmp)) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    if ($lock) { $lock.Dispose() }
    Stop-Transcript | Out-Null
}
exit ([int][bool]$script:failed)
