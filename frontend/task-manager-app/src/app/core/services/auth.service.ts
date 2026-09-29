import { Injectable, signal } from '@angular/core';
import Keycloak from 'keycloak-js';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private keycloak = new Keycloak({
    url: environment.keycloak.url,
    realm: environment.keycloak.realm,
    clientId: environment.keycloak.clientId,
  });

  // Token falso en base64, consumido por MockAuthHandler en el backend
  // cuando "Keycloak:MockAuth" esta activo alla. Ver environment.ts.
  private readonly mockToken =
    'mock.' + btoa(JSON.stringify(environment.mockUser));

  readonly isAuthenticated = signal(false);
  readonly userName = signal<string | null>(null);

  async init(): Promise<boolean> {
    if (environment.useMockAuth) {
      console.warn(
        '[AuthService] Modo MOCK activo: no se esta usando Keycloak. ' +
          'Usuario simulado:',
        environment.mockUser,
      );
      this.isAuthenticated.set(true);
      this.userName.set(environment.mockUser.name);
      return true;
    }

    const authenticated = await this.keycloak.init({
      onLoad: 'check-sso',
      silentCheckSsoRedirectUri: window.location.origin + '/silent-check-sso.html',
      silentCheckSsoFallback: false,
      checkLoginIframe: false,
      pkceMethod: 'S256',
    });

    this.isAuthenticated.set(authenticated);
    if (authenticated) {
      this.userName.set(this.keycloak.tokenParsed?.['name'] ?? null);
    }

    return authenticated;
  }

  login(): void {
    if (environment.useMockAuth) {
      return;
    }
    this.keycloak.login();
  }

  logout(): void {
    if (environment.useMockAuth) {
      this.isAuthenticated.set(false);
      return;
    }
    this.keycloak.logout({ redirectUri: window.location.origin });
  }

  // Async a proposito: keycloak-js NUNCA refresca el access token solo,
  // asi que sin esto cada request seguiria mandando el mismo JWT desde
  // el login hasta que expirara (accessTokenLifespan = 300s en el
  // realm) - a partir de ahi el backend rechaza todo con 401 y la app
  // se queda "sin poder cargar nada" hasta un F5 (que vuelve a
  // autenticar desde cero via check-sso). updateToken(30) refresca solo
  // si al token le quedan menos de 30s de vida; si ya esta vigente no
  // hace ninguna llamada de red.
  async getToken(): Promise<string> {
    if (environment.useMockAuth) return this.mockToken;

    try {
      await this.keycloak.updateToken(30);
    } catch {
      // El refresh token tambien expiro (idle timeout / maximo de
      // sesion en el realm) - no hay nada que refrescar, hay que
      // volver a loguear.
      this.keycloak.login();
      return '';
    }

    return this.keycloak.token ?? '';
  }

  getUserId(): string | undefined {
    return environment.useMockAuth ? environment.mockUser.sub : this.keycloak.tokenParsed?.['sub'];
  }

  getEmail(): string | undefined {
    return environment.useMockAuth ? environment.mockUser.email : this.keycloak.tokenParsed?.['email'];
  }

  getUsername(): string | undefined {
    return environment.useMockAuth
      ? environment.mockUser.preferred_username
      : this.keycloak.tokenParsed?.['preferred_username'];
  }

  getFullName(): string | undefined {
    return environment.useMockAuth ? environment.mockUser.name : this.keycloak.tokenParsed?.['name'];
  }

  // Claim custom "area" (Protocol Mapper del realm apptareas), resuelto
  // por el SPI de Keycloak desde info_usuarios_unidad - ver
  // docker/keycloak/README.md. undefined si Keycloak no pudo resolverla
  // (matricula sin registro institucional, BD caida) - completar-registro
  // cae al comportamiento anterior (el usuario elige a mano) en ese caso.
  getArea(): string | undefined {
    return environment.useMockAuth ? undefined : this.keycloak.tokenParsed?.['area'];
  }
}