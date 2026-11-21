import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from './language.service';

@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly language = inject(LanguageService);
  transform(text: string): string {
    return this.language.translate(text);
  }
}
