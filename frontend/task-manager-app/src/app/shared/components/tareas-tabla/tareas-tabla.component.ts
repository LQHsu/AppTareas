import {
  AfterViewInit,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  ViewChild,
  inject,
} from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TabulatorFull as Tabulator, CellComponent, ColumnDefinition } from 'tabulator-tables';
import { TaskItemDto, TaskItemStatus, TASK_STATUS_LABELS } from '../../../core/services/task.service';
import { UserService } from '../../../core/services/user.service';

// Vista compacta de una lista de tareas (reemplaza lo que antes eran
// tarjetas apiladas, que se volvia dificil de leer con muchas tareas).
// Solo muestra tareas top-level (las subtareas no salen como filas
// propias); el detalle completo -incluidas subtareas y adjuntos- vive
// en el modal que se abre con (rowClick), reutilizando TareaCardComponent.
//
// Se migro de mat-table a Tabulator (JS puro, sin dependencia de
// Angular) porque se necesitaba buscar/filtrar por columna y editar el
// estado directamente desde la fila, cosas que mat-table no trae
// resuelto de fabrica. Tabulator se monta a mano sobre un div con
// ElementRef en ngAfterViewInit y se destruye en ngOnDestroy: no hay
// integracion con el ciclo de deteccion de cambios de Angular, asi que
// cualquier cambio en @Input() se empuja a mano con setData()/updateData().
type FilaTarea = TaskItemDto & { rolLabel: string };

@Component({
  selector: 'app-tareas-tabla',
  standalone: true,
  imports: [MatIconModule],
  templateUrl: './tareas-tabla.component.html',
  styleUrl: './tareas-tabla.component.scss',
})
export class TareasTablaComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('contenedor', { static: true }) contenedor!: ElementRef<HTMLDivElement>;

  @Input({ required: true }) tasks: TaskItemDto[] = [];

  // Util en vistas que mezclan tareas de varios proyectos (mis-tareas).
  // En proyecto-detalle el proyecto es siempre el mismo, se omite.
  @Input() showProject = false;

  @Output() rowClick = new EventEmitter<TaskItemDto>();

  // El propio componente hace el fetch de usuario actual (via el signal
  // cacheado en UserService, ya poblado por app-shell) para poder marcar
  // "Asignada a mí" / "Creada por mí" sin que cada pantalla que usa esta
  // tabla tenga que pasarlo por Input.
  //
  // El cambio de estado NO se aplica aqui: se emite y el padre decide
  // (llama al backend, actualiza su signal de tareas y refresca el
  // modal si esta abierto), igual que ya hacia (click) -> openTaskDetail.
  @Output() statusChange = new EventEmitter<{ task: TaskItemDto; status: TaskItemStatus }>();

  private userService = inject(UserService);
  private tabulator?: Tabulator;

  // Buscador unico arriba de la tabla, en vez de un input de texto por
  // columna (Tarea/Proyecto/Asignada a/Creada por): busca por cualquiera
  // de esos 4 campos a la vez. Los filtros de dropdown (Rol/Estado) se
  // quedan en el encabezado de su columna — son de opciones cerradas,
  // ahi si tiene sentido filtrar por columna. Tabulator combina ambos
  // tipos de filtro con AND automaticamente (ver setFilter en initTabla).
  private terminoBusqueda = '';

  onBuscar(event: Event): void {
    this.terminoBusqueda = (event.target as HTMLInputElement).value.trim().toLowerCase();
    this.tabulator?.refreshFilter();
  }

  private coincideBusqueda = (data: FilaTarea): boolean => {
    if (!this.terminoBusqueda) return true;
    const campos = [data.title, data.projectName, data.assignedToFullName, data.createdByFullName];
    return campos.some((c) => (c ?? '').toLowerCase().includes(this.terminoBusqueda));
  };

  private get currentUserId(): string | null {
    return this.userService.currentUser()?.id ?? null;
  }

  // Misma regla que TareaCardComponent.puedeCambiarA (ver ese
  // componente para el detalle de las reglas de negocio): quien creo la
  // tarea puede moverla a cualquier estado; quien la tiene asignada
  // solo entre En atencion/Atendida; nadie mas puede tocarla desde aca.
  private opcionesEstadoPermitidas(task: TaskItemDto): TaskItemStatus[] {
    const uid = this.currentUserId;
    if (uid !== null && task.createdById === uid) {
      return Object.values(TaskItemStatus).filter((v) => typeof v === 'number') as TaskItemStatus[];
    }
    if (uid !== null && task.assignedToId === uid) {
      return [TaskItemStatus.EnAtencion, TaskItemStatus.Atendida];
    }
    return [];
  }

  ngAfterViewInit(): void {
    this.initTabla();
  }

  ngOnChanges(changes: SimpleChanges): void {
    // showProject cambia las columnas (agrega/quita "Proyecto"); como
    // eso solo pasa al montar la pantalla (nunca cambia en caliente), no
    // vale la pena reconstruir columnas dinamicamente: si tasks llega
    // antes que la vista este lista, ngAfterViewInit ya toma el valor
    // correcto de this.tasks directamente.
    if (this.tabulator && changes['tasks']) {
      this.tabulator.setData(this.toFilas(this.tasks));
    }
  }

  ngOnDestroy(): void {
    this.tabulator?.destroy();
  }

  private toFilas(tasks: TaskItemDto[]): FilaTarea[] {
    const uid = this.currentUserId;
    return tasks.map((t) => {
      const esAsignado = uid !== null && t.assignedToId === uid;
      const esCreador = uid !== null && t.createdById === uid;
      const rolLabel =
        esAsignado && esCreador ? 'Asignada y creada por mí' : esAsignado ? 'Asignada a mí' : 'Creada por mí';
      return { ...t, rolLabel };
    });
  }

  private initTabla(): void {
    const statusOptions: Record<string, string> = {};
    Object.entries(TASK_STATUS_LABELS).forEach(([value, label]) => {
      statusOptions[value] = label;
    });

    const columns: ColumnDefinition[] = [
      {
        title: 'Tarea',
        field: 'title',
        widthGrow: 2,
        // Estilo "enlace" (ver .tabulator-cell[tabulator-field='title']
        // en el scss): es la unica celda que deja claro a simple vista
        // que la fila entera es clicable.
        cssClass: 'celda-titulo',
        cellClick: (_e, cell: CellComponent) => this.rowClick.emit(cell.getRow().getData() as TaskItemDto),
      },
      ...(this.showProject
        ? [
            {
              title: 'Proyecto',
              field: 'projectName',
              formatter: (cell: CellComponent) => cell.getValue() ?? 'Tarea suelta',
              cellClick: (_e: UIEvent, cell: CellComponent) =>
                this.rowClick.emit(cell.getRow().getData() as TaskItemDto),
            } as ColumnDefinition,
          ]
        : []),
      {
        title: 'Rol',
        field: 'rolLabel',
        headerFilter: 'list',
        headerFilterParams: {
          values: { '': 'Todos', 'Asignada a mí': 'Asignada a mí', 'Creada por mí': 'Creada por mí' },
        },
        headerFilterFunc: (headerValue, rowValue) =>
          !headerValue || (rowValue as string).startsWith(headerValue as string),
        cellClick: (_e, cell: CellComponent) => this.rowClick.emit(cell.getRow().getData() as TaskItemDto),
      },
      {
        title: 'Asignada a',
        field: 'assignedToFullName',
        formatter: (cell: CellComponent) => cell.getValue() ?? 'Sin asignar',
        cellClick: (_e, cell: CellComponent) => this.rowClick.emit(cell.getRow().getData() as TaskItemDto),
      },
      {
        title: 'Creada por',
        field: 'createdByFullName',
        cellClick: (_e, cell: CellComponent) => this.rowClick.emit(cell.getRow().getData() as TaskItemDto),
      },
      {
        title: 'Estado',
        field: 'status',
        headerFilter: 'list',
        headerFilterParams: { values: { '': 'Todos', ...statusOptions } },
        // Pill de color en vez de texto plano: mismo lenguaje visual que
        // el resto de la app (paleta azul/verde/rojo), en progresion de
        // "sin empezar" (neutro) -> "en curso" (azul) -> "atencion" o
        // "listo" (rojo/verde). No se inventan colores nuevos.
        formatter: (cell: CellComponent) => this.badgeEstado(cell.getValue() as TaskItemStatus),
        // Editable directo desde la tabla: un dropdown nativo de
        // Tabulator, sin abrir el modal. cellEdited emite el cambio, el
        // padre hace el POST y actualiza su signal (ver comentario del
        // @Output arriba). Si el backend rechaza la transicion, el
        // padre setea errorMessage y la fila queda con el valor viejo
        // hasta el proximo refresh (no hay rollback optimista aqui).
        // No todos pueden mover una tarea a cualquier estado (ver
        // TareaCardComponent.puedeCambiarA, misma regla replicada aca):
        // el editor solo ofrece las opciones permitidas para quien esta
        // viendo la tabla en esta fila puntual. No conoce si el usuario
        // es dueño del proyecto (esa tabla no recibe esa info) — el
        // dueño sin ser creador/asignado no vera nada editable aca,
        // aunque si podria hacerlo desde el modal de detalle.
        editable: (cell: CellComponent) =>
          this.opcionesEstadoPermitidas(cell.getRow().getData() as TaskItemDto).length > 0,
        editor: 'list',
        editorParams: (cell: CellComponent) => ({
          values: this.opcionesEstadoPermitidas(cell.getRow().getData() as TaskItemDto).reduce(
            (acc, s) => ({ ...acc, [s]: TASK_STATUS_LABELS[s] }),
            {} as Record<string, string>
          ),
        }),
        cellEdited: (cell: CellComponent) => {
          const task = cell.getRow().getData() as TaskItemDto;
          this.statusChange.emit({ task, status: Number(cell.getValue()) as TaskItemStatus });
        },
      },
      {
        title: 'Creada',
        field: 'createdAt',
        formatter: (cell: CellComponent) => this.formatFecha(cell.getValue()),
        sorter: 'datetime',
      },
      {
        title: 'Último cambio de estado',
        field: 'lastStatusChangeAt',
        formatter: (cell: CellComponent) => this.formatFecha(cell.getValue()),
        sorter: 'datetime',
      },
    ];

    this.tabulator = new Tabulator(this.contenedor.nativeElement, {
      data: this.toFilas(this.tasks),
      columns,
      layout: 'fitColumns',
      // Sin "height": el host no tiene una altura fija (crece con la
      // pagina), asi que Tabulator debe auto-ajustarse al contenido en
      // vez de intentar llenar un 100% que no existe (eso dejaba la
      // tabla colapsada a 0px con overflow:hidden en el host).
      placeholder: 'No hay tareas.',
      // La fila entera ya no dispara rowClick: algunas celdas (estado)
      // son editables y un click ahi debe abrir el editor, no el modal.
      // Cada columna no editable emite rowClick desde su propio
      // cellClick (arriba).
      pagination: true,
      paginationSize: 25,
      paginationSizeSelector: [10, 25, 50],
      paginationCounter: 'rows',
      initialSort: [{ column: 'createdAt', dir: 'desc' }],
      locale: 'es-mx',
      langs: {
        'es-mx': {
          pagination: {
            first: 'Primera',
            first_title: 'Primera página',
            last: 'Última',
            last_title: 'Última página',
            prev: 'Anterior',
            prev_title: 'Página anterior',
            next: 'Siguiente',
            next_title: 'Página siguiente',
            all: 'Todas',
            counter: {
              showing: 'Mostrando',
              of: 'de',
              rows: 'tareas',
              pages: 'páginas',
            },
          },
        },
      },
    });

    // Filtro de texto libre del buscador (ver onBuscar): se combina con
    // los headerFilter de Rol/Estado con AND automaticamente, no hace
    // falta coordinarlos a mano.
    this.tabulator.setFilter(this.coincideBusqueda);
  }

  private formatFecha(iso: string | null | undefined): string {
    if (!iso) return '—';
    return new Date(iso).toLocaleString('es-MX', {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  private badgeEstado(status: TaskItemStatus): string {
    // Un color distinto por estado (no una progresion de 3 tonos): con
    // 8 estados, agruparlos en pocos tonos los volvia dificil de
    // distinguir a simple vista (ver --estado-* en styles.scss).
    const clases: Record<TaskItemStatus, string> = {
      [TaskItemStatus.Creada]: 'badge-estado--creada',
      [TaskItemStatus.Asignada]: 'badge-estado--asignada',
      [TaskItemStatus.Leida]: 'badge-estado--leida',
      [TaskItemStatus.EnAtencion]: 'badge-estado--en-atencion',
      [TaskItemStatus.Atendida]: 'badge-estado--atendida',
      [TaskItemStatus.VolverARevisar]: 'badge-estado--volver-a-revisar',
      [TaskItemStatus.Terminada]: 'badge-estado--terminada',
      [TaskItemStatus.Cancelada]: 'badge-estado--cancelada',
    };
    const label = TASK_STATUS_LABELS[status];
    return `<span class="badge-estado ${clases[status]}">${label}</span>`;
  }
}
