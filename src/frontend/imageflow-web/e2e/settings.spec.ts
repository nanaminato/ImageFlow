import { test, expect } from '@playwright/test';

test('model, ratio and custom dimensions can be reviewed and saved', async ({ page }) => {
  let settings = {
    provider: 'OpenAICompatible', promptModel: 'gpt-6-luna', imageModel: 'gpt-image-2.6-flare',
    maxConcurrency: 2, estimatedCostPerImageUsd: 0, revision: 1, estimateConfirmed: true,
    imageSize: '1024x1024', imageQuality: 'medium', imageComplexity: 'high', outputFormat: 'png',
    imageCapabilities: ['gpt-image-2.6-flare', 'gpt-image-2.6-sunburst', 'gpt-image-2.5-flare', 'gpt-image-2.5-sunburst'].map(model => ({
      model, sizes: ['1024x1024', '1536x1024', '1024x1536'], qualities: ['low', 'medium', 'high', 'xhigh', 'max'],
      complexities: model.includes('2.5') ? ['none'] : ['low', 'medium', 'high'], formats: ['png'], customDimensions: true,
    })),
  };
  // Intercept every application request; never send planning, jobs or settings writes to the real service.
  await page.route('**/api/v1/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/settings')) {
      if (route.request().method() === 'PUT') settings = { ...settings, ...route.request().postDataJSON(), revision: settings.revision + 1 };
      await route.fulfill({ json: settings });
    } else if (path.endsWith('/events')) await route.fulfill({ contentType: 'text/event-stream', body: 'event: jobs\ndata: []\n\n' });
    else await route.fulfill({ json: [] });
  });
  await page.addInitScript(() => localStorage.setItem('imageflow.language', 'zh'));
  await page.goto('/');
  // Select through the normal language control so the test does not depend on storage-key internals.
  await page.getByLabel(/Language|语言/, { exact: true }).selectOption('zh');
  await page.getByRole('button', { name: '引擎设置' }).click();
  await page.getByRole('combobox', { name: '图像模型' }).click();
  await page.getByRole('option', { name: 'gpt-image-2.5-flare', exact: true }).click();
  await expect(page.getByRole('combobox', { name: '复杂度' })).toBeDisabled();
  await page.getByRole('combobox', { name: '分辨率与画面比例' }).click();
  await page.getByRole('option', { name: '1536x864 · 16:9', exact: true }).click();
  await page.getByRole('combobox', { name: '质量', exact: true }).click();
  await page.getByRole('option', { name: '超高', exact: true }).click();
  await page.getByRole('button', { name: '保存设置', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(settings.imageModel).toBe('gpt-image-2.5-flare'); expect(settings.imageSize).toBe('1536x864');
  expect(settings.imageQuality).toBe('xhigh'); expect(settings.imageComplexity).toBe('none');
  await page.reload();
  await page.getByRole('button', { name: '引擎设置' }).click();
  await expect(page.getByRole('combobox', { name: '图像模型' })).toContainText('gpt-image-2.5-flare');
  await page.getByRole('combobox', { name: '分辨率与画面比例' }).click();
  await page.getByRole('option', { name: '自定义宽高', exact: true }).click();
  await page.getByLabel('宽度（像素）').fill('1025');
  await expect(page.getByRole('button', { name: '保存设置', exact: true })).toBeDisabled();
  await page.getByLabel('宽度（像素）').fill('1536');
  await page.getByLabel('高度（像素）').fill('864');
  await expect(page.getByRole('button', { name: '保存设置', exact: true })).toBeEnabled();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator('body')).toHaveJSProperty('scrollWidth', 390);
  await page.screenshot({ path: '../../../.cache/settings-generation-mobile.png' });
});
