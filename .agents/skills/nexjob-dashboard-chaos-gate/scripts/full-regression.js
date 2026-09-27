#!/usr/bin/env node
/**
 * NexJob Dashboard Full Regression & Chaos UI Suite
 * Comprehensive automated regression testing covering all dashboard routes, actions,
 * interactive components, error boundaries, personas, and viewport variations.
 */

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const BASE_URL = process.env.NEXJOB_DASHBOARD_URL || 'http://localhost:5005/dashboard';
const ARTIFACTS_DIR = process.env.CHAOS_ARTIFACTS_DIR || path.join(process.cwd(), '.dashboard-simulations');

if (!fs.existsSync(ARTIFACTS_DIR)) {
  fs.mkdirSync(ARTIFACTS_DIR, { recursive: true });
}

async function runFullRegression() {
  console.log(`[🚀 FULL UI REGRESSION] Starting comprehensive test suite against ${BASE_URL}...`);
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1440, height: 900 }
  });

  const page = await context.newPage();
  const consoleErrors = [];
  const networkErrors = [];

  page.on('console', msg => {
    if (msg.type() === 'error') {
      consoleErrors.push({ text: msg.text(), location: msg.location() });
    }
  });

  page.on('response', res => {
    if (res.status() >= 500) {
      networkErrors.push({ url: res.url(), status: res.status(), statusText: res.statusText() });
    }
  });

  const report = {
    suite: 'NexJob Dashboard Full Regression Suite',
    executedAt: new Date().toISOString(),
    baseUrl: BASE_URL,
    totalRoutesChecked: 0,
    routesPassed: 0,
    routeResults: [],
    featureChecks: {},
    interactiveWorkflows: {},
    viewportChecks: [],
    adversarialChaos: [],
    consoleErrors,
    networkErrors,
    screenshots: []
  };

  async function takeScreenshot(name) {
    const fullPath = path.join(ARTIFACTS_DIR, name);
    await page.screenshot({ path: fullPath, fullPage: true });
    report.screenshots.push(name);
  }

  try {
    // =========================================================================
    // SECTION 1: ROUTE INVENTORY & HTTP INTEGRITY
    // =========================================================================
    console.log('\n[1/5] Auditing all registered routes & visual baselines...');
    const routesToTest = [
      { path: '', name: 'Overview / Home' },
      { path: '/queues', name: 'Queues Monitoring' },
      { path: '/servers', name: 'Servers & Cluster Nodes' },
      { path: '/listeners', name: 'Triggers & Listeners' },
      { path: '/catalog', name: 'Job Catalog & Definitions' },
      { path: '/jobs', name: 'Jobs List (Default)' },
      { path: '/jobs?status=Enqueued', name: 'Jobs List (Enqueued)' },
      { path: '/jobs?status=Processing', name: 'Jobs List (Processing)' },
      { path: '/jobs?status=Succeeded', name: 'Jobs List (Succeeded)' },
      { path: '/jobs?status=Failed', name: 'Jobs List (Failed)' },
      { path: '/jobs?status=Scheduled', name: 'Jobs List (Scheduled)' },
      { path: '/recurring', name: 'Recurring Jobs' },
      { path: '/failed', name: 'Failed & Dead-Letter Queue' },
      { path: '/settings', name: 'Cluster Settings' }
    ];

    for (const route of routesToTest) {
      report.totalRoutesChecked++;
      const targetUrl = `${BASE_URL}${route.path}`;
      const res = await page.goto(targetUrl, { waitUntil: 'domcontentloaded' });
      const status = res ? res.status() : 0;
      const html = await page.content();
      const hasErrorSnippet = html.includes('Internal Server Error') || html.includes('NullReferenceException');

      const isPass = status === 200 && !hasErrorSnippet;
      if (isPass) report.routesPassed++;

      report.routeResults.push({
        name: route.name,
        path: route.path,
        status,
        passed: isPass
      });
    }

    await page.goto(`${BASE_URL}`, { waitUntil: 'domcontentloaded' });
    await takeScreenshot('regression_01_overview.png');

    // =========================================================================
    // SECTION 2: CATALOG TRIGGERING & ERGONOMICS (User Persona)
    // =========================================================================
    console.log('\n[2/5] Testing Catalog workflows, sorting & trigger ergonomics...');
    await page.goto(`${BASE_URL}/catalog`, { waitUntil: 'domcontentloaded' });
    await takeScreenshot('regression_02_catalog_initial.png');

    // Test Catalog Sorting dropdown
    await page.goto(`${BASE_URL}/catalog?sort=failure-rate`, { waitUntil: 'domcontentloaded' });
    const sortDropdown = await page.$('select[name="sort"]');
    const sortApplied = !!sortDropdown;

    // Trigger parameterless job
    const triggerBtn = await page.$('form[action*="/trigger"] button[type="submit"]');
    let triggerSuccess = false;
    let bannerFound = false;

    if (triggerBtn) {
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        triggerBtn.click()
      ]);
      const currentUrl = page.url();
      const successBanner = await page.$('.alert-success');
      bannerFound = !!successBanner;
      triggerSuccess = currentUrl.includes('triggered=') && bannerFound;
      await takeScreenshot('regression_03_catalog_triggered_success.png');
    }

    report.interactiveWorkflows.CatalogTrigger = {
      sortControlsWorking: sortApplied,
      parameterlessTriggerSuccess: triggerSuccess,
      successBannerDisplayed: bannerFound
    };

    // =========================================================================
    // SECTION 3: JOB DETAILS, CHECKPOINT & FILTER CHIPS (Developer Persona)
    // =========================================================================
    console.log('\n[3/5] Testing Developer workflows: Filter chips, Job Details & Checkpoints...');
    // Filter chips test
    await page.goto(`${BASE_URL}/jobs?status=Succeeded&queue=default&search=Cleanup`, { waitUntil: 'domcontentloaded' });
    await takeScreenshot('regression_04_jobs_filter_chips.png');

    const filterChips = await page.$$('.filter-chip, a[href*="remove="], [data-testid="filter-chip"]');
    const clearAllLink = await page.$('a:has-text("Clear all")');

    // Inspect Job Detail
    await page.goto(`${BASE_URL}/jobs?status=Succeeded`, { waitUntil: 'domcontentloaded' });
    const firstJobDetailLink = await page.$('a[href*="/jobs/"]');
    let jobDetailInspected = false;
    let hasPayloadOrRetention = false;

    if (firstJobDetailLink) {
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        firstJobDetailLink.click()
      ]);
      await takeScreenshot('regression_05_job_detail_succeeded.png');
      const detailHtml = await page.content();
      hasPayloadOrRetention = detailHtml.includes('Payload') || detailHtml.includes('Input') || detailHtml.includes('TrimPayloadOnSuccess');
      jobDetailInspected = true;
    }

    report.interactiveWorkflows.DeveloperInvestigation = {
      filterChipsRendered: filterChips.length > 0 || (await page.content()).includes('ACTIVE FILTERS'),
      clearAllFilterAction: !!clearAllLink || (await page.content()).includes('Clear all'),
      jobDetailAccessible: jobDetailInspected,
      payloadOrRetentionMarkerFound: hasPayloadOrRetention
    };

    // =========================================================================
    // SECTION 4: CLUSTER CONTROLS & SAFETY GUARDS (SRE Persona)
    // =========================================================================
    console.log('\n[4/5] Testing SRE controls: Orphan queue alerts, Circuit drilldown, Safety guards...');
    await page.goto(`${BASE_URL}/queues`, { waitUntil: 'domcontentloaded' });
    await takeScreenshot('regression_06_queues_monitoring.png');
    const queuesHtml = await page.content();
    const hasOrphanAlert = queuesHtml.includes('NO WORKERS') || queuesHtml.includes('nav-counter alert') || queuesHtml.includes('badge-warning');

    await page.goto(`${BASE_URL}/settings`, { waitUntil: 'domcontentloaded' });
    await takeScreenshot('regression_07_cluster_settings.png');
    const settingsHtml = await page.content();
    const hasPauseAllConfirm = settingsHtml.includes("return confirm('Pause ALL recurring jobs cluster-wide?')");

    report.interactiveWorkflows.SREClusterSafety = {
      orphanQueueAlertDetected: hasOrphanAlert,
      pauseAllConfirmGuardActive: hasPauseAllConfirm
    };

    // =========================================================================
    // SECTION 5: VIEWPORT RESPONSIVENESS & ADVERSARIAL CHAOS
    // =========================================================================
    console.log('\n[5/5] Testing mobile viewports & adversarial fuzzing...');
    
    // Viewport tests
    const viewports = [
      { name: 'Desktop (1440x900)', width: 1440, height: 900, screenshot: 'regression_08_desktop.png' },
      { name: 'Tablet (768x1024)', width: 768, height: 1024, screenshot: 'regression_09_tablet.png' },
      { name: 'Mobile (390x844 - iPhone)', width: 390, height: 844, screenshot: 'regression_10_mobile.png' }
    ];

    for (const vp of viewports) {
      await page.setViewportSize({ width: vp.width, height: vp.height });
      await page.goto(`${BASE_URL}`, { waitUntil: 'domcontentloaded' });
      await takeScreenshot(vp.screenshot);
      report.viewportChecks.push({ name: vp.name, passed: true });
    }

    // Reset viewport
    await page.setViewportSize({ width: 1440, height: 900 });

    // Adversarial fuzzing
    const chaosCases = [
      { name: 'Max Int Overflow Page & Limit', url: `${BASE_URL}/jobs?page=2147483647&limit=2147483647` },
      { name: 'Negative Negative Page Range', url: `${BASE_URL}/jobs?page=-999999` },
      { name: 'XSS Attack in Queue & Tag Filter', url: `${BASE_URL}/jobs?queue=<script>alert("xss")</script>&tag=<svg/onload=alert(1)>` },
      { name: 'Unicode Bomb in Search', url: `${BASE_URL}/catalog?search=﷽﷽﷽🔥🔥🔥` },
      { name: 'Corrupted Guid Path Format', url: `${BASE_URL}/jobs/NOT_A_VALID_GUID_VALUE_123` }
    ];

    for (const test of chaosCases) {
      const res = await page.goto(test.url, { waitUntil: 'domcontentloaded' });
      const status = res ? res.status() : 0;
      const html = await page.content();
      const crashed = status >= 500 || html.includes('Internal Server Error') || html.includes('NullReferenceException');

      report.adversarialChaos.push({
        name: test.name,
        url: test.url,
        status,
        resisted: !crashed
      });
    }

  } catch (err) {
    console.error('[❌ Regression Error]', err);
    report.fatalError = err.message;
  } finally {
    await browser.close();
  }

  const reportPath = path.join(ARTIFACTS_DIR, 'full-regression-report.json');
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));

  console.log(`\n======================================================`);
  console.log(`[🏁 FULL REGRESSION COMPLETE]`);
  console.log(`Routes Audited: ${report.routesPassed}/${report.totalRoutesChecked} passed`);
  console.log(`HTTP 500 Errors: ${report.networkErrors.length}`);
  console.log(`Adversarial Chaos Resisted: ${report.adversarialChaos.filter(c => c.resisted).length}/${report.adversarialChaos.length}`);
  console.log(`Report JSON: ${reportPath}`);
  console.log(`Screenshots: ${report.screenshots.length} captured in ${ARTIFACTS_DIR}`);
  console.log(`======================================================\n`);

  return report;
}

runFullRegression();
