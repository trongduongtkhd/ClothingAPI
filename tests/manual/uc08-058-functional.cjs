// End-to-end regression of FUNC 058.1–058.40 against the exact deployed images.
// Uses disposable SQL Server database/containers; never writes the main database.
// Run with Node >= 20 and PLAYWRIGHT_MODULE_PATH pointing to an installed Playwright package.
const { execFileSync } = require('node:child_process');
const { randomBytes, createHmac } = require('node:crypto');
const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const pwPath = process.env.PLAYWRIGHT_MODULE_PATH;
if (!pwPath) throw new Error('Set PLAYWRIGHT_MODULE_PATH to the installed Playwright package');
const { chromium } = require(pwPath);
const { expect: baseExpect } = require(path.join(pwPath, 'test'));
const expect = baseExpect.configure({ timeout: 10000 });
const suffix = new Date().toISOString().replace(/\D/g, '').slice(0, 14) + '_' + randomBytes(3).toString('hex');
const database = 'UC08_' + suffix;
const apiContainer = 'uc08-api-' + suffix.toLowerCase();
const webContainer = 'uc08-web-' + suffix.toLowerCase();
const apiBase = 'http://127.0.0.1:18082';
const appBase = 'http://127.0.0.1:14202';
const output = path.join(__dirname, 'results', database);
fs.mkdirSync(output, { recursive: true });
const report = { startedAt: new Date().toISOString(), database, apiContainer, webContainer,
  method: 'Real production Angular bundles in isolated headless Edge + real API/SQL Server. Authentication and stock/legacy data are test fixtures. Network failures are scoped browser request aborts. No production records modified.', cases: [] };
const save = () => fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify(report, null, 2));
const selectedArgument = process.argv.find(arg => arg.startsWith('--cases='));
const selectedCases = selectedArgument ? new Set(selectedArgument.slice(8).split(',').map(Number)) : null;
report.selectedCases = selectedCases ? [...selectedCases] : 'all';
let browser, customers, jwtKey;
const password = 'Cart@' + randomBytes(10).toString('hex');
function docker(args, opts = {}) {
  try { return execFileSync('docker', args, { encoding: 'utf8', stdio: ['pipe', 'pipe', 'pipe'], ...opts }).trim(); }
  catch (e) { throw new Error('Docker ' + args[0] + ' failed: ' + String(e.stderr || '').slice(0, 1200)); }
}
function sql(query, db = database) {
  return docker(['exec', '-i', 'clothing-db', 'sh', '-c',
    'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d "$1" -y 0 -w 65535',
    'uc08', db], { input: 'SET NOCOUNT ON;\n' + query + '\nGO\n' });
}
const jsonSql = query => JSON.parse(sql(query));
async function api(method, endpoint, body, token, raw = false) {
  return new Promise((resolve, reject) => {
    const payload = body === undefined ? undefined : raw ? body : JSON.stringify(body);
    const req = http.request(apiBase + endpoint, { method, headers: { 'Content-Type': 'application/json',
      ...(payload ? { 'Content-Length': Buffer.byteLength(payload) } : {}),
      ...(token ? { Authorization: 'Bearer ' + token } : {}) } }, res => {
      const chunks = [];
      res.on('data', c => chunks.push(c));
      res.on('end', () => {
        const text = Buffer.concat(chunks).toString('utf8');
        try { resolve({ status: res.statusCode, body: text ? JSON.parse(text) : null }); }
        catch { reject(new Error('Invalid JSON response for ' + method + ' ' + endpoint)); }
      });
    });
    req.on('error', reject); req.setTimeout(20000, () => req.destroy(new Error('HTTP timeout'))); req.end(payload);
  });
}
function ok(result, status = 200) { assert.equal(result.status, status, JSON.stringify(result)); return result.body.data; }
const cart = async (user = customers[0]) => ok(await api('GET', '/api/cart', undefined, user.token));
const add = async (variant, quantity, user = customers[0]) => ok(await api('POST', '/api/cart/items', { variantId: variant.id, quantity }, user.token), 201);
const clear = async (user = customers[0]) => ok(await api('DELETE', '/api/cart', undefined, user.token));
const item = (data, variant) => data.items.find(i => i.variantId === variant.id);
function stock(variant, quantity) { sql(`UPDATE ProductVariants SET StockQuantity=${quantity} WHERE VariantId=${variant.id};`); }
function sale(variant, active, parent = false) { sql(parent
  ? `UPDATE Products SET IsActive=${active ? 1 : 0} WHERE ProductId=${variant.productId};`
  : `UPDATE ProductVariants SET IsActive=${active ? 1 : 0} WHERE VariantId=${variant.id};`); }
function fixture(number) {
  const prefix = `UC08-${suffix}-${number}`;
  const values = jsonSql(`
DECLARE @c int=(SELECT TOP(1) CategoryId FROM Categories ORDER BY CategoryId);
DECLARE @black int=(SELECT ColorId FROM Colors WHERE ColorName=N'Đen');
DECLARE @be int=(SELECT ColorId FROM Colors WHERE ColorName=N'Be');
DECLARE @m int=(SELECT SizeId FROM Sizes WHERE SizeName='M');
DECLARE @l int=(SELECT SizeId FROM Sizes WHERE SizeName='L');
DECLARE @p TABLE(label varchar(1), productId int, name nvarchar(250), slug varchar(300), price int);
INSERT Products(CategoryId,ProductName,Slug,Description,Gender,BasePrice,IsFeatured,IsActive,CreatedAt)
VALUES(@c,N'${prefix}-X','${prefix.toLowerCase()}-x',N'UC08 isolated functional test','Unisex',100000,0,1,GETUTCDATE());
INSERT @p VALUES('x',SCOPE_IDENTITY(),N'${prefix}-X','${prefix.toLowerCase()}-x',100000);
INSERT Products(CategoryId,ProductName,Slug,Description,Gender,BasePrice,IsFeatured,IsActive,CreatedAt)
VALUES(@c,N'${prefix}-Y','${prefix.toLowerCase()}-y',N'UC08 isolated functional test','Unisex',150000,0,1,GETUTCDATE());
INSERT @p VALUES('y',SCOPE_IDENTITY(),N'${prefix}-Y','${prefix.toLowerCase()}-y',150000);
INSERT Products(CategoryId,ProductName,Slug,Description,Gender,BasePrice,IsFeatured,IsActive,CreatedAt)
VALUES(@c,N'${prefix}-Z','${prefix.toLowerCase()}-z',N'UC08 isolated functional test','Unisex',150000,0,1,GETUTCDATE());
INSERT @p VALUES('z',SCOPE_IDENTITY(),N'${prefix}-Z','${prefix.toLowerCase()}-z',150000);
INSERT ProductVariants(ProductId,ColorId,SizeId,SKU,StockQuantity,IsActive)
SELECT productId,CASE WHEN label='y' THEN @be ELSE @black END,CASE WHEN label='x' THEN @m ELSE @l END,'${prefix}-'+label,200,1 FROM @p;
SELECT p.label,p.productId,p.name,p.slug,p.price,v.VariantId AS id FROM @p p JOIN ProductVariants v ON v.ProductId=p.productId FOR JSON PATH;`);
  return Object.fromEntries(values.map(v => [v.label, v]));
}
async function session(user = customers[0]) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'vi-VN' });
  context.setDefaultTimeout(10000);
  const response = await context.request.post(appBase + '/api/auth/login', { data: { email: user.email, password, captchaToken: 'isolated-test-fixture' } });
  assert.equal(response.status(), 200, 'Authentication fixture failed');
  const auth = (await response.json()).data;
  await context.addInitScript(({ origin, user }) => {
    if (location.origin === origin && !sessionStorage.getItem('uc08FixtureInitialized')) {
      localStorage.setItem('clothing_current_user', JSON.stringify(user));
      sessionStorage.setItem('uc08FixtureInitialized', '1');
    }
  }, { origin: appBase, user: auth.user });
  return context;
}
async function load(t, reload = false) {
  if (reload) await t.page.reload(); else await t.page.goto(appBase + '/cart');
  await expect(t.page.locator('app-cart')).toBeVisible();
  await expect(t.page.locator('app-cart .spinner-border').first()).toBeHidden();
}
const row = (t, v) => t.page.locator('.cart-item').filter({ hasText: v.name });
async function quantity(t, v, count) { await expect(row(t, v).locator('.quantity-value')).toHaveText(String(count)); }
async function badge(t, count) {
  const locator = t.page.locator('header a[title="Giỏ hàng"] .icon-badge');
  if (count === 0) await expect(locator).toHaveCount(0); else await expect(locator).toHaveText(String(count));
}
async function total(t, expected) { await expect(t.page.locator('.summary-total strong')).toHaveText(new RegExp(expected.toLocaleString('en-US').replace(/,/g, '[,.]'))); }
async function delta(t, v, direction, expected) {
  await row(t, v).getByRole('button', { name: direction === '+' ? 'Tăng số lượng' : 'Giảm số lượng' }).click();
  await quantity(t, v, expected);
}
async function remove(t, v, accept = true) {
  t.page.once('dialog', dialog => accept ? dialog.accept() : dialog.dismiss());
  await row(t, v).locator('.remove-button').click();
  if (accept) await expect(row(t, v)).toHaveCount(0);
}
async function detail(t, v, count = 1) {
  await t.page.goto(appBase + '/products/' + v.slug);
  await expect(t.page.locator('.color-button').first()).toBeVisible();
  await t.page.locator('.color-button').first().click();
  await t.page.locator('.size-button').first().click();
  for (let i = 1; i < count; i++) await t.page.locator('.product-quantity-control').getByRole('button', { name: 'Tăng số lượng' }).click();
}
async function submitAdd(t, success = true) {
  const response = t.page.waitForResponse(r => r.url().endsWith('/api/cart/items') && r.request().method() === 'POST');
  await t.page.locator('.add-cart-button').click();
  const result = await response;
  assert.equal(result.status(), success ? 201 : 400);
  await expect(t.page.locator(success ? '.alert-success' : '.alert-danger')).toBeVisible();
  if (!success) await expect(t.page.locator('.alert-danger')).toContainText('Số lượng phải từ 1 đến 100.');
}
async function persisted(expected) {
  const actual = await cart();
  assert.equal(actual.items.length, expected.length);
  for (const [variant, count] of expected) assert.equal(item(actual, variant)?.quantity, count);
  return actual;
}
async function run(number, title, test) {
  if (selectedCases && !selectedCases.has(number)) return;
  const result = { id: 'FUNC 058.' + number, title, startedAt: new Date().toISOString(), network: [] };
  report.cases.push(result); save();
  let context, t;
  try {
    await clear(); await clear(customers[1]);
    const f = fixture(number);
    context = await session();
    t = { ...f, context, page: await context.newPage(), result };
    t.page.on('response', response => {
      if (/\/api\/cart(?:\/|$)/.test(new URL(response.url()).pathname)) result.network.push({ method: response.request().method(), path: new URL(response.url()).pathname, status: response.status() });
    });
    t.page.on('requestfailed', req => { if (req.url().includes('/api/cart')) result.network.push({ method: req.method(), path: new URL(req.url()).pathname, failed: req.failure()?.errorText }); });
    await test(t);
    result.status = 'Passed';
    result.actual = 'Các bước và kiểm tra tự động của testcase đều đạt trên giao diện/API thật; dữ liệu lưu trong SQL được đối chiếu.';
  } catch (error) {
    result.status = 'Failed';
    result.actual = String(error.message).replace(/Bearer\s+[^\s]+/g, 'Bearer [redacted]').slice(0, 4000);
  } finally {
    if (t) {
      try { await t.page.screenshot({ path: path.join(output, 'FUNC-058-' + number + '.png'), fullPage: true, timeout: 5000 }); result.screenshot = 'FUNC-058-' + number + '.png'; } catch {}
      try { result.persistedCart = await cart(); } catch {}
    }
    if (context) await context.close();
    result.completedAt = new Date().toISOString(); save();
    console.log(result.id + ': ' + result.status + (result.status === 'Failed' ? ' — ' + result.actual.split('\n')[0] : ''));
  }
}

async function tests() {
  await run(1, 'Xóa đúng 1/3 dòng', async t => {
    await add(t.x, 1); await add(t.y, 3); await add(t.z, 2); await load(t); await total(t, 850000); await badge(t, 6);
    await remove(t, t.y); await quantity(t, t.x, 1); await quantity(t, t.z, 2); await total(t, 400000); await badge(t, 3);
    await load(t, true); await quantity(t, t.x, 1); await quantity(t, t.z, 2); await expect(t.page.locator('.cart-item')).toHaveCount(2); await persisted([[t.x,1],[t.z,2]]);
  });
  await run(2, 'Giữ dòng không khả dụng sau xóa dòng khác', async t => {
    for (const reason of ['stock','inactive']) {
      await clear(); stock(t.z, 10); sale(t.z,true); await add(t.x,1); await add(t.z,2);
      if(reason==='stock') stock(t.z,0); else sale(t.z,false);
      await load(t); await expect(row(t,t.z).locator('.text-danger')).toBeVisible(); await remove(t,t.x); await load(t,true);
      await quantity(t,t.z,2); await total(t,300000); await badge(t,2); await remove(t,t.z); await badge(t,0); await persisted([]);
    }
  });
  await run(3, 'Xóa dòng cuối', async t => {
    await add(t.x,2); await load(t); await badge(t,2); await remove(t,t.x); await expect(t.page.locator('.empty-cart')).toBeVisible(); await badge(t,0);
    await expect(t.page.getByRole('button',{name:'Tiến hành thanh toán'})).toHaveCount(0); await load(t,true); await badge(t,0); await persisted([]);
  });
  await run(4, 'Lỗi mạng DELETE giữ giỏ', async t => {
    await add(t.x,2); await add(t.y,1); await load(t);
    await t.page.route('**/api/cart/items/*', route => route.request().method()==='DELETE' ? route.abort('failed') : route.continue());
    t.page.once('dialog',d=>d.accept()); await row(t,t.x).locator('.remove-button').click(); await expect(t.page.locator('app-cart .alert-danger')).toBeVisible();
    await quantity(t,t.x,2); await quantity(t,t.y,1); await total(t,350000); await badge(t,3); await expect(row(t,t.x).locator('.remove-button')).toBeEnabled();
    await persisted([[t.x,2],[t.y,1]]); await t.page.unrouteAll(); await load(t,true); await remove(t,t.x); await persisted([[t.y,1]]);
  });
  await run(5, 'Hủy xóa toàn bộ', async t => {
    await add(t.x,1); await add(t.y,2); await add(t.z,3); await load(t); const count=t.result.network.filter(r=>r.method==='DELETE').length;
    t.page.once('dialog',d=>d.dismiss()); await t.page.locator('.clear-cart-btn').click(); await expect(t.page.locator('.cart-item')).toHaveCount(3); await badge(t,6); await total(t,850000);
    assert.equal(t.result.network.filter(r=>r.method==='DELETE').length,count); await load(t,true); await persisted([[t.x,1],[t.y,2],[t.z,3]]);
  });
  await run(6, 'Phân quyền sửa/xóa giỏ', async t => {
    await add(t.x,2); await add(t.y,3,customers[1]); const own=item(await cart(),t.x);
    assert.equal((await api('PUT','/api/cart/items/'+own.cartItemId,{quantity:1},customers[1].token)).status,404);
    assert.equal((await api('DELETE','/api/cart/items/'+own.cartItemId,undefined,customers[1].token)).status,404);
    await persisted([[t.x,2]]); const other=await cart(customers[1]); assert.equal(other.items.length,1); assert.equal(item(other,t.y).quantity,3); await load(t); await quantity(t,t.x,2);
  });
  for(const [n,stockCount] of [[7,2],[8,1]]) await run(n, 'Giảm tồn về '+stockCount, async t => {
    stock(t.x,10); await add(t.x,5); stock(t.x,stockCount); await load(t);
    await expect(row(t,t.x)).toContainText('Chỉ còn '+stockCount); await expect(row(t,t.x).getByRole('button',{name:'Giảm số lượng'})).toBeEnabled(); await expect(row(t,t.x).getByRole('button',{name:'Tăng số lượng'})).toBeDisabled();
    await delta(t,t.x,'-',stockCount); await total(t,stockCount*100000); await badge(t,stockCount); await expect(row(t,t.x).locator('.text-danger')).toHaveCount(0);
    if(stockCount===1) await expect(row(t,t.x).getByRole('button',{name:'Giảm số lượng'})).toBeDisabled(); await load(t,true); await quantity(t,t.x,stockCount); await persisted([[t.x,stockCount]]);
  });
  await run(9, 'Tồn kho bằng 0', async t => {
    await add(t.x,3); await add(t.y,1); stock(t.x,0); await load(t); await expect(row(t,t.x)).toContainText('Sản phẩm hiện đã hết hàng.');
    await expect(row(t,t.x).getByRole('button',{name:'Giảm số lượng'})).toBeDisabled(); await expect(row(t,t.x).getByRole('button',{name:'Tăng số lượng'})).toBeDisabled(); await remove(t,t.x); await persisted([[t.y,1]]);
  });
  await run(10, 'Tồn thay đổi sau khi tải giỏ', async t => {
    stock(t.x,10); await add(t.x,5); await load(t); stock(t.x,2);
    await row(t,t.x).getByRole('button',{name:'Giảm số lượng'}).click(); await expect(t.page.locator('app-cart .alert-danger')).toContainText('tồn kho (2)'); await expect(row(t,t.x)).toContainText('Chỉ còn 2');
    await expect(row(t,t.x).getByRole('button',{name:'Giảm số lượng'})).toBeEnabled(); await delta(t,t.x,'-',2); await badge(t,2); await persisted([[t.x,2]]);
  });
  await run(11, 'Ngừng bán sản phẩm cha', async t => {
    const variant=jsonSql(`INSERT ProductVariants(ProductId,ColorId,SizeId,SKU,StockQuantity,IsActive) SELECT ${t.x.productId},(SELECT ColorId FROM Colors WHERE ColorName=N'Be'),(SELECT SizeId FROM Sizes WHERE SizeName='L'),'UC08-${suffix}-11-extra',10,1; SELECT CAST(SCOPE_IDENTITY() AS int) AS id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;`);
    const extra={...t.x,id:variant.id}; await add(t.x,2); await add(extra,3); sale(t.x,false,true); await load(t);
    await expect(t.page.locator('.cart-item')).toHaveCount(2); for(const line of await t.page.locator('.cart-item').all()) { await expect(line).toContainText('không còn được bán'); await expect(line.getByRole('button',{name:'Giảm số lượng'})).toBeDisabled(); await expect(line.getByRole('button',{name:'Tăng số lượng'})).toBeDisabled(); }
    t.page.once('dialog',d=>d.accept()); await t.page.locator('.remove-button').first().click(); await expect(t.page.locator('.cart-item')).toHaveCount(1); assert.equal((await cart()).items.length,1);
  });
  await run(12, 'Có hàng trở lại', async t => {
    await add(t.x,5); stock(t.x,2); await load(t); await expect(row(t,t.x)).toContainText('Chỉ còn 2'); stock(t.x,10); await load(t,true);
    await quantity(t,t.x,5); await expect(row(t,t.x).locator('.text-danger')).toHaveCount(0); await delta(t,t.x,'+',6); await delta(t,t.x,'-',5); await persisted([[t.x,5]]);
  });
  for(const [n,existing,added,accepted] of [[13,99,2,false],[14,99,1,true],[15,100,1,false]]) await run(n,'Giới hạn cộng dồn '+existing+' + '+added,async t=>{
    await add(t.x,existing); await detail(t,t.x,added); await submitAdd(t,accepted); await load(t); const q=accepted?100:existing; await quantity(t,t.x,q); await badge(t,q); await total(t,q*100000); await load(t,true); await persisted([[t.x,q]]);
  });
  await run(16, 'Cộng dồn nhiều yêu cầu nhỏ', async t => {
    await detail(t,t.x,11); for(let i=0;i<9;i++) await submitAdd(t,true); await submitAdd(t,false); await load(t); await quantity(t,t.x,99); await badge(t,99); await persisted([[t.x,99]]);
  });
  await run(17, 'Giới hạn riêng từng biến thể', async t => {
    await add(t.x,100); await add(t.y,100); await load(t); await quantity(t,t.x,100); await quantity(t,t.y,100); await badge(t,200); await total(t,25000000); await persisted([[t.x,100],[t.y,100]]);
  });
  await run(18, 'Nút tăng dừng ở 100', async t => {
    await add(t.x,100); await load(t); await expect(row(t,t.x).getByRole('button',{name:'Tăng số lượng'})).toBeDisabled(); await delta(t,t.x,'-',99); await delta(t,t.x,'+',100); await expect(row(t,t.x).getByRole('button',{name:'Tăng số lượng'})).toBeDisabled(); await persisted([[t.x,100]]);
  });
  await run(19, 'Dữ liệu cũ số lượng 150', async t => {
    sql(`INSERT CartItems(UserId,VariantId,Quantity,CreatedAt) VALUES(${customers[0].id},${t.x.id},150,GETUTCDATE());`); await load(t); await quantity(t,t.x,150); await expect(row(t,t.x)).toContainText('tối đa 100'); await delta(t,t.x,'-',100); await badge(t,100); await total(t,10000000); await load(t,true); await persisted([[t.x,100]]);
  });
  await run(20, 'Hai tab cùng thêm tại 99', async t => {
    await add(t.x,99); await detail(t,t.x); const second=await t.context.newPage(); const other={...t,page:second}; await detail(other,t.x); await submitAdd(t,true); await submitAdd(other,false); await load(t); await load(other); await quantity(t,t.x,100); await quantity(other,t.x,100); await persisted([[t.x,100]]);
  });
  await run(21, 'API số lượng nguyên không hợp lệ', async t => {
    await add(t.x,2); const id=item(await cart(),t.x).cartItemId; const observations=[];
    for(const q of [0,-1,101,2147483647]) for(const method of ['POST','PUT']) {
      const r=await api(method,method==='POST'?'/api/cart/items':'/api/cart/items/'+id,method==='POST'?{variantId:t.x.id,quantity:q}:{quantity:q},customers[0].token);
      observations.push({method,quantity:q,status:r.status}); assert.equal(r.status,400); assert.ok(JSON.stringify(r.body).includes('Số lượng phải từ 1 đến 100.'));
    }
    t.result.apiChecks=observations; await load(t); await quantity(t,t.x,2); await persisted([[t.x,2]]);
  });
  await run(22, 'API số lượng sai kiểu', async t => {
    await add(t.x,2); const id=item(await cart(),t.x).cartItemId; const observations=[];
    for(const value of ['1.5','"abc"','999999999999999999999']) for(const method of ['POST','PUT']) {
      const payload=`{${method==='POST'?'"variantId":'+t.x.id+',':''}"quantity":${value}}`;
      const r=await api(method,method==='POST'?'/api/cart/items':'/api/cart/items/'+id,payload,customers[0].token,true); observations.push({method,value,status:r.status}); assert.equal(r.status,400);
    }
    t.result.apiChecks=observations; await load(t); await quantity(t,t.x,2); await persisted([[t.x,2]]);
  });
  await run(23, 'API biến thể không hợp lệ/không tồn tại', async t => {
    await add(t.x,2); for(const id of [0,-1,2147483647]) { const r=await api('POST','/api/cart/items',{variantId:id,quantity:1},customers[0].token); assert.equal(r.status,id>0?404:400); }
    await load(t); await persisted([[t.x,2]]); await quantity(t,t.x,2);
  });
  await run(24, 'Cùng sản phẩm khác biến thể độc lập', async t => {
    const variant=jsonSql(`INSERT ProductVariants(ProductId,ColorId,SizeId,SKU,StockQuantity,IsActive) SELECT ${t.x.productId},(SELECT ColorId FROM Colors WHERE ColorName=N'Be'),(SELECT SizeId FROM Sizes WHERE SizeName='L'),'UC08-${suffix}-24-extra',20,1; SELECT CAST(SCOPE_IDENTITY() AS int) AS id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;`);
    const extra={...t.x,id:variant.id}; await add(t.x,2); await add(extra,3); await load(t); const black=t.page.locator('.cart-item').filter({hasText:'Màu: Đen'}); const be=t.page.locator('.cart-item').filter({hasText:'Màu: Be'});
    await expect(black.locator('.quantity-value')).toHaveText('2'); await expect(be.locator('.quantity-value')).toHaveText('3'); await black.getByRole('button',{name:'Tăng số lượng'}).click(); await expect(black.locator('.quantity-value')).toHaveText('3'); await expect(be.locator('.quantity-value')).toHaveText('3');
    t.page.once('dialog',d=>d.accept()); await black.locator('.remove-button').click(); await expect(black).toHaveCount(0); await load(t,true); await expect(be.locator('.quantity-value')).toHaveText('3'); await persisted([[extra,3]]);
  });
  await run(25, 'Thành tiền và số trên giỏ sau tăng giảm', async t => {
    await add(t.x,2); await add(t.y,3); await load(t); await total(t,650000); await badge(t,5); await delta(t,t.x,'+',3); await total(t,750000); await badge(t,6); await delta(t,t.y,'-',2); await total(t,600000); await badge(t,5); await load(t,true); await total(t,600000); await persisted([[t.x,3],[t.y,2]]);
  });
  await run(26, 'Giá hiện hành khi tải lại giỏ', async t => {
    await add(t.x,2); await load(t); await total(t,200000); sql(`UPDATE Products SET BasePrice=120000,SalePrice=NULL WHERE ProductId=${t.x.productId};`); await load(t,true); await expect(row(t,t.x).locator('.cart-price')).toContainText('120,000'); await total(t,240000); await delta(t,t.x,'+',3); await total(t,360000); assert.equal((await cart()).subtotal,360000);
  });
  await run(27, 'Giỏ lưu sau điều hướng/F5/tab mới', async t => {
    await add(t.x,2); await add(t.y,1); await load(t); await delta(t,t.x,'+',3); await remove(t,t.y); await t.page.locator('header .main-nav a[routerlink="/products"]').click(); await t.page.locator('header a[title="Giỏ hàng"]').click(); await quantity(t,t.x,3);
    await load(t,true); await quantity(t,t.x,3); const second={...t,page:await t.context.newPage()}; await load(second); await quantity(second,t.x,3); await persisted([[t.x,3]]);
  });
  await run(28, 'Đăng xuất/đăng nhập tài khoản khác', async t => {
    await add(t.x,2); await add(t.y,3,customers[1]); await load(t); await badge(t,2);
    async function logout() { await t.page.locator('.account-trigger').click(); await t.page.locator('.logout-item').click(); await expect(t.page.locator('.account-trigger')).toHaveCount(0); }
    async function loginFixture(user) { const res=await t.context.request.post(appBase+'/api/auth/login',{data:{email:user.email,password,captchaToken:'isolated-test-fixture'}}); assert.equal(res.status(),200); const data=(await res.json()).data; await t.page.evaluate(user=>localStorage.setItem('clothing_current_user',JSON.stringify(user)),data.user); await load(t); }
    await logout(); await loginFixture(customers[1]); await quantity(t,t.y,3); await expect(row(t,t.x)).toHaveCount(0); await badge(t,3); await logout(); await loginFixture(customers[0]); await quantity(t,t.x,2); await expect(row(t,t.y)).toHaveCount(0); await badge(t,2);
    t.result.fixtureNote='Đăng xuất bằng nút giao diện thật; đăng nhập bằng API + cookie refresh thật trong cùng browser context. Không kiểm thử CAPTCHA/biểu mẫu đăng nhập.';
  });
  await run(29, 'Không phiên và token hết hạn', async t => {
    await add(t.x,2); const id=item(await cart(),t.x).cartItemId; const base64=o=>Buffer.from(JSON.stringify(o)).toString('base64url');
    const content=base64({alg:'HS256',typ:'JWT'})+'.'+base64({iss:'UC08',aud:'UC08',exp:Math.floor(Date.now()/1000)-3600,'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier':String(customers[0].id)});
    const expired=content+'.'+createHmac('sha256',jwtKey).update(content).digest('base64url');
    for(const token of [undefined,expired]) for(const [method,url,body] of [['GET','/api/cart'],['POST','/api/cart/items',{variantId:t.x.id,quantity:1}],['PUT','/api/cart/items/'+id,{quantity:1}],['DELETE','/api/cart/items/'+id],['DELETE','/api/cart']]) assert.equal((await api(method,url,body,token)).status,401);
    await load(t); await quantity(t,t.x,2); await persisted([[t.x,2]]);
  });
  await run(30, 'Lỗi GET giỏ và phục hồi', async t => {
    await add(t.x,2); await add(t.y,1); await t.page.route('**/api/cart',route=>route.request().method()==='GET'?route.abort('failed'):route.continue()); await t.page.goto(appBase+'/cart');
    await expect(t.page.locator('app-cart .alert-danger')).toContainText('Không thể tải giỏ hàng.'); await expect(t.page.locator('app-cart .spinner-border')).toHaveCount(0); await expect(t.page.locator('.empty-cart')).toHaveCount(0); await persisted([[t.x,2],[t.y,1]]);
    await t.page.unrouteAll(); await load(t,true); await quantity(t,t.x,2); await quantity(t,t.y,1); await badge(t,3);
  });
  await run(31, 'PUT lỗi mạng giữ giỏ và thử lại', async t => {
    stock(t.x,10); await add(t.x,2); await load(t); await t.page.route('**/api/cart/items/*',route=>route.request().method()==='PUT'?route.abort('failed'):route.continue());
    await row(t,t.x).getByRole('button',{name:'Tăng số lượng'}).click(); await expect(t.page.locator('app-cart .alert-danger')).toContainText('Không thể cập nhật số lượng.'); await quantity(t,t.x,2); await total(t,200000); await badge(t,2); await expect(row(t,t.x).getByRole('button',{name:'Tăng số lượng'})).toBeEnabled(); await persisted([[t.x,2]]);
    await t.page.unrouteAll(); await delta(t,t.x,'+',3); await total(t,300000); await badge(t,3); await load(t,true); await persisted([[t.x,3]]);
  });
  await run(32, 'Bấm tăng liên tiếp khi PUT Pending', async t => {
    await add(t.x,2); await load(t); let release; const gate=new Promise(r=>release=r); let intercepted=0;
    await t.page.route('**/api/cart/items/*',async route=>{if(route.request().method()==='PUT'){intercepted++;await gate;} await route.continue();});
    const plus=row(t,t.x).getByRole('button',{name:'Tăng số lượng'}); await plus.click(); await expect(plus).toBeDisabled();
    // Real pointer clicks on the disabled control, not dispatching handlers or changing app state.
    const box=await plus.boundingBox(); for(let i=0;i<5;i++) await t.page.mouse.click(box.x+box.width/2,box.y+box.height/2);
    assert.equal(intercepted,1); release(); await quantity(t,t.x,3); await expect(plus).toBeEnabled(); await t.page.unrouteAll(); await load(t,true); await persisted([[t.x,3]]);
  });
  await run(33, 'Bấm xóa lặp khi DELETE Pending', async t => {
    await add(t.x,1); await add(t.y,3); await add(t.z,2); await load(t); let release; const gate=new Promise(r=>release=r); let intercepted=0;
    await t.page.route('**/api/cart/items/*',async route=>{if(route.request().method()==='DELETE'){intercepted++;await gate;}await route.continue();});
    t.page.once('dialog',d=>d.accept()); const button=row(t,t.y).locator('.remove-button'); await button.click(); await expect(button).toBeDisabled(); const box=await button.boundingBox(); for(let i=0;i<4;i++) await t.page.mouse.click(box.x+box.width/2,box.y+box.height/2);
    assert.equal(intercepted,1);release();await expect(row(t,t.y)).toHaveCount(0);await t.page.unrouteAll();await load(t,true);await quantity(t,t.x,1);await quantity(t,t.z,2);await total(t,400000);await badge(t,3);await persisted([[t.x,1],[t.z,2]]);
  });
  await run(34, 'Dòng đã xóa ở tab khác không bị tạo lại', async t => {
    await add(t.x,2);await add(t.y,1);await load(t);const other={...t,page:await t.context.newPage()};await load(other);await remove(t,t.x);await row(other,t.x).getByRole('button',{name:'Tăng số lượng'}).click();await expect(other.page.locator('app-cart .alert-danger')).toContainText('Không tìm thấy sản phẩm trong giỏ hàng.');await load(t,true);await load(other,true);await expect(row(other,t.x)).toHaveCount(0);await quantity(other,t.y,1);await persisted([[t.y,1]]);
  });
  await run(35, 'Xóa toàn bộ lặp trên giỏ rỗng', async t => {
    await add(t.y,3,customers[1]);await clear();await clear();const actual=await persisted([]);assert.equal(actual.totalQuantity,0);assert.equal(actual.subtotal,0);const other=await cart(customers[1]);assert.equal(other.items.length,1);assert.equal(item(other,t.y).quantity,3);await load(t);await expect(t.page.locator('.empty-cart')).toBeVisible();await badge(t,0);
  });
  await run(36, 'Giỏ không thay đổi tồn kho', async t => {
    stock(t.x,10);const read=()=>jsonSql(`SELECT StockQuantity AS stock FROM ProductVariants WHERE VariantId=${t.x.id} FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`).stock;
    assert.equal(read(),10);await add(t.x,2);assert.equal(read(),10);await load(t);await delta(t,t.x,'+',3);assert.equal(read(),10);await remove(t,t.x);assert.equal(read(),10);await persisted([]);
  });
  await run(37, 'Nút tăng dừng tại tồn kho thấp hơn 100', async t => {
    stock(t.x,3);await add(t.x,2);await load(t);await delta(t,t.x,'+',3);await expect(row(t,t.x).getByRole('button',{name:'Tăng số lượng'})).toBeDisabled();await delta(t,t.x,'-',2);await delta(t,t.x,'+',3);await expect(row(t,t.x).getByRole('button',{name:'Tăng số lượng'})).toBeDisabled();await persisted([[t.x,3]]);
  });
  await run(38, 'Chuyển sang thanh toán giữ giỏ mới nhất', async t => {
    await add(t.x,2);await add(t.y,1);await load(t);await delta(t,t.x,'+',3);await t.page.getByRole('button',{name:'Tiến hành thanh toán'}).click();await expect(t.page).toHaveURL(appBase+'/checkout');await expect(t.page.locator('app-checkout')).toContainText(t.x.name+' × 3');await expect(t.page.locator('app-checkout')).toContainText(t.y.name+' × 1');await expect(t.page.locator('app-checkout')).toContainText('450,000');await t.page.goBack();await quantity(t,t.x,3);await persisted([[t.x,3],[t.y,1]]);assert.equal(jsonSql(`SELECT COUNT(*) AS n FROM Orders FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`).n,0);
  });
  await run(39, 'Ảnh lỗi và ảnh thay thế', async t => {
    sql(`UPDATE ProductVariants SET ImageUrl='/uploads/uc08-missing-${suffix}.png' WHERE VariantId=${t.x.id};`);await add(t.x,2);await add(t.y,1);await load(t);const image=row(t,t.x).locator('.cart-image img');
    await expect(image).toHaveAttribute('src','assets/images/product-placeholder.png');
    let imageLoaded=true;try{await expect.poll(()=>image.evaluate(img=>img.complete&&img.naturalWidth>0),{timeout:3000}).toBe(true);}catch{imageLoaded=false;}
    t.result.imageEvidence=await image.evaluate(img=>({src:img.getAttribute('src'),complete:img.complete,naturalWidth:img.naturalWidth}));
    const fallback=await t.context.request.get(appBase+'/assets/images/product-placeholder.png');t.result.imageEvidence.responseStatus=fallback.status();t.result.imageEvidence.contentType=fallback.headers()['content-type'];
    await t.page.screenshot({path:path.join(output,'FUNC-058-39-broken-image.png'),fullPage:true});
    await delta(t,t.x,'+',3);await remove(t,t.x);await persisted([[t.y,1]]);await badge(t,1);t.result.otherControlsPassed=true;
    assert.ok(imageLoaded,'DEF-UC08-039: Ảnh thay thế không tải được (naturalWidth=0); /assets/images/product-placeholder.png trả '+fallback.status()+' '+fallback.headers()['content-type']+'. Tăng số lượng và xóa đúng dòng vẫn hoạt động.');
  });
  await run(40, 'Giỏ rỗng và quay lại mua hàng', async t => {
    await load(t);await expect(t.page.locator('.empty-cart')).toContainText('Giỏ hàng của bạn đang trống');await badge(t,0);await expect(t.page.locator('.clear-cart-btn')).toHaveCount(0);await expect(t.page.getByRole('button',{name:'Tiến hành thanh toán'})).toHaveCount(0);
    await t.page.getByRole('link',{name:'Khám phá sản phẩm'}).click();await expect(t.page).toHaveURL(appBase+'/products');await detail(t,t.x);await submitAdd(t);await load(t);await quantity(t,t.x,1);await badge(t,1);await total(t,100000);await expect(t.page.locator('.empty-cart')).toHaveCount(0);await persisted([[t.x,1]]);
  });
}
async function main() {
  const source=JSON.parse(docker(['inspect','clothing-api']))[0];
  const sourceWeb=JSON.parse(docker(['inspect','clothing-frontend']))[0];
  const sourceEnv=Object.fromEntries(source.Config.Env.map(e=>{const split=e.indexOf('=');return [e.slice(0,split),e.slice(split+1)];}));
  const connection=sourceEnv.ConnectionStrings__DefaultConnection;
  if(!connection || !/(Database|Initial Catalog)\s*=/i.test(connection)) throw new Error('Cannot isolate database');
  const network=Object.keys(source.NetworkSettings.Networks)[0];
  jwtKey=randomBytes(48).toString('hex');
  const env={ASPNETCORE_ENVIRONMENT:'Docker',ASPNETCORE_URLS:'http://+:8080',
    ConnectionStrings__DefaultConnection:connection.replace(/(Database|Initial Catalog)\s*=\s*[^;]+/i,'Database='+database),
    JwtSettings__SecretKey:jwtKey,JwtSettings__Issuer:'UC08',JwtSettings__Audience:'UC08',JwtSettings__ExpirationMinutes:'60',
    TwoFactorSettings__RecoveryCodePepper:randomBytes(48).toString('hex'),TurnstileSettings__Enabled:'false',
    RefreshTokenSettings__CookieSecure:'false',AdminSeedSettings__Email:'',AdminSeedSettings__Password:''};
  report.apiImage=source.Image;report.frontendImage=sourceWeb.Image;
  docker(['run','-d','--name',apiContainer,'--network',network,'-p','127.0.0.1:18082:8080',...Object.keys(env).flatMap(k=>['--env',k]),source.Image],{env:{...process.env,...env}});
  const config=`server { listen 80; root /usr/share/nginx/html; index index.html; location /api/ { proxy_pass http://${apiContainer}:8080; proxy_set_header Host $host; } location /uploads/ { proxy_pass http://${apiContainer}:8080; } location / { try_files $uri $uri/ /index.html; } }`;
  const configPath=path.join(output,'nginx.conf');fs.writeFileSync(configPath,config);
  docker(['run','-d','--name',webContainer,'--network',network,'-p','127.0.0.1:14202:80','--mount',`type=bind,source=${configPath},target=/etc/nginx/conf.d/default.conf,readonly`,sourceWeb.Image]);
  let ready=false;for(let i=0;i<90;i++){try{if((await api('GET','/api/products')).status===200){ready=true;break;}}catch{}await new Promise(r=>setTimeout(r,500));}if(!ready)throw new Error('Test API not ready');
  sql("INSERT Categories(CategoryName,Slug,IsActive,CreatedAt) VALUES(N'UC08','uc08',1,GETUTCDATE()); INSERT Colors(ColorName,ColorCode) VALUES(N'Đen','#000000'),(N'Be','#F5F5DC'); INSERT Sizes(SizeName,DisplayOrder) VALUES('M',1),('L',2);");
  customers=[];for(let i=0;i<2;i++){const email=`uc08.${i}@test.local`;const auth=ok(await api('POST','/api/auth/register',{email,password,confirmPassword:password,fullName:i?'Khách kiểm thử B':'Khách kiểm thử A',phoneNumber:'090000000'+i,captchaToken:'isolated-test-fixture'}),201);customers.push({email,id:auth.user.userId,token:auth.accessToken});}
  browser=await chromium.launch({channel:'msedge',headless:true});
  console.log('Running 40 browser/API cases against '+database+' with exact deployed images.');save();await tests();
}
main().catch(e=>{report.infrastructureError=String(e.message).slice(0,2000);process.exitCode=2;}).finally(async()=>{
  if(browser)try{await browser.close();}catch{}
  try{fs.writeFileSync(path.join(output,'api.log'),docker(['logs',apiContainer]));}catch{}
  report.cleanup={};
  for(const name of [webContainer,apiContainer]){try{docker(['rm','-f',name]);report.cleanup[name]='removed';}catch{report.cleanup[name]='not present or failed';}}
  try{
    if(!/^UC08_\d{14}_[a-f0-9]{6}$/.test(database))throw new Error('Unsafe database name');
    sql(`IF DB_ID(N'${database}') IS NOT NULL BEGIN ALTER DATABASE [${database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${database}]; END; SELECT CASE WHEN DB_ID(N'${database}') IS NULL THEN 1 ELSE 0 END AS removed FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`,'master');
    report.cleanup.databaseRemoved=true;
  }catch(e){report.cleanup.databaseRemoved=false;report.cleanup.error=e.message.slice(0,500);process.exitCode=2;}
  report.completedAt=new Date().toISOString();report.summary={passed:report.cases.filter(c=>c.status==='Passed').length,failed:report.cases.filter(c=>c.status==='Failed').length,total:report.cases.length};save();console.log(JSON.stringify({summary:report.summary,cleanup:report.cleanup,report:path.join(output,'result.json')}));if(report.infrastructureError)console.error(report.infrastructureError);
});
