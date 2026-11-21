import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { WorkspaceStore } from '../../data-access/workspace.store';
import { ItemSelection } from '../../data-access/item-selection';
import { PlanIdentityCard } from './plan-identity-card';
import { planCreatedAt } from './plan-identification';

@Component({
  selector: 'app-delete-plans-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatDialogModule, TranslatePipe, PlanIdentityCard],
  templateUrl: './delete-plans-dialog.html',
})
export class DeletePlansDialog {
  readonly store = inject(WorkspaceStore);
  private readonly dialog = inject(MatDialogRef<DeletePlansDialog>);
  readonly selection = new ItemSelection();
  readonly search = signal('');
  readonly reviewIds = signal<string[] | null>(null);
  readonly submitting = signal(false);
  readonly timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  readonly sortedPlans = computed(() => [...this.store.plans()].sort((a, b) =>
    ((planCreatedAt(b)?.getTime() ?? -Infinity) - (planCreatedAt(a)?.getTime() ?? -Infinity)) || a.id.localeCompare(b.id)));
  readonly secondsRequired = computed(() => {
    const groups = new Map<string, string[]>();
    for (const plan of this.sortedPlans()) {
      const date = planCreatedAt(plan); if (!date) continue;
      const key = `${plan.theme}|${Math.floor(date.getTime() / 60000)}`;
      groups.set(key, [...(groups.get(key) ?? []), plan.id]);
    }
    return new Set([...groups.values()].filter(ids => ids.length > 1).flat());
  });
  readonly planJobs = computed(() => {
    const groups = new Map<string, ReturnType<typeof this.store.jobs>>();
    for (const job of this.store.jobs()) groups.set(job.planId, [...(groups.get(job.planId) ?? []), job]);
    return groups;
  });
  readonly filtered = computed(() => {
    const text = this.search().trim().toLocaleLowerCase();
    return this.sortedPlans().filter(p => p.theme.toLocaleLowerCase().includes(text) || p.id.includes(text));
  });
  readonly visibleIds = computed(() => this.filtered().map(p => p.id));
  readonly selected = computed(() => this.sortedPlans().filter(p => this.selection.ids().has(p.id)));
  readonly allVisible = computed(() => this.visibleIds().length > 0 && this.visibleIds().every(id => this.selection.ids().has(id)));
  readonly someVisible = computed(() => this.visibleIds().some(id => this.selection.ids().has(id)));
  readonly jobs = computed(() => this.store.jobs().filter(j => (this.reviewIds() ?? this.selected().map(p => p.id)).includes(j.planId)));
  readonly imageCount = computed(() => this.jobs().flatMap(j => j.tasks).filter(t => !!t.imagePath).length);
  readonly running = computed(() => this.jobs().some(j => j.tasks.some(t => t.status === 'Running')));
  readonly missing = computed(() => this.reviewIds()?.some(id => !this.store.plans().some(p => p.id === id)) ?? false);
  review(): void {
    if (!this.selected().length || this.selected().length > 1000 || this.store.busy()) return;
    this.reviewIds.set(this.selected().map(p => p.id));
  }
  back(): void { this.reviewIds.set(null); }
  async remove(): Promise<void> {
    const ids = this.reviewIds();
    if (!ids?.length || this.submitting() || this.store.busy() || this.running() || this.missing()) return;
    this.submitting.set(true); this.dialog.disableClose = true;
    try {
      const result = await this.store.deleteItem({ kind: 'plan', id: ids[0], ids, label: '', jobs: this.jobs().length, images: this.imageCount(), running: false });
      if (result) this.dialog.close(result);
    } finally { this.submitting.set(false); this.dialog.disableClose = false; }
  }
}
