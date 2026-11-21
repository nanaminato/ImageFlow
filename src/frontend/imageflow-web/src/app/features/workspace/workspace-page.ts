import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LanguageService } from '../../core/i18n/language.service';
import { ChangeDetectionStrategy, Component, Injector, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { EngineSettingsDialog } from './components/engine-settings/engine-settings-dialog';
import { WorkspaceStore } from './data-access/workspace.store';
import { WorkspaceViewState } from './data-access/workspace-view-state';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-workspace-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [WorkspaceStore, WorkspaceViewState],
  imports: [
    TranslatePipe,
    MatButtonModule,
    MatDialogModule,
    MatProgressBarModule,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
  ],
  templateUrl: './workspace-page.html',
})
export class WorkspacePage {
  readonly i18n = inject(LanguageService);
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
