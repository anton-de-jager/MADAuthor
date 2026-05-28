import { CommonModule } from '@angular/common';
import { Component, Input, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { BookDetail } from '../../core/api/books.api';
import { MadCloudTaskService, MadCloudWorkflowArea } from '../../core/api/madcloud-task.service';

@Component({
  selector: 'app-book-action-nav',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  template: `
    <nav class="book-action-nav" aria-label="Book workflow navigation">
      <a [routerLink]="['/books', book.id]" routerLinkActive="is-active" [routerLinkActiveOptions]="{ exact: true }">Overview</a>
      <a [routerLink]="['/books', book.id, 'metadata']" routerLinkActive="is-active">Metadata</a>
      <a [routerLink]="['/books', book.id, 'covers']" routerLinkActive="is-active">Covers</a>
      <a [routerLink]="['/books', book.id, 'assets']" routerLinkActive="is-active">Assets</a>
      <a [routerLink]="['/books', book.id, 'exports']" routerLinkActive="is-active">Exports</a>
      <a [routerLink]="['/books', book.id, 'translate']" routerLinkActive="is-active">Translation</a>
      <a [routerLink]="['/books', book.id, 'read']">Reader</a>
      <button type="button" (click)="createTask('General')" [disabled]="busy()">MADCloud task</button>
    </nav>
    @if (message()) {
      <p class="book-action-nav__message">{{ message() }}</p>
    }
  `,
  styles: [`
    :host { display: block; position: sticky; top: 0; z-index: 10; }
    .book-action-nav {
      display: flex;
      gap: .45rem;
      align-items: center;
      overflow-x: auto;
      padding: .7rem;
      border: 1px solid rgba(51, 65, 85, .85);
      border-radius: 8px;
      background: rgba(15, 23, 42, .92);
      backdrop-filter: blur(12px);
    }
    a, button {
      white-space: nowrap;
      border: 1px solid rgba(71, 85, 105, .8);
      border-radius: 8px;
      padding: .55rem .75rem;
      color: #cbd5e1;
      background: rgba(30, 41, 59, .7);
      font-size: .82rem;
      font-weight: 800;
      letter-spacing: 0;
    }
    a:hover, button:hover, .is-active {
      border-color: rgba(124, 58, 237, .8);
      color: #f8fafc;
      background: rgba(124, 58, 237, .22);
    }
    button:disabled { opacity: .55; cursor: wait; }
    .book-action-nav__message {
      margin: .45rem 0 0;
      color: #86efac;
      font-size: .78rem;
    }
  `],
})
export class BookActionNavComponent {
  @Input({ required: true }) book!: BookDetail;
  busy = signal(false);
  message = signal('');

  constructor(private madcloud: MadCloudTaskService) {}

  createTask(area: MadCloudWorkflowArea) {
    if (this.busy()) return;
    this.busy.set(true);
    this.message.set('');
    this.madcloud.createBookTask(
      this.book,
      area,
      `Create a MADCloud work item for ${this.book.title}. Focus area: ${area}.`,
    ).subscribe({
      next: () => {
        this.busy.set(false);
        this.message.set('MADCloud task created.');
      },
      error: () => {
        this.busy.set(false);
        this.message.set('Could not create MADCloud task.');
      },
    });
  }
}
