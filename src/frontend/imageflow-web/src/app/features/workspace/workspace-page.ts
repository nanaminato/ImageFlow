import { ChangeDetectionStrategy, Component, Injector, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { EngineSettingsDialog } from './components/engine-settings/engine-settings-dialog';
import { WorkspaceStore } from './data-access/workspace.store';
import { PromptPlanner } from './components/prompt-planner/prompt-planner';
import { PromptEditor } from './components/prompt-editor/prompt-editor';
import { JobQueue } from './components/job-queue/job-queue';
import { ImageGallery } from './components/image-gallery/image-gallery';

@Component({
  selector: 'app-workspace-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [WorkspaceStore],
  imports: [
    MatButtonModule,
    MatDialogModule,
    MatProgressBarModule,
    PromptPlanner,
    PromptEditor,
    JobQueue,
    ImageGallery,
  ],
  templateUrl: './workspace-page.html',
})
export class WorkspacePage {
  readonly store = inject(WorkspaceStore);
  private readonly dialog = inject(MatDialog);
  private readonly injector = inject(Injector);

  openSettings(): void {
    this.dialog.open(EngineSettingsDialog, {
      width: '600px',
      maxWidth: 'calc(100vw - 2rem)',
      injector: this.injector,
      panelClass: 'settings-dialog-panel',
      autoFocus: 'first-heading',
    });
  }
}
