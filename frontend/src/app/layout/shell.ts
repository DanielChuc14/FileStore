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

  private readonly allItems: NavItem[] = [
    { route: '/dashboard', labelKey: 'nav.dashboard' },
    { route: '/admin/clients', labelKey: 'nav.clients', superAdminOnly: true },
    { route: '/files', labelKey: 'nav.files' },
    { route: '/trash', labelKey: 'nav.trash' },
    { route: '/api-keys', labelKey: 'nav.apiKeys' },
    { route: '/audit', labelKey: 'nav.audit' },
    { route: '/profile', labelKey: 'nav.profile' },
  ];

  protected get navItems(): NavItem[] {
    return this.allItems.filter((item) => !item.superAdminOnly || this.isSuperAdmin());
  }

  protected logout(): void {
    this.auth.logout().subscribe(() => void this.router.navigate(['/login']));
  }
}
