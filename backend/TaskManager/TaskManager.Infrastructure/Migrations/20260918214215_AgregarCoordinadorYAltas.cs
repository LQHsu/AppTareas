using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCoordinadorYAltas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCoordinador",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AltasCoordinador",
                columns: table => new
                {
                    NumeroEconomico = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AltasCoordinador", x => x.NumeroEconomico);
                    table.ForeignKey(
                        name: "FK_AltasCoordinador_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AltasCoordinador_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AltasCoordinador_AreaId",
                table: "AltasCoordinador",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_AltasCoordinador_CreatedById",
                table: "AltasCoordinador",
                column: "CreatedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AltasCoordinador");

            migrationBuilder.DropColumn(
                name: "IsCoordinador",
                table: "Users");
        }
    }
}
