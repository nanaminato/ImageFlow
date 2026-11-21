import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { LanguageService } from '../../../../core/i18n/language.service';
import { GenerationTask, Job, Plan } from '../../../../core/models/generation.models';
import { ImageLightbox } from '../image-gallery/image-lightbox';
import { planCreatedAt } from './plan-identification';

@Component({
  selector: 'app-plan-identity-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './plan-identity-card.html',
})
export class PlanIdentityCard {
  readonly plan = input.required<Plan>();
  readonly jobs = input<Job[]>([]);
  readonly showSeconds = input(false);
  private readonly language = inject(LanguageService);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);
  private viewer?: MatDialogRef<ImageLightbox>;
  readonly failedImages = signal<ReadonlySet<string>>(new Set());
  readonly images = computed(() => this.jobs().flatMap(j => j.tasks).filter(t => !!t.imagePath));
  readonly status = computed(() => {
    const jobs = this.jobs();
    if (!jobs.length) return 'Not queued';
    if (jobs.some(j => j.tasks.some(t => t.status === 'Running'))) return 'Generating';
    if (jobs.some(j => j.status === 'NeedsAttention' || j.tasks.some(t => t.status === 'Unknown' || t.status === 'Failed'))) return 'NeedsAttention';
    if (jobs.some(j => j.status === 'Active')) return 'Queued';
    if (jobs.some(j => j.status === 'Paused')) return 'Paused';
    if (jobs.every(j => j.status === 'Completed')) return 'Completed';
    if (jobs.every(j => j.status === 'Cancelled')) return 'Cancelled';
    return 'Queued';
  });
  readonly createdAt = computed(() => planCreatedAt(this.plan()));
  readonly formattedTime = computed(() => {
    const date = this.createdAt();
    if (!date) return '';
    const parts = new Intl.DateTimeFormat(this.language.language() === 'zh' ? 'zh-CN' : 'en-GB', {
      year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
      second: this.showSeconds() ? '2-digit' : undefined, hourCycle: 'h23',
    }).formatToParts(date);
    const part = (type: Intl.DateTimeFormatPartTypes) => parts.find(p => p.type === type)?.value;
    return `${part('year')}-${part('month')}-${part('day')} ${part('hour')}:${part('minute')}${this.showSeconds() ? ':' + part('second') : ''}`;
  });
  constructor() { this.destroyRef.onDestroy(() => this.viewer?.close()); }
  failed(id: string): void { this.failedImages.update(ids => new Set([...ids, id])); }
  preview(image: GenerationTask): void {
    if (this.viewer) return;
    const images = [...this.images()]; const index = images.findIndex(t => t.id === image.id);
    if (index < 0) return;
    this.viewer = this.dialog.open(ImageLightbox, {
      data: { images, index }, panelClass: 'image-lightbox-panel', backdropClass: 'image-lightbox-backdrop',
      width: 'calc(100vw - 32px)', height: 'calc(100dvh - 32px)', maxWidth: '1600px', maxHeight: '100dvh',
      ariaLabelledBy: 'image-preview-title', autoFocus: '.lightbox-close', restoreFocus: true, closeOnNavigation: true,
    });
    this.viewer.afterClosed().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.viewer = undefined);
  }
}
