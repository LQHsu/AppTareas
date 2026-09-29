using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace TaskManager.Api.Notificaciones;

// Correo institucional via el relay SMTP interno de la UAM
// (xsmtp.xoc.uam.mx:25) - el mismo relay que ya usa appcafeteria en
// produccion (PHP/CodeIgniter), aca via MailKit. Sin autenticacion ni
// TLS a proposito: es un relay interno (no expuesto a internet) que
// confia en la IP de origen del servidor, no en usuario/password - los
// campos User/Password de EmailSettings quedan vacios en este entorno,
// no es un descuido.
//
// Registrado como singleton (ver Program.cs): no guarda estado propio
// aparte de la config ya resuelta, y MailKit.SmtpClient se crea/conecta
// por cada envio (no se comparte una conexion entre requests).
public class EmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<EmailSettings> settings, ILogger<EmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    // Host vacio = correo deshabilitado (ej. desarrollo local, sin
    // acceso al relay institucional): SendAsync no lanza, solo no manda
    // nada - el resto de la app sigue funcionando igual sin esto
    // configurado, como ya pasa con InfoUsuariosUnidad.
    public bool Enabled => !string.IsNullOrWhiteSpace(_settings.Host);

    public async Task SendAsync(
        string toAddress,
        string toName,
        string subject,
        string htmlBody,
        CancellationToken ct = default)
    {
        if (!Enabled)
        {
            _logger.LogInformation(
                "Correo no enviado (Email:Host vacio en la configuracion): \"{Subject}\" -> {To}",
                subject, toAddress);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(new MailboxAddress(toName, toAddress));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();

        // SecureSocketOptions.None: el relay institucional no habla TLS
        // en el puerto 25 (ver comentario de la clase) - StartTls
        // tiraria error de conexion contra este servidor en particular.
        await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.None, ct);

        if (!string.IsNullOrEmpty(_settings.User))
        {
            await client.AuthenticateAsync(_settings.User, _settings.Password ?? string.Empty, ct);
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
