import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { ChangeDetectionStrategy, Component, input, model, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { PlanInput } from '../../../../core/models/generation.models';
@Component({
  selector: 'app-prompt-planner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './prompt-planner.html',
})
export class PromptPlanner {
  readonly theme = model('');
  readonly count = model(3);
  readonly busy = input(false);
  readonly create = output<PlanInput>();
  submit() {
    if (
      this.theme().trim() &&
      Number.isInteger(this.count()) &&
      this.count() >= 1 &&
      this.count() <= 20
    )
      this.create.emit({ theme: this.theme().trim(), count: this.count() });
  }
}
