using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaskManager.Domain.Entities;

namespace TaskManager.Infrastructure;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // SQL Server "datetime2" no guarda zona horaria, y el driver de
    // EF Core regresa cada DateTime leido con Kind=Unspecified aunque el
    // valor SIEMPRE sea UTC en este proyecto (todo el codigo escribe con
    // DateTime.UtcNow, nunca DateTime.Now - verificado, cero usos). Sin
    // esto, System.Text.Json serializa esas fechas SIN sufijo "Z" (por
    // ser Unspecified), y el navegador las interpreta como si ya fueran
    // hora local -las muestra ~6h adelantadas respecto a la hora real de
    // Mexico City (UTC-6). Se corrige centralizado aca (aplica a TODO
    // DateTime/DateTime? del modelo) en vez de tocar cada Property() o
    // cada controller: al escribir se guarda tal cual (ya es UTC), al
    // leer se re-marca el Kind como Utc sin cambiar el valor.
    // HaveConversion<T>() pide un TIPO con constructor sin parametros,
    // no una instancia - por eso son clases chicas y no un Func/lambda
    // guardado en un campo.
    private sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter() : base(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
    }

    private sealed class UtcNullableDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public UtcNullableDateTimeConverter()
            : base(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v) { }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }

    public DbSet<Area> Areas => Set<Area>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();
    public DbSet<TaskStatusHistory> TaskStatusHistories => Set<TaskStatusHistory>();
    public DbSet<ProjectFolder> ProjectFolders => Set<ProjectFolder>();
    public DbSet<AltaCoordinador> AltasCoordinador => Set<AltaCoordinador>();
 
    // "sub" de Keycloak: para un usuario federado (ver
    // CusxacdiUserStorageProvider en docker/keycloak/) es un string
    // compuesto tipo "f:<uuid>:<matricula>", nunca un Guid. 128 alcanza
    // de sobra ese formato (~50-60 chars tipicos) con margen. Fijado
    // explicito (en vez de dejar que EF use el default nvarchar(max) de
    // "string") porque las columnas indexadas de abajo (HasIndex) no
    // pueden ser nvarchar(max) en SQL Server.
    private const int UserIdMaxLength = 128;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().Property(u => u.Id).HasMaxLength(UserIdMaxLength);
        modelBuilder.Entity<Project>().Property(p => p.OwnerId).HasMaxLength(UserIdMaxLength);
        modelBuilder.Entity<ProjectFolder>().Property(f => f.OwnerId).HasMaxLength(UserIdMaxLength);
        modelBuilder.Entity<ProjectFolder>().Property(f => f.SharedWithId).HasMaxLength(UserIdMaxLength);
        modelBuilder.Entity<ProjectMember>().Property(m => m.UserId).HasMaxLength(UserIdMaxLength);
        modelBuilder.Entity<TaskAttachment>().Property(a => a.UploadedById).HasMaxLength(UserIdMaxLength);
        modelBuilder.Entity<TaskComment>().Property(c => c.UserId).HasMaxLength(UserIdMaxLength);
        modelBuilder.Entity<TaskItem>().Property(t => t.CreatedById).HasMaxLength(UserIdMaxLength);
        modelBuilder.Entity<TaskItem>().Property(t => t.AssignedToId).HasMaxLength(UserIdMaxLength);
        modelBuilder.Entity<TaskStatusHistory>().Property(h => h.ChangedById).HasMaxLength(UserIdMaxLength);
        modelBuilder.Entity<AltaCoordinador>().Property(a => a.CreatedById).HasMaxLength(UserIdMaxLength);
        modelBuilder.Entity<AltaCoordinador>().Property(a => a.UserId).HasMaxLength(UserIdMaxLength);

        // Color: hex de 7 caracteres ("#RRGGBB"), igual de fijo que
        // NumeroEconomico mas abajo.
        modelBuilder.Entity<ProjectFolder>().Property(f => f.Color).HasMaxLength(7);
        modelBuilder.Entity<Project>().Property(p => p.Color).HasMaxLength(7);

        // Nombre de Area unico: AreasController.Resolve (sincronizacion
        // perezosa desde el claim "area" del token) depende de esto para
        // detectar una carrera de creacion concurrente via
        // DbUpdateException - sin el indice, dos requests simultaneas
        // creando la misma area nueva generarian un duplicado silencioso
        // en vez de que la segunda reconsulte y reuse la primera.
        modelBuilder.Entity<Area>().HasIndex(a => a.Nombre).IsUnique();

        // NumeroEconomico como PK natural: no tiene sentido un Id
        // artificial aparte, y de paso el UNIQUE sale gratis (no puedes
        // dar de alta el mismo numero dos veces).
        modelBuilder.Entity<AltaCoordinador>().HasKey(a => a.NumeroEconomico);
        modelBuilder.Entity<AltaCoordinador>().Property(a => a.NumeroEconomico).HasMaxLength(20);
        modelBuilder.Entity<AltaCoordinador>()
            .HasOne(a => a.Area)
            .WithMany()
            .HasForeignKey(a => a.AreaId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AltaCoordinador>()
            .HasOne(a => a.CreatedBy)
            .WithMany()
            .HasForeignKey(a => a.CreatedById)
            .HasPrincipalKey(u => u.Id)
            .OnDelete(DeleteBehavior.Restrict);

        // SetNull: si alguna vez se borra el User activado, la alta
        // queda como bitacora igual (ver comentario en ActivatedAt),
        // solo se pierde la referencia para resolver su oficina vigente.
        modelBuilder.Entity<AltaCoordinador>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .HasPrincipalKey(u => u.Id)
            .OnDelete(DeleteBehavior.SetNull);

        // SetNull: borrar la oficina no debe invalidar la alta, solo
        // deja de tener oficina asignada (misma regla que Project.Folder).
        modelBuilder.Entity<AltaCoordinador>()
            .HasOne(a => a.Oficina)
            .WithMany()
            .HasForeignKey(a => a.OficinaId)
            .OnDelete(DeleteBehavior.SetNull);

        // Clave compuesta para la tabla intermedia Project <-> User
        modelBuilder.Entity<ProjectMember>()
            .HasKey(pm => new { pm.ProjectId, pm.UserId });

        // Restrict en User -> Area y Project -> Area/Owner para evitar el error
        // de SQL Server "multiple cascade paths" (Area->User->Project y
        // Area->Project serian dos caminos de cascada hacia la misma tabla).
        // Ademas tiene sentido de negocio: borrar un usuario o un area no
        // deberia arrastrar en cascada los proyectos que dependen de ellos.
        modelBuilder.Entity<User>()
            .HasOne(u => u.Area)
            .WithMany(a => a.Users)
            .HasForeignKey(u => u.AreaId)
            .OnDelete(DeleteBehavior.Restrict);
 
        modelBuilder.Entity<Project>()
            .HasOne(p => p.Area)
            .WithMany(a => a.Projects)
            .HasForeignKey(p => p.AreaId)
            .OnDelete(DeleteBehavior.Restrict);
 
        modelBuilder.Entity<Project>()
            .HasOne(p => p.Owner)
            .WithMany(u => u.OwnedProjects)
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
 
        // Auto-referencia de TaskItem (subtareas). Restrict evita el error
        // de SQL Server "multiple cascade paths" al ser una FK hacia si misma.
        modelBuilder.Entity<TaskItem>()
            .HasOne(t => t.ParentTask)
            .WithMany(t => t.SubTasks)
            .HasForeignKey(t => t.ParentTaskId)
            .OnDelete(DeleteBehavior.Restrict);
 
        // Dos relaciones TaskItem -> User (asignado y creador).
        // Restrict para que borrar un usuario no borre en cascada sus tareas.
        modelBuilder.Entity<TaskItem>()
            .HasOne(t => t.AssignedTo)
            .WithMany(u => u.AssignedTasks)
            .HasForeignKey(t => t.AssignedToId)
            .OnDelete(DeleteBehavior.Restrict);
 
        modelBuilder.Entity<TaskItem>()
            .HasOne(t => t.CreatedBy)
            .WithMany(u => u.CreatedTasks)
            .HasForeignKey(t => t.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
 
        // ProjectFolder: dos FKs hacia User (dueno y con quien se
        // comparte). Restrict por la misma razon que el resto —evitar
        // "multiple cascade paths" en SQL Server— y porque borrar un
        // usuario no deberia arrastrar carpetas.
        modelBuilder.Entity<ProjectFolder>()
            .HasOne(f => f.Owner)
            .WithMany()
            .HasForeignKey(f => f.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProjectFolder>()
            .HasOne(f => f.SharedWith)
            .WithMany()
            .HasForeignKey(f => f.SharedWithId)
            .OnDelete(DeleteBehavior.Restrict);

        // SetNull y no Cascade: borrar una carpeta NO debe borrar los
        // proyectos que contiene, solo dejarlos sin carpeta.
        modelBuilder.Entity<Project>()
            .HasOne(p => p.Folder)
            .WithMany(f => f.Projects)
            .HasForeignKey(p => p.FolderId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ProjectFolder>().HasIndex(f => f.OwnerId);
        modelBuilder.Entity<ProjectFolder>().HasIndex(f => f.SharedWithId);
        modelBuilder.Entity<Project>().HasIndex(p => p.FolderId);

        // Indices para las queries mas frecuentes del tablero
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.ProjectId);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.AssignedToId);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.AreaId);
        modelBuilder.Entity<TaskItem>().HasIndex(t => t.Status);
    }
}