import { test, expect } from '@playwright/test';

async function workspace(page: import('@playwright/test').Page, running = false, reject = false) {
  let plans = [{ id: 'plan-1', theme: 'Saved theme', prompts: [{ id: 'prompt-1', text: 'Saved prompt' }] }];
  let jobs = [{ id: 'job-1', planId: 'plan-1', status: running ? 'Active' : 'Completed', mode: 'Finite', maxImages: 1,
    tasks: [{ id: 'task-1', status: running ? 'Running' : 'Completed', imagePath: '/local/test.svg', promptText: 'Saved prompt', attempts: [], reviews: [] }] }];
  let clears = 0;
  await page.route('**/api/v1/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/workspace/clear')) {
      clears++;
      expect(route.request().method()).toBe('POST');
      expect(route.request().postDataJSON()).toEqual({ confirmed: true });
      if (reject) { await route.fulfill({ status: 409, json: { error: 'Requests are still running.' } }); return; }
      plans = []; jobs = [];
      await route.fulfill({ json: { plans: 1, jobs: 1, tasks: 1, images: 1, remainingImageFiles: 0 } });
    } else if (path.endsWith('/plans')) await route.fulfill({ json: plans });
    else if (path.endsWith('/jobs')) await route.fulfill({ json: jobs });
    else if (path.endsWith('/settings')) await route.fulfill({ json: { provider: 'OpenAICompatible', maxConcurrency: 2, estimatedCostPerImageUsd: .01, revision: 1 } });
    else if (path.endsWith('/events')) await route.fulfill({ contentType: 'text/event-stream', body: 'event: jobs\ndata: []\n\n' });
    else await route.fulfill({ json: [] });
  });
  await page.addInitScript(() => localStorage.setItem('imageflow.language', 'zh'));
  return () => clears;
}

test('clearing requires confirmation; cancelling preserves records and confirmation resets the workspace', async ({ page }) => {
  const clears = await workspace(page);
  await page.goto('/create');
  await page.getByLabel('主题与约束').fill('Unsaved theme');
  await page.getByRole('button', { name: '清除工作区', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('无法撤销');
  await expect(page.getByRole('button', { name: '永久清除', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: '取消', exact: true }).click();
  expect(clears()).toBe(0);
  await expect(page.getByLabel('主题与约束')).toHaveValue('Unsaved theme');
  await page.getByRole('button', { name: '清除工作区', exact: true }).click();
  await page.getByRole('checkbox').check();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator('body')).toHaveJSProperty('scrollWidth', 390);
  await page.screenshot({ path: '../../../.cache/clear-workspace-mobile.png' });
  await page.getByRole('button', { name: '永久清除', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(clears()).toBe(1);
  await expect(page.getByLabel('主题与约束')).toHaveValue('');
  await page.reload();
  await page.getByRole('button', { name: '清除工作区', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('0');
  await expect(page.getByRole('dialog')).toContainText('引擎设置会保留');
});

test('running image requests block confirmation without sending clear requests', async ({ page }) => {
  const clears = await workspace(page, true);
  await page.goto('/create');
  await page.getByRole('button', { name: '清除工作区', exact: true }).click();
  await page.getByRole('checkbox').check();
  await expect(page.getByRole('alert')).toContainText('等待已发出的请求结束');
  await expect(page.getByRole('button', { name: '永久清除', exact: true })).toBeDisabled();
  expect(clears()).toBe(0);
});

test('server-side rejection keeps the dialog and workspace records', async ({ page }) => {
  await workspace(page, false, true);
  await page.goto('/create');
  await page.getByRole('button', { name: '清除工作区', exact: true }).click();
  await page.getByRole('checkbox').check();
  await page.getByRole('button', { name: '永久清除', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Requests are still running.');
  await expect(page.getByRole('dialog')).toContainText('1');
});
