import { computed, inject } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  patchState,
  signalStore,
  withComputed,
  withHooks,
  withMethods,
  withState,
} from '@ngrx/signals';
import { Subscription } from 'rxjs';
import { GenerationApi } from '../../../core/data-access/generation-api.service';
import {
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
  WorkspaceClearResult,
  ItemDeletionResult,
  DeleteTarget,
} from '../../../core/models/generation.models';

interface WorkspaceState {
  plans: Plan[];
  jobs: Job[];
  settings: EngineSettings | null;
  selectedPlanId: string | null;
  busy: boolean;
  error: string;
  connected: boolean;
}
const initialState: WorkspaceState = {
  plans: [],
  jobs: [],
  settings: null,
  selectedPlanId: null,
  busy: false,
  error: '',
  connected: false,
};

export const WorkspaceStore = signalStore(
  withState(initialState),
  withComputed((store) => ({
    selectedPlan: computed(
      () => store.plans().find((p) => p.id === store.selectedPlanId()) ?? null,
    ),
    queuedPlanIds: computed(() => new Set(store.jobs().map((j) => j.planId))),
    images: computed(() => store.jobs().flatMap((j) => j.tasks.filter((t) => t.imagePath))),
    hasRunningRequests: computed(() => store.jobs().some(j => j.tasks.some(t => t.status === 'Running'))),
    providerLabel: computed(() => {
      const s = store.settings();
      return s ? `${s.provider} · ${s.maxConcurrency} outstanding requests` : 'Loading settings…';
    }),
  })),
  withMethods((store, api = inject(GenerationApi)) => {
    let subscription: Subscription | undefined;
    let refreshing = false;
    let destroyed = false;
    let dataVersion = 0;
    const report = (error: unknown) => {
      const message =
        error instanceof HttpErrorResponse
          ? error.error?.error || error.error?.detail || `HTTP ${error.status}`
          : error instanceof Error
            ? error.message
            : 'Request failed';
      if (!destroyed) patchState(store, { error: message });
    };
    const perform = async (operation: () => Promise<void>) => {
      if (store.busy() || destroyed) return false;
      patchState(store, { busy: true, error: '' });
      try {
        await operation();
        return true;
      } catch (error) {
        report(error);
        return false;
      } finally {
        if (!destroyed) patchState(store, { busy: false });
      }
    };
    const refresh = async () => {
      if (refreshing || destroyed) return;
      refreshing = true;
      const version = dataVersion;
      try {
        const [jobs, settings, savedPlans] = await Promise.all([api.jobs(), api.settings(), api.plans()]);
        if (!destroyed && version === dataVersion) {
          const plans = savedPlans.map(p => store.plans().find(local => local.id === p.id) ?? p);
          patchState(store, {
            jobs, plans,
            selectedPlanId: plans.some(p => p.id === store.selectedPlanId()) ? store.selectedPlanId() : plans[0]?.id ?? null,
            ...(settings.revision > (store.settings()?.revision ?? -1) ? { settings } : {}),
          });
        }
      } finally {
        refreshing = false;
      }
    };
    return {
      dismissError() { patchState(store, { error: '' }); },
      async deleteItem(target: DeleteTarget): Promise<ItemDeletionResult | null> {
        let result: ItemDeletionResult | null = null;
        await perform(async () => {
          dataVersion++;
          result = await api.deleteItem(target);
          dataVersion++;
          if (destroyed) return;
          const ids = new Set(target.ids ?? [target.id]);
          // Apply the confirmed deletion locally even if a later refresh fails.
          const removedJobs = new Set(store.jobs().filter(j => target.kind === 'plan' ? ids.has(j.planId) : target.kind === 'job' && ids.has(j.id)).map(j => j.id));
          const plans = target.kind === 'plan' ? store.plans().filter(p => !ids.has(p.id)) : store.plans();
          patchState(store, {
            plans,
            selectedPlanId: plans.some(p => p.id === store.selectedPlanId()) ? store.selectedPlanId() : plans[0]?.id ?? null,
            jobs: store.jobs().filter(j => !removedJobs.has(j.id)).map(j => target.kind === 'image'
              ? { ...j, tasks: j.tasks.map(t => ids.has(t.id) ? { ...t, imagePath: null } : t) } : j),
          });
        });
        return result;
      },
      async clearWorkspace(): Promise<WorkspaceClearResult | null> {
        let result: WorkspaceClearResult | null = null;
        await perform(async () => {
          dataVersion++;
          result = await api.clearWorkspace();
          dataVersion++;
          if (!destroyed) patchState(store, { plans: [], jobs: [], selectedPlanId: null });
        });
        return result;
      },
      async initialize() {
        await perform(async () => {
          const [settings, plans, jobs] = await Promise.all([
            api.settings(),
            api.plans(),
            api.jobs(),
          ]);
          if (!destroyed)
            patchState(store, { settings, plans, jobs, selectedPlanId: plans[0]?.id ?? null });
        });
        if (destroyed) return;
        subscription = api.events().subscribe((connected) => {
          patchState(store, { connected });
          if (connected) void refresh().catch(report);
        });
      },
      disconnect() {
        destroyed = true;
        subscription?.unsubscribe();
      },
      selectPlan(id: string) {
        patchState(store, { selectedPlanId: id });
      },
      editPrompt(id: string, text: string) {
        patchState(store, {
          plans: store.plans().map((p) =>
            store.queuedPlanIds().has(p.id)
              ? p
              : {
                  ...p,
                  prompts: p.prompts.map((prompt) =>
                    prompt.id === id ? { ...prompt, text } : prompt,
                  ),
                },
          ),
        });
      },
      createPlan(input: PlanInput) {
        return perform(async () => {
          const plan = await api.plan(input);
          if (!destroyed)
            patchState(store, { plans: [plan, ...store.plans()], selectedPlanId: plan.id });
        });
      },
      savePrompt(prompt: Prompt) {
        return perform(async () => {
          await api.savePrompt(prompt);
        });
      },
      updateSettings(input: SettingsInput) {
        return perform(async () => {
          const settings = await api.updateSettings(input);
          if (!destroyed && settings.revision >= (store.settings()?.revision ?? -1))
            patchState(store, { settings });
        });
      },
      enqueue(plan: Plan, options: JobOptions = { mode: 'Finite' }) {
        return perform(async () => {
          if (store.queuedPlanIds().has(plan.id)) return;
          for (const prompt of plan.prompts) await api.savePrompt(prompt);
          await api.enqueue(plan.id, options);
          await refresh();
        });
      },
      reviewTask(id: string, input: ReviewInput) {
        return perform(async () => {
          await api.reviewTask(id, input);
          await refresh();
        });
      },
      retryTask(id: string, input: RetryInput) {
        return perform(async () => {
          await api.retryTask(id, input);
          await refresh();
        });
      },
      controlJob(job: Job, action: JobAction) {
        return perform(async () => {
          await api.control(job.id, action);
          await refresh();
        });
      },
    };
  }),
  withHooks({
    onInit(store) {
      void store.initialize();
    },
    onDestroy(store) {
      store.disconnect();
    },
  }),
);
