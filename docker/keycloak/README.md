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

## Fase 3.1 — hecho (autenticación via CUSXACDI)

`backend/keycloak-extensions/` (proyecto Maven aparte, no es parte de la
solución .NET) implementa un `UserStorageProvider` custom
(`cusxacdi-uamx`) que autentica contra el SOAP institucional CUSXACDI en
vez de una User Federation LDAP (ver más arriba por qué). Confirmado
funcionando con un login real de un trabajador (matrícula + NIP) el
2026-09-14.

### Build del jar (sin instalar Java/Maven en el servidor)

```bash
cd backend/keycloak-extensions
MSYS_NO_PATHCONV=1 docker run --rm \
  -v "$(pwd -W 2>/dev/null || pwd)":/workspace \
  -w /workspace \
  maven:3.9-eclipse-temurin-21 \
  mvn -B -q clean package

cp target/keycloak-cusxacdi-provider.jar ../../docker/keycloak/providers/
```

Luego, con el stack corriendo:

```bash
cd docker/keycloak
export MSYS_NO_PATHCONV=1
docker compose exec keycloak /opt/keycloak/bin/kc.sh build
docker compose restart keycloak
```

Repetir este ciclo completo cada vez que cambie el código Java.

**Versión de Keycloak vs. dependencias Maven**: la imagen `26.0` del
`docker-compose.yml` resuelve hoy a `26.0.8` internamente — confirmado
inspeccionando los jars reales del contenedor (`docker compose cp` +
`javap`), no asumido. El `pom.xml` fija `keycloak.version` a esa misma
versión exacta a propósito: un desalineamiento causa `NoSuchMethodError`
o peor, un `StackOverflowError` silencioso como el de abajo. Si se
actualiza la imagen de Keycloak, actualizar `keycloak.version` igual.

**Gotcha real ya resuelto (dejar documentado, es fácil de repetir)**: en
Keycloak 26.x, `AbstractUserAdapterFederatedStorage` NO tiene
`setUsername`/`setFirstName`/`setLastName` propios (username/nombre se
manejan como atributos genéricos vía `setSingleAttribute`). Si se
sobreescribe `setUsername()` y ese override llama a
`setSingleAttribute(UserModel.USERNAME, ...)`, es una **recursión
infinita** con la clase base (que internamente llama a `setUsername()`
al recibir esa key) — se manifiesta como `StackOverflowError` en el login
("internal server error" genérico en la UI, sin pista del problema real
salvo mirando los logs). Ver el comentario en `CusxacdiUserAdapter.java`.

**Nota de UX (no bug)**: justo después del primer login, Keycloak puede
mostrar "Update Account Information" pidiendo completar el perfil — es el
Required Action `Update Profile` disparándose porque el usuario llega sin
email (Fase 3.2 todavía no lo completa). Se desactivó temporalmente en
Authentication → Required Actions para no estorbar mientras se prueba;
debería dejar de aparecer solo una vez que el email real se puebla desde
`info_usuarios_unidad`. Reevaluar si reactivarlo antes de producción.

## Siguiente paso (Fase 3.2 — correo y área desde MySQL)

`CusxacdiUserAdapter` ya tiene el punto marcado con `TODO Fase 3.2`: un
`SELECT` a `info_usuarios_unidad` (MySQL, `148.206.99.178`) por matrícula,
para poblar `UserModel.EMAIL` y un atributo `area` (vía
`setSingleAttribute`, mismo patrón que nombres/apellidos). Pendiente
credenciales de esa BD (usuario/password aparte de lo que ya está en el
`.env` de este stack — la que se compartió en el chat ya se marcó para
rotar).

Después de eso: Protocol Mapper (config, no código) para exponer el
atributo `area` como claim custom en el token de `task-manager-uamx`, y
el theme custom de login (relabelar "Username"/"Password" a "Matrícula o
número económico"/"NIP").
