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
    body: JSON.stringify({ version: '0.4.0', environment: 'Test', releaseChannel: 'stable' }),
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

test('report detail is responsive and exposes partial diagnostics', async ({ page }) => {
  await mockAuthenticated(page)
  const metrics = {
    totalRequests: '30',
    totalInputTokens: '3000',
    totalOutputTokens: '1500',
    totalCacheTokens: '750',
    totalCacheCreationTokens: '300',
    totalCacheReadTokens: '450',
    totalTokens: '9007199254740993',
    totalCost: 6,
    totalActualCost: 3,
    averageDurationMs: 125.5,
  }
  await page.route('**/api/v1/reports/11111111-1111-1111-1111-111111111111', (route) => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({
      schemaVersion: 1,
      reportId: '11111111-1111-1111-1111-111111111111',
      status: 'Partial',
      trigger: 'ManualDryRun',
      generatedAt: '2026-08-26T12:00:00Z',
      timezone: 'Asia/Shanghai',
      connectionRevision: 3,
      sevenDayWindow: { days: 7, startDate: '2026-08-19', endDate: '2026-08-25' },
      thirtyDayWindow: { days: 30, startDate: '2026-07-27', endDate: '2026-08-25' },
      sevenDayTotal: { ...metrics, totalRequests: '7', totalTokens: '1225', totalActualCost: 0.7 },
      thirtyDayTotal: metrics,
      people: [{
        personId: '22222222-2222-2222-2222-222222222222',
        code: 'person-a',
        displayName: '合成人员 A',
        keyCount: 1,
        sevenDay: { ...metrics, totalRequests: '7', totalTokens: '1225', totalActualCost: 0.7 },
        thirtyDay: metrics,
      }],
      keys: [{
        keyId: '33333333-3333-3333-3333-333333333333',
        externalId: '9007199254740993',
        name: 'Synthetic Key',
        status: 'active',
        lastUsedAt: null,
        retiredAt: null,
        sevenDay: { ...metrics, totalRequests: '7', totalTokens: '1225', totalActualCost: 0.7 },
        thirtyDay: metrics,
        segments: [{
          startDate: '2026-07-27',
          endDate: '2026-08-25',
          personId: null,
          personCode: null,
          personDisplayName: null,
          metrics,
          failureKind: null,
          diagnosticCode: 'unassigned',
        }],
      }],
      diagnostics: {
        failedSegments: [],
        unassignedSegments: [{
          externalKeyId: '9007199254740993',
          keyName: 'Synthetic Key',
          startDate: '2026-07-27',
          endDate: '2026-08-25',
          code: 'unassigned',
          failureKind: null,
        }],
        conflictingSegments: [],
        zeroUsageKeyIds: [],
      },
    }),
  }))
  await page.goto('/reports/11111111-1111-1111-1111-111111111111')

  await expect(page.getByRole('heading', { level: 1, name: /报告/ })).toBeVisible()
  await expect(page.getByText('报告数据不完整')).toBeVisible()
  await expect(page.getByText('Synthetic Key')).toBeVisible()
  await expect(page.getByText('9,007,199,254,740,993').first()).toBeVisible()
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
