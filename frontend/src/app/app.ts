import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';

interface NavItem {
  route: string;
  labelKey: string;
}

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly navItems: NavItem[] = [
    { route: '/dashboard', labelKey: 'nav.dashboard' },
    { route: '/files', labelKey: 'nav.files' },
    { route: '/trash', labelKey: 'nav.trash' },
    { route: '/api-keys', labelKey: 'nav.apiKeys' },
    { route: '/audit', labelKey: 'nav.audit' },
    { route: '/profile', labelKey: 'nav.profile' },
  ];
}
