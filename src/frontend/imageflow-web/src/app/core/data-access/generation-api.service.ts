import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, Observable } from 'rxjs';
import {
  GenerationTask,
  WorkspaceClearResult,
  ItemDeletionResult,
  DeleteTarget,
  ReviewInput,
  RetryInput,
  EngineSettings,
  SettingsInput,
  JobOptions,
  Job,
  JobAction,
  Plan,
  PlanInput,
  Prompt,
} from '../models/generation.models';

@Injectable({ providedIn: 'root' })
export class GenerationApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1';
  settings() {
    return firstValueFrom(this.http.get<EngineSettings>(`${this.base}/settings`));
  }
  plans() {
    return firstValueFrom(this.http.get<Plan[]>(`${this.base}/plans`));
  }
  jobs() {
    return firstValueFrom(this.http.get<Job[]>(`${this.base}/jobs`));
  }
  clearWorkspace() {
    return firstValueFrom(this.http.post<WorkspaceClearResult>(`${this.base}/workspace/clear`, { confirmed: true }));
  }
  deleteItem(target: DeleteTarget) {
    const resource = { plan: 'plans', job: 'jobs', image: 'images' }[target.kind];
    if (target.ids) return firstValueFrom(this.http.post<ItemDeletionResult>(`${this.base}/${resource}/bulk-delete`, { ids: target.ids, confirmed: true }));
    return firstValueFrom(this.http.delete<ItemDeletionResult>(`${this.base}/${resource}/${target.id}?confirmed=true`));
  }
  plan(input: PlanInput) {
    return firstValueFrom(this.http.post<Plan>(`${this.base}/plans`, input));
  }
  savePrompt(prompt: Prompt) {
    return firstValueFrom(
      this.http.put<Prompt>(`${this.base}/prompts/${prompt.id}`, { text: prompt.text }),
    );
  }
  updateSettings(settings: SettingsInput) {
    return firstValueFrom(this.http.put<EngineSettings>(`${this.base}/settings`, settings));
  }
  enqueue(planId: string, options: JobOptions = { mode: 'Finite' }) {
    return firstValueFrom(this.http.post<Job>(`${this.base}/jobs`, { planId, ...options }));
  }
  control(jobId: string, action: JobAction) {
    return firstValueFrom(this.http.post<Job>(`${this.base}/jobs/${jobId}/${action}`, {}));
  }
  task(id: string) {
    return firstValueFrom(this.http.get<GenerationTask>(`${this.base}/tasks/${id}`));
  }
  reviewTask(id: string, input: ReviewInput) {
    return firstValueFrom(
      this.http.post<GenerationTask>(`${this.base}/tasks/${id}/reviews`, input),
    );
  }
  retryTask(id: string, input: RetryInput) {
    return firstValueFrom(this.http.post<GenerationTask>(`${this.base}/tasks/${id}/retry`, input));
  }
  events(): Observable<boolean> {
    return new Observable((subscriber) => {
      const source = new EventSource(`${this.base}/events`);
      source.addEventListener('jobs', () => subscriber.next(true));
      source.onerror = () => subscriber.next(false);
      return () => source.close();
    });
  }
}
