import { test, expect } from '@playwright/test';
test('plan, edit, queue and view generated collection', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByText('Mock · 2 outstanding requests')).toBeVisible();
  await page.getByLabel('Theme & constraints').fill('Quiet coastal towns in watercolor');
  await page.getByLabel('Images', { exact: true }).fill('2');
  await page.getByRole('button', { name: 'Plan prompts', exact: true }).click();
  await expect(page.getByLabel('Prompt 1', { exact: true })).toBeVisible();
  await page.getByLabel('Prompt 1', { exact: true }).fill('A seaside lighthouse in morning light');
  await page.getByRole('button', { name: 'Save & queue collection' }).click();
  await expect(page.getByText('2 / 2 completed', { exact: true })).toBeVisible({ timeout: 15000 });
  await expect(page.locator('.gallery img')).toHaveCount(2);
  await expect(page.getByRole('button', { name: 'Already queued' })).toBeDisabled();
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
  await expect(page.getByRole('button', { name: 'Plan prompts', exact: true })).toBeEnabled();
  await expect(page.locator('.gallery img')).toHaveCount(2);
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator('body')).toHaveJSProperty('scrollWidth', 390);
});

test('runtime settings persist and continuous generation stops at its estimate limit', async ({
  page,
}) => {
  await page.goto('/');
  await page.getByLabel('Concurrent image requests').fill('1');
  await page.getByLabel('Estimated USD per image request').fill('0.01');
  await page.waitForTimeout(1200); // An SSE refresh must not discard the unsaved form draft.
  await expect(page.getByLabel('Concurrent image requests')).toHaveValue('1');
  await page.getByRole('button', { name: 'Save settings', exact: true }).click();
  await expect(page.getByText('Mock · 1 outstanding requests')).toBeVisible();
  await page.reload();
  await expect(page.getByLabel('Concurrent image requests')).toHaveValue('1');
  await expect(page.getByLabel('Estimated USD per image request')).toHaveValue('0.01');
  await page.getByLabel('Theme & constraints').fill('Continuous collection with estimate cap');
  await page.getByLabel('Images', { exact: true }).fill('1');
  await page.getByRole('button', { name: 'Plan prompts', exact: true }).click();
  await expect(page.getByLabel('Prompt 1', { exact: true })).toBeVisible();
  await page.getByLabel('Generation mode').selectOption('Continuous');
  await page.getByLabel('Maximum continuous images').fill('5');
  await page.getByLabel('Estimated budget limit (USD)').fill('0.025');
  await page.getByRole('button', { name: 'Save & queue collection' }).click();
  const job = page.locator('article.job').filter({ hasText: 'Stopped: BudgetLimit' });
  await expect(job).toBeVisible({ timeout: 15000 });
  await expect(job.getByText('2 / 5 completed', { exact: true })).toBeVisible();
  await expect(job).toContainText('$0.02');
  await page.reload();
  await expect(
    page.locator('article.job').filter({ hasText: 'Stopped: BudgetLimit' }),
  ).toBeVisible();
  await page.screenshot({ path: '../../../.cache/continuous-workspace.png', fullPage: true });
});
