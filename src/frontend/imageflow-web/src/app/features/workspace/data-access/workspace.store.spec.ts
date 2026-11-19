import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { GenerationApi } from '../../../core/data-access/generation-api.service';
import { Plan } from '../../../core/models/generation.models';
import { WorkspaceStore } from './workspace.store';

describe('WorkspaceStore', () => {
  const plan: Plan = {
    id: 'plan-1',
    theme: 'coast',
    prompts: [{ id: 'prompt-1', text: 'original' }],
  };
  let events: Subject<boolean>;
  let api: {
    settings: ReturnType<typeof vi.fn>;
    updateSettings: ReturnType<typeof vi.fn>;
    plans: ReturnType<typeof vi.fn>;
    jobs: ReturnType<typeof vi.fn>;
    plan: ReturnType<typeof vi.fn>;
    savePrompt: ReturnType<typeof vi.fn>;
    enqueue: ReturnType<typeof vi.fn>;
    control: ReturnType<typeof vi.fn>;
    events: ReturnType<typeof vi.fn>;
  };
  beforeEach(() => {
    events = new Subject<boolean>();
    api = {
      settings: vi.fn().mockResolvedValue({
        provider: 'Mock',
        maxConcurrency: 2,
        estimatedCostPerImageUsd: 0,
        revision: 1,
      }),
      updateSettings: vi.fn(),
      plans: vi.fn().mockResolvedValue([plan]),
      jobs: vi.fn().mockResolvedValue([]),
      plan: vi.fn(),
      savePrompt: vi.fn().mockResolvedValue({}),
      enqueue: vi.fn().mockResolvedValue({}),
      control: vi.fn(),
      events: vi.fn().mockReturnValue(events),
    };
    TestBed.configureTestingModule({
      providers: [WorkspaceStore, { provide: GenerationApi, useValue: api }],
    });
  });
  async function initialized() {
    const store = TestBed.inject(WorkspaceStore);
    await vi.waitFor(() => expect(store.busy()).toBe(false));
    return store;
  }
  it('loads state and selects a saved plan on initialization', async () => {
    const store = await initialized();
    expect(store.selectedPlan()?.id).toBe(plan.id);
    expect(store.providerLabel()).toContain('Mock');
  });
  it('edits immutable drafts without mutating API objects', async () => {
    const store = await initialized();
    store.editPrompt('prompt-1', 'edited');
    expect(store.selectedPlan()?.prompts[0].text).toBe('edited');
    expect(plan.prompts[0].text).toBe('original');
  });
  it('persists edits before enqueueing and prevents duplicate enqueue', async () => {
    const store = await initialized();
    store.editPrompt('prompt-1', 'edited');
    api.jobs.mockResolvedValue([{ id: 'job-1', planId: plan.id, status: 'Active', tasks: [] }]);
    await store.enqueue(store.selectedPlan()!);
    expect(api.savePrompt).toHaveBeenCalledWith({ id: 'prompt-1', text: 'edited' });
    expect(api.savePrompt.mock.invocationCallOrder[0]).toBeLessThan(
      api.enqueue.mock.invocationCallOrder[0],
    );
    await store.enqueue(store.selectedPlan()!);
    expect(api.enqueue).toHaveBeenCalledTimes(1);
    store.editPrompt('prompt-1', 'forbidden');
    expect(store.selectedPlan()?.prompts[0].text).toBe('edited');
  });
  it('reports API failure, releases busy state and allows another command', async () => {
    const store = await initialized();
    api.plan.mockRejectedValueOnce(
      new HttpErrorResponse({ status: 502, error: { detail: 'Provider unavailable' } }),
    );
    await store.createPlan({ theme: 'theme', count: 1 });
    expect(store.error()).toBe('Provider unavailable');
    expect(store.busy()).toBe(false);
    api.plan.mockResolvedValue({ ...plan, id: 'plan-2' });
    await store.createPlan({ theme: 'theme', count: 1 });
    expect(store.error()).toBe('');
    expect(store.selectedPlan()?.id).toBe('plan-2');
  });
  it('closes SSE subscription when its scope is destroyed', async () => {
    const store = await initialized();
    events.next(true);
    expect(store.connected()).toBe(true);
    TestBed.resetTestingModule();
    expect(events.observed).toBe(false);
  });
  it('persists settings and updates the effective concurrency label', async () => {
    const store = await initialized();
    api.updateSettings.mockResolvedValue({
      provider: 'Mock',
      maxConcurrency: 4,
      estimatedCostPerImageUsd: 0.02,
      revision: 2,
    });
    await store.updateSettings({ maxConcurrency: 4, estimatedCostPerImageUsd: 0.02 });
    expect(api.updateSettings).toHaveBeenCalledWith({
      maxConcurrency: 4,
      estimatedCostPerImageUsd: 0.02,
    });
    expect(store.providerLabel()).toContain('4 outstanding requests');
    expect(store.settings()?.estimatedCostPerImageUsd).toBe(0.02);
  });
  it('keeps persisted settings visible when an update fails', async () => {
    const store = await initialized();
    api.updateSettings.mockRejectedValue(
      new HttpErrorResponse({ status: 400, error: { error: 'Invalid settings' } }),
    );
    await store.updateSettings({ maxConcurrency: 0, estimatedCostPerImageUsd: 0 });
    expect(store.settings()?.maxConcurrency).toBe(2);
    expect(store.error()).toBe('Invalid settings');
    expect(store.busy()).toBe(false);
  });
  it('passes continuous safety limits after persisting prompts', async () => {
    const store = await initialized();
    await store.enqueue(store.selectedPlan()!, {
      mode: 'Continuous',
      maxImages: 10,
      budgetUsd: 0.1,
    });
    expect(api.enqueue).toHaveBeenCalledWith(plan.id, {
      mode: 'Continuous',
      maxImages: 10,
      budgetUsd: 0.1,
    });
    expect(api.savePrompt.mock.invocationCallOrder[0]).toBeLessThan(
      api.enqueue.mock.invocationCallOrder[0],
    );
  });
  it('refreshes settings from live notifications without replacing unsaved prompt drafts', async () => {
    const store = await initialized();
    store.editPrompt('prompt-1', 'local draft');
    api.settings.mockResolvedValue({
      provider: 'Mock',
      maxConcurrency: 3,
      estimatedCostPerImageUsd: 0.01,
      revision: 2,
    });
    events.next(true);
    await vi.waitFor(() => expect(store.settings()?.maxConcurrency).toBe(3));
    expect(store.selectedPlan()?.prompts[0].text).toBe('local draft');
  });
  it('does not roll back a saved settings revision when an older SSE refresh arrives', async () => {
    const store = await initialized();
    api.updateSettings.mockResolvedValue({
      provider: 'Mock',
      maxConcurrency: 4,
      estimatedCostPerImageUsd: 0.02,
      revision: 2,
    });
    await store.updateSettings({ maxConcurrency: 4, estimatedCostPerImageUsd: 0.02 });
    events.next(true);
    await vi.waitFor(() => expect(api.settings).toHaveBeenCalledTimes(2));
    expect(store.settings()?.revision).toBe(2);
    expect(store.settings()?.maxConcurrency).toBe(4);
  });
});
