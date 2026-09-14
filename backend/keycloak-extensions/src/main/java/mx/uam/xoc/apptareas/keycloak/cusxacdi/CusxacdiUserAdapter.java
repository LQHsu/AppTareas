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
 * PENDIENTE (Fase 3.2, requiere credenciales de info_usuarios_unidad):
 * setEmailVerified()/atributo EMAIL_ATTRIBUTE y el atributo "area" deben
 * poblarse aqui con un SELECT a esa BD por matricula. Mientras tanto el
 * correo queda vacio - el Audience Mapper y el login en si YA funcionan
 * sin esto, pero el frontend de AppTareas no podra autocompletar
 * correo/area en completar-registro hasta que se resuelva.
 */
public class CusxacdiUserAdapter extends AbstractUserAdapterFederatedStorage {

    private final String username;

    public CusxacdiUserAdapter(KeycloakSession session, RealmModel realm, ComponentModel model,
                                String username, CusxacdiClient cusxacdi) {
        super(session, realm, model);
        this.username = username;

        CusxacdiClient.CuentaInfo cuentaInfo = cusxacdi.recuperaInfo(username);
        if (cuentaInfo != null) {
            setSingleAttribute("nombreCompleto", cuentaInfo.nombreCompleto());
            setSingleAttribute(UserModel.FIRST_NAME, cuentaInfo.nombres());
            setSingleAttribute(UserModel.LAST_NAME, cuentaInfo.apellidos());
            setEnabled(cuentaInfo.habilitada());
        }

        // TODO Fase 3.2: SELECT correo/area a info_usuarios_unidad (MySQL,
        // 148.206.99.178) por "username" y setSingleAttribute(UserModel.EMAIL, ...) /
        // setSingleAttribute("area", ...) aqui.
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
