using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCarpetasDeProyectos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "Projects",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SharedWithId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SharedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectFolders_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectFolders_Users_SharedWithId",
                        column: x => x.SharedWithId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_FolderId",
                table: "Projects",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFolders_OwnerId",
                table: "ProjectFolders",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFolders_SharedWithId",
                table: "ProjectFolders",
                column: "SharedWithId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_ProjectFolders_FolderId",
                table: "Projects",
                column: "FolderId",
                principalTable: "ProjectFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_ProjectFolders_FolderId",
                table: "Projects");

            migrationBuilder.DropTable(
                name: "ProjectFolders");

            migrationBuilder.DropIndex(
                name: "IX_Projects_FolderId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "Projects");
        }
    }
}
