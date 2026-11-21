import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { ChangeDetectionStrategy, Component, effect, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import {
  EngineSettings as Settings,
  SettingsInput,
} from '../../../../core/models/generation.models';
@Component({
  selector: 'app-engine-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  templateUrl: './engine-settings.html',
})
export class EngineSettings {
  readonly settings = input<Settings | null>(null);
  readonly busy = input(false);
  readonly save = output<SettingsInput>();
  concurrency = 2;
  estimate = 0;
  model = '';
  size = '';
  quality = '';
  complexity = '';
  width = 1024;
  height = 1024;
  readonly presets = ['1024x1024', '1536x1024', '1024x1536', '1536x864', '864x1536', '2048x2048', '2560x1440', '1440x2560', '3840x2160', '2160x3840'];
  get capability() { return this.settings()?.imageCapabilities?.find(c => c.model === this.model); }
  get sizes() { return this.capability?.customDimensions ? this.presets : this.capability?.sizes ?? []; }
  get effectiveSize() { return this.size === 'custom' ? `${this.width}x${this.height}` : this.size; }
  get dimensionsValid() {
    if (!this.capability?.customDimensions) return this.sizes.includes(this.effectiveSize);
    const [w, h] = this.effectiveSize.split('x').map(Number);
    return Number.isInteger(w) && Number.isInteger(h) && w > 0 && h > 0 && w % 16 === 0 && h % 16 === 0 &&
      w <= 3840 && h <= 3840 && w / h <= 3 && h / w <= 3 && w * h >= 655360 && w * h <= 8294400;
  }
  is4k(size: string): boolean { return size === '3840x2160' || size === '2160x3840'; }
  isExperimental(size: string): boolean {
    const [w, h] = size.split('x').map(Number);
    return this.model.startsWith('gpt-image-2.5-') && w * h > 2560 * 1440;
  }
  ratio(size: string) {
    const [w, h] = size.split('x').map(Number);
    const gcd = (a: number, b: number): number => b ? gcd(b, a % b) : a;
    const d = gcd(w, h); return `${w / d}:${h / d}`;
  }
  changeModel() {
    const c = this.capability;
    if (!c) return;
    if (!c.qualities.includes(this.quality)) this.quality = c.qualities[0];
    if (!c.complexities.includes(this.complexity)) this.complexity = c.complexities[0];
    if (!this.dimensionsValid) this.size = c.sizes[0];
  }
  private revision = -1;
  constructor() {
    effect(() => {
      const settings = this.settings();
      if (settings && settings.revision !== this.revision) {
        this.revision = settings.revision;
        this.concurrency = settings.maxConcurrency;
        this.estimate = settings.estimatedCostPerImageUsd;
        this.model = settings.imageModel;
        this.quality = settings.imageQuality;
        this.complexity = settings.imageComplexity;
        [this.width, this.height] = settings.imageSize.split('x').map(Number);
        this.size = this.sizes.includes(settings.imageSize) ? settings.imageSize : 'custom';
      }
    });
  }
  submit() {
    if (
      this.dimensionsValid &&
      Number.isInteger(this.concurrency) &&
      this.concurrency >= 1 &&
      this.concurrency <= 8 &&
      Number.isFinite(this.estimate) &&
      this.estimate >= 0 &&
      this.estimate <= 1000
    )
      this.save.emit({ maxConcurrency: this.concurrency, estimatedCostPerImageUsd: this.estimate,
        imageModel: this.model, imageSize: this.effectiveSize, imageQuality: this.quality, imageComplexity: this.complexity });
  }
}
