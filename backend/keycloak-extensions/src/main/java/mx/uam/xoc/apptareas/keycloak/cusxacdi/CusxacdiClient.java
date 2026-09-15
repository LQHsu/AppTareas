package mx.uam.xoc.apptareas.keycloak.cusxacdi;

import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.logging.Logger;

/**
 * Cliente para los web services SOAP institucionales de CUSXACDI
 * (cusxacdi.xoc.uam.mx). Es SOAP 1.1 estilo RPC/encoded (WSDL viejo,
 * generado con nuSOAP del lado PHP) - las librerias Java modernas
 * (JAX-WS/CXF) estan pensadas para document/literal y no valen la pena
 * para dos operaciones tan simples, asi que el sobre XML se arma a mano.
 *
 * Replica el mismo flujo que ya usa el backend PHP de appcafeteria
 * (AuthModel::soapAuthenticate): ValidaAccesoCuenta para autenticar,
 * RecuperaInfoCuenta para el nombre completo.
 */
public class CusxacdiClient {

    private static final Logger LOG = Logger.getLogger(CusxacdiClient.class.getName());

    private static final String VALIDA_ACCESO_URL =
            "https://cusxacdi.xoc.uam.mx/ws/ValidaAccesoCuenta_WS.php";
    private static final String VALIDA_ACCESO_NS =
            "http://cusxacdi.xoc.uam.mx/soap/ValidaAccesoCuenta_WS";
    private static final String RECUPERA_INFO_URL =
            "https://cusxacdi.xoc.uam.mx/ws/RecuperaInfoCuenta_WS.php";
    private static final String RECUPERA_INFO_NS =
            "http://cusxacdi.xoc.uam.mx/soap/RecuperaInfoCuenta_WS";

    private final HttpClient httpClient;

    public CusxacdiClient() {
        this.httpClient = HttpClient.newBuilder()
                .connectTimeout(Duration.ofSeconds(10))
                .build();
    }

    /**
     * Llama a ValidaAccesoCuenta. Devuelve true solo si el servicio
     * regresa exactamente 1 (segun el WSDL: "1 = Datos correctos, Vacio =
     * Datos incorrectos" - un fallo de red o una respuesta inesperada
     * tambien cuentan como credenciales invalidas, nunca como excepcion
     * que tumbe el login).
     */
    public boolean validaAcceso(String idUsuario, String password) {
        String body = buildRpcEnvelope(
                VALIDA_ACCESO_NS,
                "ValidaAccesoCuenta",
                new String[] { "IdUsuario", idUsuario },
                new String[] { "Password", password }
        );

        try {
            String response = post(VALIDA_ACCESO_URL,
                    VALIDA_ACCESO_NS + "/ValidaAccesoCuenta", body);
            String returnValue = extractReturnValue(response);
            return "1".equals(returnValue == null ? null : returnValue.trim());
        } catch (Exception e) {
            LOG.warning("CUSXACDI ValidaAccesoCuenta fallo para usuario "
                    + idUsuario + ": " + e.getMessage());
            return false;
        }
    }

    /**
     * Llama a RecuperaInfoCuenta y parte la respuesta pipe-delimited,
     * igual formato que ya documenta el WSDL y ya consume el AuthModel.php
     * existente:
     *   [0]=numeco/username [1]=nombre_completo [2]=nombres [3]=apellidos
     *   [4]=password [5]=fecha_vigencia [6]=estado
     *
     * Devuelve null si el servicio no responde o el formato no es el
     * esperado (nunca lanza hacia el caller - la autenticacion ya paso en
     * validaAcceso(), no tiene sentido tumbar el login por esto; el SPI
     * decide que hacer con campos faltantes).
     */
    public CuentaInfo recuperaInfo(String idUsuario) {
        String requestBody = buildRpcEnvelope(
                RECUPERA_INFO_NS,
                "RecuperaInfoCuenta",
                new String[] { "IdUsuario", idUsuario }
        );

        try {
            String response = post(RECUPERA_INFO_URL,
                    RECUPERA_INFO_NS + "/RecuperaInfoCuenta", requestBody);
            String raw = extractReturnValue(response);
            if (raw == null || raw.isBlank()) {
                return null;
            }

            String[] fields = raw.split("\\|", -1);
            if (fields.length < 7) {
                LOG.warning("CUSXACDI RecuperaInfoCuenta: formato inesperado para "
                        + idUsuario + " (" + fields.length + " campos)");
                return null;
            }

            return new CuentaInfo(
                    fields[0].trim(),  // matricula/username
                    fields[1].trim(),  // nombre completo
                    fields[2].trim(),  // nombres
                    fields[3].trim(),  // apellidos
                    fields[5].trim(),  // fecha vigencia (dd-mm-aaaa)
                    "1".equals(fields[6].trim())  // estado habilitado
            );
        } catch (Exception e) {
            LOG.warning("CUSXACDI RecuperaInfoCuenta fallo para usuario "
                    + idUsuario + ": " + e.getMessage());
            return null;
        }
    }

    // -------------------------------------------------------------------
    // SOAP 1.1 RPC/encoded a mano
    // -------------------------------------------------------------------

    /** params: pares {nombre, valor} en el orden del WSDL. */
    private String buildRpcEnvelope(String ns, String operation, String[]... params) {
        StringBuilder args = new StringBuilder();
        for (String[] p : params) {
            String name = p[0];
            String value = xmlEscape(p[1]);
            args.append("<").append(name)
                    .append(" xsi:type=\"xsd:string\">")
                    .append(value)
                    .append("</").append(name).append(">");
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
                + "<SOAP-ENV:Envelope"
                + " xmlns:SOAP-ENV=\"http://schemas.xmlsoap.org/soap/envelope/\""
                + " xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\""
                + " xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\""
                + " xmlns:SOAP-ENC=\"http://schemas.xmlsoap.org/soap/encoding/\""
                + " SOAP-ENV:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">"
                + "<SOAP-ENV:Body>"
                + "<tns:" + operation + " xmlns:tns=\"" + ns + "\">"
                + args
                + "</tns:" + operation + ">"
                + "</SOAP-ENV:Body>"
                + "</SOAP-ENV:Envelope>";
    }

    private String post(String url, String soapAction, String body) throws IOException, InterruptedException {
        HttpRequest request = HttpRequest.newBuilder()
                .uri(URI.create(url))
                .timeout(Duration.ofSeconds(15))
                .header("Content-Type", "text/xml; charset=utf-8")
                .header("SOAPAction", "\"" + soapAction + "\"")
                .POST(HttpRequest.BodyPublishers.ofString(body, java.nio.charset.StandardCharsets.UTF_8))
                .build();

        // GOTCHA REAL (confirmado inspeccionando los BYTES crudos de la
        // respuesta con curl, no solo el header): el servidor de CUSXACDI
        // miente en su propio Content-Type - dice "charset=ISO-8859-1"
        // (y el WSDL tambien declara ISO-8859-1 en su XML declaration),
        // pero los bytes reales para 'Ñ' son C3 91, que es UTF-8, no
        // ISO-8859-1. Confiar en el header (o en el WSDL) corrompe todo
        // nombre con acentos ("QUIÑONES" -> "QUIÃONES"). Se fuerza UTF-8
        // sin importar lo que el servidor declare.
        HttpResponse<String> response = httpClient.send(request,
                HttpResponse.BodyHandlers.ofString(java.nio.charset.StandardCharsets.UTF_8));
        if (response.statusCode() != 200) {
            throw new IOException("HTTP " + response.statusCode() + " de CUSXACDI");
        }
        return response.body();
    }

    // La respuesta trae <return xsi:type="xsd:...">valor</return> en
    // algun punto del sobre - basta con un regex simple, no hace falta un
    // parser XML completo para un solo campo.
    private static final Pattern RETURN_PATTERN = Pattern.compile(
            "<return[^>]*>(.*?)</return>", Pattern.DOTALL);

    private String extractReturnValue(String soapResponse) {
        Matcher m = RETURN_PATTERN.matcher(soapResponse);
        if (!m.find()) {
            return null;
        }
        return xmlUnescape(m.group(1));
    }

    private String xmlEscape(String s) {
        return s.replace("&", "&amp;")
                .replace("<", "&lt;")
                .replace(">", "&gt;")
                .replace("\"", "&quot;")
                .replace("'", "&apos;");
    }

    private String xmlUnescape(String s) {
        return s.replace("&lt;", "<")
                .replace("&gt;", ">")
                .replace("&quot;", "\"")
                .replace("&apos;", "'")
                .replace("&amp;", "&");
    }

    /** Datos de RecuperaInfoCuenta ya parseados. */
    public record CuentaInfo(
            String username,
            String nombreCompleto,
            String nombres,
            String apellidos,
            String fechaVigencia,
            boolean habilitada
    ) {}
}
