using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace TaskManager.Api.Auth;

// Autenticacion falsa para desarrollo local cuando Keycloak no esta
// disponible. Lee un token con forma "Bearer mock.<base64(json)>"
// (generado por el AuthService del frontend en modo mock) y arma el
// ClaimsPrincipal directamente a partir de ese JSON, sin validar firma
// ni contactar ningun servidor externo.
//
// SOLO se activa si "Keycloak:MockAuth" es true (ver Program.cs). Por
// default esa llave esta en false en appsettings.Development.json.
// NUNCA debe activarse en produccion.
public class MockAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "MockJwt";
    private const string TokenPrefix = "Bearer mock.";

    public MockAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? encodedPayload = null;

        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var value = authHeader.ToString();
            if (!value.StartsWith(TokenPrefix, StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.Fail("No es un token mock valido."));
            }

            encodedPayload = value[TokenPrefix.Length..];
        }
        else if (Request.Path.StartsWithSegments("/hubs") &&
                 Request.Query.TryGetValue("access_token", out var queryToken))
        {
            // Mismo caso que el JwtBearer real (ver Program.cs): SignalR
            // manda el token por query string, sin el prefijo "Bearer ",
            // porque el handshake de websocket no puede llevar headers
            // personalizados. Solo se acepta asi para el hub.
            var raw = queryToken.ToString();
            const string mockPrefix = "mock.";
            if (!raw.StartsWith(mockPrefix, StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.Fail("No es un token mock valido."));
            }

            encodedPayload = raw[mockPrefix.Length..];
        }
        else
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        try
        {
            var payloadJson = Convert.FromBase64String(encodedPayload);
            var claimsDict = JsonSerializer.Deserialize<Dictionary<string, string>>(payloadJson)
                ?? new Dictionary<string, string>();

            var claims = claimsDict.Select(kv => new Claim(kv.Key, kv.Value)).ToList();
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch
        {
            return Task.FromResult(AuthenticateResult.Fail("Token mock invalido o corrupto."));
        }
    }
}
