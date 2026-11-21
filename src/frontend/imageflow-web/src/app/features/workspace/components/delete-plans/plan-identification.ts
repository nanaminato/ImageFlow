import { Plan } from '../../../../core/models/generation.models';

export function planCreatedAt(plan: Plan): Date | null {
  if (!plan.createdAt) return null;
  // Domain timestamps are UTC. SQLite can round-trip DateTime with an unspecified kind.
  const value = /(?:Z|[+-]\d{2}:\d{2})$/i.test(plan.createdAt) ? plan.createdAt : `${plan.createdAt}Z`;
  const date = new Date(value);
  return Number.isFinite(date.getTime()) ? date : null;
}
