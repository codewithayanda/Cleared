// Stands in for navigator.locks, which jsdom does not have. Callers queue behind each other
// the way tabs do.
export class FakeLocks {
  readonly requested: string[] = [];
  private tail: Promise<unknown> = Promise.resolve();

  request<T>(name: string, callback: () => Promise<T>): Promise<T> {
    this.requested.push(name);
    const run = this.tail.then(callback);
    this.tail = run.catch(() => undefined);

    return run;
  }
}

export function installFakeLocks(): FakeLocks {
  const locks = new FakeLocks();
  Object.defineProperty(navigator, 'locks', { value: locks, configurable: true });

  return locks;
}

export function removeFakeLocks(): void {
  Reflect.deleteProperty(navigator, 'locks');
}
