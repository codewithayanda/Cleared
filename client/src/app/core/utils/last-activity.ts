const KEY = 'cleared.last-activity';

// When anyone last used the app in any tab, as a time and nothing else, so the tabs can agree on how
// long it has been. If storage cannot be read there is no answer, and the caller keeps its own count.
export const lastActivity = {
  read(): number | null {
    try {
      const stored = Number(localStorage.getItem(KEY));

      return Number.isFinite(stored) && stored > 0 ? stored : null;
    } catch {
      return null;
    }
  },

  write(at: number): void {
    try {
      localStorage.setItem(KEY, String(at));
    } catch {
      // Storage can be blocked, and then each tab goes by its own count.
    }
  },
};
