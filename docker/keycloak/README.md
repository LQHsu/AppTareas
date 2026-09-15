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

**Paso obligatorio antes de commitear** — `kc.sh export` mete dos tipos de
secretos reales en el JSON, hay que quitar ambos:

1. Las **claves criptográficas del realm** (firma JWT, encriptación) en
   `components."org.keycloak.keys.KeyProvider"` — con eso se pueden
   falsificar tokens válidos.
2. Las **credenciales de MySQL del provider `cusxacdi-uamx`**
   (`mysqlJdbcUrl`/`mysqlUser`/`mysqlPassword`) — pese a que el campo
   `mysqlPassword` está declarado como `PASSWORD` type en la Admin
   Console, el export las saca en texto plano igual (confirmado
   directamente en el JSON, no asumido) — Keycloak no las vault-ea salvo
   que se registre explícitamente un vault provider, que este stack no
   tiene.

```js
node -e "
const fs = require('fs');
const path = 'import/apptareas-realm.json';
const data = JSON.parse(fs.readFileSync(path, 'utf-8'));

delete (data.components || {})['org.keycloak.keys.KeyProvider'];

function stripSecrets(obj) {
  if (Array.isArray(obj)) { obj.forEach(stripSecrets); return; }
  if (obj && typeof obj === 'object') {
    for (const key of Object.keys(obj)) {
      if (key === 'mysqlPassword' || key === 'mysqlUser' || key === 'mysqlJdbcUrl') {
        delete obj[key];
        continue;
      }
      stripSecrets(obj[key]);
    }
  }
}
stripSecrets(data);

fs.writeFileSync(path, JSON.stringify(data, null, 2) + '\n', 'utf-8');
"
```

Un Keycloak nuevo que importe este archivo sin las claves genera las
suyas propias automáticamente. Las credenciales de MySQL sí hay que
volver a ponerlas a mano desde la Admin Console tras un import limpio
(User federation → cusxacdi-uamx) — no hay forma de que se auto-generen,
son datos reales de otra parte.

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

## Fase 3.2 — hecho (correo y área desde MySQL)

`InfoUsuariosUnidadClient` consulta `info_usuarios_unidad` (MySQL,
`148.206.99.178`) por número económico/matrícula. Esquema real (sin FKs
declaradas — MyISAM ni las soporta):

- **Correo**: tabla `CE_Correo_Empleados` (`CE_NumEconomico` → `CE_Email`).
  No está en `Empleados`/`Adscripciones`, hay que tener el `GRANT` sobre
  esa tabla específica además de las otras dos.
- **Área**: `Empleados.Pagaduria` == `Adscripciones.ClaveAdscripcion` (no
  es una FK declarada, pero el valor coincide 1:1 — confirmado con datos
  reales). Se usa `NombreAdscripcion2` (nivel intermedio de la jerarquía
  de 3 niveles: Secretaría → *esta* → Oficina puntual).

Config del provider (Admin Console → User federation → cusxacdi-uamx →
campos `MySQL JDBC URL`/`Usuario`/`Password`, ej.
`jdbc:mysql://148.206.99.178:3306/info_usuarios_unidad`) — nunca
hardcodeado en el jar. Si no está configurado o la consulta falla, el
login sigue funcionando igual (solo CUSXACDI es crítico) y
completar-registro cae al comportamiento de "el usuario llena a mano".

**Protocol Mapper**: `Clients → task-manager-uamx → Client scopes →
task-manager-uamx-dedicated → Mappers`, tipo **User Attribute**, User
Attribute=`area`, Token Claim Name=`area`, agregado a ID/access
token/userinfo. Expone el atributo custom como claim `area` en el JWT.

**Gotcha real (doble mojibake, dos causas distintas)**: nombres con
acentos salían corruptos (`QUIÑONES` → `QUIÃONES`) desde dos fuentes
separadas, cada una necesitó su propio fix:

- **CUSXACDI (SOAP)**: el servidor *miente* en su propio
  `Content-Type: charset=ISO-8859-1` (y el WSDL también lo declara así) —
  confirmado inspeccionando los *bytes crudos* de la respuesta con
  `curl`: `Ñ` llega como `C3 91`, que es UTF-8, no ISO-8859-1. Fix:
  `CusxacdiClient` fuerza `HttpResponse.BodyHandlers.ofString(UTF_8)`
  explícito, ignorando lo que el servidor declare.
- **MySQL**: el servidor tiene `character_set_client`/`connection`/
  `results` en `latin1` por default (`character_set_database` sí es
  `utf8`) — los datos están en UTF-8 dentro de la BD pero MySQL los sirve
  mal-decodificados si la sesión no pide `utf8` explícitamente. Fix:
  `InfoUsuariosUnidadClient` agrega `useUnicode=true&characterEncoding=UTF-8`
  a la URL JDBC.

Si aparece mojibake nuevo en cualquier dato institucional, **revisar
bytes crudos con curl/hexdump antes de asumir la causa** — ninguna de las
dos veces el header/WSDL declarado coincidía con la realidad.

**Sincronización de `Area` (backend .NET)**: el claim `area` es texto
libre (nombre institucional), no un `Area.Id` local. `POST
/api/areas/resolve` (`AreasController`) busca por nombre exacto y crea la
fila si no existe — sincronización *perezosa*: no hay job ni catálogo
pre-poblado, cada área nueva se crea sola la primera vez que alguien de
ahí se registra. Requirió agregar índice único en `Area.Nombre`
(migración `AgregarIndiceUnicoAreaNombre`) para poder detectar una
carrera de creación concurrente. El frontend (`completar-registro`)
llama esto al cargar, con el nombre que trae `AuthService.getArea()`, y
preselecciona el área resultante — si el token no trae `area`, el
usuario elige a mano como antes.

## Fase 6 — hecho (corte a producción, `apptareas.xoc.uam.mx`)

- `docker-compose.yml`: `command: start` (no `start-dev`),
  `KC_HTTP_RELATIVE_PATH=/auth-server` (mismo path que usaba el Keycloak
  compartido), `KC_PROXY_HEADERS=xforwarded` (Apache ya termina TLS).
- `.env`: `KC_HOSTNAME=apptareas.xoc.uam.mx` — **rompe el login local**
  (`localhost:4200`) mientras esté así; regresarlo a `localhost` +
  reiniciar el contenedor para volver a developer en local.
- Apache (`C:/Apache24/conf/extra/httpd-vhosts.conf`, vhost `:443` de
  `apptareas.xoc.uam.mx`): `ProxyPass /auth-server` hacia
  `localhost:8080/auth-server`, más `RequestHeader set
  X-Forwarded-Proto "https"` explícito — `mod_proxy` manda
  `X-Forwarded-For/-Host/-Server` solo, pero NO `-Proto`, y sin eso
  Keycloak (con `KC_PROXY_HEADERS=xforwarded`) ve la conexión interna
  como HTTP plano y rechaza.
- **Admin Console en producción**: `https://apptareas.xoc.uam.mx/auth-server/admin/master/console/`
  (sin puerto — `8080` solo existe en `localhost` del servidor, nunca
  expuesto público).
- **Gotcha real**: un `UPDATE` directo en la BD de Keycloak (Postgres) no
  se refleja hasta reiniciar el contenedor — Keycloak cachea el modelo
  del realm en memoria (Infinispan). Pasó al corregir a mano un
  `redirect_uri`/`web_origin` que quedaron en `http://` en vez de
  `https://`: el `UPDATE` SQL fue correcto pero el login siguió fallando
  con `400 Invalid parameter: redirect_uri` hasta el `docker compose
  restart keycloak`.
- **Bloqueador externo activo**: el certificado TLS wildcard
  (`*.xoc.uam.mx`, `C:/Apache24/LlavesCertificados2/`) está **vencido**
  (venció 2026-05-19). El navegador puede tolerarlo, pero el
  `HttpClient` interno de `JwtBearer` en el backend .NET lo rechaza al
  descargar la metadata OIDC de Keycloak (`SEC_E_CERT_EXPIRED` /
  `curl` sin `-k` da error 35) — todo endpoint `[Authorize]` responde
  `401` aunque el token sea válido y el login en sí funcione. No se
  puede resolver desde este proyecto; pendiente que renueven el
  certificado institucional.
