# Despliegue (CI/CD por pull)

Flujo: se crea un tag `vX.Y.Z` en `master` → GitHub Actions compila, ofusca y
empaqueta y deja un release **en draft, sin firmar**, en el repo privado
`AppTareas-releases` → **tu** corres `scripts/sign-release.ps1` en tu maquina:
verifica los archivos, te muestra el commit, firma con tu llave privada y
publica → el servidor (tarea programada) detecta el release, verifica firma y
hashes, migra, cambia de version y hace rollback si `/health` falla.

La llave privada de firma vive solo en tu maquina de desarrollo, no en GitHub.
Por eso, aunque alguien tome tu cuenta de GitHub o robe el token de publicacion,
puede subir archivos pero no firmarlos, y el servidor los rechaza. Esto no
depende de rulesets ni environments (que en repos privados requieren plan de pago).

El servidor no tiene codigo fuente ni credenciales que lean el repo de codigo.

## 1. Una sola vez, en GitHub

**Repo `AppTareas-releases`** (privado): crearlo con un README inicial
(un repo sin commits no deja crear releases). Desactivar Actions en ese repo
(Settings → Actions → General) porque no lo necesita.

**Tokens** (Settings → Developer settings → Fine-grained tokens):

| Token | Repo | Permiso | Donde se guarda |
|---|---|---|---|
| `RELEASES_TOKEN` | solo `AppTareas-releases` | Contents: **Read and write** | Secreto del repo `AppTareas` (Settings → Secrets and variables → Actions) **y** tu maquina (para `sign-release.ps1`) |
| token del servidor | solo `AppTareas-releases` | Contents: **Read-only** | `shared\releases-token.xml` en el servidor |

Ninguno debe tener acceso al repo `AppTareas`. Caducidad: 1 ano (anotar la fecha).

**Llaves de firma** (en tu maquina de desarrollo, no en el servidor):

```powershell
pwsh .\scripts\new-signing-key.ps1 -OutDir $HOME\keys
```

- `signing-private.pem` → se queda en `$HOME\keys` (respaldala en un lugar seguro, por ejemplo cifrada en un USB). **No se sube a GitHub.**
- `signing-public.pem` → servidor, `shared\signing-public.pem`.

**Proteccion de `AppTareas`** (lo que si funciona en plan gratuito):
- 2FA con llave de seguridad o passkey en tu cuenta.
- Actions → General: permisos de workflow en "Read repository contents", permitir solo actions de GitHub, y exigir aprobacion para workflows de forks.
- Los rulesets pueden crearse pero no se aplican en repos privados sin plan de pago; no dependas de ellos.

## 1b. Cada release

```powershell
git tag v1.0.0 && git push origin v1.0.0     # dispara el workflow (desde master)
# esperar a que termine; el release queda en draft en AppTareas-releases
pwsh .\scripts\sign-release.ps1 -Tag v1.0.0  # verifica, muestra el commit, firma y publica
```

## 2. Una sola vez, en el servidor

Requisito: **PowerShell 7** (`pwsh`). Instalar el MSI oficial. `pull-deploy.ps1` no corre en Windows PowerShell 5.1.

Estructura:

```
C:\apps\apptareas\
  releases\            cada version descomprimida
  current              junction al release activo (lo gestiona el script)
  shared\
    appsettings.Local.json     copiado desde backend\publish actual
    App_Data\                  copiado desde backend\publish\App_Data (adjuntos)
    signing-public.pem
    releases-token.xml         ver abajo
    hooks\pre-migrate.ps1      backup de BD (recomendado)
  logs\
```

Token del servidor (ejecutar con la cuenta que correra la tarea; queda cifrado con DPAPI y solo esa cuenta en esa maquina lo puede leer):

```powershell
Get-Credential -UserName releases | Export-Clixml C:\apps\apptareas\shared\releases-token.xml   # la "contrasena" es el token
```

Permisos NTFS sobre `C:\apps\apptareas`: control total solo para `SYSTEM`, Administradores y la cuenta de despliegue; la cuenta del servicio `TaskManagerApi` solo lectura, mas escritura en `shared\App_Data`.

Cuentas: la cuenta del servicio hoy es `LocalSystem`. Conviene una cuenta dedicada sin privilegios. La tarea de despliegue necesita poder detener/iniciar el servicio (`sc sdset` o ser administrador).

Tarea programada (cada 5 min):

```powershell
$a = New-ScheduledTaskAction -Execute 'pwsh.exe' -Argument '-NoProfile -File C:\apps\apptareas\pull-deploy.ps1'
$t = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5)
Register-ScheduledTask -TaskName 'AppTareas pull-deploy' -Action $a -Trigger $t -User '<cuenta>' -Password '<...>' -RunLevel Highest
```

`pull-deploy.ps1` se copia al servidor **una vez a mano** (no se autoactualiza: asi un release comprometido no puede cambiar la logica de despliegue).

Prueba sin riesgo: `pwsh C:\apps\apptareas\pull-deploy.ps1 -DryRun` baja el ultimo release y verifica firma y hashes sin tocar nada.

## 3. Corte (cuando todo lo anterior este verificado)

1. Apuntar Apache: `DocumentRoot` y `<Directory>` a `C:/apps/apptareas/current/frontend`.
2. Apuntar el servicio: `sc config TaskManagerApi binPath= "\"C:\Program Files\dotnet\dotnet.exe\" \"C:\apps\apptareas\current\api\TaskManager.Api.dll\""`
3. Reiniciar Apache y el servicio; verificar la app.
4. Esperar unos dias con la carpeta vieja intacta como respaldo, luego borrar `C:\Apache24\htdocs\AppTareas` (y papelera, shadow copies y backups que la contengan).
5. Retirar `scripts\deploy.ps1` del repo (ya no aplica).

## Rollback y limites conocidos

- Si `/health` falla tras el cambio, el script vuelve solo al release anterior y marca la version como fallida (no reintenta sola).
- Rollback manual: `pwsh pull-deploy.ps1 -Version vX.Y.Z -AllowDowngrade`.
- **Las migraciones no se revierten solas.** Escribirlas compatibles hacia atras (agregar, no borrar/renombrar en el mismo release) y mantener el hook de backup.
- La ofuscacion es disuasoria, no impide que un administrador del servidor analice los binarios.
- Si se rota la llave de firma, actualizar `shared\signing-public.pem` en el servidor ANTES del siguiente release.
