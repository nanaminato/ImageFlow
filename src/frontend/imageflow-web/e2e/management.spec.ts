import { test, expect } from '@playwright/test';

test.use({ locale: 'en-US' });

test.beforeEach(async ({ page }) => {
  const plans = [
    {
      id: 'alpha',
      theme: 'Alpine collection',
      prompts: [{ id: 'p-alpha', text: 'Snowy mountain' }],
    },
    {
      id: 'beta',
      theme: 'Coastal collection',
      prompts: [{ id: 'p-beta', text: 'Quiet lighthouse' }],
    },
    {
      id: 'draft',
      theme: 'Draft plan',
      prompts: [{ id: 'p-draft', text: 'Original draft prompt' }],
    },
  ];
  const jobs = plans.slice(0, 2).map((p, i) => ({
    id: `job-${p.id}`,
    planId: p.id,
    status: i ? 'Paused' : 'Completed',
    mode: 'Finite',
    maxImages: 1,
    estimatedExposureUsd: 0.01,
    estimatedCostPerImageUsd: 0.01,
    budgetUsd: null,
    stopReason: null,
    tasks: [
      {
        id: `image-${p.id}`,
        status: 'Completed',
        promptText: p.prompts[0].text,
        imagePath: 'fixture.svg',
        error: null,
        nextAttemptAt: null,
        attempts: [],
        reviews: [],
      },
    ],
  }));
  await page.route('**/api/v1/**', async (route) => {
    const path = new URL(route.request().url()).pathname;
    expect(route.request().method()).toBe('GET'); // This scenario must never enqueue or send work.
    if (path.endsWith('/settings'))
      await route.fulfill({
        json: {
          provider: 'Mock',
          promptModel: 'mock',
          imageModel: 'mock-svg',
          maxConcurrency: 2,
          estimatedCostPerImageUsd: 0.01,
          revision: 1,
          estimateConfirmed: true,
          imageSize: '768x512',
          imageQuality: 'preview',
          imageComplexity: 'none',
          outputFormat: 'svg',
        },
      });
    else if (path.endsWith('/plans')) await route.fulfill({ json: plans });
    else if (path.endsWith('/jobs')) await route.fulfill({ json: jobs });
    else if (path.includes('/images/'))
      await route.fulfill({
        contentType: 'image/svg+xml',
        body: '<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40"><rect width="40" height="40" fill="#0f6b58"/></svg>',
      });
    else
      await route.fulfill({ contentType: 'text/event-stream', body: 'event: jobs\ndata: []\n\n' });
  });
});

test('separate management pages preserve drafts and support search and filters', async ({
  page,
}) => {
  await page.goto('/');
  await expect(page).toHaveURL(/\/create$/);
  await expect(page.locator('app-prompt-editor, app-job-queue, app-image-gallery')).toHaveCount(0);
  await page.getByLabel('Theme & constraints').fill('Unsubmitted idea');
  await page.getByRole('link', { name: 'Prompt management', exact: true }).click();
  await page.getByLabel('Plan status', { exact: true }).selectOption('draft');
  await expect(page.locator('.managed-plan')).toHaveCount(1);
  await page.getByRole('link', { name: /Draft plan/ }).click();
  await page.getByLabel('Prompt 1', { exact: true }).fill('Unsaved prompt edit');
  await page.getByLabel('Generation mode').selectOption('Continuous');
  await page.getByLabel('Maximum continuous images').fill('8');
  await page.getByLabel('Estimated budget limit (USD)').fill('0.5');
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: '../../../.cache/management-prompts.png', fullPage: true });

  await page.getByRole('link', { name: 'Queue management', exact: true }).click();
  await expect(page.locator('app-prompt-editor, app-image-gallery')).toHaveCount(0);
  await page.getByLabel('Job status', { exact: true }).selectOption('Paused');
  await expect(page.locator('article.job')).toHaveCount(1);
  await expect(page.locator('article.job')).toContainText('Coastal collection');
  await page.getByLabel('Search jobs').fill('no matching task');
  await expect(page.getByText('No matching jobs.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Reset filters' }).click();
  await expect(page.locator('article.job')).toHaveCount(2);
  await page.screenshot({ path: '../../../.cache/management-queue.png', fullPage: true });

  await page.getByRole('link', { name: 'Create collection', exact: true }).first().click();
  await expect(page.getByLabel('Theme & constraints')).toHaveValue('Unsubmitted idea');
  await page.getByRole('link', { name: 'Prompt management', exact: true }).click();
  await expect(page.getByLabel('Prompt 1', { exact: true })).toHaveValue('Unsaved prompt edit');
  await expect(page.getByLabel('Maximum continuous images')).toHaveValue('8');
  await expect(page.getByLabel('Estimated budget limit (USD)')).toHaveValue('0.5');
  await page.getByLabel('Plan status', { exact: true }).selectOption('all');
  await page.getByLabel('Search plans').fill('coastal');
  await expect(page.locator('.managed-plan')).toHaveCount(1);
  await page.getByRole('link', { name: /Coastal collection/ }).click();
  await expect(page.getByRole('button', { name: 'Already queued' })).toBeDisabled();
  await page.getByRole('link', { name: 'View this plan in queue' }).click();
  await expect(page.locator('article.job')).toHaveCount(1);
  await page.locator('.job-summary').click();
  await page.getByRole('link', { name: 'View images', exact: true }).click();
  await expect(page).toHaveURL(/\/gallery\?job=job-beta/);
  await expect(page.locator('.gallery img')).toHaveCount(1);
  await page.getByLabel('Search images').fill('unmatched');
  await expect(page.getByText('No matching images.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Reset filters' }).click();
  await expect(page.locator('.gallery img')).toHaveCount(2);
  await page.screenshot({ path: '../../../.cache/management-gallery.png', fullPage: true });
});

test('direct links, reload, back navigation and mobile layout work', async ({ page }) => {
  await page.goto('/prompts?plan=draft');
  await expect(page.getByLabel('Prompt 1', { exact: true })).toHaveValue('Original draft prompt');
  await page.reload();
  await expect(page.getByLabel('Prompt 1', { exact: true })).toHaveValue('Original draft prompt');
  await page.getByRole('link', { name: 'Collection library', exact: true }).click();
  await expect(page.locator('.gallery img')).toHaveCount(2);
  await page.goBack();
  await expect(page).toHaveURL(/\/prompts\?plan=draft/);
  await expect(page.getByLabel('Prompt 1', { exact: true })).toHaveValue('Original draft prompt');
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByLabel('Language', { exact: true }).selectOption('zh');
  for (const [path, name] of [
    ['/create', '创建作品集'],
    ['/prompts', '提示词管理'],
    ['/queue', '队列管理'],
    ['/gallery', '作品集'],
  ]) {
    await page.getByRole('link', { name, exact: true }).first().click();
    await expect(page).toHaveURL(new RegExp(path + '$'));
    await expect(page.locator('body')).toHaveJSProperty('scrollWidth', 390);
    await expect(page.locator('.workspace-nav a[aria-current="page"]')).toHaveText(
      new RegExp(name),
    );
  }
  await expect(page.locator('.gallery img')).toHaveCount(2);
  await page.screenshot({ path: '../../../.cache/management-mobile.png', fullPage: true });
});
