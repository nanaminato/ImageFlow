import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { GenerationTask, ReviewInput, RetryInput } from '../../../../core/models/generation.models';
@Component({
  selector: 'app-task-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, FormsModule, MatButtonModule],
  templateUrl: './task-detail.html',
})
export class TaskDetail {
  readonly task = input.required<GenerationTask>();
  readonly busy = input(false);
  readonly review = output<{ id: string; input: ReviewInput }>();
  readonly retry = output<{ id: string; input: RetryInput }>();
  resolution = 'Generated';
  evidence = '';
  confirmed = false;
  retryConfirmed = false;
  attempts() {
    return [...this.task().attempts].sort((a, b) => a.startedAt.localeCompare(b.startedAt));
  }
  latest() {
    return this.attempts().at(-1);
  }
  canRetry() {
    const a = this.latest();
    return (
      this.task().status === 'Paused' &&
      (this.task().reviews ?? []).some(
        (r) =>
          r.attemptId === a?.id &&
          r.resolution === 'NotGeneratedNotCharged' &&
          !r.retryAuthorizedAt,
      )
    );
  }
  submit() {
    const a = this.latest();
    if (!a || !this.confirmed || !this.evidence.trim()) return;
    this.review.emit({
      id: this.task().id,
      input: {
        attemptId: a.id,
        resolution: this.resolution,
        evidence: this.evidence.trim(),
        confirmed: true,
      },
    });
    this.confirmed = false;
  }
  authorize() {
    const a = this.latest();
    if (a && this.retryConfirmed)
      this.retry.emit({ id: this.task().id, input: { attemptId: a.id, confirmed: true } });
    this.retryConfirmed = false;
  }
}
