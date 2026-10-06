import { IdempotencyKey } from './idempotency-key';

describe('IdempotencyKey', () => {
  it('keeps its value until renewed, so a retry sends the same key', () => {
    const key = new IdempotencyKey();

    expect(key.value).toBe(key.value);
  });

  it('changes after renew, so the next action is a new one', () => {
    const key = new IdempotencyKey();
    const first = key.value;

    key.renew();

    expect(key.value).not.toBe(first);
  });

  it('is different for every instance', () => {
    expect(new IdempotencyKey().value).not.toBe(new IdempotencyKey().value);
  });

  it('is a UUID, which is what the API accepts', () => {
    expect(new IdempotencyKey().value).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/,
    );
  });
});
