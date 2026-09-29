using System.Text;
using System.Text.RegularExpressions;

namespace TaskManager.Api.Institucional;

// Cliente .NET del mismo web service SOAP institucional que ya consume
// el SPI de Keycloak (backend/keycloak-extensions/.../CusxacdiClient.java)
// - aqui solo se usa RecuperaInfoCuenta (nombre completo), nunca
// ValidaAccesoCuenta: esto es para previsualizar datos en la vista de
// "Dar de alta" antes de confirmar, no para autenticar a nadie. Duplica
// el mismo sobre SOAP armado a mano que el SPI Java porque es el mismo
// WSDL viejo (nuSOAP, RPC/encoded) y no vale la pena una libreria
// SOAP completa para una sola operacion.
public class CusxacdiInfoClient
{
    private const string RecuperaInfoUrl = "https://cusxacdi.xoc.uam.mx/ws/RecuperaInfoCuenta_WS.php";
    private const string RecuperaInfoNs = "http://cusxacdi.xoc.uam.mx/soap/RecuperaInfoCuenta_WS";

    private readonly HttpClient _httpClient;

    public CusxacdiInfoClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public record CuentaInfo(
        string Username,
        string NombreCompleto,
        string Nombres,
        string Apellidos,
        string FechaVigencia,
        bool Habilitada
    );

    // Devuelve null si el servicio no responde o el formato no es el
    // esperado - nunca lanza hacia el caller, un fallo aca solo significa
    // que la vista de alta no tiene preview, nunca debe bloquear que el
    // coordinador de la alta a mano (mismo criterio que el SPI de
    // Keycloak, ver CusxacdiClient.java).
    public async Task<CuentaInfo?> RecuperaInfoAsync(string idUsuario, CancellationToken ct = default)
    {
        var body = BuildRpcEnvelope(RecuperaInfoNs, "RecuperaInfoCuenta", ("IdUsuario", idUsuario));

        try
        {
            var response = await PostAsync(RecuperaInfoUrl, $"{RecuperaInfoNs}/RecuperaInfoCuenta", body, ct);
            var raw = ExtractReturnValue(response);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            // Formato pipe-delimited del WSDL:
            // [0]=numeco [1]=nombre_completo [2]=nombres [3]=apellidos
            // [4]=password [5]=fecha_vigencia [6]=estado
            var fields = raw.Split('|');
            if (fields.Length < 7)
                return null;

            return new CuentaInfo(
                fields[0].Trim(),
                fields[1].Trim(),
                fields[2].Trim(),
                fields[3].Trim(),
                fields[5].Trim(),
                fields[6].Trim() == "1");
        }
        catch
        {
            return null;
        }
    }

    private static string BuildRpcEnvelope(string ns, string operation, params (string Name, string Value)[] parametros)
    {
        var args = new StringBuilder();
        foreach (var (name, value) in parametros)
        {
            args.Append('<').Append(name)
                .Append(" xsi:type=\"xsd:string\">")
                .Append(XmlEscape(value))
                .Append("</").Append(name).Append('>');
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<SOAP-ENV:Envelope"
            + " xmlns:SOAP-ENV=\"http://schemas.xmlsoap.org/soap/envelope/\""
            + " xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\""
            + " xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\""
            + " xmlns:SOAP-ENC=\"http://schemas.xmlsoap.org/soap/encoding/\""
            + " SOAP-ENV:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">"
            + "<SOAP-ENV:Body>"
            + $"<tns:{operation} xmlns:tns=\"{ns}\">"
            + args
            + $"</tns:{operation}>"
            + "</SOAP-ENV:Body>"
            + "</SOAP-ENV:Envelope>";
    }

    private async Task<string> PostAsync(string url, string soapAction, string body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/xml"),
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{soapAction}\"");

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        // Mismo gotcha que el SPI de Keycloak (ver CusxacdiClient.java):
        // el servidor declara "charset=ISO-8859-1" pero los bytes reales
        // de acentos son UTF-8 - se decodifica forzando UTF-8 sin
        // importar lo que el header declare.
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return Encoding.UTF8.GetString(bytes);
    }

    private static readonly Regex ReturnPattern = new(
        "<return[^>]*>(.*?)</return>", RegexOptions.Singleline | RegexOptions.Compiled);

    private static string? ExtractReturnValue(string soapResponse)
    {
        var match = ReturnPattern.Match(soapResponse);
        return match.Success ? XmlUnescape(match.Groups[1].Value) : null;
    }

    private static string XmlEscape(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");

    private static string XmlUnescape(string s) => s
        .Replace("&lt;", "<")
        .Replace("&gt;", ">")
        .Replace("&quot;", "\"")
        .Replace("&apos;", "'")
        .Replace("&amp;", "&");
}
