using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using TaskManager.Infrastructure;

namespace TaskManager.Api.Controllers;

// Recibe los mensajes/clics que llegan del bot de Google Chat - ver
// plan de notificaciones (Estado-Proyecto.md / plan de correo+Chat). A
// diferencia de TODO el resto del backend, este controller NO usa
// [Authorize]: quien llama es Google Chat, no una persona con token de
// Keycloak. En su lugar, cada request se verifica a mano contra el JWT
// que Google manda en el header Authorization (ver
// EsRequestLegitimoDeGoogleAsync).
//
// IMPORTANTE sobre el formato del payload: la app quedo configurada
// (via Cloud Console) bajo el runtime de "Google Workspace Add-ons"
// para Chat, no el modelo clasico de Chat app documentado como "Event"/
// DeprecatedEvent (type/message/user/action/common al nivel superior).
// Confirmado en produccion con logging temporal: lo que de verdad llega
// es un objeto anidado {commonEventObject, authorizationEventObject,
// chat: {user, messagePayload / buttonClickedPayload}}. Los tipos
// generados en Google.Apis.HangoutsChat.v1.Data (DeprecatedEvent, etc.)
// son del modelo VIEJO y no matchean nada de este payload -por eso se
// navega a mano con JObject de Newtonsoft en vez de deserializar a un
// tipo fuerte.
[ApiController]
[Route("api/chat/interactions")]
public class ChatInteractionsController : ControllerBase
{
    // Dominio de la cuenta de servicio con la que Google firma estos
    // tokens para apps de Chat/Add-ons configuradas via Cloud Console
    // con audiencia "HTTP endpoint URL" - confirmado en produccion (ver
    // logs): NO es la cuenta fija vieja "chat@system.gserviceaccount.com"
    // que documenta la guia general de Chat, sino una cuenta propia del
    // PROYECTO de GCP bajo este dominio administrado por Google
    // ("service-<numero-de-proyecto>@gcp-sa-gsuiteaddons.iam.gserviceaccount.com").
    // Como el dominio es de Google (nadie mas puede tener una cuenta de
    // servicio ahi) y el token ya viene firmado por Google Y con la
    // audiencia = nuestro endpoint (verificado antes de llegar aca),
    // validar el sufijo del dominio es suficiente.
    private const string ChatServiceAccountEmailDomain = "@gcp-sa-gsuiteaddons.iam.gserviceaccount.com";

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<ChatInteractionsController> _logger;

    public ChatInteractionsController(AppDbContext db, IConfiguration config, ILogger<ChatInteractionsController> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Handle()
    {
        if (!await EsRequestLegitimoDeGoogleAsync())
            return Unauthorized();

        string raw;
        using (var reader = new StreamReader(Request.Body))
        {
            raw = await reader.ReadToEndAsync();
        }

        JObject body;
        try
        {
            body = JObject.Parse(raw);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return BadRequest();
        }

        var chat = body["chat"];
        if (chat is null) return SinRespuesta(); // evento que no es de Chat (poco probable, pero no truena)

        var userEmail = (string?)chat["user"]?["email"];
        var chatUserId = (string?)chat["user"]?["name"]; // "users/{id}" - id de Directory, NO el email

        // Cualquier mensaje/clic que llegue ya trae el correo - se
        // vincula (o re-confirma) la cuenta de una vez, sin pedirle
        // nada a la persona (a diferencia del primer intento de este
        // flujo, que le pedia escribir su correo a mano porque se asumio
        // -mal- que Chat no lo mandaba).
        if (!string.IsNullOrEmpty(userEmail) && !string.IsNullOrEmpty(chatUserId))
        {
            await VincularCuentaAsync(userEmail, chatUserId);
        }

        // Clic en un boton (ej. "Marcar como Atendida") - el manejo
        // completo del dialogo interactivo bajo este runtime de Add-ons
        // queda pendiente (usa un mecanismo de respuesta distinto al que
        // ya se habia armado para el modelo clasico de Chat app, ver
        // nota de la clase) - por ahora solo se reconoce el evento sin
        // tronar, para no dejar a Chat esperando una respuesta que nunca
        // llega.
        if (chat["buttonClickedPayload"] is not null)
        {
            _logger.LogWarning("buttonClickedPayload recibido, todavia sin implementar: {Body}", raw);
            return JsonTexto("Esa acción todavía no está lista, pero ya tomé nota de tu clic.");
        }

        var mensaje = (string?)chat["messagePayload"]?["message"]?["text"];
        if (mensaje is not null)
        {
            return JsonTexto(
                string.IsNullOrEmpty(userEmail)
                    ? "Hola, soy el bot de AppTareas."
                    : "¡Hola! Ya tengo vinculada tu cuenta de AppTareas — aquí te voy a avisar cuando haya novedades en tus tareas."
            );
        }

        // Otros eventos (ADDED_TO_SPACE, etc.) no necesitan respuesta.
        return SinRespuesta();
    }

    // A diferencia de JsonTexto, esto NO manda un mensaje vacio al chat
    // (se veria como una burbuja en blanco) - un objeto vacio le dice a
    // Chat "recibido, sin nada que agregar".
    private ContentResult SinRespuesta() =>
        Content("{}", "application/json");

    private async Task VincularCuentaAsync(string email, string chatUserId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || user.GoogleChatUserId == chatUserId) return;

        user.GoogleChatUserId = chatUserId;
        await _db.SaveChangesAsync();
    }

    // Bajo el runtime de Add-ons (ver nota de la clase), la respuesta
    // sincrona NO es un Message plano ({"text": "..."}) como en el
    // modelo clasico de Chat app - hay que envolverlo en
    // hostAppDataAction.chatDataAction.createMessageAction.message,
    // confirmado en la documentacion de "Respond to Google Chat app
    // commands" para Workspace Add-ons. Se serializa a mano (no con
    // Ok(...)) para no depender de ninguna politica de casing/formato
    // que no sea la que Chat espera literalmente.
    private ContentResult JsonTexto(string texto)
    {
        var respuesta = new
        {
            hostAppDataAction = new
            {
                chatDataAction = new
                {
                    createMessageAction = new
                    {
                        message = new { text = texto },
                    },
                },
            },
        };

        return Content(Newtonsoft.Json.JsonConvert.SerializeObject(respuesta), "application/json");
    }

    // Verifica que este POST de verdad venga de Google Chat, siguiendo
    // https://developers.google.com/workspace/chat/verify-requests-from-chat.
    // La audiencia esperada es la URL de este mismo endpoint (modo
    // "HTTP endpoint URL" elegido al configurar la app en Cloud Console
    // - ver GoogleChat:InteractionsAudience), y ademas se confirma que
    // el token lo emitio una cuenta de servicio de Google para esta app.
    private async Task<bool> EsRequestLegitimoDeGoogleAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("POST a /api/chat/interactions sin header Authorization Bearer.");
            return false;
        }

        var token = authHeader["Bearer ".Length..];
        var audience = _config["GoogleChat:InteractionsAudience"];
        if (string.IsNullOrWhiteSpace(audience))
        {
            _logger.LogWarning("GoogleChat:InteractionsAudience no esta configurado.");
            return false;
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(token, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { audience },
            });

            if (payload.Email is null || !payload.Email.EndsWith(ChatServiceAccountEmailDomain, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Token valido pero de un emisor inesperado en /api/chat/interactions: {Email}",
                    payload.Email);
                return false;
            }

            return true;
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Token invalido en /api/chat/interactions (audiencia configurada: {Audience})", audience);
            return false;
        }
    }
}
