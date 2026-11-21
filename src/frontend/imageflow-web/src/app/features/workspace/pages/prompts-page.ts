import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { EnqueueInput } from '../../../core/models/generation.models';
import { PromptEditor } from '../components/prompt-editor/prompt-editor';
import { WorkspaceStore } from '../data-access/workspace.store';
import { WorkspaceViewState } from '../data-access/workspace-view-state';

@Component({
  selector: 'app-prompts-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, FormsModule, RouterLink, PromptEditor],
  template: `
    <section class="hero page-heading">
      <div>
        <span class="eyebrow">{{ 'Manage your work' | t }}</span>
        <h1>{{ 'Prompt management' | t }}</h1>
        <p>{{ 'Find saved plans, refine drafts, and queue reviewed prompts.' | t }}</p>
      </div>
      <a class="page-action" routerLink="/create">+ {{ 'Create collection' | t }}</a>
    </section>
    <div class="management-layout">
      <aside class="panel plan-manager" [attr.aria-label]="'Saved prompt plans' | t">
        <h2>
          {{ 'Saved prompt plans' | t }} <span class="count-badge">{{ store.plans().length }}</span>
        </h2>
        <label class="filter-field"
          >{{ 'Search plans' | t }}<input type="search" [(ngModel)]="view.planSearch"
        /></label>
        <label class="filter-field"
          >{{ 'Plan status' | t
          }}<select [attr.aria-label]="'Plan status' | t" [(ngModel)]="view.planStatus">
            <option value="all">{{ 'All plans' | t }}</option>
            <option value="draft">{{ 'Draft' | t }}</option>
            <option value="queued">{{ 'Queued' | t }}</option>
          </select></label
        >
        <div class="managed-plan-list">
          @for (plan of filteredPlans(); track plan.id) {
            <a
              class="managed-plan"
              [class.is-selected]="store.selectedPlanId() === plan.id"
              [attr.aria-current]="store.selectedPlanId() === plan.id ? 'true' : null"
              routerLink="/prompts"
              [queryParams]="{ plan: plan.id }"
            >
              <strong>{{ plan.theme }}</strong><span
                >{{ 'Prompts' | t }}: {{ plan.prompts.length }} ·
                {{ (store.queuedPlanIds().has(plan.id) ? 'Queued' : 'Draft') | t }}</span
              >
            </a>
          } @empty {
            <p class="empty">{{ 'No matching plans.' | t }}</p>
          }
        </div>
      </aside>
      <div class="manager-detail">
        @if (missingPlan()) {
          <div class="panel" role="status">
            {{ 'This plan is unavailable. Select another saved plan.' | t }}
          </div>
        } @else {
          <app-prompt-editor
            [selected]="store.selectedPlan()"
            [estimatedCostPerImage]="store.settings()?.estimatedCostPerImageUsd ?? 0"
            [queuedIds]="store.queuedPlanIds()"
            [busy]="store.busy()"
            [mode]="generation().mode"
            (modeChange)="view.updateGeneration(store.selectedPlanId(), { mode: $event })"
            [maxImages]="generation().maxImages"
            (maxImagesChange)="view.updateGeneration(store.selectedPlanId(), { maxImages: $event })"
            [budget]="generation().budget"
            (budgetChange)="view.updateGeneration(store.selectedPlanId(), { budget: $event })"
            (edit)="store.editPrompt($event.id, $event.text)"
            (save)="store.savePrompt($event)"
            (enqueue)="enqueue($event)"
          />
          @if (store.selectedPlan(); as plan) {
            @if (store.queuedPlanIds().has(plan.id)) {
              <a class="text-link" routerLink="/queue" [queryParams]="{ plan: plan.id }"
                >{{ 'View this plan in queue' | t }} →</a
              >
            }
          }
        }
      </div>
    </div>
  `,
})
export class PromptsPage {
  readonly store = inject(WorkspaceStore);
  readonly view = inject(WorkspaceViewState);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly params = toSignal(inject(ActivatedRoute).queryParamMap);
  readonly generation = computed(() => this.view.generationFor(this.store.selectedPlanId()));
  readonly missingPlan = computed(() => {
    const id = this.params()?.get('plan');
    return !!id && !this.store.busy() && !this.store.plans().some((p) => p.id === id);
  });
  readonly filteredPlans = computed(() => {
    const search = this.view.planSearch().trim().toLocaleLowerCase();
    const status = this.view.planStatus();
    return this.store
      .plans()
      .filter(
        (p) =>
          (p.theme.toLocaleLowerCase().includes(search) || p.id.includes(search)) &&
          (status === 'all' || this.store.queuedPlanIds().has(p.id) === (status === 'queued')),
      );
  });
  constructor() {
    effect(() => {
      const id = this.params()?.get('plan');
      if (id && this.store.plans().some((p) => p.id === id) && id !== this.store.selectedPlanId())
        this.store.selectPlan(id);
    });
  }
  async enqueue(input: EnqueueInput): Promise<void> {
    if ((await this.store.enqueue(input.plan, input.options)) && !this.destroyRef.destroyed) {
      await this.router.navigate(['/queue'], { queryParams: { plan: input.plan.id } });
    }
  }
}
