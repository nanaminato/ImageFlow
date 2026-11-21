import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { GenerationApi } from '../../../core/data-access/generation-api.service';
import { Job, Plan } from '../../../core/models/generation.models';
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
    reviewTask: ReturnType<typeof vi.fn>;
    retryTask: ReturnType<typeof vi.fn>;
    clearWorkspace: ReturnType<typeof vi.fn>;
    deleteItem: ReturnType<typeof vi.fn>;
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
      reviewTask: vi.fn().mockResolvedValue({}),
      retryTask: vi.fn().mockResolvedValue({}),
      deleteItem: vi.fn().mockResolvedValue({ plans: 1, jobs: 0, images: 0, pendingImageFiles: 0 }),
      clearWorkspace: vi.fn().mockResolvedValue({ plans: 1, jobs: 0, tasks: 0, images: 0, remainingImageFiles: 0 }),
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
  it('clears records and selection while retaining engine settings', async () => {
    const store = await initialized(); const settings = store.settings();
    const result = await store.clearWorkspace();
    expect(api.clearWorkspace).toHaveBeenCalledTimes(1);
    expect(result?.remainingImageFiles).toBe(0);
    expect(store.plans()).toEqual([]); expect(store.jobs()).toEqual([]);
    expect(store.selectedPlan()).toBeNull(); expect(store.settings()).toEqual(settings);
  });
  it('ignores a stale SSE refresh that finishes after clearing', async () => {
    const store = await initialized();
    let resolve!: (jobs: Job[]) => void;
    api.jobs.mockReturnValueOnce(new Promise<Job[]>(done => resolve = done));
    events.next(true);
    await store.clearWorkspace();
    resolve([{ id: 'stale-job', planId: plan.id, status: 'Completed', tasks: [], mode: 'Finite', maxImages: 1, budgetUsd: null, estimatedCostPerImageUsd: 0, estimatedExposureUsd: 0, stopReason: null }]);
    await new Promise(done => setTimeout(done, 0));
    expect(store.jobs()).toEqual([]); expect(store.plans()).toEqual([]);
  });
  it('retains workspace records when clearing is rejected', async () => {
    const store = await initialized();
    api.clearWorkspace.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { error: 'Requests still running' } }));
    expect(await store.clearWorkspace()).toBeNull();
    expect(store.plans()).toEqual([plan]); expect(store.error()).toBe('Requests still running');
  });
  it('deletes only the selected plan and keeps other local drafts', async () => {
    api.plans.mockResolvedValue([plan, { ...plan, id: 'other-plan' }]);
    const store = await initialized();
    const result = await store.deleteItem({ kind: 'plan', id: plan.id, label: plan.theme, jobs: 0, images: 0, running: false });
    expect(result?.plans).toBe(1); expect(store.plans().map(p => p.id)).toEqual(['other-plan']);
    expect(store.selectedPlanId()).toBe('other-plan'); expect(store.settings()?.provider).toBe('Mock');
  });
  it('removes an image without removing the task or its usage history', async () => {
    api.jobs.mockResolvedValue([{ id: 'job-1', planId: plan.id, tasks: [{ id: 'image-1', imagePath: 'saved.png', status: 'Completed', attempts: [{ id: 'attempt' }] }] }]);
    const store = await initialized();
    await store.deleteItem({ kind: 'image', id: 'image-1', label: 'image', jobs: 0, images: 1, running: false });
    expect(store.images()).toEqual([]); expect(store.jobs()).toHaveLength(1);
    expect(store.jobs()[0].tasks[0].attempts).toEqual([{ id: 'attempt' }]);
  });
  it('does not remove records on rejected deletion', async () => {
    const store = await initialized();
    api.deleteItem.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { error: 'In-flight request' } }));
    expect(await store.deleteItem({ kind: 'plan', id: plan.id, label: 'plan', jobs: 1, images: 1, running: false })).toBeNull();
    expect(store.plans()).toEqual([plan]); expect(store.error()).toBe('In-flight request');
  });
  it('reflects remote plan deletion while retaining unsaved edits on other plans', async () => {
    const other = { ...plan, id: 'other-plan' }; api.plans.mockResolvedValue([plan, other]);
    const store = await initialized(); store.editPrompt('prompt-1', 'unsaved');
    api.plans.mockResolvedValue([other]); events.next(true);
    await vi.waitFor(() => expect(store.plans()).toHaveLength(1));
    expect(store.selectedPlan()?.id).toBe(other.id); expect(store.selectedPlan()?.prompts[0].text).toBe('unsaved');
  });
  it('applies a confirmed batch plan deletion to all selected IDs and keeps unselected plans', async () => {
    api.plans.mockResolvedValue([plan, { ...plan, id: 'plan-2' }, { ...plan, id: 'plan-3' }]);
    api.jobs.mockResolvedValue([{ id: 'job-1', planId: plan.id, tasks: [] }, { id: 'job-2', planId: 'plan-2', tasks: [] }, { id: 'job-3', planId: 'plan-3', tasks: [] }]);
    const store = await initialized();
    await store.deleteItem({ kind: 'plan', id: plan.id, ids: [plan.id, 'plan-2'], label: 'batch', jobs: 2, images: 0, running: false });
    expect(store.plans().map(p => p.id)).toEqual(['plan-3']);
    expect(store.jobs().map(j => j.id)).toEqual(['job-3']);
    expect(store.selectedPlanId()).toBe('plan-3');
  });
  it('a rejected batch leaves all selected plans in the local store', async () => {
    api.plans.mockResolvedValue([plan, { ...plan, id: 'plan-2' }]);
    const store = await initialized();
    api.deleteItem.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { error: 'Running item' } }));
    expect(await store.deleteItem({ kind: 'plan', id: plan.id, ids: [plan.id, 'plan-2'], label: 'batch', jobs: 1, images: 0, running: false })).toBeNull();
    expect(store.plans()).toHaveLength(2);
  });
  it('counts running image requests across jobs and updates after SSE without changing the configured limit', async () => {
    api.jobs.mockResolvedValue([
      { id: 'paused-job', status: 'Paused', tasks: [{ status: 'Running' }, { status: 'Pending' }, { status: 'Unknown' }] },
      { id: 'active-job', status: 'Active', tasks: [{ status: 'Running' }, { status: 'Completed' }] },
    ]);
    const store = await initialized();
    expect(store.runningImageRequests()).toBe(2);
    expect(store.hasRunningRequests()).toBe(true);
    api.jobs.mockResolvedValue([{ id: 'paused-job', status: 'Paused', tasks: [{ status: 'Completed' }, { status: 'Pending' }] }]);
    events.next(true);
    await vi.waitFor(() => expect(store.runningImageRequests()).toBe(0));
    expect(store.hasRunningRequests()).toBe(false);
    expect(store.settings()?.maxConcurrency).toBe(2);
    expect(store.providerLabel()).toContain('Concurrency limit: 2 · Generating now: 0');
  });
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
    expect(store.providerLabel()).toContain('Concurrency limit: 4');
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
  it('records a review without authorizing retry or resuming a job', async () => {
    const store = await initialized();
    const input = {
      attemptId: 'attempt-1',
      resolution: 'NotGeneratedNotCharged',
      evidence: 'Provider records checked',
      confirmed: true,
    };
    await store.reviewTask('task-1', input);
    expect(api.reviewTask).toHaveBeenCalledWith('task-1', input);
    expect(api.retryTask).not.toHaveBeenCalled();
    expect(api.control).not.toHaveBeenCalled();
    expect(api.enqueue).not.toHaveBeenCalled();
  });
  it('authorizes retry only through a separate command and never auto-resumes', async () => {
    const store = await initialized();
    await store.retryTask('task-1', { attemptId: 'attempt-1', confirmed: true });
    expect(api.retryTask).toHaveBeenCalledWith('task-1', {
      attemptId: 'attempt-1',
      confirmed: true,
    });
    expect(api.control).not.toHaveBeenCalled();
    expect(api.enqueue).not.toHaveBeenCalled();
  });
  it('displays stale-review rejection without attempting another request', async () => {
    const store = await initialized();
    api.reviewTask.mockRejectedValue(
      new HttpErrorResponse({ status: 409, error: { error: 'Stale review' } }),
    );
    await store.reviewTask('task-1', {
      attemptId: 'old',
      resolution: 'Generated',
      evidence: 'checked',
      confirmed: true,
    });
    expect(store.error()).toBe('Stale review');
    expect(store.busy()).toBe(false);
    expect(api.retryTask).not.toHaveBeenCalled();
  });
});
