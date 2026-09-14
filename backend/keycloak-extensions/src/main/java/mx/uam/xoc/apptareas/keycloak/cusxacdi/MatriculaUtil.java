package mx.uam.xoc.apptareas.keycloak.cusxacdi;

import java.util.regex.Pattern;

/**
 * Replica AuthModel::isAlumno() del backend PHP de appcafeteria: las
 * matriculas de alumnos UAM son 10 digitos (ej. 2183012345), y este
 * sistema es exclusivo para trabajadores - un numero economico de
 * trabajador no sigue ese patron.
 *
 * A diferencia del PHP (que lee "auth.alumno_pattern" de .env), aqui el
 * patron queda fijo en codigo: no hay necesidad real de configurarlo por
 * ambiente (dev/prod comparten el mismo formato institucional), y
 * mantenerlo fijo evita un config mal puesto que abra la puerta a
 * alumnos por accidente.
 */
final class MatriculaUtil {

    private static final Pattern ALUMNO_PATTERN = Pattern.compile("^\\d{10}$");

    private MatriculaUtil() {
    }

    static boolean esAlumno(String idUsuario) {
        return idUsuario != null && ALUMNO_PATTERN.matcher(idUsuario).matches();
    }
}
