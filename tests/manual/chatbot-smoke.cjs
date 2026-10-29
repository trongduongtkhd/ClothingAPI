// Read-only smoke test of the deployed chatbot. No products, orders or accounts are changed.
// Node >=20, PLAYWRIGHT_MODULE_PATH=path/to/playwright, CHATBOT_TEST_OUTPUT=artifact directory.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE_PATH);
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const output = process.env.CHATBOT_TEST_OUTPUT;
fs.mkdirSync(output, { recursive: true });
const report = { cases: [], errors: [] };

(async () => {
  const browser = await chromium.launch({ headless: true,
    executablePath: 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe' });
  try {
    const page = await browser.newPage({ viewport: { width: 1366, height: 900 } });
    page.on('pageerror', error => report.errors.push(error.message));
    await page.goto('http://localhost:4200/products?categoryId=1');
    await page.locator('.chat-launcher').click();
    await page.locator('.shopping-chat').waitFor();
    await page.screenshot({ path: path.join(output, 'desktop-welcome.png') });

    async function ask(message) {
      const start = Date.now();
      const response = page.waitForResponse(r => r.url().endsWith('/api/chat') && r.request().method() === 'POST');
      await page.locator('#shopping-chat-input').fill(message);
      await page.locator('.send-button').click();
      const res = await response;
      const body = await res.json();
      assert.equal(res.status(), 200, JSON.stringify(body));
      await page.locator('.thinking').waitFor({ state: 'hidden' });
      report.cases.push({ message, milliseconds: Date.now() - start, products: body.data.products.map(p => p.productName), reply: body.data.reply });
      return body.data;
    }

    const search = await ask('Tìm áo nam dưới 150 nghìn');
    assert.equal(search.products.length, 5);
    assert(search.products.every(p => p.maxPrice <= 150000));
    const first = await ask('Cái đầu tiên còn màu và size nào?');
    assert.equal(first.products.length, 1);
    assert.equal(first.products[0].productId, search.products[0].productId);
    await page.screenshot({ path: path.join(output, 'desktop-conversation.png') });
    const compare = await ask('So sánh Áo thun Basic với Áo Thun Trắng');
    assert.equal(compare.products.length, 2);
    assert(compare.reply.includes('149.000đ') && compare.reply.includes('120.000đ'));
    const policy = await ask('Chính sách trả hàng');
    assert(policy.reply.includes('7 ngày'));

    await page.goto('http://localhost:4200/products/ao-polo');
    await page.locator('.chat-launcher').click();
    const contextResponse = page.waitForResponse(r => r.url().endsWith('/api/chat') && r.request().method() === 'POST');
    await page.locator('.product-context').click();
    const context = await (await contextResponse).json();
    assert.equal(context.data.products[0].slug, 'ao-polo');
    report.cases.push({ message: 'Current product context', product: context.data.products[0].productName });
    await page.locator('.thinking').waitFor({ state: 'hidden' });

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForFunction(() => {
      const latest = document.querySelector('.chat-message:last-of-type');
      const list = document.querySelector('.message-list');
      return latest && list && latest.getBoundingClientRect().bottom <= list.getBoundingClientRect().bottom + 1;
    });
    await page.screenshot({ path: path.join(output, 'mobile-conversation.png') });
    const panel = await page.locator('.shopping-chat').boundingBox();
    assert(panel.x >= 0 && panel.x + panel.width <= 390 && panel.y >= 0 && panel.y + panel.height <= 844);
    const input = await page.locator('#shopping-chat-input').boundingBox();
    assert(input.y + input.height < 844);
    report.cases.push({ message: 'Mobile panel and composer fit viewport', panel });

    await page.route('**/api/chat', route => route.fulfill({ status: 503, contentType: 'application/json',
      body: JSON.stringify({ success: false, message: 'Trợ lý AI đang tạm ngừng hoạt động.' }) }));
    await page.locator('#shopping-chat-input').fill('Tìm áo');
    await page.locator('.send-button').click();
    await page.locator('.chat-error').waitFor();
    assert((await page.locator('.chat-error').innerText()).includes('Tìm sản phẩm'));
    await page.screenshot({ path: path.join(output, 'mobile-unavailable.png') });
    report.cases.push({ message: 'Unavailable AI shows retry and catalog fallback' });
    assert.deepEqual(report.errors, []);
    report.passed = true;
  } catch (error) {
    report.passed = false; report.failure = error.stack; process.exitCode = 1;
  } finally {
    await browser.close();
    fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify(report, null, 2));
  }
})();
