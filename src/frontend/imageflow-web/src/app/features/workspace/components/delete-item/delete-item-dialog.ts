import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { DeleteTarget } from '../../../../core/models/generation.models';
import { WorkspaceStore } from '../../data-access/workspace.store';

@Component({
  selector: 'app-delete-item-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatCheckboxModule, MatDialogModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ title | t }}</h2>
    <mat-dialog-content>
      <p class="delete-item-label">{{ target.label }}</p>
      @if (target.ids) { <p class="batch-confirm-count" role="status">{{ 'Selected' | t }}: {{ target.ids.length }} {{ selectedKind | t }}</p> }
      <p>{{ explanation | t }}</p>
      <p class="hint">{{ 'Jobs' | t }}: {{ target.jobs }} · {{ 'Images' | t }}: {{ target.images }}</p>
      <p>{{ 'This cannot be undone and does not reverse provider charges.' | t }}</p>
      @if (target.running) { <p class="error" role="alert">{{ 'Pause jobs and wait for in-flight requests to finish before deleting.' | t }}</p> }
      <mat-checkbox [(ngModel)]="acknowledged" [disabled]="submitting()">{{ 'I confirm permanent deletion of this item and the related data described above.' | t }}</mat-checkbox>
      @if (store.error()) { <p class="error" role="alert">{{ store.error() | t }}</p> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close [disabled]="submitting()">{{ 'Cancel' | t }}</button>
      <button mat-flat-button class="clear-confirm-button" (click)="remove()" [disabled]="!acknowledged || submitting() || store.busy() || target.running">{{ 'Permanently delete' | t }}</button>
    </mat-dialog-actions>
  `,
})
export class DeleteItemDialog {
  readonly target = inject<DeleteTarget>(MAT_DIALOG_DATA);
  readonly store = inject(WorkspaceStore);
  private readonly dialog = inject(MatDialogRef<DeleteItemDialog>);
  readonly submitting = signal(false);
  acknowledged = false;
  readonly selectedKind = { plan: 'Plans', job: 'Jobs', image: 'Images' }[this.target.kind];
  readonly title = this.target.ids ? { plan: 'Delete selected plans', job: 'Delete selected jobs', image: 'Delete selected images' }[this.target.kind]
    : { plan: 'Delete plan', job: 'Delete job', image: 'Delete image' }[this.target.kind];
  readonly explanation = this.target.ids ? {
    plan: 'Deleting selected plans also deletes their jobs, images, usage and review history. Unselected plans are kept.',
    job: 'Deleting selected jobs also deletes their images, usage and review history. Their prompt plans are kept. Queueing those plans again starts new potentially charged requests.',
    image: 'Only selected saved images are deleted. Their tasks, prompts and usage history are kept. Images will not be generated again automatically.',
  }[this.target.kind] : {
    plan: 'Deleting this plan also deletes its jobs, images, usage and review history. Other plans are kept.',
    job: 'Deleting this job also deletes its images, usage and review history. Its prompt plan is kept. Queueing that plan again starts new potentially charged requests.',
    image: 'Only this saved image is deleted. Its task, prompt and usage history are kept. The image will not be generated again automatically.',
  }[this.target.kind];
  async remove(): Promise<void> {
    if (!this.acknowledged || this.submitting() || this.store.busy() || this.target.running) return;
    this.submitting.set(true); this.dialog.disableClose = true;
    try {
      const result = await this.store.deleteItem(this.target);
      if (result) this.dialog.close(result);
    } finally { this.submitting.set(false); this.dialog.disableClose = false; }
  }
}
