const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright-core');

const base = process.env.BEAFSOUP_WEB_URL || 'http://localhost:5287';
const apiBase = process.env.BEAFSOUP_API_URL || 'http://localhost:5049';
const browserPath = process.env.BEAFSOUP_BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const output = path.resolve(__dirname, 'screenshots');
fs.mkdirSync(output, { recursive: true });
const lessons = [
  'de-l01-greetings', 'de-l02-personal-information', 'de-l03-numbers-time',
  'de-l04-family', 'de-l05-food-cafe', 'de-l06-daily-routine'
];

(async () => {
  const browser = await chromium.launch({ executablePath: browserPath, headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage();
  const errors = [];
  const report = { lessons: 0, vocabulary: 0, sentences: 0, audioUrls: 0, audioPlayed: 0, graded: 0 };
  const expectedNotFound = new Set();
  const expectedNetworkErrors = new Set();
  page.on('pageerror', error => errors.push(`pageerror: ${error.message}`));
  page.on('console', message => {
    if (message.type() === 'error' && !expectedNetworkErrors.has(message.location().url)) {
      errors.push(`console: ${message.text()} (${JSON.stringify(message.location())})`);
    }
  });
  page.on('response', response => {
    if (response.status() >= 400 && !expectedNotFound.has(response.url())) {
      errors.push(`http ${response.status()}: ${response.url()}`);
    }
  });
  page.on('requestfailed', request => {
    if (request.failure()?.errorText !== 'net::ERR_ABORTED') {
      errors.push(`requestfailed: ${request.url()} ${request.failure()?.errorText}`);
    }
  });
  await page.addInitScript(() => {
    window.__playedAudio = [];
    window.__audioErrors = [];
    window.__lastAudioReady = false;
    const originalPlay = HTMLMediaElement.prototype.play;
    HTMLMediaElement.prototype.play = function () {
      window.__playedAudio.push(this.currentSrc || this.src);
      window.__lastAudioReady = false;
      this.addEventListener('loadedmetadata', () => { window.__lastAudioReady = true; }, { once: true });
      const promise = originalPlay.call(this);
      promise.catch(error => window.__audioErrors.push(`${this.currentSrc}: ${error.name}: ${error.message}`));
      return promise;
    };
    const originalPause = HTMLMediaElement.prototype.pause;
    HTMLMediaElement.prototype.pause = function () {
      window.__playedAudio.push(`pause:${this.currentSrc}`);
      return originalPause.call(this);
    };
  });

  const progress = () => page.evaluate(() => {
    const value = JSON.parse(localStorage.getItem('beafsoup.guest-progress.v1'));
    return { attempts: value.Attempts ?? value.attempts, completed: value.CompletedLessons ?? value.completedLessons };
  });
  const waitForQuestion = () => page.locator('.question-card').waitFor();
  const gradeCurrent = async (optionIndex = 0) => {
    await page.locator('.option-button').nth(optionIndex).click();
    await page.getByRole('button', { name: '提交答案' }).click();
    await page.locator('.answer-feedback').waitFor();
    report.graded++;
    assert.equal(await page.locator('.option-button:disabled').count(), 4);
    assert.ok((await page.locator('.answer-feedback').innerText()).trim().length > 10);
  };
  const finishLesson = async (id, restart = false) => {
    await page.goto(`${base}/practice/${id}`, { waitUntil: 'domcontentloaded' });
    await waitForQuestion();
    for (let i = 0; i < 10; i++) {
      assert.match(await page.locator('.practice-progress').innerText(), new RegExp(`第 ${i + 1} 题`));
      assert.equal(await page.getByRole('button', { name: '提交答案' }).isDisabled(), true);
      await gradeCurrent(i % 4);
      await page.getByRole('button', { name: i === 9 ? /查看结果/ : /下一题/ }).click();
    }
    await page.locator('.result-card').waitFor();
    assert.match(await page.locator('.result-card').innerText(), /\/ 10/);
    if (restart) {
      await page.getByRole('button', { name: '再练一遍' }).click();
      await waitForQuestion();
      assert.match(await page.locator('.practice-progress').innerText(), /第 1 题/);
    }
  };

  try {
    await page.goto(base, { waitUntil: 'domcontentloaded' });
    await page.locator('.lesson-card').first().waitFor({ timeout: 30000 });
    await page.waitForLoadState('networkidle');
    assert.equal(await page.locator('.lesson-card').count(), 6);
    assert.match(await page.locator('.course-metrics').innerText(), /60/);
    assert.match(await page.locator('.course-metrics').innerText(), /30/);

    for (const id of lessons) {
      await page.goto(`${base}/lessons/${id}`, { waitUntil: 'domcontentloaded' });
      await page.locator('.vocabulary-card').first().waitFor();
      assert.equal(await page.locator('.vocabulary-card').count(), 10);
      assert.equal(await page.locator('.sentence-card').count(), 5);
      assert.equal(await page.locator('.audio-button:disabled').count(), 0);
      const audioCount = await page.locator('.audio-button').count();
      report.audioUrls += audioCount;
      report.vocabulary += 10;
      report.sentences += 5;
      report.lessons++;
      for (const card of await page.locator('.sentence-card').all()) {
        await card.locator('.translation-toggle').click();
        await card.locator('.sentence-translation').waitFor();
        assert.ok((await card.locator('.sentence-translation').innerText()).trim().length > 0);
      }
      await page.locator('.sentence-token').first().click();
      await page.locator('.word-hint').first().waitFor();
      await page.locator('.vocabulary-card .audio-button').first().click();
      await page.locator('.sentence-card .audio-button').first().click();
      await page.waitForFunction(() => window.__playedAudio.length >= 2);
      await page.waitForFunction(() => window.__lastAudioReady === true, null, { timeout: 15000 });
      const played = await page.evaluate(() => window.__playedAudio.filter(url => !url.startsWith('pause:')));
      assert.equal(played.length, 2, JSON.stringify(played));
      assert.ok(played.every(url => url.includes('/audio/de-DE/')), JSON.stringify(played));
      report.audioPlayed += played.length;
      assert.equal(await page.locator('.audio-error').count(), 0, JSON.stringify(await page.evaluate(() => ({ played: window.__playedAudio, errors: window.__audioErrors }))));
    }
    assert.equal(report.audioUrls, 90);
    assert.equal(report.audioPlayed, 12);

    const audioUrls = new Set();
    const course = await (await page.request.get(`${apiBase}/api/v1/courses`)).json();
    const detail = await (await page.request.get(`${apiBase}/api/v1/courses/${course.id}`)).json();
    for (const lesson of detail.lessons) {
      const lessonDetail = await (await page.request.get(`${apiBase}/api/v1/lessons/${lesson.id}`)).json();
      for (const url of [
        ...lessonDetail.vocabulary.map(word => word.audioUrl),
        ...lessonDetail.sentences.map(sentence => sentence.audioUrl)
      ]) {
        assert.ok(url, `缺少音频地址：${lesson.id}`);
        audioUrls.add(url);
      }
    }
    assert.equal(audioUrls.size, 90);
    for (const url of audioUrls) {
      const response = await page.request.get(`${apiBase}${url}`);
      assert.equal(response.status(), 200, url);
      assert.match(response.headers()['content-type'], /^audio\//, url);
      assert.ok((await response.body()).length > 1000, url);
    }

    await page.goto(`${base}/lessons/de-l04-family`, { waitUntil: 'domcontentloaded' });
    await page.locator('.sentence-card').nth(2).locator('.sentence-token.is-hint').click();
    assert.match(await page.locator('.sentence-card').nth(2).locator('.word-hint').innerText(), /阳性单数第四格/);
    await page.locator('.sentence-card').nth(2).locator('.translation-toggle').click();
    assert.match(await page.locator('.sentence-card').nth(2).locator('.sentence-note').innerText(), /第四格/);

    await page.goto(`${base}/practice/de-l01-greetings`, { waitUntil: 'domcontentloaded' });
    await waitForQuestion();
    assert.equal(await page.getByRole('button', { name: '提交答案' }).isDisabled(), true);
    assert.equal(await page.getByRole('button', { name: /下一题/ }).count(), 0);
    await gradeCurrent(0);
    assert.equal((await progress()).attempts.length, 1);
    await page.reload({ waitUntil: 'domcontentloaded' });
    await waitForQuestion();
    assert.match(await page.locator('.practice-progress').innerText(), /第 1 题/);
    assert.equal((await progress()).attempts.length, 1);
    for (let i = 0; i < 10; i++) {
      await gradeCurrent(i % 4);
      await page.getByRole('button', { name: i === 9 ? /查看结果/ : /下一题/ }).click();
    }
    await page.locator('.result-card').waitFor();
    assert.deepEqual((await progress()).completed, ['de-l01-greetings']);
    assert.equal((await progress()).attempts.length, 11);
    await page.screenshot({ path: path.join(output, '06-lesson-result.png'), fullPage: true });
    await page.getByRole('button', { name: '再练一遍' }).click();
    await waitForQuestion();
    assert.match(await page.locator('.practice-progress').innerText(), /第 1 题/);
    assert.equal(await page.locator('.answer-feedback').count(), 0);

    for (const id of lessons.slice(1)) await finishLesson(id);
    assert.equal((await progress()).attempts.length, 61);
    assert.equal((await progress()).completed.length, 6);
    await page.goto(base, { waitUntil: 'domcontentloaded' });
    await page.locator('.lesson-card').first().waitFor();
    assert.equal(await page.locator('.lesson-card.is-complete').count(), 6);
    assert.match(await page.locator('.local-progress').innerText(), /6 \/ 6 课已完成/);
    await page.screenshot({ path: path.join(output, '07-course-complete.png'), fullPage: true });
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.locator('.lesson-card').first().waitFor();
    assert.equal(await page.locator('.lesson-card.is-complete').count(), 6);

    await page.goto(`${base}/practice`, { waitUntil: 'domcontentloaded' });
    await waitForQuestion();
    assert.match(await page.locator('.practice-progress').innerText(), /第 1 题.*60/);
    for (let i = 0; i < 60; i++) {
      assert.match(await page.locator('.practice-progress').innerText(), new RegExp(`第 ${i + 1} 题.*60`));
      await gradeCurrent(i % 4);
      await page.getByRole('button', { name: i === 59 ? /查看结果/ : /下一题/ }).click();
    }
    await page.locator('.result-card').waitFor();
    assert.match(await page.locator('.result-card').innerText(), /\/ 60/);
    assert.equal((await progress()).attempts.length, 121);
    assert.deepEqual((await progress()).completed, lessons);
    await page.screenshot({ path: path.join(output, '09-full-bank-result.png'), fullPage: true });
    await page.goto(`${base}/practice`, { waitUntil: 'domcontentloaded' });
    await waitForQuestion();
    await page.locator('.lesson-filter select').selectOption('de-l03-numbers-time');
    await waitForQuestion();
    assert.match(await page.locator('.practice-progress').innerText(), /第 1 题.*10/);
    assert.match(page.url(), /de-l03-numbers-time/);
    await page.locator('.lesson-filter select').selectOption('');
    await waitForQuestion();
    assert.match(await page.locator('.practice-progress').innerText(), /第 1 题.*60/);
    assert.match(await page.locator('.practice-heading h1').innerText(), /完整德语选择题/);

    for (const path of ['/api/v1/lessons/de-l99-missing', '/api/v1/lessons/de-l99-missing/questions']) {
      const url = new URL(path, apiBase).href;
      expectedNotFound.add(url);
      expectedNetworkErrors.add(url);
    }
    await page.goto(`${base}/lessons/de-l99-missing`, { waitUntil: 'domcontentloaded' });
    await page.getByRole('heading', { name: '课时无法加载' }).waitFor();
    assert.match(await page.locator('.error-state').innerText(), /不存在/);
    await page.goto(`${base}/practice/de-l99-missing`, { waitUntil: 'domcontentloaded' });
    await page.getByRole('heading', { name: '练习无法加载' }).waitFor();
    assert.match(await page.locator('.error-state').innerText(), /不存在/);
    await page.goto(`${base}/missing-page`, { waitUntil: 'domcontentloaded' });
    await page.getByRole('heading', { name: '没有找到这个页面' }).waitFor();

    const degraded = await context.newPage();
    await degraded.addInitScript(() => {
      Storage.prototype.setItem = () => { throw new DOMException('Blocked', 'SecurityError'); };
    });
    await degraded.goto(`${base}/practice/de-l01-greetings`, { waitUntil: 'networkidle' });
    await degraded.locator('.question-card').waitFor();
    await degraded.locator('.option-button').first().click();
    await degraded.getByRole('button', { name: '提交答案' }).click();
    await degraded.locator('.answer-feedback').waitFor();
    assert.equal(await degraded.evaluate(() => {
      try { localStorage.setItem('beafsoup-check', '1'); return false; } catch { return true; }
    }), true);
    assert.match(await degraded.locator('.inline-error').innerText(), /无法保存/);
    await degraded.close();

    await page.goto(base, { waitUntil: 'networkidle' });
    await page.locator('.lesson-card').first().waitFor();
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.goto(`${base}/practice/de-l01-greetings`, { waitUntil: 'domcontentloaded' });
    await waitForQuestion();
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.screenshot({ path: path.join(output, '08-mobile-final.png'), fullPage: true });

    assert.equal(errors.length, 0, errors.join('\n'));
    console.log(JSON.stringify({ result: 'passed', ...report, completedLessons: 6, storedAttempts: 121, fullBankQuestions: 60, consoleErrors: errors }, null, 2));
  } catch (error) {
    await page.screenshot({ path: path.join(output, 'full-course-failure.png'), fullPage: true }).catch(() => {});
    console.error(error);
    console.error(errors.join('\n'));
    process.exitCode = 1;
  } finally {
    await browser.close();
  }
})();
