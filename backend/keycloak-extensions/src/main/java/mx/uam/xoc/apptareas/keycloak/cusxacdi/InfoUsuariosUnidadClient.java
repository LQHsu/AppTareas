package mx.uam.xoc.apptareas.keycloak.cusxacdi;

import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.util.logging.Logger;

/**
 * Consulta la BD institucional info_usuarios_unidad (MySQL,
 * 148.206.99.178) para correo y area - CUSXACDI (SOAP) no expone
 * ninguno de los dos. Esquema real confirmado a mano (sin FKs
 * declaradas, tipico de esta BD - MyISAM ni las soporta):
 *
 *   CE_Correo_Empleados (CE_NumEconomico PK) -> CE_Email
 *   Empleados (NumeroEconomico PK) -> Pagaduria
 *   Adscripciones (ClaveAdscripcion PK) -> NombreAdscripcion2
 *
 *   Empleados.Pagaduria == Adscripciones.ClaveAdscripcion (no es FK
 *   declarada en el schema, pero el valor coincide 1:1 - confirmado con
 *   datos reales, ej. Pagaduria=36601 -> "COORD. SERVICIOS DE COMPUTO").
 *
 * NombreAdscripcion2 es el nivel de jerarquia elegido para "area" (ver
 * docker/keycloak/README.md): ni tan general como NombreAdscripcion1
 * (la Secretaria completa) ni tan especifico como
 * NombreSeccionOficinaArea (la oficina puntual).
 */
public class InfoUsuariosUnidadClient {

    private static final Logger LOG = Logger.getLogger(InfoUsuariosUnidadClient.class.getName());

    private final String jdbcUrl;
    private final String user;
    private final String password;

    public InfoUsuariosUnidadClient(String jdbcUrl, String user, String password) {
        this.jdbcUrl = jdbcUrl;
        this.user = user;
        this.password = password;
    }

    /** true si esta configurado (los 3 campos del provider tienen valor). */
    public boolean isConfigured() {
        return jdbcUrl != null && !jdbcUrl.isBlank()
                && user != null && !user.isBlank();
    }

    public record DatosInstitucionales(String email, String area) {}

    /**
     * Devuelve null si no esta configurado, si la matricula no tiene
     * registro en alguna de las 2 tablas, o si la consulta falla - un
     * problema aqui nunca debe tumbar el login (ya paso por CUSXACDI
     * antes de llegar hasta este punto), solo significa que el usuario
     * completa correo/area a mano como hasta ahora.
     */
    public DatosInstitucionales buscar(String numeroEconomico) {
        if (!isConfigured()) {
            return null;
        }

        // GOTCHA REAL (confirmado con "SHOW VARIABLES LIKE
        // 'character_set%'" contra el servidor real): character_set_client/
        // connection/results del SERVIDOR MySQL son "latin1" por default,
        // aunque character_set_database sea "utf8" - los datos SI estan en
        // UTF-8 dentro de la BD, pero MySQL los sirve mal-decodificados a
        // menos que la sesion pida explicitamente utf8 (doble encoding:
        // "QUI?ONES" en vez de "QUIÑONES"). characterEncoding=UTF-8 solo
        // le dice al DRIVER que charset usar para decodificar bytes, no
        // cambia lo que el servidor le manda - hace falta ademas
        // useUnicode=true (fuerza al driver a mandar "SET NAMES utf8" al
        // conectar) para que el servidor tambien cambie de sesion.
        String url = jdbcUrl.contains("?")
                ? jdbcUrl + "&useUnicode=true&characterEncoding=UTF-8&connectTimeout=5000"
                : jdbcUrl + "?useUnicode=true&characterEncoding=UTF-8&connectTimeout=5000";

        try (Connection conn = DriverManager.getConnection(url, user, password)) {
            String email = buscarEmail(conn, numeroEconomico);
            String area = buscarArea(conn, numeroEconomico);
            if (email == null && area == null) {
                return null;
            }
            return new DatosInstitucionales(email, area);
        } catch (SQLException e) {
            LOG.warning("info_usuarios_unidad: fallo la consulta para "
                    + numeroEconomico + ": " + e.getMessage());
            return null;
        }
    }

    private String buscarEmail(Connection conn, String numeroEconomico) throws SQLException {
        String sql = "SELECT CE_Email FROM CE_Correo_Empleados WHERE CE_NumEconomico = ?";
        try (PreparedStatement ps = conn.prepareStatement(sql)) {
            ps.setString(1, numeroEconomico);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? rs.getString("CE_Email") : null;
            }
        }
    }

    private String buscarArea(Connection conn, String numeroEconomico) throws SQLException {
        String sql = "SELECT a.NombreAdscripcion2 "
                + "FROM Empleados e "
                + "JOIN Adscripciones a ON a.ClaveAdscripcion = e.Pagaduria "
                + "WHERE e.NumeroEconomico = ?";
        try (PreparedStatement ps = conn.prepareStatement(sql)) {
            ps.setString(1, numeroEconomico);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? rs.getString("NombreAdscripcion2") : null;
            }
        }
    }
}
