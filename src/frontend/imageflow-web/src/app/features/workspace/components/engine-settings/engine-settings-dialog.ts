import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { SettingsInput } from '../../../../core/models/generation.models';
import { WorkspaceStore } from '../../data-access/workspace.store';
import { EngineSettings } from './engine-settings';

@Component({
  selector: 'app-engine-settings-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EngineSettings, MatButtonModule, MatDialogModule],
  templateUrl: './engine-settings-dialog.html',
})
export class EngineSettingsDialog {
  protected readonly store = inject(WorkspaceStore);
  private readonly dialogRef = inject(MatDialogRef<EngineSettingsDialog>);

  async save(input: SettingsInput): Promise<void> {
    await this.store.updateSettings(input);
    if (!this.store.busy() && !this.store.error()) this.dialogRef.close();
  }
}
