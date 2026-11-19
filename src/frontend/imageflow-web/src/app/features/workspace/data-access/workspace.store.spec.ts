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
      settings: vi.fn().mockResolvedValue({ provider: 'Mock', maxConcurrency: 2 }),
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
});
