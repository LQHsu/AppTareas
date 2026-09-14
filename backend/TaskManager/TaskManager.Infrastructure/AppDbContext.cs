using Microsoft.EntityFrameworkCore;
using TaskManager.Domain.Entities;
 
namespace TaskManager.Infrastructure;
 
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
 
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();
    public DbSet<TaskStatusHistory> TaskStatusHistories => Set<TaskStatusHistory>();
    public DbSet<ProjectFolder> ProjectFolders => Set<ProjectFolder>();
 
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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