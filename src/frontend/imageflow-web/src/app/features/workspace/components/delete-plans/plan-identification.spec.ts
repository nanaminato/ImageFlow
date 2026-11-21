import { describe, expect, it } from 'vitest';
import { planCreatedAt } from './plan-identification';
const plan = (createdAt?: string) => ({ id: 'plan', theme: 'theme', prompts: [], createdAt });
describe('persisted plan creation timestamps', () => {
  it('treats SQLite timestamps without a zone as domain UTC timestamps', () => {
    expect(planCreatedAt(plan('2026-11-21T06:00:42'))?.getTime()).toBe(Date.UTC(2026, 10, 21, 6, 0, 42));
    expect(planCreatedAt(plan('2026-11-21T06:00:42Z'))?.getTime()).toBe(Date.UTC(2026, 10, 21, 6, 0, 42));
  });
  it('preserves explicit offsets instead of appending a second zone', () => {
    expect(planCreatedAt(plan('2026-11-21T15:00:42+09:00'))?.getTime()).toBe(Date.UTC(2026, 10, 21, 6, 0, 42));
  });
  it('does not invent a creation time for missing or malformed timestamps', () => {
    expect(planCreatedAt(plan())).toBeNull();
    expect(planCreatedAt(plan('invalid'))).toBeNull();
  });
});
