export interface AuditLogEntry {
  id: string;
  userId: string;
  entityType: string;
  entityId: string;
  action: string;
  details: string | null;
  occurredAt: string;
}
