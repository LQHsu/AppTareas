using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarSnapshotInstitucionalAAltas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AreaInstitucional",
                table: "AltasCoordinador",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorreoInstitucional",
                table: "AltasCoordinador",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AreaInstitucional",
                table: "AltasCoordinador");

            migrationBuilder.DropColumn(
                name: "CorreoInstitucional",
                table: "AltasCoordinador");
        }
    }
}
