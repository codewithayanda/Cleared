import { spokenTime } from './spoken-time';

describe('spokenTime', () => {
  it.each([
    [300, '5 minutes'],
    [241, '5 minutes'],
    [240, '4 minutes'],
    [61, '2 minutes'],
    [60, '1 minute'],
    [31, '1 minute'],
    [30, '30 seconds'],
    [11, '30 seconds'],
    [10, '10 seconds'],
    [1, '10 seconds'],
  ])('says %i seconds left as "%s"', (seconds, spoken) => {
    expect(spokenTime(seconds)).toBe(spoken);
  });

  it('stays the same for every second inside one step, so a screen reader is not interrupted', () => {
    const inOneStep = [300, 290, 260, 250, 245, 241].map(spokenTime);

    expect(new Set(inOneStep).size).toBe(1);
  });
});
