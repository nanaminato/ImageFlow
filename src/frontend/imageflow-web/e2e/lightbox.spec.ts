import { test, expect } from '@playwright/test';

test.use({ locale: 'en-US' });

test.beforeEach(async ({ page }) => {
  const prompts = ['Portrait coastline', 'Landscape coastline', 'Square still life'];
  const jobs = [
    {
      id: 'gallery-job',
      planId: 'gallery-plan',
      status: 'Completed',
      mode: 'Finite',
      maxImages: 3,
      estimatedExposureUsd: 0,
      estimatedCostPerImageUsd: 0,
      budgetUsd: null,
      stopReason: null,
      tasks: prompts.map((promptText, i) => ({
        id: `sample-${i}`,
        promptText,
        status: 'Completed',
        imagePath: 'fixture.svg',
        attempts: [],
        reviews: [],
        error: null,
        nextAttemptAt: null,
      })),
    },
  ];
  await page.route('**/api/v1/**', async (route) => {
    expect(route.request().method()).toBe('GET');
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/settings'))
      await route.fulfill({
        json: {
          provider: 'Mock',
          maxConcurrency: 2,
          estimatedCostPerImageUsd: 0,
          revision: 1,
          estimateConfirmed: true,
        },
      });
    else if (path.endsWith('/plans'))
      await route.fulfill({
        json: [{ id: 'gallery-plan', theme: 'Gallery studies', prompts: [] }],
      });
    else if (path.endsWith('/jobs')) await route.fulfill({ json: jobs });
    else if (path.includes('/images/')) {
      const id = Number(path.at(-1));
      const [width, height] = [
        [900, 1400],
        [1600, 900],
        [1024, 1024],
      ][id];
      await route.fulfill({
        contentType: 'image/svg+xml',
        body: `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}"><g transform="scale(${width / 900} ${height / 900})"><defs><linearGradient id="sky" x2="0" y2="1"><stop stop-color="#e3eee5"/><stop offset="1" stop-color="#a6c6b7"/></linearGradient></defs><rect width="900" height="900" fill="url(#sky)"/><circle cx="630" cy="250" r="120" fill="#e5bd79"/><path d="M0 650 Q450 230 900 620V900H0" fill="#4e7d68"/><path d="M200 900 Q600 400 900 650V900" fill="#224d42"/></g></svg>`,
      });
    } else
      await route.fulfill({ contentType: 'text/event-stream', body: 'event: jobs\ndata: []\n\n' });
  });
});

test('desktop sidebar is compact and the lightbox traps focus, switches and restores focus', async ({
  page,
  context,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/gallery');
  const sidebar = await page.locator('.workspace-sidebar').boundingBox();
  const content = await page.locator('.workspace-content').boundingBox();
  expect(sidebar!.width).toBe(192);
  expect(content!.x).toBe(sidebar!.width);
  const first = page.getByRole('button', {
    name: 'Enlarge image: Portrait coastline',
    exact: true,
  });
  expect((await first.boundingBox())!.y).toBeLessThan(240);
  await page.screenshot({ path: '../../../.cache/workbench-desktop.png', fullPage: true });
  // Make the page scrollable so the overlay's scroll lock is actually exercised.
  await page.locator('.app-main').evaluate((el) => {
    el.style.minHeight = '1400px';
  });
  await first.click();
  const dialog = page.getByRole('dialog', { name: 'Image preview' });
  await expect(dialog).toBeVisible();
  expect(context.pages()).toHaveLength(1);
  await expect(page).toHaveURL(/\/gallery$/);
  await expect(page.locator('html')).toHaveClass(/cdk-global-scrollblock/);
  await expect(dialog.locator('.lightbox-image')).toHaveJSProperty('naturalHeight', 1400);
  await expect(dialog.locator('.lightbox-image')).toHaveCSS('object-fit', 'contain');
  await expect(dialog.getByRole('button', { name: 'Close preview' })).toBeFocused();
  for (let i = 0; i < 12; i++) {
    await page.keyboard.press('Tab');
    expect(await dialog.evaluate((el) => el.contains(document.activeElement))).toBe(true);
  }
  await page.keyboard.press('ArrowRight');
  await expect(dialog.locator('.lightbox-counter')).toHaveText('2 / 3');
  await expect(dialog.locator('.lightbox-image')).toHaveJSProperty('naturalWidth', 1600);
  await dialog.getByRole('button', { name: 'Previous image' }).click();
  await page.keyboard.press('ArrowLeft');
  await expect(dialog.locator('.lightbox-counter')).toHaveText('3 / 3');
  await dialog.getByRole('button', { name: 'View image 2', exact: true }).click();
  await dialog.getByRole('button', { name: 'Image details', exact: true }).click();
  await expect(dialog.locator('.lightbox-details')).toContainText('Landscape coastline');
  await expect(dialog.getByRole('link', { name: 'Download' })).toHaveAttribute('download', '');
  await page.screenshot({ path: '../../../.cache/workbench-lightbox.png', fullPage: false });
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(first).toBeFocused();
  await expect(page.locator('html')).not.toHaveClass(/cdk-global-scrollblock/);
  await first.click();
  await page.locator('.cdk-overlay-backdrop').click({ position: { x: 2, y: 2 } });
  await expect(dialog).toBeHidden();
});

test('preview uses the current filtered set and handles a single result', async ({ page }) => {
  await page.goto('/gallery');
  await page.getByLabel('Search images').fill('coastline');
  await expect(page.locator('.gallery img')).toHaveCount(2);
  await page
    .getByRole('button', { name: 'Enlarge image: Landscape coastline', exact: true })
    .click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.locator('.lightbox-counter')).toHaveText('2 / 2');
  await page.keyboard.press('ArrowRight');
  await expect(dialog.locator('.lightbox-counter')).toHaveText('1 / 2');
  await page.keyboard.press('Escape');
  await page.getByLabel('Search images').fill('still life');
  await page.getByRole('button', { name: 'Enlarge image: Square still life', exact: true }).click();
  await expect(dialog.locator('.lightbox-counter')).toHaveText('1 / 1');
  await expect(dialog.getByRole('button', { name: 'Previous image' })).toBeDisabled();
  await expect(dialog.getByRole('button', { name: 'Next image' })).toBeDisabled();
  await page.keyboard.press('ArrowRight');
  await expect(dialog.locator('.lightbox-counter')).toHaveText('1 / 1');
  await page.keyboard.press('Escape');
});

test('responsive layouts and mobile preview fit the viewport', async ({ page }) => {
  await page.goto('/gallery');
  for (const width of [1920, 1024, 768, 390, 320]) {
    await page.setViewportSize({ width, height: 844 });
    await expect(page.locator('body')).toHaveJSProperty('scrollWidth', width);
    await expect(page.getByRole('link', { name: 'Collection library', exact: true })).toBeVisible();
  }
  await page.getByLabel('Language', { exact: true }).selectOption('zh');
  await page.getByRole('button', { name: '放大图片: Portrait coastline', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: '图片预览' });
  await expect(dialog).toBeVisible();
  const box = await dialog.boundingBox();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.width).toBeLessThanOrEqual(320);
  expect(box!.height).toBeLessThanOrEqual(844);
  await dialog.getByRole('button', { name: '下一张', exact: true }).click();
  await expect(dialog.locator('.lightbox-counter')).toHaveText('2 / 3');
  await page.screenshot({ path: '../../../.cache/workbench-mobile-lightbox.png', fullPage: false });
  await dialog.getByRole('button', { name: '关闭预览', exact: true }).click();
  await expect(dialog).toBeHidden();
});

test('missing image can be left without losing navigation', async ({ page }) => {
  await page.route('**/api/v1/images/sample-0', (route) => route.fulfill({ status: 404 }));
  await page.goto('/gallery');
  await page
    .getByRole('button', { name: 'Enlarge image: Portrait coastline', exact: true })
    .click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('status')).toContainText('Image unavailable');
  await dialog.getByRole('button', { name: 'Next image', exact: true }).click();
  await expect(dialog.locator('.lightbox-image')).toHaveJSProperty('naturalWidth', 1600);
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
});

test('back navigation closes the viewer and releases the overlay', async ({ page }) => {
  await page.goto('/create');
  await page.getByRole('link', { name: 'Collection library', exact: true }).click();
  await page
    .getByRole('button', { name: 'Enlarge image: Portrait coastline', exact: true })
    .click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.goBack();
  await expect(page).toHaveURL(/\/create$/);
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.locator('.cdk-overlay-backdrop')).toHaveCount(0);
  await expect(page.getByLabel('Theme & constraints')).toBeVisible();
});
