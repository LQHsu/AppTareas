<#
.SYNOPSIS
    Genera el par de llaves (ECDSA P-256) con el que el CI firma cada release
    y el servidor verifica antes de desplegar.

.DESCRIPTION
    Correlo UNA vez, en tu maquina de desarrollo (NO en el servidor):
    - signing-private.pem -> secreto SIGNING_KEY del Environment "production"
      de GitHub (con required reviewer). Despues de pegarlo, borra el archivo.
    - signing-public.pem  -> se copia al servidor (shared\signing-public.pem).
      Es publica, no es secreta, pero el servidor solo debe confiar en esta.

    Si la privada se filtra: generar un par nuevo, cambiar el secreto en GitHub
    y reemplazar la publica en el servidor.
#>
[CmdletBinding()]
param([string]$OutDir = (Get-Location).Path)

$ErrorActionPreference = 'Stop'
$ecdsa = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
$priv = Join-Path $OutDir 'signing-private.pem'
$pub = Join-Path $OutDir 'signing-public.pem'
if ((Test-Path $priv) -or (Test-Path $pub)) { throw "Ya existen llaves en $OutDir; no las sobrescribo." }
Set-Content -Path $priv -Value $ecdsa.ExportPkcs8PrivateKeyPem() -Encoding ascii
Set-Content -Path $pub -Value $ecdsa.ExportSubjectPublicKeyInfoPem() -Encoding ascii
Write-Host "Privada: $priv  (secreto SIGNING_KEY en GitHub, luego BORRALA)" -ForegroundColor Yellow
Write-Host "Publica: $pub   (va al servidor)" -ForegroundColor Green
