# Keycloak propio (Docker) — AppTareas

Reemplaza la dependencia del Keycloak compartido (`appcafeteria.xoc.uam.mx`,
realm `cafeteria-uam`) por una instancia propia, alimentada por el LDAP
institucional (autenticación) y la BD institucional (correo/área) vía un
SPI custom. Ver plan completo en la conversación / `Estado-Proyecto.md`.

Este folder cubre **Fase 0 y 1** del plan: levantar Keycloak + su propia
Postgres (solo storage interno de Keycloak — no confundir con la BD
institucional, esa se conecta aparte en la Fase 3). Fases 2-6 (realm,
client, federación LDAP, SPI custom, cambios en el backend .NET, corte a
producción) van después.

## Requisitos

- Docker instalado (tú lo instalas — no incluido aquí).
- Puerto `8080` libre en `localhost`.

## Cómo levantarlo

```bash
cd docker/keycloak
cp .env.example .env
# Edita .env: pon contraseñas reales (no dejes "cambia-esto").

docker compose up -d
```

Primer arranque tarda ~30-60s (Postgres + build de Keycloak). Ver logs:

```bash
docker compose logs -f keycloak
```

Admin Console: http://localhost:8080 — entra con `KC_ADMIN_USER`/`KC_ADMIN_PASSWORD`
del `.env`.

## Apagar / limpiar

```bash
docker compose down          # apaga, conserva datos (volumen keycloak_db_data)
docker compose down -v       # apaga y BORRA los datos de Keycloak (reset total)
```

## Notas importantes

- **`start-dev`**: modo actual, pensado para configurar todo por la Admin
  Console y probar en local sin pelear con TLS/hostname estricto. Antes de
  usar esto en producción (Fase 6) hay que cambiar a `start` (modo
  producción real), fijar `KC_HOSTNAME` al dominio final, y servir TLS
  (terminado en Apache, como reverse proxy — mismo patrón ya planeado para
  Kestrel).
- **El puerto 8080 solo se publica en `127.0.0.1`** a propósito — nunca se
  expone directo a internet. En producción, Apache hace de reverse proxy
  hacia este puerto.
- **`KC_BOOTSTRAP_ADMIN_USERNAME`/`PASSWORD`** solo se usan la primera vez
  que arranca (crean al admin inicial contra una BD vacía). Cambiar el
  `.env` después de eso no hace nada — el admin se administra desde la UI.
  Si necesitas resetearlo, es más simple `docker compose down -v` y volver
  a arrancar.
- **`./import/`**: acá va el export del realm (Fase 2), se importa solo al
  arrancar (`--import-realm`). Vacío por ahora.
- **`./providers/`**: acá va el `.jar` del SPI custom que consulta la BD
  institucional (Fase 3). Vacío por ahora. Al agregar un jar nuevo,
  Keycloak necesita un rebuild interno:
  ```bash
  docker compose exec keycloak /opt/keycloak/bin/kc.sh build
  docker compose restart keycloak
  ```
- **`.env` nunca se commitea** (ver `.gitignore` de este folder) — solo
  `.env.example` con placeholders.

## Fase 2 — hecho

Realm `apptareas` + client `task-manager-uamx` (público, sin client
authentication, redirect URIs/web origins con `localhost:4200` y
`apptareas.xoc.uam.mx`) + Audience Mapper en el scope dedicado, ya
configurados desde la Admin Console.

**No hay LDAP institucional real** (se descubrió al revisar cómo el
backend PHP de `appcafeteria` valida usuarios): la autenticación es un
servicio **SOAP** (`cusxacdi.xoc.uam.mx`, funciones `ValidaAccesoCuenta`/
`RecuperaInfoCuenta`, matrícula + NIP de 5 dígitos), y correo/área viven en
una BD MySQL aparte (`info_usuarios_unidad`, host `148.206.99.178`). Fase 3
(SPI custom de Keycloak) se ajustó a esto: en vez de `LDAPStorageMapper`,
es un `UserStorageProvider` que llama a CUSXACDI por SOAP para autenticar
y a MySQL para completar el perfil.

### Re-exportar el realm (al cambiar algo desde la Admin Console)

Cada vez que se toque algo del realm/client/mappers/theme desde la UI, hay
que volver a exportarlo para que `import/apptareas-realm.json` quede al
día (si no, un `docker compose down -v` de otra persona levanta un
Keycloak vacío en vez de este):

```bash
# MSYS_NO_PATHCONV=1 evita que Git Bash reescriba las rutas /tmp/... a
# rutas de Windows antes de mandarlas al contenedor.
MSYS_NO_PATHCONV=1 docker compose exec -T keycloak \
  /opt/keycloak/bin/kc.sh export --dir /tmp/kc-export --realm apptareas --users skip

MSYS_NO_PATHCONV=1 docker compose cp \
  keycloak:/tmp/kc-export/apptareas-realm.json ./import/apptareas-realm.json
```

(El comando termina con un error de "Address already in use" al final —
es solo el intento del export de levantar su propio listener de
management mientras Keycloak ya está corriendo en el mismo contenedor; el
export en sí ya terminó antes de eso, ver el log "Export finished
successfully".)

**Paso obligatorio antes de commitear**: `kc.sh export` incluye por
default las **claves criptográficas reales del realm** (firma JWT,
encriptación) en `components."org.keycloak.keys.KeyProvider"` — con eso
se pueden falsificar tokens válidos. Hay que quitar ese bloque a mano:

```js
node -e "
const fs = require('fs');
const path = 'import/apptareas-realm.json';
const data = JSON.parse(fs.readFileSync(path, 'utf-8'));
delete (data.components || {})['org.keycloak.keys.KeyProvider'];
fs.writeFileSync(path, JSON.stringify(data, null, 2) + '\n', 'utf-8');
"
```

Un Keycloak nuevo que importe este archivo sin ese bloque genera sus
propias claves automáticamente — no hace falta reponerlas.

## Siguiente paso (Fase 3)

SPI custom (Java) para `task-manager-uamx`:
- `CredentialInputValidator` que autentica contra CUSXACDI (SOAP
  `ValidaAccesoCuenta`, matrícula + NIP), replicando `isAlumno()` /
  `soapAuthenticate()` del `AuthModel.php` de `appcafeteria` (mismo
  criterio: excluir alumnos, matrícula de 10 dígitos).
- Enriquecer el perfil con `RecuperaInfoCuenta` (nombre completo) y con un
  `SELECT` a `info_usuarios_unidad` (MySQL, `148.206.99.178`) para
  correo/área — pendiente credenciales de esa BD (usuario/password aparte
  de lo que ya está en el `.env` de este stack).
- Theme custom de login (relabelar "Username"/"Password" a "Matrícula o
  número económico"/"NIP") — se hace junto con el SPI, después de que el
  login funcional ya sirva con los campos genéricos.
