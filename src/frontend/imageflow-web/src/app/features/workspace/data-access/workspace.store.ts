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
  EngineSettings,
  SettingsInput,
  JobOptions,
  Job,
  JobAction,
  Plan,
  PlanInput,
  Prompt,
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
    providerLabel: computed(() => {
      const s = store.settings();
      return s ? `${s.provider} · ${s.maxConcurrency} outstanding requests` : 'Loading settings…';
    }),
  })),
  withMethods((store, api = inject(GenerationApi)) => {
    let subscription: Subscription | undefined;
    let refreshing = false;
    let destroyed = false;
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
      if (store.busy() || destroyed) return;
      patchState(store, { busy: true, error: '' });
      try {
        await operation();
      } catch (error) {
        report(error);
      } finally {
        if (!destroyed) patchState(store, { busy: false });
      }
    };
    const refresh = async () => {
      if (refreshing || destroyed) return;
      refreshing = true;
      try {
        const [jobs, settings] = await Promise.all([api.jobs(), api.settings()]);
        if (!destroyed)
          patchState(store, {
            jobs,
            ...(settings.revision > (store.settings()?.revision ?? -1) ? { settings } : {}),
          });
      } finally {
        refreshing = false;
      }
    };
    return {
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
