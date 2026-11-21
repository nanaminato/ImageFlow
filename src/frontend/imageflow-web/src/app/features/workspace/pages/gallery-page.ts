import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { ImageGallery } from '../components/image-gallery/image-gallery';
import { WorkspaceStore } from '../data-access/workspace.store';
import { WorkspaceViewState } from '../data-access/workspace-view-state';

@Component({
  selector: 'app-gallery-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, FormsModule, RouterLink, ImageGallery],
  template: `
    <section class="hero page-heading">
      <div>
        <span class="eyebrow">{{ 'Your images, organized' | t }}</span>
        <h1>{{ 'Collection library' | t }}</h1>
        <p>
          {{ 'Browse finished images by collection, search prompts, and download originals.' | t }}
        </p>
      </div>
      <a class="page-action" routerLink="/create">+ {{ 'Create collection' | t }}</a>
    </section>
    <section class="panel management-filters" [attr.aria-label]="'Collection filters' | t">
      <label class="filter-field"
        >{{ 'Search images' | t }}<input type="search" [(ngModel)]="view.imageSearch"
      /></label>
      <label class="filter-field"
        >{{ 'Collection' | t
        }}<select
          [attr.aria-label]="'Collection' | t"
          [ngModel]="jobFilter()"
          (ngModelChange)="filterJob($event)"
        >
          <option value="">{{ 'All collections' | t }}</option>
          @for (job of imageJobs(); track job.id) {
            <option [value]="job.id">{{ planName(job.planId) }} · {{ job.id.slice(0, 8) }}</option>
          }
        </select></label
      >
      <button class="filter-reset" type="button" (click)="reset()">
        {{ 'Reset filters' | t }}
      </button>
      <span class="filter-result-count"
        >{{ filteredImages().length }} / {{ store.images().length }} {{ 'images' | t }}</span
      >
    </section>
    @if (filteredImages().length || !store.images().length) {
      <app-image-gallery [images]="filteredImages()" />
    } @else {
      <div class="panel empty-state">
        <p>{{ 'No matching images.' | t }}</p>
      </div>
    }
  `,
})
export class GalleryPage {
  readonly store = inject(WorkspaceStore);
  readonly view = inject(WorkspaceViewState);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.queryParamMap);
  readonly jobFilter = computed(() => this.params()?.get('job') ?? '');
  readonly planNames = computed(() =>
    Object.fromEntries(this.store.plans().map((p) => [p.id, p.theme])),
  );
  readonly imageJobs = computed(() =>
    this.store.jobs().filter((j) => j.tasks.some((t) => !!t.imagePath)),
  );
  readonly filteredImages = computed(() => {
    const search = this.view.imageSearch().trim().toLocaleLowerCase();
    return this.store
      .jobs()
      .filter((j) => !this.jobFilter() || j.id === this.jobFilter())
      .flatMap((j) =>
        j.tasks.filter(
          (t) =>
            !!t.imagePath &&
            [t.promptText, this.planName(j.planId)].some((text) =>
              text.toLocaleLowerCase().includes(search),
            ),
        ),
      );
  });
  planName(id: string): string {
    return this.planNames()[id] ?? id.slice(0, 8);
  }
  filterJob(job: string): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { job: job || null },
      queryParamsHandling: 'merge',
    });
  }
  reset(): void {
    this.view.imageSearch.set('');
    this.filterJob('');
  }
}
