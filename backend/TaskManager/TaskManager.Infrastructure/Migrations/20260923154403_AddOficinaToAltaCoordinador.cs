using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOficinaToAltaCoordinador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OficinaId",
                table: "AltasCoordinador",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AltasCoordinador_OficinaId",
                table: "AltasCoordinador",
                column: "OficinaId");

            migrationBuilder.AddForeignKey(
                name: "FK_AltasCoordinador_ProjectFolders_OficinaId",
                table: "AltasCoordinador",
                column: "OficinaId",
                principalTable: "ProjectFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AltasCoordinador_ProjectFolders_OficinaId",
                table: "AltasCoordinador");

            migrationBuilder.DropIndex(
                name: "IX_AltasCoordinador_OficinaId",
                table: "AltasCoordinador");

            migrationBuilder.DropColumn(
                name: "OficinaId",
                table: "AltasCoordinador");
        }
    }
}
