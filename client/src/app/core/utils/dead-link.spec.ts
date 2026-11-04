import { HttpErrorResponse } from '@angular/common/http';
import { isDeadLink } from './dead-link';

const refused = (status: number, error: unknown) => new HttpErrorResponse({ status, error });

describe('isDeadLink', () => {
  it('is true when the API turns the link away with a message about the token', () => {
    expect(isDeadLink(refused(400, { errors: { token: ['This link is invalid.'] } }))).toBe(true);
  });

  it('is false for a refusal that is about something else', () => {
    expect(isDeadLink(refused(400, { errors: { password: ['Too short.'] } }))).toBe(false);
    expect(isDeadLink(refused(400, { errors: { token: [] } }))).toBe(false);
    expect(isDeadLink(refused(400, null))).toBe(false);
  });

  it('is false for any other status, and for anything that is not an HTTP error', () => {
    expect(isDeadLink(refused(429, { errors: { token: ['x'] } }))).toBe(false);
    expect(isDeadLink(refused(500, null))).toBe(false);
    expect(isDeadLink(new Error('offline'))).toBe(false);
    expect(isDeadLink(null)).toBe(false);
  });
});
