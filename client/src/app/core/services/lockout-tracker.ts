import { Injectable } from '@angular/core';

// Mirrors the lockout in the API's Program.cs: 5 wrong passwords lock an account for 15 minutes.
export const LOCKOUT_ATTEMPTS = 5;
export const LOCKOUT_MINUTES = 15;

const STORAGE_KEY = 'cleared.sign-in-failures';
const FORGET_AFTER_MS = 24 * 60 * 60 * 1000;

interface Failures {
  count: number;
  at: number;
  lockedUntil: number | null;
}

type Records = Record<string, Failures>;

// What this browser knows about wrong passwords, per email, so a reload or a restart does not make
// the page forget. The server never says an account is locked, because that would show anyone which
// emails have accounts, so this is the only warning a person gets. The server alone enforces it.
@Injectable({ providedIn: 'root' })
export class LockoutTracker {
  private memory: Records = {};

  // When the lock on this email ends, or null when it is not locked as far as this browser knows.
  lockedUntil(email: string): number | null {
    return this.load()[normalize(email)]?.lockedUntil ?? null;
  }

  // Notes a wrong password. Returns when the lock ends if this one tipped the account over.
  recordFailure(email: string): number | null {
    const now = Date.now();
    const key = normalize(email);
    const records = this.load();
    const current = records[key];

    if (current?.lockedUntil) {
      return current.lockedUntil;
    }

    const count = (current?.count ?? 0) + 1;
    const lockedUntil = count >= LOCKOUT_ATTEMPTS ? now + LOCKOUT_MINUTES * 60_000 : null;

    records[key] = { count: lockedUntil ? 0 : count, at: now, lockedUntil };
    this.save(records);

    return lockedUntil;
  }

  // A good sign-in wipes the slate, as it does on the server.
  recordSuccess(email: string): void {
    const records = this.load();

    delete records[normalize(email)];
    this.save(records);
  }

  // Read fresh every time, so two tabs do not overwrite each other from a stale copy. Locks that
  // have ended and counts nobody has added to for a day are dropped.
  private load(): Records {
    const now = Date.now();
    let stored: Records;

    try {
      const text = localStorage.getItem(STORAGE_KEY);
      stored = text ? (JSON.parse(text) as Records) : {};
    } catch {
      stored = this.memory;
    }

    return Object.fromEntries(
      Object.entries(stored).filter(([, record]) =>
        record.lockedUntil ? record.lockedUntil > now : now - record.at < FORGET_AFTER_MS,
      ),
    );
  }

  private save(records: Records): void {
    this.memory = records;

    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(records));
    } catch {
      // Storage can be blocked. The copy in memory still covers this page.
    }
  }
}

const normalize = (email: string): string => email.trim().toLowerCase();
