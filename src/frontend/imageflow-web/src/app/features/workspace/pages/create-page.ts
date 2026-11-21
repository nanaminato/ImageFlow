import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { PlanInput } from '../../../core/models/generation.models';
import { PromptPlanner } from '../components/prompt-planner/prompt-planner';
import { WorkspaceStore } from '../data-access/workspace.store';
import { WorkspaceViewState } from '../data-access/workspace-view-state';

@Component({
  selector: 'app-create-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, RouterLink, PromptPlanner],
  template: `
    <section class="hero">
      <span class="eyebrow">{{ 'Generation workspace' | t }}</span>
      <h1>{{ 'From an idea to a collection.' | t }}</h1>
      <p>
        {{
          'Create a plan here, then review prompts and manage generation on their own pages.' | t
        }}
      </p>
    </section>
    <div class="create-layout">
      <app-prompt-planner
        [(theme)]="view.theme"
        [(count)]="view.count"
        [busy]="store.busy()"
        (create)="create($event)"
      />
      <section class="management-overview" [attr.aria-label]="'Management overview' | t">
        <a routerLink="/prompts"
          ><strong>{{ store.plans().length }}</strong
          ><span>{{ 'Saved prompt plans' | t }}</span
          ><small>{{ 'Review and organize your plans' | t }} →</small></a
        >
        <a routerLink="/queue"
          ><strong>{{ store.jobs().length }}</strong
          ><span>{{ 'Generation jobs' | t }}</span
          ><small>{{ 'Inspect progress and control requests' | t }} →</small></a
        >
        <a routerLink="/gallery"
          ><strong>{{ store.images().length }}</strong
          ><span>{{ 'Generated images' | t }}</span
          ><small>{{ 'Browse and download your images' | t }} →</small></a
        >
      </section>
    </div>
  `,
})
export class CreatePage {
  readonly store = inject(WorkspaceStore);
  readonly view = inject(WorkspaceViewState);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  async create(input: PlanInput): Promise<void> {
    if ((await this.store.createPlan(input)) && !this.destroyRef.destroyed) {
      await this.router.navigate(['/prompts'], {
        queryParams: { plan: this.store.selectedPlanId() },
      });
    }
  }
}
