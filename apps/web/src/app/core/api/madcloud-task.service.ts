import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { BookDetail } from './books.api';
import { environment } from '../../../environments/environment';

export type MadCloudWorkflowArea =
  | 'Cover'
  | 'Translation'
  | 'Manuscript'
  | 'Metadata'
  | 'Launch'
  | 'General';

@Injectable({ providedIn: 'root' })
export class MadCloudTaskService {
  private http = inject(HttpClient);

  createBookTask(book: BookDetail, workflowArea: MadCloudWorkflowArea, detail: string): Observable<unknown> {
    const callbackBase = environment.apiBase || `${window.location.protocol}//${window.location.hostname}:3012`;
    const notes = {
      sourceApp: 'madauthor',
      projectId: book.id,
      bookTitle: book.title,
      workflowArea,
      targetPath: `book:${book.id}/${workflowArea.toLowerCase()}`,
      callbackUrl: `${callbackBase}/api/madcloud/ai-results`,
    };

    return this.http.post('/api/ai-tasks', {
      title: `${workflowArea}: ${book.title}`,
      description: detail,
      notes: JSON.stringify(notes, null, 2),
      status: 0,
      priority: 3,
    });
  }
}
