// Runs the work while holding a lock shared by every tab of this site. A browser without Web
// Locks runs it straight away, which still leaves the one-request-at-a-time rule inside a tab.
export function withTabLock<T>(name: string, work: () => Promise<T>): Promise<T> {
  const locks = typeof navigator === 'undefined' ? undefined : navigator.locks;

  return locks ? (locks.request(name, work) as Promise<T>) : work();
}
