import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export type NotificationType = 0 | 1 | 2 | 3 | 4 | 5;

export interface NotificationDto {
  id: string;
  type: NotificationType;
  title: string;
  message: string;
  linkUrl?: string | null;
  isRead: boolean;
  createdDate: string;
}

export interface NotificationListResponse {
  items: NotificationDto[];
  unreadCount: number;
}

export const NOTIFICATION_TYPE_LABELS: Record<NotificationType, string> = {
  0: 'Job started',
  1: 'Progress',
  2: 'Completed',
  3: 'Export ready',
  4: 'Error',
  5: 'System',
};

@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private http = inject(HttpClient);

  list(limit = 50): Observable<NotificationListResponse> {
    return this.http.get<NotificationListResponse>(`/api/notifications?limit=${limit}`);
  }

  unreadCount(): Observable<{ unreadCount: number }> {
    return this.http.get<{ unreadCount: number }>('/api/notifications/unread-count');
  }

  markRead(id: string): Observable<void> {
    return this.http.post<void>(`/api/notifications/${id}/read`, {});
  }

  markAllRead(): Observable<{ marked: number }> {
    return this.http.post<{ marked: number }>('/api/notifications/read-all', {});
  }
}
