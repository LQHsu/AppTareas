export const environment = {
  production: false,
  apiUrl: '/api',

  // Keycloak propio en Docker (docker/keycloak/), realm "apptareas" -
  // reemplaza al Keycloak compartido appcafeteria/cafeteria-uam. Ver
  // docker/keycloak/README.md.
  keycloak: {
    url: 'http://localhost:8080',
    realm: 'apptareas',
    clientId: 'task-manager-uamx',
  },

  // Modo mock: cuando Keycloak no esta disponible (ej. servidor caido),
  // pon esto en `true` para saltarte el login SSO por completo y
  // trabajar con un usuario simulado. El backend debe tener
  // "Keycloak:MockAuth": true en su appsettings.Development.json para
  // aceptar el token falso que esto genera (ver AuthService y
  // MockAuthHandler). NUNCA dejar esto en `true` en produccion.
  useMockAuth: false,
  mockUser: {
    sub: '11111111-1111-1111-1111-111111111111',
    email: 'dev.mock@xoc.uam.mx',
    preferred_username: 'dev.mock',
    name: 'Usuario Mock',
  },
};