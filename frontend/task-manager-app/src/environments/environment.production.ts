export const environment = {
  production: true,
  apiUrl: '/api',

  // Keycloak propio en Docker (docker/keycloak/), realm "apptareas",
  // detras de Apache (docker/keycloak/README.md). Sirve bajo /auth-server
  // (KC_HTTP_RELATIVE_PATH en docker-compose.yml) - keycloak-js arma
  // "<url>/realms/<realm>/..." solo, asi que ese path va incluido aqui.
  keycloak: {
    url: 'https://apptareas.xoc.uam.mx/auth-server',
    realm: 'apptareas',
    clientId: 'task-manager-uamx',
  },

  // NUNCA en true en produccion - ver environment.ts para el detalle del
  // modo mock. Fijo en false aqui a proposito (sin bandera de "por si
  // acaso"): este archivo es exactamente lo que corre en produccion real.
  useMockAuth: false,
  mockUser: {
    sub: '',
    email: '',
    preferred_username: '',
    name: '',
  },
};
