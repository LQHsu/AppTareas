import { Injectable, signal } from '@angular/core';
import Keycloak from 'keycloak-js';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private keycloak = new Keycloak({
    url: 'https://appcafeteria.xoc.uam.mx/auth-server',
    realm: 'cafeteria-uam',
    clientId: 'task-manager-uamx',
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

  getToken(): string | undefined {
    return environment.useMockAuth ? this.mockToken : this.keycloak.token;
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
}