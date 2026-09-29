using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdToAltaCoordinador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "AltasCoordinador",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AltasCoordinador_UserId",
                table: "AltasCoordinador",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AltasCoordinador_Users_UserId",
                table: "AltasCoordinador",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Backfill para altas activadas ANTES de que existiera esta
            // columna: UsersController.Create ya llenaba ActivatedAt pero
            // no habia donde guardar el UserId real. Se reconstruye
            // matcheando el "sub" compuesto de Keycloak
            // ("f:<uuid-del-provider>:<matricula>") contra el numero
            // economico pelado - mismo criterio que
            // UsersController.ExtraerNumeroEconomico, pero al reves.
            // Cuentas semilla/dev (Id = matricula tal cual, sin el
            // prefijo "f:...:") tambien matchean por la comparacion
            // directa. Sin esto, la oficina "vigente" de gente ya
            // activada antes de este cambio no se podria resolver (ver
            // CoordinadorController.ToDtoAsync) y se veria como si no
            // tuviera ninguna hasta que se le vuelva a compartir algo.
            migrationBuilder.Sql(@"
                UPDATE ac
                SET ac.UserId = u.Id
                FROM AltasCoordinador ac
                INNER JOIN Users u
                    ON u.Id = ac.NumeroEconomico
                    OR u.Id LIKE '%:' + ac.NumeroEconomico
                WHERE ac.ActivatedAt IS NOT NULL AND ac.UserId IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AltasCoordinador_Users_UserId",
                table: "AltasCoordinador");

            migrationBuilder.DropIndex(
                name: "IX_AltasCoordinador_UserId",
                table: "AltasCoordinador");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "AltasCoordinador");
        }
    }
}
