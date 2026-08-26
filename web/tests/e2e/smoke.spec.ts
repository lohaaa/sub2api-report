import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

async function mockAuthenticated(page: Page) {
  await page.route('**/api/v1/setup/status', (route) => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({ setupRequired: false }),
  }))
  await page.route('**/api/v1/auth/me', (route) => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({
      username: 'synthetic-admin',
      sessionStartedAt: '2026-08-26T10:00:00Z',
      stepUpExpiresAt: null,
    }),
  }))
  await page.route('**/api/v1/system/version', (route) => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({ version: '0.3.0', environment: 'Test', releaseChannel: 'stable' }),
  }))
}

async function expectNoOverflowOrAxeViolations(page: Page) {
  const dimensions = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }))
  expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth)

  const accessibility = await new AxeBuilder({ page }).analyze()
  expect(accessibility.violations).toEqual([])
}

test('authenticated dashboard shell is usable', async ({ page }) => {
  await mockAuthenticated(page)
  await page.goto('/')

  await expect(page.getByRole('heading', { level: 1, name: '工作台' })).toBeVisible()
  await expectNoOverflowOrAxeViolations(page)
})

test('people and Key management is responsive and accessible', async ({ page }) => {
  await mockAuthenticated(page)
  await page.route('**/api/v1/sub2api/connection', (route) => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({ configured: true, hasAdminApiKey: true, revision: 1 }),
  }))
  await page.route('**/api/v1/people', (route) => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify([]),
  }))
  await page.route('**/api/v1/sub2api/keys?**', (route) => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({
      items: [{
        id: '00000000-0000-0000-0000-000000000101',
        externalId: '101',
        name: 'Synthetic Key',
        status: 'active',
        groupId: '7',
        lastUsedAt: null,
        lastSeenAt: '2026-08-26T10:00:00Z',
        retiredAt: null,
        assignments: [],
      }],
      total: 1,
      page: 1,
      pageSize: 50,
      pages: 1,
      diagnostics: {
        unmappedKeys: 1,
        overlappingAssignments: 0,
        retiredKeys: 0,
      },
      lastSynchronizedAt: '2026-08-26T10:00:00Z',
    }),
  }))
  await page.goto('/people')

  await expect(page.getByRole('heading', { level: 1, name: '人员与 Key' })).toBeVisible()
  await expect(page.getByText('Synthetic Key')).toBeVisible()
  await expect(page.getByRole('checkbox', { name: '仅看未映射' })).toBeVisible()
  await page.getByRole('button', { name: '新增人员' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByRole('heading', { name: '新增人员' })).toBeVisible()
  await expectNoOverflowOrAxeViolations(page)
})

test('setup form is accessible and supports password managers', async ({ page }) => {
  await page.route('**/api/v1/setup/status', (route) => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({
      setupRequired: true,
      challengeExpiresAt: '2026-08-26T10:30:00Z',
      lockedUntil: null,
    }),
  }))
  await page.goto('/')

  await expect(page.getByRole('heading', { level: 1, name: '初始化管理员' })).toBeVisible()
  await expect(page.getByLabel('初始化码')).toHaveAttribute('autocomplete', 'one-time-code')
  await expect(page.getByLabel('管理员用户名')).toHaveAttribute('autocomplete', 'username')
  await expect(page.getByLabel('管理员密码')).toHaveAttribute('autocomplete', 'new-password')
  await expectNoOverflowOrAxeViolations(page)
})
