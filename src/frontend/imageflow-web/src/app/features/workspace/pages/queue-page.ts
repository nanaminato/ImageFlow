import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { JobQueue } from '../components/job-queue/job-queue';
import { WorkspaceStore } from '../data-access/workspace.store';
import { WorkspaceViewState } from '../data-access/workspace-view-state';
import { ItemActions } from '../data-access/item-actions';
import { ItemSelection } from '../data-access/item-selection';
import { BulkActions } from '../components/bulk-actions/bulk-actions';

@Component({
  selector: 'app-queue-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, FormsModule, RouterLink, JobQueue, BulkActions],
  template: `
    <section class="hero page-heading">
      <div>
        <span class="eyebrow">{{ 'Manage your work' | t }}</span>
        <h1>{{ 'Queue management' | t }}</h1>
        <p>{{ 'Monitor jobs, filter their status, and review individual requests.' | t }}</p>
      </div>
      <a class="page-action" routerLink="/prompts">{{ 'Review prompts' | t }} →</a>
    </section>
    <div class="queue-summary">
      <span
        >{{ 'Active jobs' | t }} <strong>{{ activeCount() }}</strong></span
      ><span
        >{{ 'Needs attention' | t }} <strong>{{ attentionCount() }}</strong></span
      ><span
        >{{ 'Total jobs' | t }} <strong>{{ store.jobs().length }}</strong></span
      >
    </div>
    <section class="panel management-filters" [attr.aria-label]="'Queue filters' | t">
      <label class="filter-field"
        >{{ 'Search jobs' | t }}<input type="search" [(ngModel)]="view.jobSearch"
      /></label>
      <label class="filter-field"
        >{{ 'Job status' | t
        }}<select [attr.aria-label]="'Job status' | t" [(ngModel)]="view.jobStatus">
          <option value="all">{{ 'All statuses' | t }}</option>
          @for (status of statuses; track status) {
            <option [value]="status">{{ status | t }}</option>
          }
        </select></label
      >
      <label class="filter-field"
        >{{ 'Prompt plan' | t
        }}<select
          [attr.aria-label]="'Prompt plan' | t"
          [ngModel]="planFilter()"
          (ngModelChange)="filterPlan($event)"
        >
          <option value="">{{ 'All plans' | t }}</option>
          @for (plan of store.plans(); track plan.id) {
            <option [value]="plan.id">{{ plan.theme }}</option>
          }
        </select></label
      >
      <button type="button" class="filter-reset" (click)="reset()">
        {{ 'Reset filters' | t }}
      </button>
    </section>
    <app-bulk-actions [visibleIds]="visibleIds()" [selectedIds]="selection.ids()" [selectedCount]="selectedJobs().length"
        [busy]="store.busy()" (toggleAll)="selection.toggleAll($event)" (clear)="selection.clear()" (remove)="items.deleteJobs(selectedJobs())" />
    @if (filteredJobs().length || !store.jobs().length) {
      <app-job-queue
        [jobs]="filteredJobs()"
        [planNames]="planNames()"
        [busy]="store.busy()"
        (control)="store.controlJob($event.job, $event.action)"
        (review)="store.reviewTask($event.id, $event.input)"
        (retry)="store.retryTask($event.id, $event.input)"
        (remove)="items.deleteJob($event)"
        [selectedIds]="selection.ids()" (toggleSelection)="selection.toggle($event)"
      />
    } @else {
      <div class="panel empty-state">
        <p>{{ 'No matching jobs.' | t }}</p>
      </div>
    }
  `,
})
export class QueuePage {
  readonly store = inject(WorkspaceStore);
  readonly view = inject(WorkspaceViewState);
  readonly items = inject(ItemActions);
  readonly selection = new ItemSelection();
  readonly selectedJobs = computed(() => this.store.jobs().filter(j => this.selection.ids().has(j.id)));
  readonly visibleIds = computed(() => this.filteredJobs().map(j => j.id));
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.queryParamMap);
  readonly planFilter = computed(() => this.params()?.get('plan') ?? '');
  readonly statuses = ['Active', 'Paused', 'NeedsAttention', 'Completed', 'Cancelled'];
  readonly planNames = computed(() =>
    Object.fromEntries(this.store.plans().map((p) => [p.id, p.theme])),
  );
  readonly activeCount = computed(
    () => this.store.jobs().filter((j) => j.status === 'Active').length,
  );
  readonly attentionCount = computed(
    () =>
      this.store
        .jobs()
        .filter((j) => j.status === 'NeedsAttention' || j.tasks.some((t) => t.status === 'Unknown'))
        .length,
  );
  readonly filteredJobs = computed(() => {
    const search = this.view.jobSearch().trim().toLocaleLowerCase();
    return this.store
      .jobs()
      .filter(
        (j) =>
          (!this.planFilter() || j.planId === this.planFilter()) &&
          (this.view.jobStatus() === 'all' || j.status === this.view.jobStatus()) &&
          [j.id, this.planNames()[j.planId] ?? '', ...j.tasks.map((t) => t.promptText)].some(
            (text) => text.toLocaleLowerCase().includes(search),
          ),
      );
  });
  filterPlan(plan: string): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { plan: plan || null },
      queryParamsHandling: 'merge',
    });
  }
  reset(): void {
    this.view.jobSearch.set('');
    this.view.jobStatus.set('all');
    this.filterPlan('');
  }
}
