import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { WorkspaceStore } from '../../data-access/workspace.store';
import { WorkspaceViewState } from '../../data-access/workspace-view-state';

@Component({
  selector: 'app-clear-workspace-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatCheckboxModule, MatDialogModule, TranslatePipe],
  templateUrl: './clear-workspace-dialog.html',
})
export class ClearWorkspaceDialog {
  protected readonly store = inject(WorkspaceStore);
  private readonly view = inject(WorkspaceViewState);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialogRef<ClearWorkspaceDialog>);
  protected readonly remaining = signal(0);
  protected readonly cleared = signal(false);
  protected readonly submitting = signal(false);
  protected acknowledged = false;

  async clear(): Promise<void> {
    if (!this.acknowledged || this.store.busy() || this.store.hasRunningRequests() || this.submitting()) return;
    this.submitting.set(true);
    this.dialog.disableClose = true;
    try {
      const result = await this.store.clearWorkspace();
      if (!result) return;
      this.view.reset();
      this.remaining.set(result.remainingImageFiles);
      this.cleared.set(true);
      await this.router.navigate(['/create']);
      if (!result.remainingImageFiles) this.dialog.close();
    } finally {
      this.submitting.set(false);
      this.dialog.disableClose = false;
    }
  }
}
