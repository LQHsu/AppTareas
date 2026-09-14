# Gestor de Tareas UAMX — Estado del proyecto

> Sistema tipo Jira simplificado (sin Scrum) para UAM Xochimilco. Multiequipo,
> multiárea, con autenticación institucional vía Keycloak/LDAP.

## Stack

| Capa | Tecnología |
|---|---|
| Backend | ASP.NET Core Web API (.NET 10), Controllers clásicos (no Minimal API) |
| ORM | Entity Framework Core |
| Base de datos | SQL Server 2022 Express |
| Auth | Keycloak (realm `cafeteria-uam`, compartido con `appcafeteria`) vía OIDC/JWT |
| Tiempo real | SignalR (`/hubs/tasks`), cliente `@microsoft/signalr` |
| Frontend | Angular (standalone components, **Zoneless**, signals) |
| UI | Angular Material + CDK |
| Servidor | Windows + Apache (reverse proxy) + Kestrel |

## Rutas de proyecto

- Backend: `C:\Apache24\htdocs\AppTareas\backend\TaskManager\`
- Frontend: `C:\Users\Ludwi\OneDrive\Proyects\AppTareas\frontend\task-manager-app\`

## Arquitectura backend (Clean Architecture, 3 proyectos)

```
TaskManager.Domain          ← entidades puras, sin dependencias externas
TaskManager.Infrastructure  ← AppDbContext (EF Core), depende de Domain
TaskManager.Api             ← Controllers, DTOs, Program.cs, depende de ambos
```

## Modelo de datos (implementado y migrado)

Entidades: `Area`, `User`, `Project`, `ProjectMember`, `TaskItem` (con
subtareas vía `ParentTaskId` self-referencing y soporte para tareas sin
proyecto vía `ProjectId` nullable), `TaskComment`, `TaskAttachment`,
`TaskStatusHistory`.

Estados de tarea (`TaskItemStatus`): Creada, Asignada, Leida, EnAtencion,
Atendida, VolverARevisar, Terminada, Cancelada.

Reglas de negocio clave:
- `Area` es tabla local, pensada para sincronizarse desde la BD
  institucional — **la sincronización automática NO está construida**,
  hay datos de prueba insertados manualmente.
- Un proyecto hereda el `AreaId` de quien lo crea (no se pide en el form).
- Solo se puede invitar a un proyecto a usuarios de la misma área.
- `IsSuperAdmin` nunca se autoasigna desde el cliente; se define en `false`
  en el registro. Se cambia desde el panel de super admin (`/admin`) — el
  primer super admin de un ambiente nuevo sigue sembrándose a mano en SQL.
- % de completado de un proyecto = `Terminadas / (Total - Canceladas)`,
  calculado al vuelo, no almacenado.
- **Flujo de estados automatizado + permisos por rol**
  (`TasksController.UpdateStatus`/`UpdateAssignee`/`MarkRead`):
  - `Creada → Asignada`: automático al asignar la tarea a alguien
    (`Create` con `AssignedToId`, o `UpdateAssignee`). Reasignar a otra
    persona (no solo la primera vez) también reinicia a `Asignada` — el
    nuevo asignado no ha leído nada todavía. Quitar la asignación
    devuelve la tarea a `Creada`. Ninguno de los dos casos aplica si la
    tarea ya está `Terminada`/`Cancelada` (no la revive a medio camino).
  - `Asignada → Leida`: automático cuando **la persona asignada** abre
    el modal de detalle (`PATCH /api/tasks/{id}/read`, llamado desde
    `openTaskDetail` en `mis-tareas`/`proyecto-detalle`). Idempotente —
    si ya pasó de `Asignada` no hace nada. No es un `PATCH /status`
    normal a propósito: es una transición pasiva, no algo que el usuario
    elige, así que no pasa por las reglas de permiso de abajo.
  - Permisos para mover el estado a mano (`PATCH /status`):
    - `Creada`/`Asignada`/`Leida`: solo quien **creó** la tarea (o el
      **dueño del proyecto**, que cuenta como creador para este efecto
      aunque no haya creado esa tarea puntual). Existen para poder
      corregir un error, no para uso normal.
    - `EnAtencion`/`Atendida`: quien tiene la tarea **asignada**, o quien
      la creó.
    - `VolverARevisar`/`Terminada`/`Cancelada`: solo quien creó la tarea
      (o el dueño del proyecto) — evaluar y cerrar el trabajo es su
      decisión.
    - Un miembro del proyecto que no es ni creador ni asignado de una
      tarea puntual puede **verla** pero no puede cambiar su estado en
      absoluto (antes cualquier miembro podía).
  - El frontend replica esta tabla de permisos para no ofrecer opciones
    que el backend va a rechazar (`TareaCardComponent.puedeCambiarA` +
    `@Input() isProjectOwner`; `TareasTablaComponent.opcionesEstadoPermitidas`
    para el dropdown en línea de la tabla — esa vista no conoce si el
    usuario es dueño del proyecto, así que ahí el dueño-no-creador ve la
    celda sin opciones, aunque sí podría editarla desde el modal). Es
    solo UX: la validación real es el backend, como en el resto del
    proyecto.

## Lo que ya funciona de punta a punta

- [x] Infraestructura: .NET 10 SDK + Hosting Bundle, SQL Server, Apache,
      Node, Git instalados en el servidor.
- [x] Migraciones de EF Core aplicadas (`InicialCatalogos`).
- [x] Login SSO completo: Angular → Keycloak → regreso autenticado.
- [x] Auto-registro: `GET /api/users/me` (404 si no existe) →
      pantalla `/completar-registro` → `POST /api/users` → redirección
      a `/proyectos`.
- [x] CRUD básico de proyectos: crear, listar los míos (dueño o miembro).
- [x] CRUD básico de tareas dentro de un proyecto: crear, cambiar estado
      (con bitácora automática en `TaskStatusHistory` y fechas
      `FechaAtencion`/`FechaTerminacion` actualizadas según el estado).
- [x] Miembros de proyecto: listar, invitar (solo dueño, solo misma área),
      quitar (solo dueño, no se puede auto-quitar el dueño).
- [x] Autorización: cada endpoint valida membresía/propiedad del proyecto
      en el backend (no solo en el frontend).

## Pendiente / próximos objetivos

### Alta prioridad
- [x] **Asignar tarea a un miembro específico** — selector de miembro
      conectado en el formulario de crear tarea de
      `proyecto-detalle.component`.
- [x] **Tareas sueltas (sin proyecto)** — formulario "Nueva tarea" en
      `mis-tareas.component`, candidatos vía `UserService.getByArea()`
      del área del usuario actual. Mismo patrón de creación múltiple que
      `proyecto-detalle` (ver nota de abajo).
- [x] **Pantalla "Mis tareas"** — `features/mis-tareas/`, consume
      `GET /api/tasks/mine`. El campo "Asignada a" se muestra de solo
      lectura ahí (no hay un único listado de miembros con quien
      reasignar cuando las tareas vienen de varios proyectos).
      `GetMine` trae tareas **asignadas al usuario O creadas por él**
      (`AssignedToId == userId || CreatedById == userId`) — antes solo
      traía las asignadas, y las que uno creaba para otra persona no
      aparecían en ningún lado propio. La columna "Rol" de
      `TareasTablaComponent` distingue cuál es cuál (y marca las que son
      ambas a la vez).

> **Nota de diseño — adjuntos y comentarios en `tarea-card`**: a
> diferencia de estado/asignado/subtareas (que se resuelven con
> `@Output()` porque dependen de contexto que solo el componente padre
> conoce — `projectId`, lista de miembros), adjuntos y comentarios se
> resolvieron inyectando `TaskAttachmentService`/`TaskCommentService`
> directamente en `TareaCardComponent`, cada uno con su propio estado
> (carga perezosa al expandir su sección). Así funcionan igual en
> `proyecto-detalle` y `mis-tareas` sin que ninguno de los dos tenga que
> cablear nada. La descarga de adjuntos se pide como `blob` vía
> `HttpClient` (no un `<a href>` directo): una navegación normal del
> navegador no manda el header `Authorization`, y el endpoint de
> descarga exige `[Authorize]`.

> **Nota de diseño — asignación a varias personas**: no existe una
> tarea con múltiples asignados en el modelo (`TaskItem.AssignedToId`
> sigue siendo un solo `Guid?`). Cuando el formulario de crear tarea
> (`proyecto-detalle` y `mis-tareas`) permite elegir varias personas, el
> frontend dispara un `POST /api/tasks` independiente por cada una
> (mismo título/descripción, `AssignedToId` distinto) — son N tareas
> separadas, no una compartida. Si falla la creación de alguna, las que
> sí se crearon se muestran igual y el formulario no se limpia, para
> poder reintentar solo las que fallaron.

### Media prioridad
- [x] Comentarios en tareas — `TaskCommentsController`
      (`api/tasks/{taskId}/comments`), mismo criterio de autorización que
      adjuntos (ver/publicar: miembro/dueño del proyecto o
      creador/asignado si es suelta; borrar: autor del comentario o
      quien administra la tarea). Límite 4000 caracteres, sin edición
      (solo crear/borrar). UI integrada directamente en `tarea-card`,
      mismo patrón que adjuntos (inyecta `TaskCommentService`, carga
      perezosa al expandir la sección — ver nota de diseño de adjuntos
      más abajo, ahora también aplica a comentarios).
- [x] Archivos adjuntos en tareas — `TaskAttachmentsController`
      (`api/tasks/{taskId}/attachments`), almacenamiento en **disco
      local** (el stack es on-prem, no hay blob storage en ningún otro
      lado del proyecto). Límite 25 MB, extensiones permitidas: pdf,
      doc/docx, xls/xlsx, ppt/pptx, png, jpg/jpeg, txt, zip. Ruta
      configurable en `Storage:AttachmentsPath` (vacío = usa
      `<ContentRoot>/App_Data/attachments`; en producción apuntar fuera
      de la carpeta de deploy para que un redeploy no borre lo subido).
      UI integrada directamente en `tarea-card` (ver nota abajo).
      El nombre que se muestra/descarga (`FileName`) se renombra al
      subir: `"{Título de la tarea} - {nombre original}.ext"` (ambas
      partes limpiadas de caracteres inválidos para nombre de archivo
      con `SanitizeForFileName`, título truncado a 60 caracteres) — así
      se puede ubicar de qué tarea viene un archivo ya descargado, sin
      perder el nombre original. Solo afecta el nombre mostrado; el
      nombre en disco (`StoragePath`) sigue siendo `{Id}.ext`, sin tocar.
- [x] Subtareas — un solo nivel de anidamiento (una subtarea no puede
      tener sus propias subtareas, validado en `TasksController.Create`).
      La subtarea hereda `ProjectId`/`AreaId` del padre; el asignado es
      independiente. `GetByProject` agrupa en memoria (sin CTE — no hace
      falta con un solo nivel) y cuelga cada subtarea de su padre en
      `TaskItemDto.Subtasks`; `GetMine` sigue plano (una subtarea
      asignada a mi aparece aunque el padre no sea mío). UI en
      `tarea-card` (sección de subtareas + botón "Agregar subtarea",
      solo visible cuando se pasa `members`).
- [x] **Refactor de la lista de tareas a tabla + modal** — las tarjetas
      apiladas (`tarea-card`) se habían vuelto ilegibles con subtareas +
      adjuntos + fechas + selects. Ahora:
      - `TareasTablaComponent` (`shared/components/tareas-tabla/`)
        reemplaza la lista en `proyecto-detalle` y `mis-tareas`: una fila
        por tarea top-level (las subtareas no salen como fila propia),
        columnas Tarea / Proyecto (solo si `showProject`) / Rol / Asignada
        a / Creada por / Estado / Creada / Último cambio de estado. Clic
        en una celda no editable emite `rowClick`.
      - Migrada de `mat-table` a **Tabulator** (JS puro, `tabulator-tables`
        + `@types/tabulator-tables`) para tener búsqueda/filtro por
        columna y edición de estado en línea, que `mat-table` no trae de
        fábrica. Se monta a mano en `ngAfterViewInit` sobre un
        `ElementRef` y se destruye en `ngOnDestroy`; los `@Input()` se
        empujan con `setData()` en `ngOnChanges` (no hay integración con
        el ciclo de detección de cambios de Angular). Tema `tabulator_simple`
        importado global en `angular.json` (antes de `styles.scss`),
        repintado con la paleta del proyecto vía `::ng-deep` en
        `tareas-tabla.component.scss` (Tabulator arma su DOM fuera de la
        vista encapsulada del componente).
      - Columna "Estado" es editable in-line (`editor: 'list'`, dropdown
        nativo de Tabulator): `cellEdited` emite `statusChange` y el
        padre (`proyecto-detalle`/`mis-tareas`) hace el PATCH y actualiza
        su signal, igual que ya hacía desde el modal. Sin rollback
        optimista: si el backend rechaza la transición, la fila queda con
        el valor viejo hasta el próximo refresh.
      - Columna "Rol" (`Asignada a mí` / `Creada por mí` / ambas) se
        calcula en el propio componente contra
        `userService.currentUser()` (signal ya poblado por `app-shell`),
        sin que cada pantalla tenga que pasarlo por `@Input`.
      - Estilo pulido tomado de otra implementación de Tabulator ya
        probada (dashboard de pedidos), adaptado a la paleta de
        AppTareas en vez de traer colores nuevos: header con fondo
        `--app-blue-soft` y borde inferior de acento, títulos de columna
        en mayúsculas pequeñas con letter-spacing, zebra striping con
        `--app-surface-subtle`, filas de 46px mínimo, y la celda "Tarea"
        con look de enlace (color + subrayado en hover) para dejar claro
        que abre el modal.
      - Columna "Estado" ahora es un **pill de color** (`badgeEstado()`)
        en vez de texto plano: neutro (sin empezar) → azul (en curso) →
        rojo/verde (requiere atención / terminada). Se reutilizan los
        mismos tokens de paleta que el resto de la app, sin inventar un
        color ámbar nuevo para "volver a revisar" (usa rojo suave, ya
        que semánticamente es "necesita atención").
      - Paginación local (`pagination: true`, 25/50/10 por página,
        orden inicial por `createdAt` descendente) con textos y botones
        (Primera/Anterior/Siguiente/Última) traducidos vía `locale`/`langs`
        de Tabulator — antes la tabla no paginaba y crecía sin límite.
      - **Buscador único en vez de un input por columna**: Tarea/Proyecto/
        Asignada a/Creada por antes tenían cada uno su propio
        `headerFilter: 'input'`; ahora hay un solo campo de texto arriba
        de la tabla (`onBuscar`/`coincideBusqueda` en el `.ts`) que
        busca por los 4 campos a la vez. Los dropdowns de Rol/Estado
        **sí** se quedan como `headerFilter: 'list'` en su columna — son
        de opciones cerradas, ahí sigue teniendo sentido filtrar por
        columna. Tabulator combina el filtro de texto (`setFilter`) y
        los header filters con AND automáticamente, no hace falta
        coordinarlos a mano; `onBuscar` solo actualiza el término y
        llama `refreshFilter()` (no vuelve a llamar `setFilter` en cada
        tecla, para no reemplazar el filtro).
      - Encabezado ahora es **azul sólido** (`--app-blue`, texto blanco)
        en vez de fondo suave + borde de acento — es lo que "resalta" el
        header. Se quitaron los marcos/bordes de los header-filter
        (ya no aplican a los de texto, que se movieron al buscador; los
        `<select>` de Rol/Estado que quedan se repintan sin borde propio,
        con un tono blanco translúcido para leerse sobre el azul).
      - Plantilla separada a `tareas-tabla.component.html` (antes era un
        template inline de una sola línea) para poder meter el input del
        buscador sin que el `.ts` se vuelva ilegible.
      - **Gotcha real (no era cache ni orden de carga)**: el azul del
        header "no se veía" porque el tema `tabulator_simple.css` usa
        selectores muy específicos (ej. 5 clases encadenadas para
        `.tabulator-col-title`), y nuestros overrides
        (`.tareas-tabla-tabulator .tabulator-col-title`, 2 clases)
        pierden **siempre** por especificidad CSS, sin importar en qué
        orden se carguen las hojas de estilo. Se resolvió agregando
        `!important` a toda propiedad que pisa una clase nativa de
        Tabulator (patrón estándar para re-skinnear un tema de terceros)
        — las clases propias del proyecto (`.badge-estado`,
        `.tareas-tabla-buscador`) no lo necesitan, no hay conflicto de
        especificidad ahí. Si se agrega un override nuevo a este archivo
        y "no se ve", **este es el motivo por defecto a revisar primero**.
      - La barra del buscador quedó del mismo azul que el header de
        Tabulator y sin borde entre ambos, para que se vean como un solo
        bloque azul continuo (el buscador arriba, el encabezado de
        columnas justo debajo). Ocupa todo el ancho de la tabla, pero el
        input en sí es una caja blanca redondeada de 30% de ancho
        (`.tareas-tabla-buscador__caja`, `min-width: 200px` para no
        quedar inservible en tablas angostas) pegada a la izquierda.
      - Pasada de "que respiren los textos": el tema trae solo 4px de
        padding en `.tabulator-col-content` (título de columna) y en
        `.tabulator-cell` (celdas de fila) — se subió a `10px 14px` en
        ambos (con `!important`, mismo motivo de siempre) y el alto
        mínimo de fila de 46px a 52px. De paso se unificó el lenguaje
        visual a "todo en píldora" (`border-radius: 999px`): los
        dropdown de Rol/Estado en el header, los botones de paginación
        y el selector de tamaño de página, y el badge de estado — antes
        mezclaban 4px/8px/50px según el elemento.
      - **Otro gotcha real**: los dropdown de Rol/Estado en el header
        (`headerFilter: 'list'`) **no son un `<select>` nativo** —
        Tabulator los implementa como un `<input type="text">` que abre
        un popup con la lista (`function list(){ ... return list.input }`
        en su código fuente), y ese popup se cuelga de `document.body`,
        fuera de este componente. El primer intento de redondearlos
        apuntaba a `.tabulator-header-filter select`, que nunca existió
        — cero efecto, no era un problema de especificidad esta vez. Se
        corrigió apuntando a `.tabulator-header-filter input`.
- [x] **Crear proyecto directo dentro de la carpeta activa** — si en
      `/proyectos` el filtro de carpetas está puesto en una carpeta
      **propia** (no `all`/`none`/una compartida contigo), el formulario
      de "Nuevo proyecto" manda `folderId` y el proyecto nace ya
      dentro de ella (evita crear + mover). `CreateProjectDto.FolderId`
      es opcional; el backend valida lo mismo que en `MoveToFolder`
      (la carpeta debe existir y ser del usuario). El formulario muestra
      un aviso ("Se creará dentro de la carpeta X") mientras el filtro
      activo sea una carpeta propia.
      - `TareaDetalleDialogComponent` (`shared/components/tarea-detalle-dialog/`)
        abre `TareaCardComponent` completa dentro de un `MatDialog` — sin
        duplicar su lógica de edición. Los outputs de la tarjeta
        (`statusChange`, `assigneeChange`, `subtaskCreate`, etc.) se
        re-emiten desde el dialog; quien lo abre (`proyecto-detalle` /
        `mis-tareas`) sigue usando los mismos handlers que ya tenía,
        ahora con un `onDone` callback opcional para refrescar
        `dialogRef.componentInstance.task` (un `signal`) con la versión
        actualizada — el dialog no vuelve a pedir datos al backend por
        su cuenta.
      - Nuevo campo `TaskItemDto.LastStatusChangeAt` (backend): máximo de
        `TaskStatusHistory.ChangedAt` para esa tarea (fallback a
        `CreatedAt` si no hay historial). Requirió agregar
        `.Include(t => t.StatusHistory)` en los queries de
        `TasksController` — importante notar que `ToDto` ya no puede
        usarse dentro de un `.Select()` de EF Core (no es traducible a
        SQL con esta lógica), así que `GetByProject`/`GetMine` primero
        materializan (`ToListAsync()`) y el mapeo a DTO ocurre en
        memoria.
- [x] **Carpetas de proyectos, con compartir** — migración
      `AgregarCarpetasDeProyectos` (entidad `ProjectFolder` +
      `Project.FolderId` nullable).
      - **Carpetas personales**: pertenecen a un usuario, que es el único
        que las administra. Un proyecto vive en **una sola** carpeta o en
        ninguna (como carpetas de archivos, no como etiquetas).
      - Endpoints: `GET/POST /api/folders`, `PATCH /api/folders/{id}`
        (renombrar), `DELETE /api/folders/{id}`,
        `PUT /api/folders/{id}/share`, y
        `PATCH /api/projects/{id}/folder` (mover).
      - **Compartir hace DOS cosas** y es destructivo:
        1. Agrega a la persona como miembro de todos los proyectos de la
           carpeta. Esto es obligatorio, no cosmético: sin membresía
           tendría tareas asignadas en proyectos que no puede ni abrir
           (`GetByProject` y `UpdateStatus` exigen ser miembro).
        2. **Reasigna TODAS las tareas** de esos proyectos a esa persona,
           pisando quien las tuviera. Incluye subtareas y también tareas
           ya Terminadas/Canceladas (fue una decisión explícita del
           usuario: "todas"). **No se puede deshacer**: el modelo no
           guarda historial de asignaciones, solo de estados.
      - Solo se comparte con **una** persona: `TaskItem.AssignedToId` es
        uno solo, con dos personas una pisaría a la otra.
      - Re-compartir con alguien más vuelve a reasignar. **Dejar de
        compartir NO revierte** las tareas: solo quita el acceso.
      - Respeta la regla de área: solo se comparte con gente de tu área,
        igual que invitar a un proyecto.
      - Solo se pueden meter en una carpeta **proyectos propios**: como
        compartir agrega miembros, permitir proyectos ajenos sería una
        puerta trasera para invitar gente a un proyecto que no es tuyo.
      - Borrar una carpeta **no borra los proyectos**: la FK usa
        `SetNull` y quedan sin carpeta.
      - **Frontend, con la administración en un solo lugar** para no
        duplicar la lógica en dos pantallas:
        - `/carpetas` (`features/carpetas/`) — vista dedicada en
          tarjetas, con secciones "Mis carpetas" y "Compartidas
          conmigo". Aquí vive TODO el CRUD: crear, renombrar,
          compartir, dejar de compartir, eliminar. Cada tarjeta muestra
          conteo de proyectos/tareas y con quién está compartida.
        - `/proyectos` — las carpetas aparecen solo como **chips de
          filtro** (sin menús de administración), más un enlace
          "Administrar carpetas". Sí conserva el menú por tarjeta para
          **mover** un proyecto, que es una acción del proyecto, no de
          la carpeta.
        - "Ver proyectos" en una tarjeta navega a
          `/proyectos?carpeta=<id>`, que preselecciona el filtro.
        - `CompartirCarpetaDialogComponent` vive en `shared/components/`
          (lo usan las dos pantallas) y muestra el aviso de cuántas
          tareas se van a reasignar antes de confirmar.
        - Link "Carpetas" en el menú lateral. Se cambió el icono de
          Proyectos a `space_dashboard` porque `folder` ahora es el de
          Carpetas.
- [x] **Lazy loading de rutas** — al agregar carpetas, el bundle inicial
      cruzó el límite duro de 1 MB de `angular.json` y **el build empezó
      a fallar**. En vez de subir el presupuesto se convirtieron todas
      las rutas a `loadComponent` (antes eran imports estáticos y todo
      caía en el bundle inicial). Resultado: **1.00 MB → 336 kB**, ya ni
      dispara el warning de 500 kB, y cada pantalla baja en su propio
      chunk al visitarla.
- [x] **Editar título y descripción** (proyectos y tareas) — antes solo
      se podían definir al crear; no había ningún endpoint de update.
      - Backend: `PATCH /api/projects/{id}` (`UpdateProjectDto`) y
        `PATCH /api/tasks/{id}/details` (`UpdateTaskDetailsDto`). Ambos
        validan que el título/nombre no quede vacío.
      - **Autorización distinta en cada uno, a propósito**: el proyecto
        solo lo edita **su dueño** (mismo criterio que invitar/quitar
        miembros); la tarea la edita cualquier **miembro/dueño del
        proyecto**, o el **creador/asignado** si es suelta (mismo
        criterio que `UpdateStatus`/`UpdateAssignee`).
      - Área y dueño del proyecto siguen sin ser editables.
      - Frontend: edición en línea. En el proyecto, botón "Editar" en el
        header (solo si `isOwner()`); en la tarea, un lápiz en el header
        de `tarea-card` que convierte el título en input y la sección
        Descripción en el editor, con Guardar/Cancelar.
      - Título y descripción se guardan **juntos** en un solo PATCH,
        por eso hay un único `detailsChange` y no dos outputs.
      - `proyecto-detalle` ahora importa `FormsModule` además de
        `ReactiveFormsModule`: el form de crear tarea sigue siendo
        reactive y la edición del proyecto usa `ngModel` sobre campos
        fuera de cualquier `formGroup` (no se mezclan en el mismo
        control, que es lo único que Angular no permite).
- [x] **Editor de texto enriquecido en las descripciones** (proyectos y
      tareas) — `shared/components/editor-texto/`. Barra mínima: tamaño (Texto
      normal / Título / Subtítulo), negrita, itálica, subrayado,
      alinear izq/centro/der, lista con viñetas y lista numerada.
      - **Sin librería** (ni Quill ni TipTap): es `contenteditable` +
        `document.execCommand`. execCommand está **deprecado** pero sigue
        implementado en todos los navegadores y es la única forma
        razonable de hacer esto sin escribir un motor de edición sobre
        Range/Selection. Como el componente es `ControlValueAccessor`,
        si algún día hay que cambiarlo por una librería los formularios
        que lo usan no se enteran.
      - El tamaño usa `formatBlock` (`<p>`/`<h3>`/`<h4>`) en vez del
        comando `fontSize`, que genera etiquetas `<font>` deprecadas.
      - **Las descripciones ahora se guardan como HTML.** Tanto
        `Projects.Description` como `Tasks.Description` ya eran
        `nvarchar(max)`, no hizo falta migración.
      - **Dónde se usa el editor**: crear proyecto (`proyectos`), crear
        tarea de proyecto (`proyecto-detalle`) y crear tarea suelta
        (`mis-tareas`). El formulario de subtarea dentro de `tarea-card`
        solo pide título, no tiene descripción.
      - **Dónde se renderiza**: detalle del proyecto y descripción de la
        tarea en `tarea-card` (dentro del modal), ambos con
        `[innerHTML]` + clase `.contenido-rico` (estilos globales en
        `styles.scss`, porque el HTML se inyecta dinámico y el scope de
        Angular no alcanza esos nodos). Angular **sanitiza `[innerHTML]`
        por default** (quita scripts y handlers) y no se usa ningún
        bypass — importa porque ese HTML lo escribió otro usuario.
        En las tarjetas de `/proyectos` se muestra en **texto plano** con
        el pipe `textoPlano` (`shared/pipes/`), porque títulos y listas
        romperían el layout de una tarjeta chica. Ese pipe usa
        `DOMParser` y no un `div` temporal: el documento que crea es
        inerte, no ejecuta scripts ni carga recursos.
      - Al pasar la descripción de tarea a HTML se quitó el
        `white-space: pre-wrap` de `.tarea__desc`: con contenido HTML
        haría que la indentación del markup se viera como espacios de
        más. **Ojo si se migra data vieja**: descripciones en texto
        plano con saltos de línea perderían esos saltos al renderizarse
        como HTML. En la BD de desarrollo se verificó que no hay
        ninguna así (0 de 10 tareas), por eso no se hizo conversión.
      - Nota: el detalle de proyecto **no mostraba la descripción en
        ningún lado** antes de esto; se agregó al implementar el editor.
- [x] **Pestaña de archivos del proyecto** —
      `features/proyectos/proyecto-archivos/`, cuarta pestaña en
      `proyecto-detalle`. Tabla plana con todos los adjuntos de todas
      las tareas del proyecto: archivo, peso, tarea de origen y quién lo
      subió, más botón de descarga.
      - Backend: nuevo `GET /api/projects/{id}/attachments`
        (`ProjectsController`) + `ProjectAttachmentDto`, que agrega
        `TaskTitle` respecto a `TaskAttachmentDto`. Misma autorización
        que ver el proyecto (miembro o dueño).
      - La **descarga reusa** `GET /api/tasks/{taskId}/attachments/{id}/download`
        — por eso el DTO incluye `TaskId`; no hizo falta endpoint nuevo.
      - Se extrajeron `formatFileSize()` y `saveBlobAsFile()` como
        funciones exportadas en `task-attachment.service.ts`, porque
        `tarea-card` y esta pestaña las necesitaban por igual (antes
        vivían duplicadas dentro de `tarea-card`).
      - Usa `<ng-template matTabContent>` para carga diferida: sin eso
        MatTab renderiza el contenido de todas las pestañas de entrada y
        el componente dispararía su GET al abrir el proyecto, aunque
        nadie mire la pestaña.
- [x] **Pestaña de estadísticas del proyecto** —
      `features/proyectos/proyecto-estadisticas/`, tercera pestaña en
      `proyecto-detalle` (Tareas / Estadísticas / Miembros). Muestra
      % de completado, total de tareas, completadas, y un gráfico de
      dona con el reparto por estado + leyenda. Todo filtrable por
      persona asignada (incluye opción "Sin asignar").
      - **Sin librería de charts**: la dona es SVG inline. Se usa el
        truco de `r = 15.9155` (circunferencia = 100), así el
        `stroke-dasharray` se expresa directo en porcentajes y el
        `stroke-dashoffset` es `-acumuladoAnterior`. El `<g>` va rotado
        -90° para que el primer segmento arranque a las 12. Se prefirió
        esto a meter una dependencia nueva por un único gráfico.
      - Es el primer componente que usa **inputs de signal**
        (`input.required<T>()`) en vez de `@Input()`: hacía falta para
        derivar todas las métricas con `computed()` y que se
        recalculen solas al cambiar el filtro o la lista de tareas.
      - Cuenta **subtareas también** (aplana `task.subtasks`), y usa la
        misma fórmula de % que el backend:
        `Terminadas / (Total - Canceladas)`, así el número coincide con
        el que muestra la tarjeta del proyecto en `/proyectos`.
- [x] **Rediseño del modal de detalle de tarea** — el contenido estaba
      pegado a los bordes, amontonado en móvil y con las secciones sin
      separación visual clara. Cambios:
      - `tarea-card` pasó de `<mat-card>` con divs sueltos a secciones
        `.bloque` separadas por línea, cada una con su título
        (Descripción / Asignación y estado / Fechas de seguimiento /
        Subtareas / Archivos adjuntos / Comentarios). Adjuntos y
        comentarios son bloques colapsables con contador y chevron.
      - Textos más descriptivos: "Adjuntos" → "Archivos adjuntos",
        "Asignada a" → "Persona asignada", "Estado" → "Estado de la
        tarea", "Adjuntar archivo" → "Subir un archivo", + estados
        vacíos explícitos ("Todavía no hay comentarios en esta tarea").
      - Los `mat-form-field` outline (56px de alto por default) se veían
        enormes junto a texto de 13px: se corrigió con
        `@include mat.form-field-density(-3)` en el `:host` del SCSS +
        `subscriptSizing="dynamic"` en el template (quita el espacio
        reservado para hints/errores, que era lo que forzaba el hack
        `margin-bottom: -20px`).
      - Móvil: el hueco a la derecha venía de
        `.tarea-detalle-content { max-width: 60vw }` peleando con el
        ancho del dialog (95vw) — se quitó. Ahora el dialog va a pantalla
        completa bajo 640px (`panelClass: 'tarea-detalle-panel'` +
        media query en `styles.scss`, con `!important` porque MatDialog
        pone el ancho como estilo inline en el overlay), y los grids de
        campos/fechas colapsan a una columna.
      - El fondo gris del modal era el `surface-container-high` de M3;
        se forzó blanco en `.mat-mdc-dialog-surface`.
      - Ver también la sección "Paleta / design tokens" más abajo.
- [x] **Panel de super admin** (v1: usuarios + proyectos, solo lectura de
      proyectos + toggle de admin en usuarios; sin borrar nada todavía).
      Descubrimiento importante al implementarlo: el comentario original
      en `User.IsSuperAdmin` decía que era un "cache" sincronizado desde
      un realm role de Keycloak — **eso nunca se construyó**, es
      simplemente una columna booleana propia de esta BD, sin ninguna
      relación con Keycloak. Se corrigió el comentario en `User.cs`. Esto
      significa que el panel **no dependía** del mapeo de roles de
      Keycloak después de todo (ver siguiente punto).
      - `AdminController` (`api/admin/...`), autorización explícita en
        cada acción (`IsSuperAdmin` propio, mismo patrón que el resto del
        proyecto — nada de `[Authorize(Roles=...)]`).
        `GET /users`, `PATCH /users/{id}/super-admin` (no te puedes
        quitar el flag si eres el único super admin que queda),
        `GET /projects` (todos, sin filtrar por membresía; agrega
        `MemberCount` vs. `ProjectDto`).
      - Frontend: `UserService.currentUser` — nuevo signal poblado por
        `getMe()`, usado para mostrar el link "Admin" en `/proyectos` y
        por `superAdminGuard` (`core/guards/super-admin.guard.ts`, async:
        si nadie pidió `/me` todavía en la sesión —ej. entras directo a
        `/admin` por URL— lo pide él mismo antes de decidir).
      - **El primer super admin de un ambiente nuevo se sigue sembrando a
        mano en SQL** (`UPDATE Users SET IsSuperAdmin = 1 WHERE Id = '...'`)
        — no hay forma de que el primero se autopromueva desde la UI, es
        el bootstrap esperado. Desde ahí, ese admin ya puede promover a
        otros desde el panel.
- [x] **Baneo de usuarios** — nueva columna `User.IsBanned` (migración
      `AgregarIsBanned`, default `false`). Se gestiona desde el panel de
      admin (`PATCH /api/admin/users/{id}/ban`; no puedes banearte a ti
      mismo). El bloqueo real no depende de cada Controller: hay un
      `BanCheckMiddleware` global (`Program.cs`, entre `UseAuthentication`
      y `UseAuthorization`) que corta CUALQUIER request autenticado de un
      usuario baneado con `403 { error: "account_banned" }`, antes de que
      llegue a ningún controller. Importante: Keycloak sigue emitiendo un
      JWT válido para ese usuario (no hay forma de revocarlo desde acá),
      así que el bloqueo es 100% a nivel de esta app, no de la sesión SSO.
      Frontend: `banInterceptor` (`core/interceptors/`) detecta ese 403 en
      cualquier respuesta y redirige a `/cuenta-bloqueada` — con un
      cuidado no obvio: usa `queueMicrotask` para disparar la navegación,
      porque `UserService.getMe()` swallowea sus propios errores a `null`
      (lo necesita para otros casos), y si no se difiere, la navegación
      de quien llamó `getMe()` (ej. `login.component` mandando a
      `/completar-registro`) puede ganarle la carrera y pisar el
      redirect a la pantalla de bloqueo.
- [x] **Menú lateral persistente (`AppShellComponent`)** —
      `shared/components/app-shell/`. Envuelve `proyectos`,
      `proyectos/:id`, `mis-tareas` y `admin` como rutas hijas en
      `app.routes.ts` (`completar-registro` queda fuera a propósito: el
      usuario todavía no tiene área asignada). Colapsado muestra solo
      iconos (Proyectos, Mis tareas, Admin si `isSuperAdmin`); un botón
      lo fija expandido, mostrando además todos los proyectos y las
      últimas 5 tareas (`TaskService.getMine()`, primeras 5 — ya viene
      ordenado por `CreatedAt desc` del backend). Clic en una tarea
      reciente navega a `proyectos/:id` (o `mis-tareas` si es suelta) en
      vez de abrir el modal de detalle directo, para no reproducir la
      lógica de miembros/permisos fuera de `proyecto-detalle`.
      - Los datos del panel expandido (`projects`/`recentTasks`) se
        recargan en cada `NavigationEnd`, no son estado reactivo
        compartido de verdad (no hay store global en este proyecto) — si
        creas un proyecto o tarea nueva se refleja en el siguiente
        cambio de ruta, no al instante.
      - `UserService.getMe()` para poblar `currentUser` (usado por el
        link de Admin) se movió de `proyectos.component` al
        `ngOnInit` del shell — así queda disponible en cualquier
        pantalla, no solo al aterrizar en `/proyectos`.
      - Se quitaron los botones/links de nav duplicados que ya vivían en
        los headers de `proyectos`, `mis-tareas` y `admin` (quedaban
        redundantes con el menú). El "Volver a proyectos" de
        `proyecto-detalle` se dejó igual a propósito: es un breadcrumb
        de una pantalla de detalle (drill-down), no navegación principal
        duplicada.
- [x] Guards de ruta — `authGuard` (`core/guards/auth.guard.ts`)
      aplicado a `completar-registro` y al shell (`AppShellComponent`,
      que cubre `proyectos`, `proyectos/:id` y `mis-tareas`); `admin`
      además tiene `superAdminGuard`. Si `AuthService.isAuthenticated()`
      es falso, redirige a `/login`. Se apoya en que `auth.init()` corre
      como `APP_INITIALIZER` y ya resolvió antes de que el router active
      cualquier ruta.
- [ ] Mapeo de roles reales de Keycloak al backend (`realm_access.roles`,
      con `OnTokenValidated` para mapear a `ClaimTypes.Role`) — resulta
      que ninguna feature construida hasta ahora lo necesita (ni siquiera
      el panel de super admin, que usa su propio flag en BD). Queda como
      mejora de infraestructura pura si algún día se quiere que Keycloak
      sea la fuente de verdad de los roles.

### Baja prioridad / infraestructura
- [ ] Sincronización automática de `Areas` desde la BD institucional
      (actualmente son datos de prueba insertados a mano).
- [x] **SignalR para actualizaciones en tiempo real** — `TaskHub`
      (`backend/.../Hubs/TaskHub.cs`, mapeado en `/hubs/tasks`) es un hub
      de "solo escucha": los clientes nunca mutan datos por ahí (eso
      sigue siendo HTTP normal vía `TasksController`, con toda su
      validación de negocio intacta); solo se conectan, se unen a
      grupos, y reciben el mismo `TaskItemDto` que ya devuelven los
      endpoints REST cuando algo cambia.
      - **Grupos**: `project:{id}` (se une explícitamente quien abre
        `proyecto-detalle`, validando membresía/propiedad igual que
        `GetByProject` — sin esto, cualquiera podría unirse a un
        proyecto ajeno y ver título/descripción de sus tareas) y
        `user:{id}` (se une automático al conectar, en
        `OnConnectedAsync` — cubre "Mis tareas", que mezcla varios
        proyectos y tareas sueltas sin unirse a cada uno).
      - **Quién dispara el evento `TaskChanged`**: `TasksController`
        (`NotificarCambio`, un helper privado) después de `Create`,
        `UpdateStatus`, `UpdateAssignee`, `UpdateDetails` y `MarkRead`.
        Se manda al grupo del proyecto (si tiene) + al creador + al
        asignado actual, y **también al asignado anterior** en una
        reasignación (si no, su "Mis tareas" nunca se entera de que la
        tarea se le fue). Un `HashSet<string>` evita mandar el evento
        dos veces a la misma conexión cuando, p.ej., el creador también
        es miembro del proyecto.
      - **JWT sobre WebSocket/SSE**: el handshake no puede llevar el
        header `Authorization`, así que el cliente manda el token como
        query string `access_token` (`accessTokenFactory` en
        `realtime.service.ts`). El backend solo acepta esa vía para
        rutas bajo `/hubs` — tanto en el `JwtBearer` real
        (`OnMessageReceived` en `Program.cs`) como en `MockAuthHandler`
        (que antes solo leía el header `Authorization`). Aceptarlo por
        query string en el resto de la API expondría el token en logs
        de acceso, por eso queda acotado a esa ruta.
      - **Frontend**: `RealtimeService` (`core/services/realtime.service.ts`)
        expone `taskChanged$` como `Subject`, no `signal` — puede llegar
        más de un cambio en el mismo tick (ej. una carpeta compartida
        reasigna varias tareas de golpe) y un signal solo notifica su
        último valor. La conexión se arranca una sola vez desde
        `AppShellComponent.ngOnInit` (cubre cualquier pantalla
        autenticada); `proyecto-detalle` se une/sale del grupo de su
        proyecto en `ngOnInit`/`ngOnDestroy` y hace upsert en su signal
        `tasks` (tarea nueva, actualización, o subtarea nueva colgada de
        su padre ya cargado); `mis-tareas` no se une a nada (ya está en
        su grupo personal) y además **quita** de la lista una tarea que
        dejó de ser suya (se reasignó a alguien más).
      - **Alcance deliberado — no cubierto todavía**: la reasignación
        masiva al compartir una carpeta (`FoldersController`) no dispara
        `TaskChanged` por cada tarea tocada; quien tenga el proyecto
        abierto no ve ese cambio en vivo, necesita refrescar. No se hizo
        porque no se pidió explícitamente y son N tareas de golpe (más
        costoso de acertar bien sin verlo funcionar) — si se retoma,
        reusar `NotificarCambio` ahí también.
      - Gotcha ya documentado más abajo (Zoneless) sigue aplicando: todo
        lo que llega por `taskChanged$` se guarda en un `signal()`, nunca
        en una propiedad plana.
- [ ] Deploy real: Apache como reverse proxy hacia Kestrel con SSL
      institucional (`VirtualHost` diseñado, no aplicado aún en producción).
- [ ] HTTPS local en desarrollo (actualmente el backend corre solo en
      HTTP en `localhost:5168`, válido para dev pero no configurado).
- [ ] Quitar/mover archivos de ejemplo si quedó alguno de la plantilla
      default (`WeatherForecast.cs` ya se eliminó).

## Configuración / credenciales (placeholders — verificar valores reales en el proyecto)

**`appsettings.json`** (`TaskManager.Api`):
```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost,1433;Database=TaskManagerDb;User Id=taskmanager_app;Password=***;TrustServerCertificate=True;Encrypt=True"
  },
  "Keycloak": {
    "Authority": "https://appcafeteria.xoc.uam.mx/auth-server/realms/cafeteria-uam",
    "ClientId": "task-manager-uamx"
  },
  "AllowedOrigins": ["http://localhost:4200"]
}
```

**`environment.ts`** (frontend):
```typescript
export const environment = {
  production: false,
  apiUrl: 'http://localhost:5168/api',
};
```

**Cliente Keycloak** `task-manager-uamx` (realm `cafeteria-uam`,
compartido con `appcafeteria`):
- Requiere **Audience Mapper** configurado (Client scopes → dedicated
  scope → Add mapper → Audience → Included Client Audience:
  `task-manager-uamx`) — sin esto, el backend rechaza el token con
  `"The audience is invalid"`.
- Web Origins debe incluir `http://localhost:4200` explícitamente
  (campo separado de Redirect URIs).

## Modo mock (desarrollar sin Keycloak levantado)

Cuando el servidor de Keycloak no está disponible, se puede seguir
desarrollando sin él en ambos lados:

- **Frontend**: poner `useMockAuth: true` en
  `frontend/task-manager-app/src/environments/environment.ts`. El
  `AuthService` se salta por completo la inicialización de `keycloak-js`,
  se autentica con el usuario simulado de `environment.mockUser`, y genera
  un token falso (`mock.<base64(claims)>`) que el interceptor manda igual
  en el header `Authorization`.
- **Backend**: poner `"Keycloak": { "MockAuth": true }` en
  `TaskManager.Api/appsettings.Development.json`. `Program.cs` registra
  entonces `MockAuthHandler` (en `TaskManager.Api/Auth/`) en vez del
  `JwtBearer` real: decodifica el token falso del frontend y arma el
  `ClaimsPrincipal` directo, sin contactar a Keycloak ni validar firma.
  Al arrancar imprime un warning en consola para que no se quede
  encendido sin querer.
- Ambos flags están en `false` por default. **Nunca activar
  `Keycloak:MockAuth` en producción** — se salta toda la validación de
  identidad.
- Como el usuario mock no existe en la BD, la primera petición dispara el
  flujo normal de auto-registro (`GET /api/users/me` → 404 →
  `/completar-registro`), útil para probar también esa pantalla.

## Gotchas ya resueltos (para no repetirlos)

1. **Zoneless**: el proyecto Angular usa detección de cambios Zoneless.
   Cualquier estado que se actualice desde un callback async (HTTP,
   timers, SignalR) **debe ser un `signal()`**, nunca una propiedad plana
   — si no, la vista nunca se actualiza aunque el dato sí cambie.
2. **`sub` claim de Keycloak**: ASP.NET Core remapea nombres de claims
   estándar por default. Hay que poner `options.MapInboundClaims = false;`
   en `AddJwtBearer` en `Program.cs`, o `User.FindFirst("sub")` regresa null.
3. **Audience del token**: Keycloak no incluye el `client_id` en `aud` por
   default (usa `"account"`) — requiere el Audience Mapper del punto
   anterior.
4. **Cascada de FKs en SQL Server**: `Area → User → Project` y
   `Area → Project` generan "multiple cascade paths". Se resolvió con
   `DeleteBehavior.Restrict` en `User.Area`, `Project.Area` y
   `Project.Owner` en `AppDbContext.OnModelCreating`.
5. **`silent-check-sso.html`**: en Angular CLI 17+, va en `public/`
   (raíz del proyecto), no en `src/assets/`.
6. **Field initializers en componentes standalone**: si un campo de clase
   depende de algo inyectado por constructor (`this.auth.algo()`), debe
   inicializarse **dentro** del cuerpo del constructor, no como field
   initializer arriba — si no, TypeScript marca "used before
   initialization".
7. **`dotnet-ef` no encontrado**: requiere que
   `C:\Users\<usuario>\.dotnet\tools` esté en el PATH del usuario
   (se agrega aparte del PATH de `dotnet` mismo).
8. **`--use-controllers`** es obligatorio al correr
   `dotnet new webapi` en .NET 10, si no, genera Minimal APIs por default.

## Convenciones del proyecto

- Nombres de archivo en inglés técnico está bien, pero variables/comentarios
  de negocio en español (consistente con el resto del código UAMX).
- DTOs como `record` de C#, inmutables.
- Todo componente Angular: standalone, signals para estado async, Reactive
  Forms (no template-driven).
- Cada Controller valida autorización de negocio explícitamente en el
  método (no solo `[Authorize]` genérico) — el frontend nunca es la capa
  de seguridad real.
- Componentes de UI reutilizados por más de una pantalla viven en
  `frontend/task-manager-app/src/app/shared/components/` (ej.
  `tarea-card`, usado por `proyecto-detalle` y `mis-tareas`).
- Las pantallas "de lista/administración" (`proyectos`, `mis-tareas`,
  `carpetas`, `admin`, `proyecto-detalle`) usan `max-width: 80vw` (con
  `margin: 0 auto`) en vez de un límite fijo en px, para aprovechar
  monitores anchos. Solo se aplica arriba de `600px` de viewport — en
  móvil no tendría sentido (dejaría una franja vacía en vez de usar
  todo el ancho disponible). Las pantallas de formulario angosto
  (`login`, `completar-registro`, `cuenta-bloqueada`) y los diálogos
  modales quedan fuera de esta convención a propósito, siguen con su
  ancho fijo pequeño.
- Los diálogos modales (`tarea-detalle-dialog`, `compartir-carpeta-dialog`)
  se agrandaron ~20% (720px→860px, 520px→624px) — un ajuste menor,
  pensado para dar más aire sin llegar a ocupar media pantalla.
- **Todo `mat-form-field appearance="outline"` del proyecto** debe traer
  `@include mat.form-field-density(-3)` en el `:host` de su componente
  y `subscriptSizing="dynamic"` en el template. Sin esto mide ~56px de
  alto por default y se ve enorme al lado de texto de 13-14px — pasó
  varias veces que se aplicaba solo en el componente que se estaba
  tocando ese día (`tarea-card`, `proyecto-estadisticas`) y se quedaba
  afuera en el resto (`mis-tareas`, `proyecto-detalle`,
  `compartir-carpeta-dialog`, `completar-registro`), dando una sensación
  de inconsistencia entre pantallas. Ya está aplicado en los 6 archivos
  que usan `mat-select`/`mat-form-field` — si se agrega un mat-form-field
  nuevo en cualquier pantalla, hay que acordarse de este patrón.
- **"Persona asignada" en modo solo lectura** (cuando `tarea-card` recibe
  `members: null`, como en "Mis tareas") pasó de ser un `<div>` a medida
  con CSS que intentaba imitar a mano el alto/borde del
  `mat-form-field` de "Estado" de al lado, a ser directamente un
  `mat-form-field` real con un `<input matInput disabled>`. Al ser el
  mismo componente que el select vecino, hereda automáticamente el
  mismo alto/densidad sin CSS de ajuste — eso era lo que se veía
  amontonado/disparejo en el modal de detalle. El input disabled se
  repinta con `::ng-deep .campo .mat-mdc-input-element:disabled` para
  que el texto no se vea "apagado" (gris tenue) como un disabled normal,
  sino como un valor de solo lectura legible.

## Paleta / design tokens

Definidos como CSS custom properties en `src/styles.scss` (bloque `html`).
**Usar estas variables, no hex sueltos, en el SCSS de componentes.**

| Token | Valor | Uso |
|---|---|---|
| `--app-surface` | `#ffffff` | Fondo de tarjetas/modales que muestran **información** (nunca cambia con el rediseño) |
| `--app-bg` | `#F2F6FF` (= `--app-blue-soft`) | Fondo de todo lo que **no** muestra información propia: el shell, el body detrás de las tarjetas |
| `--app-surface-subtle` | `#F2F3F4` (v2; antes `#f6f8fa`) | Fondos de cajas internas / campos de solo lectura / zebra striping — mezcla clara de `--app-contrast`, distinta de `--app-bg` a propósito (ese es azulado y vive fuera de las tarjetas; este es más neutro y vive dentro) |
| `--app-blue` / `-strong` / `-soft` | `#2E5966` / `#274c57` / `#F2F6FF` | Acento principal (**v2**, antes `#3985EC`/azul brillante) |
| `--app-contrast` | `#576D73` | Iconos/botones que necesitan contrastar contra `--app-bg` sin ser el azul de acento |
| `--app-green` / `-strong` / `-soft` | `#429867` / `#388158` / `#E6F2EB` | Éxito (**v2**, antes `#129468`) — mismo verde que el badge de "Terminada" (`--estado-terminada`), un solo verde de "éxito" en toda la app |
| `--app-red` / `-strong` / `-soft` | `#E02130` / `#BE1C29` / `#FBE2E4` | Error, acciones destructivas (**v2**, antes `#D42A49`) — mismo rojo que el badge de "Cancelada" (`--estado-cancelada`) |
| `--app-border` / `-strong` | `#e4e7ec` / `#d3d8e0` | Separadores y bordes de campos |
| `--app-text` / `-muted` / `-subtle` | `#1a1f28` / `#5b6472` / `#858e9c` | Jerarquía de texto |

Las variantes `-soft` son fondos de badge/chip y las `-strong` el texto
que va encima de ellos (contraste suficiente).

**Rediseño de paleta en curso (iniciado 2026-09-11)**: el usuario está
reemplazando la paleta por fases, dando 2-3 colores a la vez y pidiendo
ver cómo se ve antes de seguir. Primera pasada: azul de acento nuevo
(`#2E5966`), fondo "sin información" (`#F2F6FF`, ajustado una vez desde
un cyan más marcado que no gustó) aplicado a
`body`/`.shell-container`/`.shell-nav`/`.shell-content`, y
`--app-contrast` (`#576D73`) aplicado por ahora solo a los íconos/enlaces
del menú lateral (`app-shell`) — **no** se hizo un barrido exhaustivo de
íconos en el resto de la app todavía, queda para cuando se confirme la
dirección. Verde/rojo ya se actualizaron (ver tabla arriba: reusan el
mismo verde/rojo que Terminada/Cancelada) y `--app-surface-subtle`
también adoptó el esquema nuevo — con esto la paleta base (azul, verde,
rojo, fondos, contraste) queda completa en su primera vuelta. Pendiente
real: el barrido exhaustivo de íconos con `--app-contrast` en el resto
de la app (solo se hizo en `app-shell`). Si se retoma este hilo, seguir
la misma mecánica: nuevo(s) color(es) → actualizar tokens en
`styles.scss` → avisar qué se tocó y qué quedó pendiente.

**Gotcha real: `mat-card` NO usa `--app-surface`/`--mat-sys-surface`.**
El `appearance="elevated"` (el default, usado en Proyectos/Mis tareas/
Carpetas/etc.) toma su fondo de `card-elevated-container-color:
surface-container-low` — un tono que Material genera mezclando el color
primario, confirmado leyendo
`@angular/material/card/_m3-card.scss`. Con el primario nuevo (más
oscuro/saturado) el tinte se volvió más notorio y las tarjetas dejaron
de verse blancas puras, aunque `--app-surface` siempre fue `#ffffff` —
no era el token equivocado, era que las cards ni lo estaban usando.
Se forzó blanco puro con `--mat-card-elevated-container-color:
var(--app-surface)` (global, en el bloque `html` de `styles.scss`) y se
reaprovechó el tinte original como estado hover/presionado en
`.proyecto-card` (`--mat-sys-surface-container-low` en hover,
`-container` en active) — el usuario lo pidió así explícitamente: blanco
en reposo, el tinte de antes como feedback de que la tarjeta es
clicable. Las cards de `carpetas` no lo llevan porque no son
clicables como bloque completo (tienen su propio botón "Ver
proyectos"). Si aparece otro `mat-card` clicable en el futuro, aplicar
el mismo patrón.

**Sidebar invertida respecto al resto del shell**: `.shell-nav` (la
barra lateral de iconos/menú) quedó en blanco (`--app-surface`) en vez
de `--app-bg`, y el item activo (`.shell-link--active`) pasó a usar
`--app-bg` en vez de blanco — exactamente al revés de como se dejó el
resto del shell (`.shell-container`/`.shell-content` siguen en
`--app-bg`). Es intencional: la sidebar se distingue del área de
contenido siendo blanca, y la selección se marca con el tono de fondo.

**`mat-stroked-button` es transparente por default**: el botón
"Administrar carpetas" en `/proyectos` dejaba ver `--app-bg` detrás en
vez de verse sólido. Se agregó `background: var(--app-surface)`
explícito en `.carpetas__admin`. Si aparece otro `mat-stroked-button`
sobre un fondo que no sea blanco, revisar lo mismo.

**Colores del badge de estado de tarea** (`--estado-*` en el bloque
`html`): un color distinto por cada uno de los 8 `TaskItemStatus`, no
una progresión de 3 tonos — con 8 estados agruparlos se volvía difícil
de distinguir de un vistazo. Cada uno tiene su variante `-soft` (fondo)
y se usa tal cual (sin `-strong`) para borde y texto. Usado por
`TareaCardComponent` (`.badge--*`) y `TareasTablaComponent`
(`.badge-estado--*`).

**v2 (2026-09-11)**: "En atención" heredó el morado que tenía
"Terminada" en la v1; Terminada/Volver a revisar/Cancelada se
repintaron con colores más intuitivos (verde=hecho, ámbar=revisar,
rojo=cancelado). Cancelada **ya no es gris neutro** — esa fue la
decisión original (para no darle protagonismo) pero el usuario pidió
explícitamente darle su propio color rojo en esta pasada.

| Estado | Color |
|---|---|
| Creada | `#5A4E74` |
| Asignada | `#4A492C` |
| Leída | `#4BA600` (v3; antes `#C9C044`, y por un momento se pidió por error el mismo ámbar que Volver a revisar) |
| En atención | `#6D45C9` (antes `#C96044`; heredó el morado de Terminada v1) |
| Atendida | `#2F9AC9` |
| Volver a revisar | `#D16B00` (v3; antes `#4A2318`, luego `#FAB243`) |
| Terminada | `#429867` (antes `#6D45C9`) |
| Cancelada | `#E02130` (antes gris neutro sin color propio) |

**Resuelto (2026-09-11)**: el gráfico de dona de estadísticas ya no
tiene su propia paleta separada. `STATUS_COLORS`
(`proyecto-estadisticas.component.ts`) ahora guarda strings
`'var(--estado-*)'` en vez de hex propios, y se los pasa tal cual al
binding (`[attr.stroke]="s.color"` en el SVG, `[style.background]`
en la leyenda) — los navegadores modernos resuelven `var()` en
atributos de presentación SVG igual que en CSS normal, así que no hizo
falta ningún truco adicional. Un estado ahora es el mismo color en el
badge de la tabla, el badge del modal de detalle, y el gráfico.

Además se **remapean las variables de sistema de Material**
(`--mat-sys-primary`, `--mat-sys-error`, `--mat-sys-surface`…) a esos
tokens, para que los componentes de la librería (botones, toggles,
spinners) tomen la paleta sin estilizarlos uno por uno. Por eso los
`mat-flat-button` ya no necesitan `color="primary"`: el default ya es el
azul de la app.
