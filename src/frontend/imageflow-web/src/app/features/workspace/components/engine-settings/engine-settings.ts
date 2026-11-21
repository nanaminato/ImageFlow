import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { ChangeDetectionStrategy, Component, effect, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import {
  EngineSettings as Settings,
  SettingsInput,
} from '../../../../core/models/generation.models';
@Component({
  selector: 'app-engine-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './engine-settings.html',
})
export class EngineSettings {
  readonly settings = input<Settings | null>(null);
  readonly busy = input(false);
  readonly save = output<SettingsInput>();
  concurrency = 2;
  estimate = 0;
  private revision = -1;
  constructor() {
    effect(() => {
      const settings = this.settings();
      if (settings && settings.revision !== this.revision) {
        this.revision = settings.revision;
        this.concurrency = settings.maxConcurrency;
        this.estimate = settings.estimatedCostPerImageUsd;
      }
    });
  }
  submit() {
    if (
      Number.isInteger(this.concurrency) &&
      this.concurrency >= 1 &&
      this.concurrency <= 8 &&
      Number.isFinite(this.estimate) &&
      this.estimate >= 0 &&
      this.estimate <= 1000
    )
      this.save.emit({ maxConcurrency: this.concurrency, estimatedCostPerImageUsd: this.estimate });
  }
}
