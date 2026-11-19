import { test, expect } from '@playwright/test';
test('plan, edit, queue and view generated collection', async ({ page }) => {
 await page.goto('/');
 await expect(page.getByText('Mock · 2 outstanding requests')).toBeVisible();
 await page.getByLabel('Theme & constraints').fill('Quiet coastal towns in watercolor');
 await page.getByLabel('Images', {exact: true}).fill('2');
 await page.getByRole('button', { name: 'Plan prompts', exact: true }).click();
 await expect(page.getByLabel('Prompt 1', {exact:true})).toBeVisible();
 await page.getByLabel('Prompt 1', {exact:true}).fill('A seaside lighthouse in morning light');
 await page.getByRole('button', {name: 'Save & queue collection'}).click();
 await expect(page.getByText('2 / 2 completed', {exact: true})).toBeVisible({timeout:15000});
 await expect(page.locator('.gallery img')).toHaveCount(2);
 await expect(page.getByRole('button', {name: 'Already queued'})).toBeDisabled();
 await page.reload();
 await expect(page.locator('.gallery img')).toHaveCount(2);
 await page.screenshot({path: '../../../.cache/e2e-workspace.png', fullPage:true});
 const image = await page.request.get(await page.locator('.gallery img').first().getAttribute('src') || '');
 expect(image.headers()['content-type']).toContain('image/svg+xml');
});

test('planning errors keep existing state and release the form', async ({ page }) => {
 await page.goto('/');
 await page.getByLabel('Theme & constraints').fill('Unavailable provider test');
 await page.route('**/api/v1/plans', async route => {
  if (route.request().method() === 'POST') await route.fulfill({ status:502, contentType:'application/problem+json', body:JSON.stringify({detail:'Provider temporarily unavailable'}) });
  else await route.continue();
 });
 await page.getByRole('button', {name:'Plan prompts',exact:true}).click();
 await expect(page.getByRole('alert')).toHaveText('Provider temporarily unavailable');
 await expect(page.getByRole('button', {name:'Plan prompts',exact:true})).toBeEnabled();
 await expect(page.locator('.gallery img')).toHaveCount(2);
 await page.setViewportSize({width:390,height:844});
 await expect(page.locator('body')).toHaveJSProperty('scrollWidth',390);
});
