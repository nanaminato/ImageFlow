import { Injectable, signal } from '@angular/core';

export interface GenerationDraft {
  mode: 'Finite' | 'Continuous';
  maxImages: number;
  budget: number;
}
const defaultGeneration: GenerationDraft = { mode: 'Finite', maxImages: 10, budget: 1 };

// Shared by routed pages for the lifetime of the workspace shell. No API or secrets.
@Injectable()
export class WorkspaceViewState {
  readonly theme = signal('');
  readonly count = signal(3);
  private readonly generation = signal<Record<string, GenerationDraft>>({});
  readonly planSearch = signal('');
  readonly planStatus = signal('all');
  readonly jobSearch = signal('');
  readonly jobStatus = signal('all');
  readonly imageSearch = signal('');
  generationFor(id: string | null): GenerationDraft {
    return id ? (this.generation()[id] ?? defaultGeneration) : defaultGeneration;
  }
  updateGeneration(id: string | null, patch: Partial<GenerationDraft>): void {
    if (id)
      this.generation.update((all) => ({ ...all, [id]: { ...this.generationFor(id), ...patch } }));
  }
}
