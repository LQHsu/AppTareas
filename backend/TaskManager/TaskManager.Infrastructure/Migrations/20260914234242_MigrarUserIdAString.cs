using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Escrita a mano (no la generada tal cual por "dotnet ef migrations
    // add"): el scaffold por default emite los ALTER COLUMN directo,
    // pero SQL Server los rechaza mientras existan la PK de Users y las
    // 9 FKs que le apuntan ("ALTER TABLE ALTER COLUMN Id failed because
    // one or more objects access this column" - confirmado al intentar
    // aplicar la version generada). Orden correcto: quitar indices no-PK
    // -> quitar FKs -> quitar PK -> alterar columnas -> recrear PK ->
    // recrear FKs -> recrear indices. El ALTER COLUMN de uniqueidentifier
    // a nvarchar hace el CAST automatico (formato con guiones,
    // preservando el valor) - no hay perdida de datos real pese al
    // warning que EF muestra por default en este tipo de cambio.
    public partial class MigrarUserIdAString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- 1) Indices no-PK sobre las columnas a alterar ---
            migrationBuilder.DropIndex(name: "IX_ProjectFolders_OwnerId", table: "ProjectFolders");
            migrationBuilder.DropIndex(name: "IX_ProjectFolders_SharedWithId", table: "ProjectFolders");
            migrationBuilder.DropIndex(name: "IX_ProjectMembers_UserId", table: "ProjectMembers");
            migrationBuilder.DropIndex(name: "IX_Projects_OwnerId", table: "Projects");
            migrationBuilder.DropIndex(name: "IX_TaskAttachments_UploadedById", table: "TaskAttachments");
            migrationBuilder.DropIndex(name: "IX_TaskComments_UserId", table: "TaskComments");
            migrationBuilder.DropIndex(name: "IX_Tasks_AssignedToId", table: "Tasks");
            migrationBuilder.DropIndex(name: "IX_Tasks_CreatedById", table: "Tasks");
            migrationBuilder.DropIndex(name: "IX_TaskStatusHistories_ChangedById", table: "TaskStatusHistories");

            // --- 2) FKs hacia Users ---
            migrationBuilder.DropForeignKey(name: "FK_Projects_Users_OwnerId", table: "Projects");
            migrationBuilder.DropForeignKey(name: "FK_ProjectMembers_Users_UserId", table: "ProjectMembers");
            migrationBuilder.DropForeignKey(name: "FK_Tasks_Users_AssignedToId", table: "Tasks");
            migrationBuilder.DropForeignKey(name: "FK_Tasks_Users_CreatedById", table: "Tasks");
            migrationBuilder.DropForeignKey(name: "FK_TaskAttachments_Users_UploadedById", table: "TaskAttachments");
            migrationBuilder.DropForeignKey(name: "FK_TaskComments_Users_UserId", table: "TaskComments");
            migrationBuilder.DropForeignKey(name: "FK_TaskStatusHistories_Users_ChangedById", table: "TaskStatusHistories");
            migrationBuilder.DropForeignKey(name: "FK_ProjectFolders_Users_OwnerId", table: "ProjectFolders");
            migrationBuilder.DropForeignKey(name: "FK_ProjectFolders_Users_SharedWithId", table: "ProjectFolders");

            // --- 3) PKs que dependen de columnas a alterar: la de Users
            // (Id), y la compuesta de ProjectMembers (ProjectId, UserId)
            // - esta ultima bloquea el ALTER COLUMN de UserId igual que
            // cualquier FK/PK (confirmado al intentar aplicar sin esto:
            // "PK_ProjectMembers is dependent on column UserId"). ---
            migrationBuilder.DropPrimaryKey(name: "PK_Users", table: "Users");
            migrationBuilder.DropPrimaryKey(name: "PK_ProjectMembers", table: "ProjectMembers");

            // --- 4) Alterar columnas: uniqueidentifier -> nvarchar(128) ---
            // La normalizacion a minusculas (LOWER) va DESPUES de todos
            // los AlterColumn, no antes: un UPDATE ... SET Id = LOWER(...)
            // corrido mientras la columna todavia es uniqueidentifier no
            // sirve de nada (SQL Server reconvierte el resultado de vuelta
            // a GUID al guardarlo, perdiendo el casing - eso paso la
            // primera vez que se corrio esta migracion, quedo un usuario
            // en mayusculas pese a este UPDATE). Ver el bloque de LOWER()
            // mas abajo, ya con las columnas como string de verdad.
            migrationBuilder.AlterColumn<string>(
                name: "Id", table: "Users", type: "nvarchar(128)", maxLength: 128, nullable: false,
                oldClrType: typeof(Guid), oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<string>(
                name: "ChangedById", table: "TaskStatusHistories", type: "nvarchar(128)", maxLength: 128, nullable: false,
                oldClrType: typeof(Guid), oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedById", table: "Tasks", type: "nvarchar(128)", maxLength: 128, nullable: false,
                oldClrType: typeof(Guid), oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<string>(
                name: "AssignedToId", table: "Tasks", type: "nvarchar(128)", maxLength: 128, nullable: true,
                oldClrType: typeof(Guid), oldType: "uniqueidentifier", oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "UserId", table: "TaskComments", type: "nvarchar(128)", maxLength: 128, nullable: false,
                oldClrType: typeof(Guid), oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<string>(
                name: "UploadedById", table: "TaskAttachments", type: "nvarchar(128)", maxLength: 128, nullable: false,
                oldClrType: typeof(Guid), oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerId", table: "Projects", type: "nvarchar(128)", maxLength: 128, nullable: false,
                oldClrType: typeof(Guid), oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<string>(
                name: "UserId", table: "ProjectMembers", type: "nvarchar(128)", maxLength: 128, nullable: false,
                oldClrType: typeof(Guid), oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<string>(
                name: "SharedWithId", table: "ProjectFolders", type: "nvarchar(128)", maxLength: 128, nullable: true,
                oldClrType: typeof(Guid), oldType: "uniqueidentifier", oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OwnerId", table: "ProjectFolders", type: "nvarchar(128)", maxLength: 128, nullable: false,
                oldClrType: typeof(Guid), oldType: "uniqueidentifier");

            // Las columnas nullable (AssignedToId, SharedWithId) tambien
            // se normalizan - LOWER(NULL) es NULL, no hace falta filtrar.
            // Users.Id primero: los UPDATE de abajo comparan/derivan
            // contra columnas de otras tablas, pero cada UPDATE aqui es
            // independiente por tabla, asi que el orden entre ellos no
            // importa en si - va primero solo por claridad de lectura.
            migrationBuilder.Sql("UPDATE [Users] SET [Id] = LOWER([Id]);");
            migrationBuilder.Sql("UPDATE [TaskStatusHistories] SET [ChangedById] = LOWER([ChangedById]);");
            migrationBuilder.Sql("UPDATE [Tasks] SET [CreatedById] = LOWER([CreatedById]), [AssignedToId] = LOWER([AssignedToId]);");
            migrationBuilder.Sql("UPDATE [TaskComments] SET [UserId] = LOWER([UserId]);");
            migrationBuilder.Sql("UPDATE [TaskAttachments] SET [UploadedById] = LOWER([UploadedById]);");
            migrationBuilder.Sql("UPDATE [Projects] SET [OwnerId] = LOWER([OwnerId]);");
            migrationBuilder.Sql("UPDATE [ProjectMembers] SET [UserId] = LOWER([UserId]);");
            migrationBuilder.Sql("UPDATE [ProjectFolders] SET [OwnerId] = LOWER([OwnerId]), [SharedWithId] = LOWER([SharedWithId]);");

            // --- 5) Recrear PKs ---
            migrationBuilder.AddPrimaryKey(name: "PK_Users", table: "Users", column: "Id");
            migrationBuilder.AddPrimaryKey(name: "PK_ProjectMembers", table: "ProjectMembers", columns: new[] { "ProjectId", "UserId" });

            // --- 6) Recrear FKs (mismo Restrict/SetNull que en AppDbContext) ---
            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Users_OwnerId", table: "Projects", column: "OwnerId",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectMembers_Users_UserId", table: "ProjectMembers", column: "UserId",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Users_AssignedToId", table: "Tasks", column: "AssignedToId",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Users_CreatedById", table: "Tasks", column: "CreatedById",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskAttachments_Users_UploadedById", table: "TaskAttachments", column: "UploadedById",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskComments_Users_UserId", table: "TaskComments", column: "UserId",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskStatusHistories_Users_ChangedById", table: "TaskStatusHistories", column: "ChangedById",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectFolders_Users_OwnerId", table: "ProjectFolders", column: "OwnerId",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectFolders_Users_SharedWithId", table: "ProjectFolders", column: "SharedWithId",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            // --- 7) Recrear indices ---
            migrationBuilder.CreateIndex(name: "IX_ProjectFolders_OwnerId", table: "ProjectFolders", column: "OwnerId");
            migrationBuilder.CreateIndex(name: "IX_ProjectFolders_SharedWithId", table: "ProjectFolders", column: "SharedWithId");
            migrationBuilder.CreateIndex(name: "IX_ProjectMembers_UserId", table: "ProjectMembers", column: "UserId");
            migrationBuilder.CreateIndex(name: "IX_Projects_OwnerId", table: "Projects", column: "OwnerId");
            migrationBuilder.CreateIndex(name: "IX_TaskAttachments_UploadedById", table: "TaskAttachments", column: "UploadedById");
            migrationBuilder.CreateIndex(name: "IX_TaskComments_UserId", table: "TaskComments", column: "UserId");
            migrationBuilder.CreateIndex(name: "IX_Tasks_AssignedToId", table: "Tasks", column: "AssignedToId");
            migrationBuilder.CreateIndex(name: "IX_Tasks_CreatedById", table: "Tasks", column: "CreatedById");
            migrationBuilder.CreateIndex(name: "IX_TaskStatusHistories_ChangedById", table: "TaskStatusHistories", column: "ChangedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Mismo orden: indices -> FKs -> PK -> alterar columnas de
            // vuelta -> recrear PK -> FKs -> indices. No revierte el
            // LOWER() aplicado en Up (no hace falta: un Guid en cualquier
            // casing sigue siendo un Guid valido).
            migrationBuilder.DropIndex(name: "IX_ProjectFolders_OwnerId", table: "ProjectFolders");
            migrationBuilder.DropIndex(name: "IX_ProjectFolders_SharedWithId", table: "ProjectFolders");
            migrationBuilder.DropIndex(name: "IX_ProjectMembers_UserId", table: "ProjectMembers");
            migrationBuilder.DropIndex(name: "IX_Projects_OwnerId", table: "Projects");
            migrationBuilder.DropIndex(name: "IX_TaskAttachments_UploadedById", table: "TaskAttachments");
            migrationBuilder.DropIndex(name: "IX_TaskComments_UserId", table: "TaskComments");
            migrationBuilder.DropIndex(name: "IX_Tasks_AssignedToId", table: "Tasks");
            migrationBuilder.DropIndex(name: "IX_Tasks_CreatedById", table: "Tasks");
            migrationBuilder.DropIndex(name: "IX_TaskStatusHistories_ChangedById", table: "TaskStatusHistories");

            migrationBuilder.DropForeignKey(name: "FK_Projects_Users_OwnerId", table: "Projects");
            migrationBuilder.DropForeignKey(name: "FK_ProjectMembers_Users_UserId", table: "ProjectMembers");
            migrationBuilder.DropForeignKey(name: "FK_Tasks_Users_AssignedToId", table: "Tasks");
            migrationBuilder.DropForeignKey(name: "FK_Tasks_Users_CreatedById", table: "Tasks");
            migrationBuilder.DropForeignKey(name: "FK_TaskAttachments_Users_UploadedById", table: "TaskAttachments");
            migrationBuilder.DropForeignKey(name: "FK_TaskComments_Users_UserId", table: "TaskComments");
            migrationBuilder.DropForeignKey(name: "FK_TaskStatusHistories_Users_ChangedById", table: "TaskStatusHistories");
            migrationBuilder.DropForeignKey(name: "FK_ProjectFolders_Users_OwnerId", table: "ProjectFolders");
            migrationBuilder.DropForeignKey(name: "FK_ProjectFolders_Users_SharedWithId", table: "ProjectFolders");

            migrationBuilder.DropPrimaryKey(name: "PK_Users", table: "Users");
            migrationBuilder.DropPrimaryKey(name: "PK_ProjectMembers", table: "ProjectMembers");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id", table: "Users", type: "uniqueidentifier", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);

            migrationBuilder.AlterColumn<Guid>(
                name: "ChangedById", table: "TaskStatusHistories", type: "uniqueidentifier", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedById", table: "Tasks", type: "uniqueidentifier", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);

            migrationBuilder.AlterColumn<Guid>(
                name: "AssignedToId", table: "Tasks", type: "uniqueidentifier", nullable: true,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128, oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId", table: "TaskComments", type: "uniqueidentifier", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);

            migrationBuilder.AlterColumn<Guid>(
                name: "UploadedById", table: "TaskAttachments", type: "uniqueidentifier", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerId", table: "Projects", type: "uniqueidentifier", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId", table: "ProjectMembers", type: "uniqueidentifier", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);

            migrationBuilder.AlterColumn<Guid>(
                name: "SharedWithId", table: "ProjectFolders", type: "uniqueidentifier", nullable: true,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128, oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerId", table: "ProjectFolders", type: "uniqueidentifier", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);

            migrationBuilder.AddPrimaryKey(name: "PK_Users", table: "Users", column: "Id");
            migrationBuilder.AddPrimaryKey(name: "PK_ProjectMembers", table: "ProjectMembers", columns: new[] { "ProjectId", "UserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Users_OwnerId", table: "Projects", column: "OwnerId",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectMembers_Users_UserId", table: "ProjectMembers", column: "UserId",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Users_AssignedToId", table: "Tasks", column: "AssignedToId",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Users_CreatedById", table: "Tasks", column: "CreatedById",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskAttachments_Users_UploadedById", table: "TaskAttachments", column: "UploadedById",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskComments_Users_UserId", table: "TaskComments", column: "UserId",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskStatusHistories_Users_ChangedById", table: "TaskStatusHistories", column: "ChangedById",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectFolders_Users_OwnerId", table: "ProjectFolders", column: "OwnerId",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectFolders_Users_SharedWithId", table: "ProjectFolders", column: "SharedWithId",
                principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateIndex(name: "IX_ProjectFolders_OwnerId", table: "ProjectFolders", column: "OwnerId");
            migrationBuilder.CreateIndex(name: "IX_ProjectFolders_SharedWithId", table: "ProjectFolders", column: "SharedWithId");
            migrationBuilder.CreateIndex(name: "IX_ProjectMembers_UserId", table: "ProjectMembers", column: "UserId");
            migrationBuilder.CreateIndex(name: "IX_Projects_OwnerId", table: "Projects", column: "OwnerId");
            migrationBuilder.CreateIndex(name: "IX_TaskAttachments_UploadedById", table: "TaskAttachments", column: "UploadedById");
            migrationBuilder.CreateIndex(name: "IX_TaskComments_UserId", table: "TaskComments", column: "UserId");
            migrationBuilder.CreateIndex(name: "IX_Tasks_AssignedToId", table: "Tasks", column: "AssignedToId");
            migrationBuilder.CreateIndex(name: "IX_Tasks_CreatedById", table: "Tasks", column: "CreatedById");
            migrationBuilder.CreateIndex(name: "IX_TaskStatusHistories_ChangedById", table: "TaskStatusHistories", column: "ChangedById");
        }
    }
}
