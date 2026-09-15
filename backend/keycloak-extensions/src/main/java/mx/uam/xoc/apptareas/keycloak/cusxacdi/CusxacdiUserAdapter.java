package mx.uam.xoc.apptareas.keycloak.cusxacdi;

import org.keycloak.component.ComponentModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.storage.adapter.AbstractUserAdapterFederatedStorage;

/**
 * UserModel "en memoria" para un usuario resuelto via CUSXACDI - no hay
 * fila propia en la tabla de usuarios de Keycloak (ver
 * CusxacdiUserStorageProvider). AbstractUserAdapterFederatedStorage da
 * los atributos custom/roles/grupos "gratis" via el federated storage de
 * Keycloak (necesario para que, por ejemplo, el Protocol Mapper de
 * "area" tenga de donde leer una vez que se agregue la Fase 3.2).
 *
 * Nombre completo/nombres/apellidos vienen de RecuperaInfoCuenta_WS
 * (llamada una vez por login, aqui en el constructor - no hay cache: es
 * un objeto que vive solo durante ese login).
 *
 * Nota de API: en esta version de Keycloak (26.x),
 * AbstractUserAdapterFederatedStorage NO tiene setUsername/setFirstName/
 * setLastName propios (confirmado inspeccionando el bytecode real del
 * jar, no asumido de docs de versiones viejas) - username/nombre/
 * apellido se manejan como atributos via setSingleAttribute() +
 * getFirstAttribute(), igual que "area" mas adelante. getId() tampoco se
 * sobreescribe: la clase base ya lo resuelve con el StorageId interno
 * que arma su propio constructor.
 *
 * GOTCHA REAL (ya causo un StackOverflowError en produccion local, dejar
 * documentado): setSingleAttribute(UserModel.USERNAME, ...) en la clase
 * base internamente llama a setUsername() para mantener consistencia -
 * si aqui setUsername() a su vez llama a
 * setSingleAttribute(UserModel.USERNAME, ...), es una recursion infinita
 * entre ambos metodos. username se guarda SOLO en el campo final
 * `username` (fijado una vez en el constructor, este adapter es
 * inmutable de todas formas - no hay flujo real que llame a
 * setUsername() en este provider), nunca se le hace setSingleAttribute
 * con la key USERNAME.
 *
 * Fase 3.2: correo y area vienen de info_usuarios_unidad (MySQL, ver
 * InfoUsuariosUnidadClient) por numero economico/matricula - CUSXACDI no
 * expone ninguno de los dos. El area queda como atributo custom "area"
 * (no hay concepto nativo de "area" en UserModel) - el Protocol Mapper
 * del realm lo expone como claim en el token; el backend .NET la
 * resuelve/crea en su tabla local Area por nombre (no hay Area.Id aqui,
 * los IDs son propios de AppTareas).
 */
public class CusxacdiUserAdapter extends AbstractUserAdapterFederatedStorage {

    private final String username;

    public CusxacdiUserAdapter(KeycloakSession session, RealmModel realm, ComponentModel model,
                                String username, CusxacdiClient cusxacdi,
                                InfoUsuariosUnidadClient infoUsuariosUnidad) {
        super(session, realm, model);
        this.username = username;

        CusxacdiClient.CuentaInfo cuentaInfo = cusxacdi.recuperaInfo(username);
        if (cuentaInfo != null) {
            setSingleAttribute("nombreCompleto", cuentaInfo.nombreCompleto());
            setSingleAttribute(UserModel.FIRST_NAME, cuentaInfo.nombres());
            setSingleAttribute(UserModel.LAST_NAME, cuentaInfo.apellidos());
            setEnabled(cuentaInfo.habilitada());
        }

        // Sin fallback a un email inventado: si info_usuarios_unidad no
        // tiene el dato (no configurado, matricula sin registro ahi,
        // BD caida), el atributo simplemente no se setea y
        // completar-registro en el frontend lo deja vacio para captura
        // manual, como ya hacia antes de esto.
        InfoUsuariosUnidadClient.DatosInstitucionales datos = infoUsuariosUnidad.buscar(username);
        if (datos != null) {
            if (datos.email() != null && !datos.email().isBlank()) {
                setSingleAttribute(UserModel.EMAIL, datos.email());
                setEmailVerified(true);
            }
            if (datos.area() != null && !datos.area().isBlank()) {
                setSingleAttribute("area", datos.area());
            }
        }
    }

    @Override
    public String getUsername() {
        return username;
    }

    @Override
    public void setUsername(String username) {
        // No-op intencional: este adapter es de solo-lectura respecto al
        // username (viene fijo de CUSXACDI). Ver el gotcha documentado
        // arriba - NUNCA llamar setSingleAttribute(UserModel.USERNAME, ...)
        // desde aqui, causa recursion infinita con la clase base.
    }
}
