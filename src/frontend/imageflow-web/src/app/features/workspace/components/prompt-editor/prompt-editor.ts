import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { ChangeDetectionStrategy, Component, input, model, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { EnqueueInput, Plan, Prompt } from '../../../../core/models/generation.models';
@Component({
  selector: 'app-prompt-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './prompt-editor.html',
})
export class PromptEditor {
  readonly selected = input<Plan | null>(null);
  readonly queuedIds = input.required<Set<string>>();
  readonly busy = input(false);
  readonly edit = output<Prompt>();
  readonly save = output<Prompt>();
  readonly enqueue = output<EnqueueInput>();
  readonly estimatedCostPerImage = input(0);
  readonly mode = model<'Finite' | 'Continuous'>('Finite');
  readonly maxImages = model(10);
  readonly budget = model(1);
  validLimits() {
    return (
      this.mode() === 'Finite' ||
      (Number.isInteger(this.maxImages()) &&
        this.maxImages() >= 1 &&
        this.maxImages() <= 1000 &&
        Number.isFinite(this.budget()) &&
        this.budget() > 0 &&
        this.budget() <= 1000000 &&
        this.estimatedCostPerImage() > 0)
    );
  }
  queue(plan: Plan) {
    if (this.validLimits())
      this.enqueue.emit({
        plan,
        options:
          this.mode() === 'Finite'
            ? { mode: 'Finite' }
            : { mode: 'Continuous', maxImages: this.maxImages(), budgetUsd: this.budget() },
      });
  }
}
