import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { GenerationTask } from '../../../../core/models/generation.models';
@Component({
  selector: 'app-image-gallery',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  templateUrl: './image-gallery.html',
})
export class ImageGallery {
  readonly images = input.required<GenerationTask[]>();
}
