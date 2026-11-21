import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, input } from '@angular/core';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LanguageService } from '../../../../core/i18n/language.service';
import { ImageLightbox } from './image-lightbox';
import { GenerationTask } from '../../../../core/models/generation.models';
@Component({
  selector: 'app-image-gallery',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, MatDialogModule],
  templateUrl: './image-gallery.html',
})
export class ImageGallery {
  readonly images = input.required<GenerationTask[]>();
  private readonly dialog = inject(MatDialog);
  private readonly language = inject(LanguageService);
  private readonly destroyRef = inject(DestroyRef);
  private viewer?: MatDialogRef<ImageLightbox>;

  constructor() {
    this.destroyRef.onDestroy(() => this.viewer?.close());
  }

  open(task: GenerationTask): void {
    if (this.viewer) return;
    // Freeze the filtered order for this viewing session; SSE must not move the selected image.
    const images = [...this.images()];
    const index = images.findIndex((image) => image.id === task.id);
    if (index < 0) return;
    this.viewer = this.dialog.open(ImageLightbox, {
      data: { images, index },
      panelClass: 'image-lightbox-panel',
      backdropClass: 'image-lightbox-backdrop',
      width: 'calc(100vw - 32px)',
      height: 'calc(100dvh - 32px)',
      maxWidth: '1600px',
      maxHeight: '100dvh',
      ariaLabelledBy: 'image-preview-title',
      ariaLabel: this.language.translate('Image preview'),
      autoFocus: '.lightbox-close',
      restoreFocus: true,
      closeOnNavigation: true,
    });
    this.viewer
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.viewer = undefined;
      });
  }
}
