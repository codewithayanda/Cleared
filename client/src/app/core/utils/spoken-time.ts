// What a screen reader says about the time left. It changes in steps, so it speaks now and then
// and not every second.
export function spokenTime(seconds: number): string {
  if (seconds > 60) {
    return `${Math.ceil(seconds / 60)} minutes`;
  }

  if (seconds > 30) {
    return '1 minute';
  }

  return seconds > 10 ? '30 seconds' : '10 seconds';
}
