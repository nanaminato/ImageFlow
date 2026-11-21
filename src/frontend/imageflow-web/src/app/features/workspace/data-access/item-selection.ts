import { signal } from '@angular/core';

// Selection persists across filters; deletion callers intersect it with current server records.
export class ItemSelection {
  readonly ids = signal<ReadonlySet<string>>(new Set());
  toggle(id: string): void {
    this.ids.update(current => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }
  toggleAll(visibleIds: string[]): void {
    this.ids.update(current => {
      const next = new Set(current);
      const remove = visibleIds.every(id => current.has(id));
      for (const id of visibleIds) { if (remove) next.delete(id); else next.add(id); }
      return next;
    });
  }
  clear(): void { this.ids.set(new Set()); }
}
