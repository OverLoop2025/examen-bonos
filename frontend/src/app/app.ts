import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NotificationBanner } from './core/notification-banner';

@Component({
  imports: [RouterOutlet, NotificationBanner],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {}
