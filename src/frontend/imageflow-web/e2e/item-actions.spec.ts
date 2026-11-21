import { test, expect, Page } from '@playwright/test';

async function fixture(page: Page, running = false) {
  let plans = [
    { id: 'plan-one', theme: 'First collection', prompts: [{ id: 'prompt-one', text: 'first image' }] },
    { id: 'plan-two', theme: 'Second collection', prompts: [{ id: 'prompt-two', text: 'second image' }] },
  ];
  let jobs = plans.map((p, index) => ({ id: index ? 'job-two' : 'job-one', planId: p.id,
    status: running && !index ? 'Active' : 'Completed', mode: 'Finite', maxImages: 1, budgetUsd: null,
    estimatedExposureUsd: 0, estimatedCostPerImageUsd: 0,
    tasks: [{ id: index ? 'image-two' : 'image-one', status: running && !index ? 'Running' : 'Completed',
      promptText: p.prompts[0].text, imagePath: 'fixture.svg' as string | null, attempts: [], reviews: [] }],
  }));
  const deletes: string[] = [];
  await page.context().route('**/api/v1/**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    if (request.method() === 'DELETE') {
      expect(new URL(request.url()).searchParams.get('confirmed')).toBe('true');
      deletes.push(path);
      if (path.includes('/plans/')) { const id = path.split('/').pop(); plans = plans.filter(p => p.id !== id); jobs = jobs.filter(j => j.planId !== id); }
      else if (path.includes('/jobs/')) jobs = jobs.filter(j => j.id !== path.split('/').pop());
      else jobs = jobs.map(j => ({ ...j, tasks: j.tasks.map(t => t.id === path.split('/').pop() ? { ...t, imagePath: null } : t) }));
      await route.fulfill({ json: { plans: 0, jobs: 0, images: 1, pendingImageFiles: 0 } });
    } else if (path.includes('/images/')) await route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20"><rect width="20" height="20" fill="green"/></svg>' });
    else if (path.endsWith('/plans')) await route.fulfill({ json: plans });
    else if (path.endsWith('/jobs')) await route.fulfill({ json: jobs });
    else if (path.endsWith('/settings')) await route.fulfill({ json: { provider: 'OpenAICompatible', maxConcurrency: 2, estimatedCostPerImageUsd: 0, revision: 1 } });
    else if (path.endsWith('/events')) await route.fulfill({ contentType: 'text/event-stream', body: 'event: jobs\ndata: []\n\n' });
    else throw new Error(`Unexpected application request: ${request.method()} ${path}`);
  });
  await page.addInitScript(() => localStorage.setItem('imageflow.language', 'en'));
  return { deletes };
}
async function confirm(page: Page) {
  await expect(page.getByRole('button', { name: 'Permanently delete', exact: true })).toBeDisabled();
  await page.getByRole('dialog').getByRole('checkbox').check();
  await page.getByRole('button', { name: 'Permanently delete', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
}

test('cancel is harmless; deleting one plan removes only its related collection', async ({ page }) => {
  const state = await fixture(page);
  await page.goto('/prompts?plan=plan-one');
  await page.getByRole('button', { name: 'Delete plan: First collection', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('also deletes its jobs, images');
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  expect(state.deletes).toEqual([]);
  await page.getByRole('button', { name: 'Delete plan: First collection', exact: true }).click();
  await confirm(page);
  await expect(page.locator('.managed-plan')).toHaveCount(1);
  await expect(page.locator('.managed-plan')).toContainText('Second collection');
  await expect(page).toHaveURL(/\/prompts$/);
  await page.reload(); await expect(page.locator('.managed-plan')).toHaveCount(1);
  expect(state.deletes).toEqual(['/api/v1/plans/plan-one']);
});

test('deleting a job keeps its plan and the other job', async ({ page }) => {
  const state = await fixture(page); await page.goto('/queue');
  await page.locator('article.job').filter({ hasText: 'First collection' }).getByRole('button', { name: 'Delete job', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('Its prompt plan is kept'); await confirm(page);
  await expect(page.locator('article.job')).toHaveCount(1);
  await page.getByRole('link', { name: 'Prompt management', exact: true }).click();
  await expect(page.locator('.managed-plan')).toHaveCount(2);
  expect(state.deletes).toEqual(['/api/v1/jobs/job-one']);
});

test('collection ZIP link targets the selected job even when search matches no images; single image deletion leaves other collections', async ({ page }) => {
  const state = await fixture(page); await page.goto('/gallery?job=job-one');
  await expect(page.locator('.gallery img')).toHaveCount(1);
  await page.getByLabel('Search images', { exact: true }).fill('no matching image');
  await expect(page.locator('.gallery img')).toHaveCount(0);
  // Native browser downloads bypass Playwright request interception. Verify routing here;
  // backend integration tests download and inspect the real ZIP without touching live data.
  await expect(page.getByRole('link', { name: 'Download collection ZIP', exact: true })).toHaveAttribute('href', '/api/v1/jobs/job-one/images.zip');
  await page.getByLabel('Search images', { exact: true }).fill('');
  await page.getByRole('button', { name: 'Delete image: first image', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('will not be generated again automatically');
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator('body')).toHaveJSProperty('scrollWidth', 390);
  await page.screenshot({ path: '../../../.cache/delete-image-mobile.png', animations: 'disabled' });
  await confirm(page);
  await page.getByRole('button', { name: 'Reset filters', exact: true }).click();
  await expect(page.locator('.gallery img')).toHaveCount(1);
  await expect(page.locator('.gallery img')).toHaveAttribute('alt', 'second image');
  expect(state.deletes).toEqual(['/api/v1/images/image-one']);
});

test('in-flight job cannot be deleted from its confirmation dialog', async ({ page }) => {
  const state = await fixture(page, true); await page.goto('/queue');
  await page.locator('article.job').filter({ hasText: 'First collection' }).getByRole('button', { name: 'Delete job', exact: true }).click();
  await page.getByRole('dialog').getByRole('checkbox').check();
  await expect(page.getByRole('button', { name: 'Permanently delete', exact: true })).toBeDisabled();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('wait for in-flight requests');
  expect(state.deletes).toEqual([]);
});




