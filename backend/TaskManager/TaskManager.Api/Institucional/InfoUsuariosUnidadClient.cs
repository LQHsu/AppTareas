using MySqlConnector;

namespace TaskManager.Api.Institucional;

// Consulta la misma BD institucional info_usuarios_unidad (MySQL,
// 148.206.99.178) que ya usa el SPI de Keycloak para completar
// correo/area (ver InfoUsuariosUnidadClient.java) - aqui es solo para
// previsualizar datos en la vista de "Dar de alta", nunca para decidir
// el area que se asigna (esa sigue siendo la del coordinador que da la
// alta, ver CoordinadorController).
//
// Connection string configurable via "InfoUsuariosUnidad:ConnectionString"
// (appsettings.Local.json, nunca versionado) - mismas credenciales que
// ya se dieron de alta en Keycloak Admin Console -> User federation ->
// cusxacdi-uamx. Si no esta configurado, BuscarAsync regresa null y la
// vista de alta simplemente no muestra preview (nunca bloquea la alta).
public class InfoUsuariosUnidadClient
{
    private readonly string? _connectionString;
    private readonly ILogger<InfoUsuariosUnidadClient> _logger;

    public InfoUsuariosUnidadClient(IConfiguration configuration, ILogger<InfoUsuariosUnidadClient> logger)
    {
        _connectionString = configuration["InfoUsuariosUnidad:ConnectionString"];
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    public record DatosInstitucionales(string? Email, string? Area);

    public async Task<DatosInstitucionales?> BuscarAsync(string numeroEconomico, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return null;

        try
        {
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // Mismo gotcha que del lado Java (ver InfoUsuariosUnidadClient.java):
            // el servidor MySQL sirve character_set_client/connection/results
            // en latin1 por default aunque la BD sea utf8 - forzar la sesion
            // a utf8mb4 evita mojibake en nombres con acentos.
            using (var setNames = new MySqlCommand("SET NAMES utf8mb4", conn))
                await setNames.ExecuteNonQueryAsync(ct);

            var email = await BuscarEmailAsync(conn, numeroEconomico, ct);
            var area = await BuscarAreaAsync(conn, numeroEconomico, ct);

            if (email is null && area is null)
                return null;

            return new DatosInstitucionales(email, area);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "info_usuarios_unidad: fallo la consulta para {NumeroEconomico}", numeroEconomico);
            return null;
        }
    }

    private static async Task<string?> BuscarEmailAsync(MySqlConnection conn, string numeroEconomico, CancellationToken ct)
    {
        const string sql = "SELECT CE_Email FROM CE_Correo_Empleados WHERE CE_NumEconomico = @numeroEconomico";
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@numeroEconomico", numeroEconomico);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    private static async Task<string?> BuscarAreaAsync(MySqlConnection conn, string numeroEconomico, CancellationToken ct)
    {
        const string sql = """
            SELECT a.NombreAdscripcion2
            FROM Empleados e
            JOIN Adscripciones a ON a.ClaveAdscripcion = e.Pagaduria
            WHERE e.NumeroEconomico = @numeroEconomico
            """;
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@numeroEconomico", numeroEconomico);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }
}
