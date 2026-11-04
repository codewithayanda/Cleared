import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, map, tap } from 'rxjs';
import { environment } from '@env';
import { Account } from '@core/models/auth.model';

export type ResendResult = 'sent' | 'already-confirmed';

// The signed-in person's own account: their email, and whether they have confirmed it. The server is
// the one that decides what an unconfirmed account may do. This only lets the pages say so.
@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly http = inject(HttpClient);
  private readonly current = signal<Account | null>(null);

  readonly account = this.current.asReadonly();

  // Forgets the last person first, so someone else signing in never sees the previous account.
  load(): Observable<Account> {
    this.current.set(null);

    return this.http
      .get<Account>(`${environment.apiUrl}/users/me`)
      .pipe(tap((account) => this.current.set(account)));
  }

  // The server answers 204 when there was nothing to send because the email is already confirmed.
  resendConfirmation(): Observable<ResendResult> {
    return this.http
      .post<void>(`${environment.apiUrl}/users/me/email-verification`, null, {
        observe: 'response',
      })
      .pipe(
        map((response): ResendResult => (response.status === 204 ? 'already-confirmed' : 'sent')),
        tap((result) => {
          if (result === 'already-confirmed') {
            this.current.update((account) => account && { ...account, emailVerified: true });
          }
        }),
      );
  }
}
