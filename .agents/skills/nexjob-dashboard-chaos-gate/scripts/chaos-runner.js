#!/usr/bin/env node
/**
 * NexJob Dashboard Chaos & Persona Simulator
 * Runs headless browser walkthroughs, boundary/impossible tests, and persona simulations against NexJob dashboard.
 */

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const BASE_URL = process.env.NEXJOB_DASHBOARD_URL || 'http://localhost:5005/dashboard';
const ARTIFACTS_DIR = process.env.CHAOS_ARTIFACTS_DIR || path.join(process.cwd(), '.dashboard-simulations');

if (!fs.existsSync(ARTIFACTS_DIR)) {
  fs.mkdirSync(ARTIFACTS_DIR, { recursive: true });
}

async function run() {
  console.log(`[🚀 Chaos Gate] Launching simulation against ${BASE_URL}...`);
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1280, height: 800 }
  });

  const page = await context.newPage();
  const consoleErrors = [];
  const networkErrors = [];

  page.on('console', msg => {
    if (msg.type() === 'error') {
      consoleErrors.push(msg.text());
    }
  });

  page.on('response', res => {
    if (res.status() >= 500) {
      networkErrors.push({ url: res.url(), status: res.status() });
    }
  });

  const results = {
    testedAt: new Date().toISOString(),
    baseUrl: BASE_URL,
    personas: {},
    impossibleTests: [],
    consoleErrors,
    networkErrors,
    screenshots: []
  };

  try {
    // -------------------------------------------------------------
    // PERSONA 1: 👩‍💼 User / Operator (Catalog Discovery & Trigger)
    // -------------------------------------------------------------
    console.log('[👩‍💼 Persona: User/Operator] Testing Catalog ergonomics & trigger flow...');
    await page.goto(`${BASE_URL}/catalog`, { waitUntil: 'domcontentloaded' });
    const catalogPath = path.join(ARTIFACTS_DIR, '01_catalog_overview.png');
    await page.screenshot({ path: catalogPath, fullPage: true });
    results.screenshots.push('01_catalog_overview.png');

    // Check if trigger button exists
    const directTriggerButton = await page.$('form[action*="/trigger"] button[type="submit"]');
    const modalTriggerButton = await page.$('.trigger-modal-btn');
    let userTriggered = false;

    if (directTriggerButton) {
      console.log('[👩‍💼 Persona: User/Operator] Found direct parameterless trigger button. Enqueuing...');
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
        directTriggerButton.click()
      ]);
      const triggeredUrl = page.url();
      const hasTriggeredBanner = await page.$('.alert-success');
      const triggeredScreenshot = path.join(ARTIFACTS_DIR, '02_catalog_triggered.png');
      await page.screenshot({ path: triggeredScreenshot, fullPage: true });
      results.screenshots.push('02_catalog_triggered.png');

      results.personas.User = {
        status: hasTriggeredBanner ? 'PASS' : 'WARN',
        urlAfterTrigger: triggeredUrl,
        receivedSuccessFeedback: !!hasTriggeredBanner,
      };
      userTriggered = true;
    } else if (modalTriggerButton) {
      console.log('[👩‍💼 Persona: User/Operator] Opening parameterized trigger modal...');
      await modalTriggerButton.click();
      await page.waitForTimeout(300);
      const submitBtn = await page.$('#triggerModalForm button[type="submit"]');
      if (submitBtn) {
        await Promise.all([
          page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
          submitBtn.click()
        ]);
        const triggeredUrl = page.url();
        const hasTriggeredBanner = await page.$('.alert-success');
        const triggeredScreenshot = path.join(ARTIFACTS_DIR, '02_catalog_triggered.png');
        await page.screenshot({ path: triggeredScreenshot, fullPage: true });
        results.screenshots.push('02_catalog_triggered.png');

        results.personas.User = {
          status: hasTriggeredBanner ? 'PASS' : 'WARN',
          urlAfterTrigger: triggeredUrl,
          receivedSuccessFeedback: !!hasTriggeredBanner,
        };
        userTriggered = true;
      }
    } else {
      results.personas.User = {
        status: 'INFO',
        message: 'No runnable catalog definitions currently in storage.'
      };
    }

    // -------------------------------------------------------------
    // PERSONA 2: 👨‍💻 Developer (Failure Investigation & Checkpoint)
    // -------------------------------------------------------------
    console.log('[👨‍💻 Persona: Developer] Inspecting error trails, filters & checkpoints...');
    await page.goto(`${BASE_URL}/failed`, { waitUntil: 'domcontentloaded' });
    const failedScreenshot = path.join(ARTIFACTS_DIR, '03_failed_jobs.png');
    await page.screenshot({ path: failedScreenshot, fullPage: true });
    results.screenshots.push('03_failed_jobs.png');

    // Go to Jobs listing with complex filter chips
    await page.goto(`${BASE_URL}/jobs?status=Enqueued&queue=default&search=Order`, { waitUntil: 'domcontentloaded' });
    const filterChips = await page.$$('.filter-chip, .badge, [data-testid="filter-chip"]');
    const jobsFilterScreenshot = path.join(ARTIFACTS_DIR, '04_filtered_jobs.png');
    await page.screenshot({ path: jobsFilterScreenshot, fullPage: true });
    results.screenshots.push('04_filtered_jobs.png');

    // Check job detail if any job exists
    const jobLink = await page.$('a[href*="/jobs/"]');
    let devInspectedJob = false;
    if (jobLink) {
      const href = await jobLink.getAttribute('href');
      await page.goto(`${BASE_URL}${href.startsWith('/') ? '' : '/'}${href}`, { waitUntil: 'domcontentloaded' });
      const jobDetailScreenshot = path.join(ARTIFACTS_DIR, '05_job_detail.png');
      await page.screenshot({ path: jobDetailScreenshot, fullPage: true });
      results.screenshots.push('05_job_detail.png');
      devInspectedJob = true;
    }

    results.personas.Developer = {
      status: 'PASS',
      filterChipsDetected: filterChips.length > 0,
      inspectedJobDetail: devInspectedJob
    };

    // -------------------------------------------------------------
    // PERSONA 3: 🚨 SRE (Health, Queues, Pause Confirm, Mobile)
    // -------------------------------------------------------------
    console.log('[🚨 Persona: SRE] Auditing cluster safety, orphan queues & responsiveness...');
    await page.goto(`${BASE_URL}/queues`, { waitUntil: 'domcontentloaded' });
    const queuesScreenshot = path.join(ARTIFACTS_DIR, '06_queues_overview.png');
    await page.screenshot({ path: queuesScreenshot, fullPage: true });
    results.screenshots.push('06_queues_overview.png');

    // Check Settings & Pause All Confirm safety
    await page.goto(`${BASE_URL}/settings`, { waitUntil: 'domcontentloaded' });
    const pauseAllButton = await page.$('form[action*="/recurring/pause-all"] button, button:has-text("Pause All")');
    let hasConfirmGuard = false;
    if (pauseAllButton) {
      const onclickAttr = await pauseAllButton.getAttribute('onclick');
      hasConfirmGuard = !!(onclickAttr && onclickAttr.includes('confirm'));
    }

    // Mobile viewport audit (Max-width: 390px - iPhone 14 style)
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`${BASE_URL}`, { waitUntil: 'domcontentloaded' });
    const mobileScreenshot = path.join(ARTIFACTS_DIR, '07_mobile_topology.png');
    await page.screenshot({ path: mobileScreenshot, fullPage: true });
    results.screenshots.push('07_mobile_topology.png');

    results.personas.SRE = {
      status: hasConfirmGuard ? 'PASS' : 'WARN',
      pauseAllConfirmGuard: hasConfirmGuard,
      mobileAuditCaptured: true
    };

    // Restore viewport
    await page.setViewportSize({ width: 1280, height: 800 });

    // -------------------------------------------------------------
    // 💥 IMPOSSIBLE / BOUNDARY ADVERSARIAL TESTS
    // -------------------------------------------------------------
    console.log('[💥 Impossible & Boundary Tests] Fuzzing query params, overflows, and dead ends...');

    const adversarialUrls = [
      { name: 'Negative & Massive Pagination', url: `${BASE_URL}/jobs?page=-999999&limit=999999999` },
      { name: 'Malformed Status & Nonexistent Cluster', url: `${BASE_URL}/jobs?status=__SQL_INJECTION__&cluster=non_existent_cluster_99` },
      { name: 'Extreme Unicode Search Query', url: `${BASE_URL}/catalog?search=🔥🔥🔥%20%3Cscript%3Ealert(1)%3C/script%3E` },
      { name: 'Corrupted Job ID Format', url: `${BASE_URL}/jobs/corrupted-non-guid-job-id-xyz` }
    ];

    for (const testCase of adversarialUrls) {
      const res = await page.goto(testCase.url, { waitUntil: 'domcontentloaded' });
      const status = res ? res.status() : 0;
      const html = await page.content();
      const crashed = status >= 500 || html.includes('Internal Server Error') || html.includes('NullReferenceException');

      results.impossibleTests.push({
        name: testCase.name,
        url: testCase.url,
        httpStatus: status,
        resisted: !crashed
      });
    }

  } catch (err) {
    console.error('[❌ Chaos Gate Error]', err);
    results.fatalError = err.message;
  } finally {
    await browser.close();
  }

  const resultFile = path.join(ARTIFACTS_DIR, 'chaos-report.json');
  fs.writeFileSync(resultFile, JSON.stringify(results, null, 2));
  console.log(`[✅ Chaos Gate] Run complete! Artifacts & report stored at ${ARTIFACTS_DIR}`);
  console.log(JSON.stringify(results, null, 2));
}

run();
