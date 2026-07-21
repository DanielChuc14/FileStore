import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';

import { AuthService } from '../core/auth/auth.service';

interface NavItem {
  route: string;
  labelKey: string;
  superAdminOnly?: boolean;
}

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  templateUrl: './shell.html',
})
export class Shell {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly user = this.auth.user;
  protected readonly isSuperAdmin = this.auth.isSuperAdmin;

  /**
   * El super-admin y el cliente ven menus distintos: el primero administra el
   * servicio y no tiene contenido propio, el segundo es al reves.
   */
  private readonly clientItems: NavItem[] = [
    { route: '/dashboard', labelKey: 'nav.dashboard' },
    { route: '/files', labelKey: 'nav.files' },
    { route: '/trash', labelKey: 'nav.trash' },
    { route: '/api-keys', labelKey: 'nav.apiKeys' },
    { route: '/audit', labelKey: 'nav.audit' },
    { route: '/profile', labelKey: 'nav.profile' },
  ];

  private readonly adminItems: NavItem[] = [
    { route: '/admin/overview', labelKey: 'nav.overview', superAdminOnly: true },
    { route: '/admin/clients', labelKey: 'nav.clients', superAdminOnly: true },
    { route: '/admin/settings', labelKey: 'nav.settings', superAdminOnly: true },
  ];

  protected get navItems(): NavItem[] {
    return this.isSuperAdmin() ? this.adminItems : this.clientItems;
  }

  protected logout(): void {
    this.auth.logout().subscribe(() => void this.router.navigate(['/login']));
  }
}
