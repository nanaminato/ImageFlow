import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { GenerationTask } from '../../../../core/models/generation.models';
@Component({
  selector: 'app-image-gallery',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './image-gallery.html',
})
export class ImageGallery {
  readonly images = input.required<GenerationTask[]>();
}
