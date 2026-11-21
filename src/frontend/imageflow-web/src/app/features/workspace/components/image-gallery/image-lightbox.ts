import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { GenerationTask } from '../../../../core/models/generation.models';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';

export interface ImageLightboxData {
  images: readonly GenerationTask[];
  index: number;
}

@Component({
  selector: 'app-image-lightbox',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, MatDialogModule, MatButtonModule],
  templateUrl: './image-lightbox.html',
})
export class ImageLightbox {
  readonly data = inject<ImageLightboxData>(MAT_DIALOG_DATA);
  private readonly dialog = inject(MatDialogRef<ImageLightbox>);
  readonly index = signal(this.data.index);
  readonly current = computed(() => this.data.images[this.index()]);
  readonly detailsOpen = signal(false);
  readonly failed = signal(false);
  readonly dimensions = signal('');
  readonly thumbnails = computed(() => {
    const start = Math.max(0, Math.min(this.index() - 2, this.data.images.length - 5));
    return this.data.images
      .slice(start, start + 5)
      .map((image, i) => ({ image, index: start + i }));
  });

  constructor() {
    this.dialog
      .keydownEvents()
      .pipe(takeUntilDestroyed())
      .subscribe((event) => {
        if (event.altKey || event.ctrlKey || event.metaKey) return;
        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
          event.preventDefault();
          this.move(event.key === 'ArrowLeft' ? -1 : 1);
        }
      });
  }

  url(image: GenerationTask): string {
    return '/api/v1/images/' + encodeURIComponent(image.id);
  }
  move(delta: number): void {
    this.select((this.index() + delta + this.data.images.length) % this.data.images.length);
  }
  select(index: number): void {
    if (index === this.index()) return;
    this.failed.set(false);
    this.dimensions.set('');
    this.index.set(index);
  }
  loaded(event: Event): void {
    const image = event.target as HTMLImageElement;
    this.dimensions.set(`${image.naturalWidth} × ${image.naturalHeight}`);
  }
}
