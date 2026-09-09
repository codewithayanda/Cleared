import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AuditService } from '@core/services/audit.service';
import { AuditLogEntry } from '@core/models/audit-log.model';
import { EmptyState } from '@shared/components/empty-state/empty-state';

@Component({
  selector: 'app-audit-list',
  imports: [EmptyState, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './audit-list.html',
})
export class AuditList {
  private readonly auditService = inject(AuditService);

  protected readonly entries = signal<AuditLogEntry[]>([]);
  protected readonly loading = signal(true);

  constructor() {
    this.auditService.list().subscribe({
      next: (entries) => {
        this.entries.set(entries);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
