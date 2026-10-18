import { Component, inject } from '@angular/core';
import { Notification } from './services/notification';

@Component({
  selector: 'app-notification',
  template: `
    @if (notification.message(); as message) {
      <div [class]="'notification notification-' + message.type" [attr.role]="message.type === 'success' ? 'status' : 'alert'">
        <span>{{ message.text }}</span>
        <button type="button" class="notification-close" aria-label="Cerrar aviso" (click)="notification.clear()">×</button>
      </div>
    }
  `,
})
export class NotificationBanner {
  protected readonly notification = inject(Notification);
}
