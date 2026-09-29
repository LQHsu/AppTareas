namespace TaskManager.Api.Notificaciones;

// Bind de la seccion "Email" de appsettings.json/appsettings.Local.json
// (mismo patron que InfoUsuariosUnidad - ver Program.cs). Host vacio =
// correo deshabilitado, ver EmailService.Enabled.
public class EmailSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 25;

    // Vacios = sin autenticacion (el relay institucional
    // xsmtp.xoc.uam.mx confia en la IP de origen, no en credenciales -
    // mismo relay que ya usa appcafeteria en produccion).
    public string? User { get; set; }
    public string? Password { get; set; }

    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
}
