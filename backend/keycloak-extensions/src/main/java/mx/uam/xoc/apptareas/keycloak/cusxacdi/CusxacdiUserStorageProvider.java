package mx.uam.xoc.apptareas.keycloak.cusxacdi;

import org.keycloak.component.ComponentModel;
import org.keycloak.credential.CredentialInput;
import org.keycloak.credential.CredentialInputValidator;
import org.keycloak.models.GroupModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.models.credential.PasswordCredentialModel;
import org.keycloak.storage.StorageId;
import org.keycloak.storage.UserStorageProvider;
import org.keycloak.storage.user.UserLookupProvider;

import java.util.logging.Logger;

/**
 * Autentica contra CUSXACDI (SOAP institucional, ver CusxacdiClient) en
 * vez de contra una User Federation LDAP: no hay LDAP directamente
 * consultable, CUSXACDI es el unico punto de acceso que expone la UAM
 * para esto (ver README del modulo).
 *
 * No persiste usuarios en la tabla local de Keycloak (no hay
 * ImportedUserValidation): cada login construye un UserModel "delegado"
 * al vuelo con CusxacdiUserAdapter. Mas simple que federacion con import,
 * y correcto aqui porque CUSXACDI ya es la fuente de verdad completa -
 * no hace falta cachear nada localmente para que el login funcione.
 *
 * Correo/area (que CUSXACDI no expone) se completan aparte, ver
 * CusxacdiUserAdapter y el pendiente de la consulta a
 * info_usuarios_unidad (MySQL) documentado en el README.
 */
public class CusxacdiUserStorageProvider implements
        UserStorageProvider,
        UserLookupProvider,
        CredentialInputValidator {

    private static final Logger LOG = Logger.getLogger(CusxacdiUserStorageProvider.class.getName());

    private final KeycloakSession session;
    private final ComponentModel model;
    private final CusxacdiClient cusxacdi;

    public CusxacdiUserStorageProvider(KeycloakSession session, ComponentModel model) {
        this.session = session;
        this.model = model;
        this.cusxacdi = new CusxacdiClient();
    }

    // -------------------------------------------------------------------
    // UserLookupProvider
    // -------------------------------------------------------------------

    @Override
    public UserModel getUserById(RealmModel realm, String id) {
        String username = StorageId.externalId(id);
        return getUserByUsername(realm, username);
    }

    @Override
    public UserModel getUserByUsername(RealmModel realm, String username) {
        // No se valida contra CUSXACDI aqui (ValidaExisteCuenta_WS existe
        // en el WSDL pero no vale la pena una llamada extra solo para
        // "existe"): Keycloak llama a este metodo durante el flujo de
        // login para construir el UserModel ANTES de validar la
        // credencial (isValid() abajo) - si el password resulta
        // incorrecto, ese segundo paso rechaza el login igual. Rechazar
        // alumnos si aplica desde aqui mismo evita construir un
        // UserModel para una cuenta que nunca deberia poder entrar.
        if (MatriculaUtil.esAlumno(username)) {
            return null;
        }
        return new CusxacdiUserAdapter(session, realm, model, username, cusxacdi);
    }

    @Override
    public UserModel getUserByEmail(RealmModel realm, String email) {
        // No hay forma de buscar por correo en CUSXACDI (solo por
        // matricula/numero economico) - no aplica para este provider.
        return null;
    }

    // -------------------------------------------------------------------
    // CredentialInputValidator
    // -------------------------------------------------------------------

    @Override
    public boolean supportsCredentialType(String credentialType) {
        return PasswordCredentialModel.TYPE.equals(credentialType);
    }

    @Override
    public boolean isConfiguredFor(RealmModel realm, UserModel user, String credentialType) {
        return supportsCredentialType(credentialType);
    }

    @Override
    public boolean isValid(RealmModel realm, UserModel user, CredentialInput input) {
        if (!supportsCredentialType(input.getType())) {
            return false;
        }
        String nip = input.getChallengeResponse();
        if (nip == null || nip.isBlank()) {
            return false;
        }

        boolean valido = cusxacdi.validaAcceso(user.getUsername(), nip);
        if (!valido) {
            LOG.info("Login CUSXACDI rechazado para " + user.getUsername());
        }
        return valido;
    }

    // -------------------------------------------------------------------
    // UserStorageProvider
    // -------------------------------------------------------------------

    @Override
    public void close() {
        // CusxacdiClient no mantiene conexiones persistentes (HttpClient
        // por request), nada que cerrar.
    }
}
