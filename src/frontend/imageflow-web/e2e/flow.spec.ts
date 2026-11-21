import { test, expect } from '@playwright/test';
test('plan, edit, queue and view generated collection', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByText('Mock · Concurrency limit: 2 · Generating now: 0')).toBeVisible();
  await page.getByLabel('Theme & constraints').fill('Quiet coastal towns in watercolor');
  await page.getByLabel('Images', { exact: true }).fill('2');
  await page.getByRole('button', { name: 'Plan prompts', exact: true }).click();
  await expect(page.getByLabel('Prompt 1', { exact: true })).toBeVisible();
  await page.getByLabel('Prompt 1', { exact: true }).fill('A seaside lighthouse in morning light');
  await page.getByRole('button', { name: 'Save & queue collection' }).click();
  await expect(page.getByText('2 / 2 completed', { exact: true })).toBeVisible({ timeout: 15000 });
  await expect(page).toHaveURL(/\/queue\?plan=/);
  await expect(page.locator('app-prompt-editor')).toHaveCount(0);
  await page.locator('.job-summary').click();
  await page.getByRole('link', { name: 'View images', exact: true }).click();
  await expect(page).toHaveURL(/\/gallery\?job=/);
  await expect(page.locator('.gallery img')).toHaveCount(2);
  await page.getByRole('link', { name: 'Prompt management', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Already queued' })).toBeDisabled();
  await page.getByRole('link', { name: 'Collection library', exact: true }).click();
  await page.reload();
  await expect(page.locator('.gallery img')).toHaveCount(2);
  await page.screenshot({ path: '../../../.cache/e2e-workspace.png', fullPage: true });
  const image = await page.request.get(
    (await page.locator('.gallery img').first().getAttribute('src')) || '',
  );
  expect(image.headers()['content-type']).toContain('image/svg+xml');
});

test('planning errors keep existing state and release the form', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('Theme & constraints').fill('Unavailable provider test');
  await page.route('**/api/v1/plans', async (route) => {
    if (route.request().method() === 'POST')
      await route.fulfill({
        status: 502,
        contentType: 'application/problem+json',
        body: JSON.stringify({ detail: 'Provider temporarily unavailable' }),
      });
    else await route.continue();
  });
  await page.getByRole('button', { name: 'Plan prompts', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveText('Provider temporarily unavailable');
  await expect(page).toHaveURL(/\/create$/);
  await expect(page.getByRole('button', { name: 'Plan prompts', exact: true })).toBeEnabled();
  await expect(page.locator('app-image-gallery')).toHaveCount(0);
  await page.getByRole('link', { name: 'Collection library', exact: true }).click();
  await expect(page.locator('.gallery img')).toHaveCount(2);
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator('body')).toHaveJSProperty('scrollWidth', 390);
});

test('engine settings stay off the main page, open in a dialog and persist', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByText('Mock · Concurrency limit: 2 · Generating now: 0')).toBeVisible();
  await expect(page.getByLabel('Concurrent image requests')).toHaveCount(0);

  await page.getByRole('button', { name: 'Engine settings' }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.getByLabel('Concurrent image requests').fill('1');
  await page.getByLabel('Estimated USD per image request').fill('0.01');
  await page.waitForTimeout(1200); // An SSE refresh must not discard the unsaved form draft.
  await expect(page.getByLabel('Concurrent image requests')).toHaveValue('1');
  await page.getByRole('button', { name: 'Save settings', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.getByText('Mock · Concurrency limit: 1 · Generating now: 0')).toBeVisible();

  await page.reload();
  await expect(page.getByLabel('Concurrent image requests')).toHaveCount(0);
  await expect(page.getByText('Mock · Concurrency limit: 1 · Generating now: 0')).toBeVisible();
  await page.getByRole('button', { name: 'Engine settings' }).click();
  await expect(page.getByLabel('Concurrent image requests')).toHaveValue('1');
  await expect(page.getByLabel('Estimated USD per image request')).toHaveValue('0.01');
  await page.getByRole('button', { name: 'Close settings' }).click();
  await expect(page.getByRole('dialog')).toBeHidden();
  await page.getByLabel('Theme & constraints').fill('Continuous collection with estimate cap');
  await page.getByLabel('Images', { exact: true }).fill('1');
  await page.getByRole('button', { name: 'Plan prompts', exact: true }).click();
  await expect(page.getByLabel('Prompt 1', { exact: true })).toBeVisible();
  await page.getByLabel('Generation mode').selectOption('Continuous');
  await page.getByLabel('Maximum continuous images').fill('5');
  await page.getByLabel('Estimated budget limit (USD)').fill('0.025');
  await page.getByRole('button', { name: 'Save & queue collection' }).click();
  await page.locator('.job-summary').first().click();
  const job = page.locator('article.job').filter({ hasText: 'Stopped: BudgetLimit' });
  await expect(job).toBeVisible({ timeout: 15000 });
  await expect(job.getByText('2 / 5 completed', { exact: true })).toBeVisible();
  await expect(job).toContainText('$0.02');
  await page.reload();
  await page.locator('.job-summary').first().click();
  await expect(
    page.locator('article.job').filter({ hasText: 'Stopped: BudgetLimit' }),
  ).toBeVisible();
  await page.screenshot({ path: '../../../.cache/continuous-workspace.png', fullPage: true });
});

test('manual review does not implicitly retry or resume (intercepted API)', async ({ page }) => {
  const attemptId = 'review-attempt';
  const task: any = {
    id: 'review-task',
    status: 'Unknown',
    promptText: 'Uncertain image request',
    error: 'Connection interrupted',
    imagePath: null,
    nextAttemptAt: null,
    reviews: [],
    attempts: [
      {
        id: attemptId,
        outcome: 'Unknown',
        startedAt: '2026-11-19T12:00:00Z',
        finishedAt: '2026-11-19T12:01:00Z',
        provider: 'Mock',
        model: 'mock-svg',
        requestId: 'req-review-test',
        estimatedCostUsd: 0.01,
        costDisposition: 'Uncertain',
        error: 'Connection interrupted',
        usageJson: null,
      },
    ],
  };
  const job: any = {
    id: 'review-job',
    planId: 'fixture-plan',
    status: 'NeedsAttention',
    mode: 'Continuous',
    maxImages: 1,
    budgetUsd: 0.01,
    estimatedCostPerImageUsd: 0.01,
    estimatedExposureUsd: 0.01,
    stopReason: 'UnknownOutcome',
    tasks: [task],
  };
  let reviews = 0,
    retries = 0,
    otherPosts = 0;
  await page.route('**/api/v1/jobs', (route) => route.fulfill({ json: [job] }));
  await page.route('**/api/v1/tasks/**', async (route) => {
    const body = route.request().postDataJSON();
    expect(body.attemptId).toBe(attemptId);
    expect(body.confirmed).toBe(true);
    if (route.request().url().endsWith('/reviews')) {
      reviews++;
      expect(body.resolution).toBe('NotGeneratedNotCharged');
      task.status = 'Paused';
      task.attempts[0].estimatedCostUsd = 0;
      task.attempts[0].costDisposition = 'ManuallyReleased';
      job.stopReason = 'ManualReview';
      job.status = 'Paused';
      job.estimatedExposureUsd = 0;
      task.reviews = [
        {
          id: 'review-1',
          attemptId,
          resolution: body.resolution,
          evidence: body.evidence,
          reviewedAt: '2026-11-19T13:00:00Z',
          retryAuthorizedAt: null,
        },
      ];
    } else {
      retries++;
      task.status = 'Pending';
      task.reviews[0].retryAuthorizedAt = '2026-11-19T13:01:00Z';
    }
    await route.fulfill({ json: task });
  });
  page.on('request', (r) => {
    if (r.method() === 'POST' && !r.url().includes('/api/v1/tasks/')) otherPosts++;
  });
  await page.goto('/queue');
  await page.locator('.job-summary').click();
  await page.getByText('Unknown · Uncertain image request', { exact: true }).click();
  await expect(page.getByText('req-review-test', { exact: false })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Record review without retry' })).toBeDisabled();
  await page.getByLabel('Review conclusion').selectOption('NotGeneratedNotCharged');
  await page
    .getByLabel('Provider evidence')
    .fill('Provider billing checked: no output and no charge.');
  await page.getByLabel('I checked provider records and confirm this conclusion.').check();
  await page.getByRole('button', { name: 'Record review without retry' }).click();
  await expect(page.getByRole('button', { name: 'Authorize reviewed retry' })).toBeDisabled();
  expect(reviews).toBe(1);
  expect(retries).toBe(0);
  expect(otherPosts).toBe(0);
  await page.getByLabel('I authorize a new request that may incur a charge.').check();
  await page.getByRole('button', { name: 'Authorize reviewed retry' }).click();
  await expect(page.getByText('Pending · Uncertain image request', { exact: true })).toBeVisible();
  expect(retries).toBe(1);
  expect(otherPosts).toBe(0);
  await expect(page.getByRole('button', { name: 'Resume', exact: true })).toBeVisible();
  await page.reload();
  await page.locator('.job-summary').click();
  await page.getByText('Pending · Uncertain image request', { exact: true }).click();
  await expect(
    page.getByText('Provider billing checked: no output and no charge.', { exact: true }),
  ).toBeVisible();
  await expect(page.getByText('Attempt 1 · Unknown', { exact: true })).toBeVisible();
  await page.screenshot({ path: '../../../.cache/task-review-workspace.png', fullPage: true });
});
