using Google.Apis.Auth.OAuth2;
using Google.Apis.HangoutsChat.v1;
using Google.Apis.HangoutsChat.v1.Data;
using Google.Apis.Services;

namespace TaskManager.Api.Notificaciones;

// Notificaciones por Google Chat, como mensaje directo (DM) a una
// persona especifica - ver plan de notificaciones (correo + Chat).
// Usa "App Authentication": la app actua con su propio service account
// (scope chat.bot), no impersona usuarios - no hace falta domain-wide
// delegation, mucho mas simple de aprobar administrativamente.
//
// El service account viene de la seccion "GoogleChat" de
// appsettings.Local.json (gitignored, nunca versionado - mismo
// mecanismo que EmailSettings/InfoUsuariosUnidad). Acepta DOS formas,
// para no obligar a nadie a hacer la conversion a mano:
//   - "GoogleChat:ServiceAccountJsonBase64": el JSON completo del
//     archivo descargado, codificado en Base64 (forma recomendada,
//     cabe en una sola linea sin problemas de escapes).
//   - O el JSON del archivo pegado tal cual como sub-objeto de
//     "GoogleChat" (con sus propias claves type/project_id/private_key/
//     etc.) - se reconstruye leyendo esas claves de la configuracion.
public class GoogleChatClient
{
    private const string ChatBotScope = "https://www.googleapis.com/auth/chat.bot";

    private readonly HangoutsChatService? _service;
    private readonly ILogger<GoogleChatClient> _logger;

    public GoogleChatClient(IConfiguration configuration, ILogger<GoogleChatClient> logger)
    {
        _logger = logger;

        var json = ResolverCredencialJson(configuration.GetSection("GoogleChat"));
        if (json is null)
        {
            _service = null;
            return;
        }

        var credential = CredentialFactory.FromJson<ServiceAccountCredential>(json)
            .ToGoogleCredential()
            .CreateScoped(ChatBotScope);

        _service = new HangoutsChatService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "AppTareas",
        });
    }

    private static string? ResolverCredencialJson(IConfigurationSection googleChatSection)
    {
        var base64 = googleChatSection["ServiceAccountJsonBase64"];
        if (!string.IsNullOrWhiteSpace(base64))
        {
            var bytes = Convert.FromBase64String(base64);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        // No vino en Base64 - revisa si se pego el JSON de la cuenta de
        // servicio directo como sub-objeto (se reconoce por tener
        // "private_key", que nunca aparece en ninguna otra config de
        // esta app). Se reconstruye a un JSON plano desde esas claves.
        var privateKey = googleChatSection["private_key"];
        if (string.IsNullOrWhiteSpace(privateKey)) return null;

        var campos = googleChatSection
            .GetChildren()
            .Where(c => c.Key != "ServiceAccountJsonBase64" && c.Key != "InteractionsAudience")
            .ToDictionary(c => c.Key, c => c.Value);

        return System.Text.Json.JsonSerializer.Serialize(campos);
    }

    // Sin credencial resuelta = Chat deshabilitado (igual que
    // EmailService.Enabled) - el resto de la app sigue funcionando
    // igual sin esto configurado.
    public bool Enabled => _service is not null;

    // Encuentra el espacio DM entre la app y esa persona - NO lo crea:
    // spaces.setup (que si crea) no soporta App Authentication, solo
    // autenticacion de usuario (confirmado en produccion: "insufficient
    // authentication scopes" con el scope chat.bot). spaces.findDirectMessage
    // si soporta App Authentication, pero como su nombre lo dice, SOLO
    // encuentra un DM que ya existe (404 si no) - asi que la persona
    // necesita haberle escrito al bot al menos una vez antes (ver plan -
    // "alta manual"), no hay forma de mandarle un DM completamente en
    // frio con este tipo de autenticacion.
    //
    // chatUserId: el ID de Directory (User.GoogleChatUserId, formato
    // "users/{id}" o solo "{id}") - NO un correo. Confirmado en
    // produccion: bajo App Authentication, "users/{email}" no resuelve
    // (404 aunque la persona si le haya escrito al bot antes) - Google
    // solo acepta el id de Directory/People bajo este tipo de auth (el
    // formato con email es exclusivo de autenticacion de usuario). Ver
    // ChatInteractionsController para como se captura y guarda este id.
    //
    // Este metodo no esta expuesto por Google.Apis.HangoutsChat.v1
    // (la libreria instalada no lo genero todavia), asi que se llama
    // por HTTP crudo reusando el HttpClient ya autenticado del servicio.
    public async Task<string> FindDmAsync(string chatUserId, CancellationToken ct = default)
    {
        if (_service is null)
            throw new InvalidOperationException("Google Chat esta deshabilitado (falta configurar GoogleChat en appsettings).");

        var userResource = chatUserId.StartsWith("users/", StringComparison.Ordinal) ? chatUserId : $"users/{chatUserId}";
        // _service.BaseUri es solo "https://chat.googleapis.com/" (sin la
        // version) - a diferencia de los metodos generados (ej.
        // Spaces.Messages.Create), que agregan "v1/" por su cuenta, aqui
        // hay que ponerlo a mano al armar la URL cruda. Sin esto, Google
        // regresaba 404 (pagina de error generica, no el 404 real de
        // "no existe el DM") aunque la cuenta ya estuviera vinculada.
        var url = $"{_service.BaseUri}v1/spaces:findDirectMessage?name={Uri.EscapeDataString(userResource)}";
        using var response = await _service.HttpClient.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "No se encontro un DM con esa persona en Google Chat (deberia existir si ya vinculo su cuenta).");
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var space = Newtonsoft.Json.JsonConvert.DeserializeObject<Space>(json);
        return space!.Name;
    }

    // Tarjeta con boton "Ver tarea" (abre el link directo, no toca
    // nuestro backend) y, si aplica, boton "Marcar como Atendida" (abre
    // un dialogo de Chat - ver ChatInteractionsController). No lanza si
    // falla el envio: el llamador ya hizo lo importante (guardar el
    // cambio en la BD), la notificacion es best-effort.
    // assignedUserId: nuestro User.Id (no un email) de la persona con
    // permiso para resolver esta tarea - viaja como parametro oculto del
    // boton y vuelve en el clic (ver ChatInteractionsController), porque
    // el evento de interaccion de Chat no trae ninguna identidad que se
    // pueda mapear directo a nuestros usuarios.
    public async Task SendTaskCardAsync(
        string spaceName,
        Guid taskId,
        string cardTitle,
        string texto,
        bool incluirBotonAtendida,
        string? assignedUserId = null,
        CancellationToken ct = default)
    {
        if (_service is null) return;

        var taskUrl = $"https://apptareas.xoc.uam.mx/mis-tareas?tarea={taskId}";

        var buttons = new List<GoogleAppsCardV1Button>
        {
            new GoogleAppsCardV1Button
            {
                Text = "Ver tarea",
                OnClick = new GoogleAppsCardV1OnClick
                {
                    OpenLink = new GoogleAppsCardV1OpenLink { Url = taskUrl },
                },
            },
        };

        if (incluirBotonAtendida && !string.IsNullOrEmpty(assignedUserId))
        {
            buttons.Add(new GoogleAppsCardV1Button
            {
                Text = "Marcar como Atendida",
                OnClick = new GoogleAppsCardV1OnClick
                {
                    Action = new GoogleAppsCardV1Action
                    {
                        Function = "marcarAtendida",
                        Interaction = "OPEN_DIALOG",
                        Parameters = new List<GoogleAppsCardV1ActionParameter>
                        {
                            new GoogleAppsCardV1ActionParameter { Key = "taskId", Value = taskId.ToString() },
                            new GoogleAppsCardV1ActionParameter { Key = "actingUserId", Value = assignedUserId },
                        },
                    },
                },
            });
        }

        var card = new GoogleAppsCardV1Card
        {
            Header = new GoogleAppsCardV1CardHeader { Title = cardTitle },
            Sections = new List<GoogleAppsCardV1Section>
            {
                new GoogleAppsCardV1Section
                {
                    Widgets = new List<GoogleAppsCardV1Widget>
                    {
                        new GoogleAppsCardV1Widget { TextParagraph = new GoogleAppsCardV1TextParagraph { Text = texto } },
                        new GoogleAppsCardV1Widget { ButtonList = new GoogleAppsCardV1ButtonList { Buttons = buttons } },
                    },
                },
            },
        };

        var message = new Message
        {
            CardsV2 = new List<CardWithId>
            {
                new CardWithId { CardId = "notificacionTarea", Card = card },
            },
        };

        try
        {
            await _service.Spaces.Messages.Create(message, spaceName).ExecuteAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo mandar la tarjeta de Chat a {Space}", spaceName);
        }
    }
}
