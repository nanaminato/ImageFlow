import { test, expect } from '@playwright/test';

test.describe('interface language', () => {
  test.use({ locale: 'zh-CN' });

  test.beforeEach(async ({ page }) => {
    // Isolated UI fixture: no generation requests or backend writes.
    await page.route('**/api/v1/**', async (route) => {
      const path = new URL(route.request().url()).pathname;
      if (path.endsWith('/events')) {
        await route.fulfill({
          contentType: 'text/event-stream',
          body: 'event: jobs\ndata: []\n\n',
        });
      } else if (path.endsWith('/settings')) {
        await route.fulfill({
          json: { provider: 'Mock', maxConcurrency: 2, estimatedCostPerImageUsd: 0, revision: 1 },
        });
      } else {
        await route.fulfill({ json: [] });
      }
    });
  });

  test('detects system language, switches live, preserves draft and persists preference', async ({
    page,
  }) => {
    await page.goto('/');
    await expect(page.locator('html')).toHaveAttribute('lang', 'zh-CN');
    await expect(page.getByRole('heading', { name: '从一个想法，到一组作品。' })).toBeVisible();
    await page.getByLabel('主题与约束').fill('海滨日出 / seaside sunrise');
    await page.getByLabel('语言', { exact: true }).selectOption('en');
    await expect(page.getByLabel('Theme & constraints')).toHaveValue('海滨日出 / seaside sunrise');
    await expect(
      page.getByRole('heading', { name: 'From an idea to a collection.' }),
    ).toBeVisible();
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await page.getByLabel('Language', { exact: true }).selectOption('zh');
    await page.getByRole('button', { name: '引擎设置' }).click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await expect(page.getByLabel('并发图像请求数')).toBeVisible();
    await expect(page.getByRole('button', { name: '保存设置' })).toBeVisible();
    await page.getByRole('button', { name: '关闭设置' }).click();
    await page.setViewportSize({ width: 390, height: 844 });
    await expect(page.getByLabel('语言', { exact: true })).toBeVisible();
    await expect(page.locator('body')).toHaveJSProperty('scrollWidth', 390);
    await page.screenshot({ path: '../../../.cache/language-zh-mobile.png', fullPage: true });
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('lang', 'zh-CN');
  });

  test('translates queue and review controls while preserving user text', async ({ page }) => {
    await page.route('**/api/v1/jobs', (route) =>
      route.fulfill({
        json: [
          {
            id: 'job-12345678',
            planId: 'plan-1',
            status: 'Paused',
            mode: 'Continuous',
            maxImages: 5,
            budgetUsd: 1,
            estimatedCostPerImageUsd: 0.1,
            estimatedExposureUsd: 0.1,
            stopReason: 'ManualReview',
            tasks: [
              {
                id: 'task-1',
                status: 'Unknown',
                promptText: 'Original seaside prompt',
                imagePath: null,
                error: null,
                nextAttemptAt: null,
                reviews: [],
                attempts: [
                  {
                    id: 'attempt-1',
                    outcome: 'Unknown',
                    startedAt: '2026-11-20T00:00:00Z',
                    finishedAt: null,
                    provider: 'Mock',
                    model: 'mock-svg',
                    requestId: null,
                    costDisposition: 'Uncertain',
                    estimatedCostUsd: 0.1,
                    usageJson: null,
                    error: null,
                  },
                ],
              },
            ],
          },
        ],
      }),
    );
    await page.goto('/');
    await expect(page.getByRole('button', { name: '继续', exact: true })).toBeVisible();
    await page.locator('summary').click();
    await expect(page.getByRole('heading', { name: '核查未知结果' })).toBeVisible();
    await page.getByLabel('服务商记录依据', { exact: true }).fill('Checked provider records');
    await page.getByLabel('语言', { exact: true }).selectOption('en');
    await expect(page.getByRole('heading', { name: 'Reconcile unknown outcome' })).toBeVisible();
    await expect(page.getByLabel('Provider evidence', { exact: true })).toHaveValue(
      'Checked provider records',
    );
    await expect(page.getByRole('button', { name: 'Resume', exact: true })).toBeVisible();
    await expect(page.locator('summary')).toContainText('Original seaside prompt');
  });

  test('falls back to English for an unsupported system language', async ({ page }) => {
    await page.addInitScript(() => {
      Object.defineProperty(navigator, 'languages', { get: () => ['ja-JP'] });
    });
    await page.goto('/');
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.getByLabel('Language', { exact: true })).toHaveValue('en');
  });
});
