import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { LanguageService } from '../../core/i18n/language.service';
import { ChangeDetectionStrategy, Component, Injector, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { EngineSettingsDialog } from './components/engine-settings/engine-settings-dialog';
import { ClearWorkspaceDialog } from './components/clear-workspace/clear-workspace-dialog';
import { WorkspaceStore } from './data-access/workspace.store';
import { WorkspaceViewState } from './data-access/workspace-view-state';
import { ItemActions } from './data-access/item-actions';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-workspace-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [WorkspaceStore, WorkspaceViewState, ItemActions],
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
  readonly items = inject(ItemActions);
  private readonly router = inject(Router);
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );
  readonly pageLabel = computed(() => {
    const path = this.url().split('?')[0].split('/')[1];
    return (
      (
        {
          create: 'Create collection',
          prompts: 'Prompt management',
          queue: 'Queue management',
          gallery: 'Collection library',
        } as Record<string, string>
      )[path] ?? 'Workspace'
    );
  });
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
  openClearWorkspace(): void {
    this.dialog.open(ClearWorkspaceDialog, {
      width: '560px',
      maxWidth: 'calc(100vw - 2rem)',
      injector: this.injector,
      autoFocus: 'button[mat-dialog-close]',
    });
  }
}
