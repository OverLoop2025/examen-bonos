import { Injectable, signal } from '@angular/core';

export type NotificationType = 'error' | 'warning' | 'success';

export interface NotificationMessage {
  type: NotificationType;
  text: string;
}

/** Aviso global que se muestra en la parte superior de la aplicación. */
@Injectable({ providedIn: 'root' })
export class Notification {
  readonly message = signal<NotificationMessage | null>(null);
  private timer?: ReturnType<typeof setTimeout>;

  show(type: NotificationType, text: string): void {
    clearTimeout(this.timer);
    this.message.set({ type, text });
    // Los avisos de éxito se ocultan solos; los errores esperan a que el usuario los cierre.
    if (type === 'success') this.timer = setTimeout(() => this.clear(), 5000);
  }

  clear(): void {
    clearTimeout(this.timer);
    this.message.set(null);
  }
}
