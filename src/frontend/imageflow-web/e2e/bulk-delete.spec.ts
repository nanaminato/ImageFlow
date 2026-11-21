import { test, expect, Page } from '@playwright/test';

async function setup(page: Page, reject = false) {
  let plans = ['First', 'Second', 'Third'].map((name, i) => ({ id: `plan-${i}`, theme: name, prompts: [{ id: `prompt-${i}`, text: `${name} image` }] }));
  let jobs = plans.map((p, i) => ({ id: `job-${i}`, planId: p.id, status: 'Completed', mode: 'Finite', maxImages: 1, budgetUsd: null,
    estimatedExposureUsd: 0, estimatedCostPerImageUsd: 0,
    tasks: [{ id: `image-${i}`, status: 'Completed', promptText: `${p.theme} image`, imagePath: 'fixture.svg' as string | null, attempts: [], reviews: [] }],
  }));
  const batches: { resource: string; ids: string[]; confirmed: boolean }[] = [];
  await page.route('**/api/v1/**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    if (path.endsWith('/bulk-delete')) {
      const resource = path.split('/')[3]; const body = request.postDataJSON();
      expect(request.method()).toBe('POST'); expect(body.confirmed).toBe(true);
      batches.push({ resource, ...body });
      if (reject) { await route.fulfill({ status: 409, json: { error: 'Batch contains a running request.' } }); return; }
      const ids = new Set(body.ids);
      if (resource === 'plans') { plans = plans.filter(p => !ids.has(p.id)); jobs = jobs.filter(j => !ids.has(j.planId)); }
      else if (resource === 'jobs') jobs = jobs.filter(j => !ids.has(j.id));
      else jobs = jobs.map(j => ({ ...j, tasks: j.tasks.map(t => ids.has(t.id) ? { ...t, imagePath: null } : t) }));
      await route.fulfill({ json: { plans: 0, jobs: 0, images: 2, pendingImageFiles: 0 } });
    } else if (path.endsWith('/plans')) await route.fulfill({ json: plans });
    else if (path.endsWith('/jobs')) await route.fulfill({ json: jobs });
    else if (path.endsWith('/settings')) await route.fulfill({ json: { provider: 'OpenAICompatible', maxConcurrency: 2, estimatedCostPerImageUsd: 0, revision: 1 } });
    else if (path.endsWith('/events')) await route.fulfill({ contentType: 'text/event-stream', body: 'event: jobs\ndata: []\n\n' });
    else if (path.includes('/images/')) await route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20"/>' });
    else throw Error(`Unexpected application request: ${request.method()} ${path}`);
  });
  await page.addInitScript(() => localStorage.setItem('imageflow.language', 'zh'));
  return batches;
}
async function confirm(page: Page, count: number) {
  await expect(page.locator('.batch-confirm-count')).toContainText(`已选择: ${count}`);
  await expect(page.getByRole('button', { name: '永久删除', exact: true })).toBeDisabled();
  await page.getByRole('dialog').getByRole('checkbox').check();
  await page.getByRole('button', { name: '永久删除', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
}

test('plan selection survives filters, shows the count, cancels safely and deletes only selected plans', async ({ page }) => {
  const batches = await setup(page); await page.goto('/prompts?plan=plan-0');
  await expect(page.getByRole('button', { name: '删除选中项', exact: true })).toBeDisabled();
  await page.getByRole('checkbox', { name: '选择方案: First', exact: true }).check();
  await page.getByLabel('搜索方案', { exact: true }).fill('Second');
  await page.getByRole('checkbox', { name: '全选当前结果', exact: true }).check();
  await expect(page.locator('app-bulk-actions')).toContainText('已选择: 2');
  await expect(page.locator('app-bulk-actions')).toContainText('被筛选条件隐藏');
  await page.getByRole('button', { name: '删除选中项', exact: true }).click();
  await expect(page.locator('.batch-confirm-count')).toContainText('已选择: 2');
  await page.getByRole('button', { name: '取消', exact: true }).click(); expect(batches).toEqual([]);
  await page.getByRole('button', { name: '删除选中项', exact: true }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator('body')).toHaveJSProperty('scrollWidth', 390);
  await page.screenshot({ path: '../../../.cache/bulk-delete-mobile.png', animations: 'disabled' });
  await confirm(page, 2);
  expect(batches[0]).toEqual({ resource: 'plans', ids: ['plan-0', 'plan-1'], confirmed: true });
  await page.getByLabel('搜索方案', { exact: true }).fill('');
  await expect(page.locator('.managed-plan')).toHaveCount(1); await expect(page.locator('.managed-plan')).toContainText('Third');
  await expect(page.locator('app-bulk-actions')).toContainText('已选择: 0');
  await page.reload(); await expect(page.locator('.managed-plan')).toHaveCount(1);
});

test('job select-all supports deselection and clearing; batch keeps plans', async ({ page }) => {
  const batches = await setup(page); await page.goto('/queue');
  await page.getByRole('checkbox', { name: '全选当前结果', exact: true }).check();
  await expect(page.locator('app-bulk-actions')).toContainText('已选择: 3');
  await page.getByRole('button', { name: '清空选择', exact: true }).click();
  await expect(page.getByRole('button', { name: '删除选中项', exact: true })).toBeDisabled();
  await page.getByRole('checkbox', { name: '全选当前结果', exact: true }).check();
  await expect(page.getByRole('checkbox', { name: '选择任务: Third', exact: true })).toBeChecked();
  await page.getByRole('checkbox', { name: '选择任务: Third', exact: true }).uncheck();
  await expect(page.locator('app-bulk-actions')).toContainText('已选择: 2');
  await page.getByRole('button', { name: '删除选中项', exact: true }).click(); await confirm(page, 2);
  await expect(page.locator('article.job')).toHaveCount(1); expect(batches[0].ids).toEqual(['job-0', 'job-1']);
  await page.getByRole('link', { name: '提示词管理', exact: true }).click(); await expect(page.locator('.managed-plan')).toHaveCount(3);
});

test('image batch deletes only selected images and keeps their task records', async ({ page }) => {
  const batches = await setup(page); await page.goto('/gallery');
  await page.getByRole('checkbox', { name: '选择图片: First image', exact: true }).check();
  await page.getByRole('checkbox', { name: '选择图片: Second image', exact: true }).check();
  await page.getByRole('button', { name: '删除选中项', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('不会自动重新生成'); await confirm(page, 2);
  expect(batches[0].ids).toEqual(['image-0', 'image-1']);
  await expect(page.locator('.gallery img')).toHaveCount(1); await expect(page.locator('.gallery img')).toHaveAttribute('alt', 'Third image');
  await page.getByRole('link', { name: '队列管理', exact: true }).click(); await expect(page.locator('article.job')).toHaveCount(3);
});

test('server rejection keeps all selected rows and the dialog', async ({ page }) => {
  await setup(page, true); await page.goto('/queue');
  await page.getByRole('checkbox', { name: '全选当前结果', exact: true }).check();
  await page.getByRole('button', { name: '删除选中项', exact: true }).click();
  await page.getByRole('dialog').getByRole('checkbox').check();
  await page.getByRole('button', { name: '永久删除', exact: true }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Batch contains a running request.');
  await expect(page.locator('article.job')).toHaveCount(3);
  await expect(page.locator('.batch-confirm-count')).toContainText('已选择: 3');
});

