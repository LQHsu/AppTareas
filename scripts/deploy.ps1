<#
.SYNOPSIS
    Despliega AppTareas a produccion: migraciones EF -> publish del
    backend -> reinicio del servicio Windows -> (opcional) build del
    frontend.

.DESCRIPTION
    Este servidor sirve AppTareas directo desde este repo (ver
    docker/keycloak/README.md y C:\Apache24\conf\extra\httpd-vhosts.conf):
    - Frontend: Apache sirve frontend/task-manager-app/dist/task-manager-app/browser
      directo como DocumentRoot - "desplegarlo" es solo correr `ng build`.
    - Backend: corre como el servicio de Windows "TaskManagerApi" desde
      backend/publish/TaskManager.Api.dll - "desplegarlo" es publish +
      reiniciar el servicio.
    - Base de datos: las migraciones de EF Core no se aplican solas, hay
      que correr `dotnet ef database update` a mano.

    Este script encadena esos pasos en el orden seguro: primero publica
    en una carpeta TEMPORAL (para detectar un build roto ANTES de tocar
    el servicio en vivo), aplica las migraciones pendientes, recien ahi
    detiene el servicio, copia los archivos nuevos (sin borrar
    App_Data/attachments, que vive dentro de backend/publish pero no es
    parte del build) y lo vuelve a levantar.

    Pide confirmacion antes de tocar produccion, salvo que se pase -Force.

.PARAMETER SkipMigration
    No corre "dotnet ef database update". Usalo si ya la aplicaste a mano
    o si este despliegue no trae cambios de esquema.

.PARAMETER SkipFrontend
    No corre "ng build". Usalo si este despliegue es solo de backend.

.PARAMETER SkipBackend
    No publica ni reinicia el servicio. Usalo si este despliegue es solo
    de frontend (implica tambien saltarse la migracion).

.PARAMETER Force
    No pide confirmacion interactiva antes de empezar.

.EXAMPLE
    .\scripts\deploy.ps1
    Despliegue completo: migracion + backend + frontend, con confirmacion.

.EXAMPLE
    .\scripts\deploy.ps1 -SkipFrontend
    Solo backend (migracion + publish + restart), sin tocar el frontend.

.EXAMPLE
    .\scripts\deploy.ps1 -SkipBackend
    Solo frontend (ng build), no toca la BD ni el servicio.
#>
[CmdletBinding()]
param(
    [switch]$SkipMigration,
    [switch]$SkipFrontend,
    [switch]$SkipBackend,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$BackendDir = Join-Path $RepoRoot 'backend\TaskManager'
$ApiProject = Join-Path $BackendDir 'TaskManager.Api\TaskManager.Api.csproj'
$PublishDir = Join-Path $RepoRoot 'backend\publish'
$FrontendDir = Join-Path $RepoRoot 'frontend\task-manager-app'
$ServiceName = 'TaskManagerApi'

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Checked {
    # Corre un comando externo y revienta el script si el exit code no
    # es 0 - los cmdlets de PowerShell ya respetan $ErrorActionPreference
    # solos, pero dotnet.exe/otros binarios externos NO (solo devuelven
    # un exit code, no lanzan una excepcion de PowerShell), asi que sin
    # esto un "dotnet publish" roto dejaria seguir el script como si nada.
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList
    )
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath $($ArgumentList -join ' ')' fallo con exit code $LASTEXITCODE"
    }
}

Write-Host "AppTareas - script de despliegue" -ForegroundColor Yellow
Write-Host "Repo: $RepoRoot"
Write-Host "Pasos: $(if (-not $SkipBackend -and -not $SkipMigration) { 'migracion, ' })$(if (-not $SkipBackend) { 'backend, ' })$(if (-not $SkipFrontend) { 'frontend' })"

if (-not $Force) {
    $resp = Read-Host "Esto toca produccion (BD/servicio real). Continuar? (s/N)"
    if ($resp -notin @('s', 'S', 'si', 'Si', 'SI')) {
        Write-Host "Cancelado." -ForegroundColor Yellow
        exit 0
    }
}

$serviceWasRunning = $false

try {
    if (-not $SkipBackend) {
        if (-not $SkipMigration) {
            Write-Step "Aplicando migraciones pendientes de EF Core"
            Push-Location $BackendDir
            try {
                Invoke-Checked dotnet @('ef', 'database', 'update', '--project', 'TaskManager.Infrastructure', '--startup-project', 'TaskManager.Api')
            } finally {
                Pop-Location
            }
        } else {
            Write-Host "Migracion saltada (-SkipMigration)." -ForegroundColor DarkYellow
        }

        Write-Step "Publicando el backend en una carpeta temporal (para no tocar el servicio en vivo si el build falla)"
        $tempPublish = Join-Path $env:TEMP "apptareas-publish-$(Get-Date -Format 'yyyyMMddHHmmss')"
        Invoke-Checked dotnet @('publish', $ApiProject, '-c', 'Release', '-o', $tempPublish)

        Write-Step "Deteniendo el servicio $ServiceName"
        $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($null -eq $svc) {
            throw "No se encontro el servicio '$ServiceName'. Verifica el nombre con Get-Service."
        }
        $serviceWasRunning = ($svc.Status -eq 'Running')
        if ($serviceWasRunning) {
            Stop-Service -Name $ServiceName
            (Get-Service -Name $ServiceName).WaitForStatus('Stopped', '00:00:30')
        } else {
            Write-Host "El servicio ya estaba detenido." -ForegroundColor DarkYellow
        }

        Write-Step "Copiando el build nuevo a $PublishDir"
        # Copy-Item (no robocopy /MIR): sobreescribe lo que coincide pero
        # NO borra archivos que no vengan en el build nuevo - App_Data\
        # attachments vive dentro de esta misma carpeta y no es parte del
        # build, /MIR lo habria borrado entero.
        if (-not (Test-Path $PublishDir)) {
            New-Item -ItemType Directory -Path $PublishDir | Out-Null
        }
        Copy-Item -Path (Join-Path $tempPublish '*') -Destination $PublishDir -Recurse -Force
        Remove-Item -Path $tempPublish -Recurse -Force

        Write-Step "Iniciando el servicio $ServiceName"
        Start-Service -Name $ServiceName
        (Get-Service -Name $ServiceName).WaitForStatus('Running', '00:00:30')

        $dll = Get-Item (Join-Path $PublishDir 'TaskManager.Api.dll')
        Write-Host "Servicio '$ServiceName': $((Get-Service -Name $ServiceName).Status) - build del $($dll.LastWriteTime)" -ForegroundColor Green
    } else {
        Write-Host "Backend saltado (-SkipBackend): sin migracion, sin publish, sin tocar el servicio." -ForegroundColor DarkYellow
    }

    if (-not $SkipFrontend) {
        Write-Step "Compilando el frontend (produccion)"
        Push-Location $FrontendDir
        try {
            Invoke-Checked npx @('ng', 'build', '--configuration', 'production')
        } finally {
            Pop-Location
        }
        Write-Host "Frontend actualizado: dist/ ES el DocumentRoot de Apache, ya queda en vivo." -ForegroundColor Green
    } else {
        Write-Host "Frontend saltado (-SkipFrontend)." -ForegroundColor DarkYellow
    }

    Write-Host ""
    Write-Host "Despliegue terminado." -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "ERROR durante el despliegue: $($_.Exception.Message)" -ForegroundColor Red

    if (-not $SkipBackend -and $serviceWasRunning) {
        $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($null -ne $svc -and $svc.Status -ne 'Running') {
            Write-Host "Intentando volver a levantar el servicio con lo que haya quedado en $PublishDir..." -ForegroundColor Yellow
            try {
                Start-Service -Name $ServiceName
                Write-Host "Servicio levantado de nuevo (revisa si el build que quedo es el viejo o el nuevo a medias)." -ForegroundColor Yellow
            } catch {
                Write-Host "No se pudo volver a levantar el servicio solo. Revisalo a mano: Start-Service $ServiceName" -ForegroundColor Red
            }
        }
    }

    exit 1
}
