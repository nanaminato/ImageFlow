import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TaskDetail } from '../task-detail/task-detail';
import { RouterLink } from '@angular/router';
import { ReviewInput, RetryInput, Job, JobAction } from '../../../../core/models/generation.models';
@Component({
  selector: 'app-job-queue',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    TranslatePipe,
    RouterLink,
    TaskDetail,
    DecimalPipe,
    MatButtonModule,
    MatProgressBarModule,
  ],
  templateUrl: './job-queue.html',
})
export class JobQueue {
  readonly jobs = input.required<Job[]>();
  readonly planNames = input<Record<string, string>>({});
  readonly busy = input(false);
  readonly control = output<{ job: Job; action: JobAction }>();
  readonly review = output<{ id: string; input: ReviewInput }>();
  readonly retry = output<{ id: string; input: RetryInput }>();
  readonly expanded = signal<ReadonlySet<string>>(new Set());
  setExpanded(id: string, open: boolean): void {
    this.expanded.update(current => {
      if (current.has(id) === open) return current;
      const next = new Set(current); if (open) next.add(id); else next.delete(id); return next;
    });
  }
  completed(job: Job) {
    return job.tasks.filter((t) => t.status === 'Completed').length;
  }
  hasImages(job: Job): boolean {
    return job.tasks.some((t) => !!t.imagePath);
  }
}
