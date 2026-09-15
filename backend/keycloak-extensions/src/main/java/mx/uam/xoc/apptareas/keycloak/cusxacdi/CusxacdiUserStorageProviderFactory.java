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

    // Nombres de config, compartidos con CusxacdiUserStorageProvider para
    // leer el ComponentModel sin repetir los literales.
    public static final String CONFIG_MYSQL_JDBC_URL = "mysqlJdbcUrl";
    public static final String CONFIG_MYSQL_USER = "mysqlUser";
    public static final String CONFIG_MYSQL_PASSWORD = "mysqlPassword";

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        // Las URLs de CUSXACDI (SOAP) siguen fijas en CusxacdiClient - son
        // constantes institucionales, no varian por ambiente. La BD MySQL
        // de correo/area (info_usuarios_unidad) SI se configura aqui: son
        // credenciales reales, no deben quedar hardcodeadas en el jar.
        List<ProviderConfigProperty> props = new ArrayList<>();

        ProviderConfigProperty jdbcUrl = new ProviderConfigProperty();
        jdbcUrl.setName(CONFIG_MYSQL_JDBC_URL);
        jdbcUrl.setLabel("MySQL JDBC URL");
        jdbcUrl.setType(ProviderConfigProperty.STRING_TYPE);
        jdbcUrl.setHelpText("Ej: jdbc:mysql://148.206.99.178:3306/info_usuarios_unidad");
        props.add(jdbcUrl);

        ProviderConfigProperty user = new ProviderConfigProperty();
        user.setName(CONFIG_MYSQL_USER);
        user.setLabel("MySQL Usuario");
        user.setType(ProviderConfigProperty.STRING_TYPE);
        props.add(user);

        ProviderConfigProperty password = new ProviderConfigProperty();
        password.setName(CONFIG_MYSQL_PASSWORD);
        password.setLabel("MySQL Password");
        password.setType(ProviderConfigProperty.PASSWORD);
        props.add(password);

        return props;
    }
}
