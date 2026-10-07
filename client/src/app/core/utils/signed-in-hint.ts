const KEY = 'cleared.signed-in';

// A flag, never a token. It only saves a request at startup for people who were never signed in.
// If storage cannot be read the answer is yes, because a wasted call is better than a lost session.
export const signedInHint = {
  isSet(): boolean {
    try {
      return localStorage.getItem(KEY) === '1';
    } catch {
      return true;
    }
  },

  set(): void {
    try {
      localStorage.setItem(KEY, '1');
    } catch {
      // Storage can be blocked, and the flag is only an optimisation.
    }
  },

  clear(): void {
    try {
      localStorage.removeItem(KEY);
    } catch {
      // Storage can be blocked, and the flag is only an optimisation.
    }
  },
};
