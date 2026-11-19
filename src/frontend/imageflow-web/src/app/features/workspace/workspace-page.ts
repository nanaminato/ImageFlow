import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { WorkspaceStore } from './data-access/workspace.store';
import { PromptPlanner } from './components/prompt-planner/prompt-planner';
import { PromptEditor } from './components/prompt-editor/prompt-editor';
import { JobQueue } from './components/job-queue/job-queue';
import { ImageGallery } from './components/image-gallery/image-gallery';
@Component({
  selector: 'app-workspace-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [WorkspaceStore],
  imports: [MatProgressBarModule, PromptPlanner, PromptEditor, JobQueue, ImageGallery],
  templateUrl: './workspace-page.html',
})
export class WorkspacePage {
  readonly store = inject(WorkspaceStore);
}
