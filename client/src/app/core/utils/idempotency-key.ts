export const IDEMPOTENCY_HEADER = 'Idempotency-Key';

// One key per user action. A retry of the same action must send the same key, so the server can
// recognise it and not do the work twice. A new action needs a new key.
export class IdempotencyKey {
  private current = crypto.randomUUID();

  get value(): string {
    return this.current;
  }

  // Call once the server has accepted the action, so the next submit counts as a new one.
  renew(): void {
    this.current = crypto.randomUUID();
  }
}
