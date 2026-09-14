package mx.uam.xoc.apptareas.keycloak.cusxacdi;

import org.keycloak.component.ComponentModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.provider.ProviderConfigProperty;
import org.keycloak.storage.UserStorageProviderFactory;

import java.util.ArrayList;
import java.util.List;

/**
 * Registra el provider en Keycloak. El "Provider ID" (id()) es el nombre
 * que aparece en Admin Console -> User federation -> Add provider.
 */
public class CusxacdiUserStorageProviderFactory implements UserStorageProviderFactory<CusxacdiUserStorageProvider> {

    public static final String PROVIDER_ID = "cusxacdi-uamx";

    @Override
    public CusxacdiUserStorageProvider create(KeycloakSession session, ComponentModel model) {
        return new CusxacdiUserStorageProvider(session, model);
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getHelpText() {
        return "Autentica contra el servicio SOAP institucional CUSXACDI "
                + "(UAM Xochimilco) en vez de una User Federation LDAP "
                + "estandar - ver docker/keycloak/README.md.";
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        // Sin configuracion por ahora: las URLs de CUSXACDI estan fijas
        // en CusxacdiClient (son constantes institucionales, no varian
        // por ambiente). Si eso cambia, aqui es donde se agregarian
        // campos editables desde la Admin Console.
        return new ArrayList<>();
    }
}
