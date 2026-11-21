import { Injectable, Injector, inject, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { DeleteTarget, GenerationTask, ItemDeletionResult, Job, Plan } from '../../../core/models/generation.models';
import { DeleteItemDialog } from '../components/delete-item/delete-item-dialog';
import { WorkspaceStore } from './workspace.store';

@Injectable()
export class ItemActions {
  private readonly store = inject(WorkspaceStore);
  private readonly dialog = inject(MatDialog);
  private readonly injector = inject(Injector);
  private readonly router = inject(Router);
  readonly pendingFiles = signal(0);
  deletePlans(plans: Plan[]): void {
    if (!plans.length) return;
    const ids = plans.map(p => p.id);
    const jobs = this.store.jobs().filter(j => ids.includes(j.planId));
    void this.confirm({ kind: 'plan', id: ids[0], ids, label: plans.slice(0, 3).map(p => p.theme).join(' · '),
      jobs: jobs.length, images: jobs.flatMap(j => j.tasks).filter(t => !!t.imagePath).length,
      running: jobs.some(j => j.tasks.some(t => t.status === 'Running')) });
  }
  deleteJobs(jobs: Job[]): void {
    if (!jobs.length) return;
    const ids = jobs.map(j => j.id);
    void this.confirm({ kind: 'job', id: ids[0], ids, label: ids.slice(0, 3).map(id => id.slice(0, 8)).join(' · '),
      jobs: ids.length, images: jobs.flatMap(j => j.tasks).filter(t => !!t.imagePath).length,
      running: jobs.some(j => j.tasks.some(t => t.status === 'Running')) });
  }
  deleteImages(images: GenerationTask[]): void {
    if (!images.length) return;
    const ids = images.map(t => t.id);
    void this.confirm({ kind: 'image', id: ids[0], ids, label: images.slice(0, 3).map(t => t.promptText).join(' · '),
      jobs: 0, images: ids.length, running: images.some(t => t.status === 'Running') });
  }
  deletePlan(plan: Plan): void {
    const jobs = this.store.jobs().filter(j => j.planId === plan.id);
    void this.confirm({ kind: 'plan', id: plan.id, label: plan.theme, jobs: jobs.length,
      images: jobs.flatMap(j => j.tasks).filter(t => !!t.imagePath).length,
      running: jobs.some(j => j.tasks.some(t => t.status === 'Running')) });
  }
  deleteJob(job: Job): void {
    void this.confirm({ kind: 'job', id: job.id, label: `${this.store.plans().find(p => p.id === job.planId)?.theme ?? ''} · ${job.id.slice(0, 8)}`,
      jobs: 1, images: job.tasks.filter(t => !!t.imagePath).length, running: job.tasks.some(t => t.status === 'Running') });
  }
  deleteImage(image: GenerationTask): void {
    void this.confirm({ kind: 'image', id: image.id, label: image.promptText, jobs: 0, images: 1, running: image.status === 'Running' });
  }
  private async confirm(target: DeleteTarget): Promise<void> {
    if (this.store.busy() || this.dialog.openDialogs.length) return;
    const result = await firstValueFrom(this.dialog.open<DeleteItemDialog, DeleteTarget, ItemDeletionResult>(DeleteItemDialog, {
      data: target, injector: this.injector, width: '560px', maxWidth: 'calc(100vw - 2rem)', autoFocus: 'button[mat-dialog-close]',
    }).afterClosed());
    if (!result) return;
    this.pendingFiles.set(result.pendingImageFiles);
    // Remove only links that reference the deleted item; preserve unrelated page filters.
    const tree = this.router.parseUrl(this.router.url);
    const ids = target.ids ?? [target.id];
    if (target.kind === 'plan' && ids.includes(tree.queryParams['plan'])) delete tree.queryParams['plan'];
    if (target.kind === 'job' && ids.includes(tree.queryParams['job'])) delete tree.queryParams['job'];
    await this.router.navigateByUrl(tree);
  }
}
