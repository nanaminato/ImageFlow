import { test, expect, Page } from '@playwright/test';
test.use({ timezoneId: 'Asia/Tokyo' });
async function setup(page: Page, broken = false) {
  const plans = [
    { id: 'older-11111111', theme: '同名花海', createdAt: '2026-11-21T06:00:10Z', prompts: [{ id: 'p-old', text: '旧方案的水彩花海提示词' }] },
    { id: 'newer-22222222', theme: '同名花海', createdAt: '2026-11-21T06:00:42', prompts: [{ id: 'p-new', text: '新方案的电影光照提示词' }] },
    { id: 'draft-33333333', theme: '尚未入队的方案', createdAt: '2026-11-21T06:05:00Z', prompts: [{ id: 'p-draft', text: '没有图片时显示这条提示词摘要' }] },
    { id: 'legacy-44444444', theme: '无时间记录的方案', prompts: [] },
    { id: 'attention-5555', theme: '需要处理的方案', createdAt: '2026-11-21T06:04:00Z', prompts: [] },
    { id: 'running-66666', theme: '正在生成的方案', createdAt: '2026-11-21T06:03:00Z', prompts: [] },
  ];
  const task = (id: string, status: string, saved: boolean) => ({ id, status, imagePath: saved ? 'fixture.svg' : null, promptText: id + ' prompt', attempts: [], reviews: [] });
  const jobs = [
    { id: 'job-old', planId: plans[0].id, status: 'Completed', tasks: [0, 1, 2, 3].map(i => task('old-image-' + i, 'Completed', true)) },
    { id: 'job-new', planId: plans[1].id, status: 'Paused', tasks: [task('new-pending', 'Pending', false)] },
    { id: 'job-attention', planId: plans[4].id, status: 'NeedsAttention', tasks: [task('unknown', 'Unknown', false)] },
    { id: 'job-running', planId: plans[5].id, status: 'Active', tasks: [task('running', 'Running', false)] },
  ];
  await page.route('**/api/v1/**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    expect(request.method()).toBe('GET'); // This suite previews only; it cannot send deletion or generation requests.
    if (path.endsWith('/plans')) await route.fulfill({ json: plans });
    else if (path.endsWith('/jobs')) await route.fulfill({ json: jobs });
    else if (path.endsWith('/settings')) await route.fulfill({ json: { provider: 'OpenAICompatible', maxConcurrency: 2, estimatedCostPerImageUsd: 0, revision: 1 } });
    else if (path.endsWith('/events')) await route.fulfill({ contentType: 'text/event-stream', body: 'event: jobs\ndata: []\n\n' });
    else if (path.includes('/images/')) await route.fulfill(broken && path.endsWith('/old-image-0') ? { status: 404, body: '' } : { contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="80"><rect width="100" height="80" fill="#298574"/></svg>' });
    else throw Error('Unexpected request: ' + path);
  });
  await page.addInitScript(() => localStorage.setItem('imageflow.language', 'zh'));
  await page.goto('/create'); await page.getByRole('button', { name: '删除管理', exact: true }).click();
  return page.locator('.deletion-plan-card');
}
test('dates distinguish duplicate names, use local time, sort newest first and show honest status/fallbacks', async ({ page }) => {
  const cards = await setup(page);
  await expect(cards.first()).toContainText('尚未入队的方案');
  const newer = cards.filter({ hasText: '编号 newer-22' });
  const older = cards.filter({ hasText: '编号 older-11' });
  await expect(newer.locator('time')).toHaveText('2026-11-21 15:00:42');
  await expect(older.locator('time')).toHaveText('2026-11-21 15:00:10');
  await expect(newer.locator('time')).toHaveAttribute('datetime', '2026-11-21T06:00:42.000Z');
  await expect(newer).toContainText('已暂停');
  await expect(newer).toContainText('新方案的电影光照提示词');
  await expect(cards.filter({ hasText: '尚未入队的方案' })).toContainText('未入队');
  await expect(cards.filter({ hasText: '需要处理的方案' }).locator('.status')).toContainText('需要处理');
  await expect(cards.filter({ hasText: '正在生成的方案' }).locator('.status')).toContainText('生成中');
  await expect(cards.last()).toContainText('创建时间未记录');
  await expect(older.locator('.deletion-thumbnail')).toHaveCount(3);
  await expect(older.locator('.deletion-thumbnails')).toContainText('+1');
  await page.setViewportSize({ width: 390, height: 844 }); await expect(page.locator('body')).toHaveJSProperty('scrollWidth', 390);
  await page.screenshot({ path: '../../../.cache/deletion-identity-mobile.png', animations: 'disabled' });
});
test('prompt and full-image previews preserve selection and identification is repeated in confirmation', async ({ page }) => {
  const cards = await setup(page); const older = cards.filter({ hasText: '编号 older-11' });
  await older.getByRole('checkbox').check();
  await older.locator('.deletion-prompt-preview summary').click();
  await expect(older.getByRole('checkbox')).toBeChecked();
  await expect(older.locator('ol')).toContainText('旧方案的水彩花海提示词');
  await older.getByRole('button', { name: '预览图片: old-image-0 prompt', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(2);
  await page.getByRole('button', { name: '关闭预览', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(1); await expect(older.getByRole('checkbox')).toBeChecked();
  await page.getByRole('button', { name: '核对删除范围', exact: true }).click();
  const review = page.locator('.deletion-review-cards');
  await expect(review.locator('time')).toHaveText('2026-11-21 15:00:10');
  await expect(review.locator('.deletion-thumbnail')).toHaveCount(3);
  await expect(page.getByRole('button', { name: '永久删除', exact: true })).toBeEnabled();
  await page.screenshot({ path: '../../../.cache/deletion-identity-review.png', animations: 'disabled' });
});
test('missing thumbnails have a fallback without changing counts or disabling selection', async ({ page }) => {
  const cards = await setup(page, true); const older = cards.filter({ hasText: '编号 older-11' });
  await older.scrollIntoViewIfNeeded();
  const button = older.getByRole('button', { name: '预览图片: old-image-0 prompt', exact: true });
  await expect(button).toBeDisabled(); await expect(button).toContainText('预览不可用');
  await older.getByRole('checkbox').check(); await expect(older).toContainText('图片数量: 4');
  await page.getByRole('button', { name: '取消', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
});
