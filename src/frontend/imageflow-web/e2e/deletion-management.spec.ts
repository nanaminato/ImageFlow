import { test, expect, Page } from '@playwright/test';

async function setup(page: Page, running = false, reject = false) {
  let plans = ['First', 'Second', 'Third'].map((name, i) => ({ id: `plan-${i}`, theme: name, prompts: [{ id: `prompt-${i}`, text: `${name} image` }] }));
  let jobs = plans.map((p, i) => ({ id: `job-${i}`, planId: p.id, status: running && i === 0 ? 'Active' : 'Completed', mode: 'Finite', maxImages: 1, budgetUsd: null,
    estimatedExposureUsd: .01, estimatedCostPerImageUsd: .01, stopReason: 'Completed',
    tasks: [{ id: `image-${i}`, status: running && i === 0 ? 'Running' : 'Completed', promptText: `${p.theme} image`, imagePath: 'fixture.svg', attempts: [], reviews: [] }],
  }));
  const deletes: string[][] = [];
  await page.route('**/api/v1/**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    if (path === '/api/v1/plans/bulk-delete') {
      const body = request.postDataJSON(); expect(body.confirmed).toBe(true); expect(request.method()).toBe('POST'); deletes.push(body.ids);
      if (reject) { await route.fulfill({ status: 409, json: { error: 'Selected plan is running.' } }); return; }
      plans = plans.filter(p => !body.ids.includes(p.id)); jobs = jobs.filter(j => !body.ids.includes(j.planId));
      await route.fulfill({ json: { plans: body.ids.length, jobs: body.ids.length, images: body.ids.length, pendingImageFiles: 0 } });
    } else if (path.endsWith('/plans')) await route.fulfill({ json: plans });
    else if (path.endsWith('/jobs')) await route.fulfill({ json: jobs });
    else if (path.endsWith('/settings')) await route.fulfill({ json: { provider: 'OpenAICompatible', maxConcurrency: 2, estimatedCostPerImageUsd: 0, revision: 1 } });
    else if (path.endsWith('/events')) await route.fulfill({ contentType: 'text/event-stream', body: 'event: jobs\ndata: []\n\n' });
    else if (path.includes('/images/')) await route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20"/>' });
    else throw Error(`Unexpected application request: ${request.method()} ${path}`);
  });
  await page.addInitScript(() => localStorage.setItem('imageflow.language', 'zh'));
  return deletes;
}

test('one deletion entry replaces clear and all page-local deletion controls', async ({ page }) => {
  await setup(page);
  for (const url of ['/prompts', '/queue', '/gallery']) {
    await page.goto(url);
    await expect(page.getByRole('button', { name: '删除管理', exact: true })).toHaveCount(1);
    await expect(page.getByRole('button', { name: /清除工作区|删除选中项|删除图片|删除任务|删除方案/ })).toHaveCount(0);
    await expect(page.getByRole('checkbox')).toHaveCount(0);
  }
});

test('choose roots, review counts, go back and confirm cascading deletion without touching other collections', async ({ page }) => {
  const deletes = await setup(page); await page.goto('/prompts?plan=plan-0');
  await page.getByRole('button', { name: '删除管理', exact: true }).click();
  await expect(page.getByRole('button', { name: '核对删除范围', exact: true })).toBeDisabled();
  await page.getByRole('checkbox', { name: '选择方案: First', exact: true }).check();
  await page.getByRole('dialog').getByLabel('搜索方案', { exact: true }).fill('Second');
  await page.getByRole('checkbox', { name: '全选当前结果', exact: true }).check();
  await page.getByRole('button', { name: '核对删除范围', exact: true }).click();
  await expect(page.locator('.batch-confirm-count')).toContainText('已选择: 2');
  await expect(page.getByRole('dialog')).toContainText('任务: 2 · 图片数量: 2');
  await expect(page.getByRole('button', { name: '永久删除', exact: true })).toBeEnabled();
  await expect(page.getByRole('dialog').getByRole('checkbox')).toHaveCount(0);
  await page.getByRole('button', { name: '返回选择', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('已选择: 2');
  await page.getByRole('button', { name: '核对删除范围', exact: true }).click();
  await page.setViewportSize({ width: 390, height: 844 }); await expect(page.locator('body')).toHaveJSProperty('scrollWidth', 390);
  await page.screenshot({ path: '../../../.cache/central-deletion-mobile.png', animations: 'disabled' });
  await page.getByRole('button', { name: '永久删除', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0); expect(deletes).toEqual([['plan-0', 'plan-1']]);
  await expect(page).toHaveURL(/\/prompts$/); await expect(page.locator('.managed-plan')).toHaveCount(1);
  await page.getByRole('link', { name: '队列管理', exact: true }).click(); await expect(page.locator('article.job')).toHaveCount(1);
  await page.getByRole('link', { name: '作品集', exact: true }).click(); await expect(page.locator('.gallery img')).toHaveCount(1);
  await page.reload(); await expect(page.locator('.gallery img')).toHaveCount(1);
});

test('cancelling either step never sends deletion; running roots cannot be confirmed', async ({ page }) => {
  const deletes = await setup(page, true); await page.goto('/create');
  await page.getByRole('button', { name: '删除管理', exact: true }).click();
  await page.getByRole('checkbox', { name: '选择方案: First', exact: true }).check();
  await page.getByRole('button', { name: '核对删除范围', exact: true }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('等待已发出的请求结束');
  await expect(page.getByRole('button', { name: '永久删除', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: '取消', exact: true }).click(); expect(deletes).toEqual([]);
  await page.getByRole('button', { name: '删除管理', exact: true }).click();
  await page.keyboard.press('Escape'); await expect(page.getByRole('dialog')).toHaveCount(0); expect(deletes).toEqual([]);
});

test('rejected deletion preserves selection and records', async ({ page }) => {
  await setup(page, false, true); await page.goto('/queue');
  await page.getByRole('button', { name: '删除管理', exact: true }).click();
  await page.getByRole('checkbox', { name: '选择方案: First', exact: true }).check();
  await page.getByRole('button', { name: '核对删除范围', exact: true }).click();
  await page.getByRole('button', { name: '永久删除', exact: true }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Selected plan is running.');
  await expect(page.locator('article.job')).toHaveCount(3);
});

test('queue defaults to compact summaries and expands details independently with keyboard support', async ({ page }) => {
  await setup(page); await page.goto('/queue');
  await expect(page.locator('article.job')).toHaveCount(3);
  await expect(page.locator('.job-expanded-content')).toHaveCount(0);
  await expect(page.locator('.job-summary').first()).toContainText('1 / 1');
  const summary = page.locator('.job-summary').first(); await summary.focus(); await page.keyboard.press('Enter');
  await expect(page.locator('.job-expanded-content')).toHaveCount(1);
  await expect(page.locator('article.job').first()).toContainText('预估费用占用');
  await expect(page.locator('.task-list summary').first()).toContainText('First image');
  await page.getByLabel('语言', { exact: true }).selectOption('en');
  await expect(page.locator('.job-expanded-content')).toHaveCount(1);
  await summary.click(); await expect(page.locator('.job-expanded-content')).toHaveCount(0);
  await page.setViewportSize({ width: 390, height: 844 }); await expect(page.locator('body')).toHaveJSProperty('scrollWidth', 390);
  await page.screenshot({ path: '../../../.cache/compact-queue-mobile.png', animations: 'disabled' });
});

