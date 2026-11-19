import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Plan, Prompt } from '../../../../core/models/generation.models';
@Component({
  selector: 'app-prompt-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './prompt-editor.html',
})
export class PromptEditor {
  readonly plans = input.required<Plan[]>();
  readonly selected = input<Plan | null>(null);
  readonly queuedIds = input.required<Set<string>>();
  readonly busy = input(false);
  readonly selectPlan = output<string>();
  readonly edit = output<Prompt>();
  readonly save = output<Prompt>();
  readonly enqueue = output<Plan>();
}
