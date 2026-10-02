#Requires -Version 7.0
<#
.SYNOPSIS
    Firma y publica un release que el CI dejo en DRAFT. Se corre en TU maquina de
    desarrollo; la llave privada nunca sale de aqui ni esta en GitHub.

.DESCRIPTION
    1. Busca el release draft del tag en el repo de releases.
    2. Baja manifest.json y los archivos, y verifica que cada hash coincida con el
       manifest (para firmar exactamente lo que el servidor va a recibir).
    3. Te muestra version/commit/fecha y pide confirmacion.
    4. Firma manifest.json (ECDSA P-256, SHA-256), sube manifest.sig y pasa el
       release de draft a publicado/latest. Desde ese momento el servidor lo
       toma en su siguiente consulta.

    Antes de confirmar, comprueba que el commit mostrado es el que tu reconoces
    (git log) - esa verificacion humana es la aprobacion del despliegue.

.PARAMETER Tag
    Tag a firmar, p. ej. v1.2.0.

.PARAMETER KeyPath
    signing-private.pem (ver scripts/new-signing-key.ps1).

.PARAMETER Token
    Token fine-grained con Contents: read/write sobre el repo de releases. Si no
    se pasa, se usa $env:RELEASES_TOKEN o se pide por consola (no se guarda).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Tag,
    [string]$Repo = 'LQHsu/AppTareas-releases',
    [string]$KeyPath = (Join-Path $HOME 'keys\signing-private.pem'),
    [securestring]$Token
)

$ErrorActionPreference = 'Stop'

if (-not $Token) {
    $Token = if ($env:RELEASES_TOKEN) { ConvertTo-SecureString $env:RELEASES_TOKEN -AsPlainText -Force }
             else { Read-Host 'Token de publicacion (Contents: read/write)' -AsSecureString }
}
$plain = [System.Net.NetworkCredential]::new('', $Token).Password
$Headers = @{ Authorization = "Bearer $plain"; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'apptareas-sign-release' }

function Save-Asset($asset, [string]$dest) {
    # Misma mecanica que pull-deploy.ps1: la URL prefirmada del redirect NO debe llevar el token.
    $h = $Headers.Clone(); $h.Accept = 'application/octet-stream'
    $r = Invoke-WebRequest -Uri $asset.url -Headers $h -MaximumRedirection 0 -SkipHttpErrorCheck
    if ($r.StatusCode -in 301, 302, 303, 307, 308) {
        Invoke-WebRequest -Uri ([string]$r.Headers.Location) -OutFile $dest -UseBasicParsing
    } elseif ($r.StatusCode -eq 200) {
        [IO.File]::WriteAllBytes($dest, $r.Content)
    } else { throw "Descarga de '$($asset.name)' fallo: HTTP $($r.StatusCode)" }
}

if (-not (Test-Path $KeyPath)) { throw "No encuentro la llave privada en $KeyPath" }

# Los drafts no se pueden pedir por /releases/tags/{tag}: se busca en el listado.
$rel = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases?per_page=50" -Headers $Headers |
    Where-Object { $_.tag_name -eq $Tag } | Select-Object -First 1
if (-not $rel) { throw "No hay release con tag $Tag en $Repo (¿termino el workflow?)." }
if (-not $rel.draft) { throw "El release $Tag ya esta publicado; no se firma dos veces." }
if ($rel.assets.name -contains 'manifest.sig') { throw "El draft $Tag ya tiene manifest.sig." }

$tmp = Join-Path ([IO.Path]::GetTempPath()) "apptareas-sign-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $tmp | Out-Null
try {
    foreach ($n in 'manifest.json', 'api.zip', 'frontend.zip', 'efbundle.exe') {
        $a = $rel.assets | Where-Object name -eq $n
        if (-not $a) { throw "Falta '$n' en el draft $Tag" }
        Write-Host "Bajando $n ..."
        Save-Asset $a (Join-Path $tmp $n)
    }
    $manifest = Get-Content (Join-Path $tmp 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.version -ne $Tag) { throw "El manifest dice '$($manifest.version)', se esperaba '$Tag'." }
    foreach ($n in 'api.zip', 'frontend.zip', 'efbundle.exe') {
        $h = (Get-FileHash (Join-Path $tmp $n) -Algorithm SHA256).Hash.ToLower()
        if ($h -ne $manifest.files.$n) { throw "El hash de $n NO coincide con el manifest. NO firmo." }
    }
    Write-Host "`nHashes de los archivos coinciden con el manifest." -ForegroundColor Green
    Write-Host "  version : $($manifest.version)"
    Write-Host "  commit  : $($manifest.commit)"
    Write-Host "  creado  : $($manifest.createdUtc)"
    $resp = Read-Host "`nEse commit es el que quieres desplegar a PRODUCCION? Firmar y publicar (s/N)"
    if ($resp -notin 's', 'S', 'si', 'Si', 'SI') { Write-Host 'Cancelado, el draft queda sin firmar.'; return }

    $ecdsa = [System.Security.Cryptography.ECDsa]::Create()
    $ecdsa.ImportFromPem((Get-Content $KeyPath -Raw))
    $sig = $ecdsa.SignData([IO.File]::ReadAllBytes((Join-Path $tmp 'manifest.json')),
                           [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    $sigPath = Join-Path $tmp 'manifest.sig'
    Set-Content $sigPath ([Convert]::ToBase64String($sig)) -Encoding ascii -NoNewline

    Write-Host 'Subiendo manifest.sig ...'
    $uploadUrl = ($rel.upload_url -replace '\{\?.*$', '') + '?name=manifest.sig'
    $h = $Headers.Clone(); $h['Content-Type'] = 'text/plain'
    Invoke-RestMethod -Method Post -Uri $uploadUrl -Headers $h -InFile $sigPath | Out-Null

    Write-Host 'Publicando release ...'
    $body = @{ draft = $false; make_latest = 'true' } | ConvertTo-Json
    Invoke-RestMethod -Method Patch -Uri $rel.url -Headers $Headers -Body $body -ContentType 'application/json' | Out-Null
    Write-Host "Release $Tag firmado y publicado. El servidor lo tomara en su proxima consulta." -ForegroundColor Green
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
