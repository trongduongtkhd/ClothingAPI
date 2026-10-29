// Runs a separately built project image against an isolated SQL Server database.
// Requires the existing clothing-api and clothing-db Docker containers.
// Build: see tests/manual/README.md (local dotnet publish + runtime-only Docker build).
// Run: node tests/manual/pt009-concurrent-last-item.cjs
const { execFileSync } = require('child_process');
const { randomBytes } = require('crypto');
const { performance } = require('perf_hooks');
const http = require('http');
const fs = require('fs');
const path = require('path');

const suffix = new Date().toISOString().replace(/\D/g, '').slice(0, 14) + '_' + randomBytes(3).toString('hex');
const database = 'PT009_' + suffix;
const container = 'pt009-api-' + suffix.toLowerCase();
const base = 'http://127.0.0.1:18081';
const output = path.join(__dirname, 'results', database);
fs.mkdirSync(output, { recursive: true });
const report = { testcase: 'PT009 / P8', database, container, startedAt: new Date().toISOString(), rounds: [] };

function docker(args, options = {}) {
  try {
    return execFileSync('docker', args, { encoding: 'utf8', stdio: ['pipe', 'pipe', 'pipe'], ...options }).trim();
  } catch (error) {
    throw new Error('Docker ' + args[0] + ' failed: ' + String(error.stderr || '').slice(0, 2000));
  }
}

function sql(query, db = database) {
  // Credentials stay inside the existing database container, never in output/arguments.
  return docker(['exec', '-i', 'clothing-db', 'sh', '-c',
    'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d "$1" -y 0 -w 65535',
    'pt009', db], { input: 'SET NOCOUNT ON;\n' + query + '\nGO\n' });
}

function jsonSql(query) {
  return JSON.parse(sql(query));
}

async function api(method, endpoint, body, token) {
  return new Promise((resolve, reject) => {
    const payload = body === undefined ? undefined : JSON.stringify(body);
    const request = http.request(base + endpoint, {
      method,
      headers: { 'Content-Type': 'application/json',
        ...(payload ? { 'Content-Length': Buffer.byteLength(payload) } : {}),
        ...(token ? { Authorization: 'Bearer ' + token } : {}) },
    }, response => {
      const chunks = [];
      response.on('data', chunk => chunks.push(chunk));
      response.on('end', () => {
        try { resolve({ status: response.statusCode, body: JSON.parse(Buffer.concat(chunks).toString('utf8')) }); }
        catch (error) { reject(error); }
      });
    });
    request.setTimeout(30000, () => request.destroy(new Error('HTTP timeout')));
    request.on('error', reject);
    request.end(payload);
  });
}

function expect(result, status, step) {
  if (result.status !== status) throw new Error(step + ': ' + JSON.stringify(result));
  return result.body.data;
}

async function main() {
  const source = JSON.parse(docker(['inspect', 'clothing-api']))[0];
  const sourceEnv = Object.fromEntries(source.Config.Env.map(entry => {
    const split = entry.indexOf('=');
    return [entry.slice(0, split), entry.slice(split + 1)];
  }));
  const connection = sourceEnv.ConnectionStrings__DefaultConnection;
  if (!connection || !/(Database|Initial Catalog)\s*=/i.test(connection)) throw new Error('Cannot isolate database connection');
  const network = Object.keys(source.NetworkSettings.Networks)[0];
  const env = {
    ASPNETCORE_ENVIRONMENT: 'Docker',
    ASPNETCORE_URLS: 'http://+:8080',
    ConnectionStrings__DefaultConnection: connection.replace(/(Database|Initial Catalog)\s*=\s*[^;]+/i, 'Database=' + database),
    JwtSettings__SecretKey: randomBytes(48).toString('hex'),
    JwtSettings__Issuer: 'PT009',
    JwtSettings__Audience: 'PT009',
    JwtSettings__ExpirationMinutes: '30',
    TwoFactorSettings__RecoveryCodePepper: randomBytes(48).toString('hex'),
    TurnstileSettings__Enabled: 'false',
    RefreshTokenSettings__CookieSecure: 'false',
    AdminSeedSettings__Email: '',
    AdminSeedSettings__Password: '',
  };
  const testImage = JSON.parse(docker(['image', 'inspect', 'clothing-api-pt009:local']))[0];
  report.image = testImage.Id;
  report.mainApiImage = source.Image;
  report.network = network;
  report.method = 'Two independent customer sessions; simultaneous HTTP POST requests; persisted SQL Server verification; three rounds with distinct stock=1 variants.';
  docker(['run', '-d', '--name', container, '--network', network, '-p', '127.0.0.1:18081:8080',
    ...Object.keys(env).flatMap(key => ['--env', key]), testImage.Id], { env: { ...process.env, ...env } });
  console.log('Started isolated test API and database ' + database);

  let ready = false;
  for (let i = 0; i < 90; i++) {
    try {
      const response = await api('GET', '/api/products');
      if (response.status === 200) { ready = true; break; }
    } catch {}
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  if (!ready) throw new Error('Isolated API did not become ready');

  sql(`INSERT INTO Categories (CategoryName, Slug, IsActive, CreatedAt) VALUES (N'PT009', 'pt009', 1, GETUTCDATE());
INSERT INTO Colors (ColorName, ColorCode) VALUES (N'Be', '#F5F5DC');
INSERT INTO Sizes (SizeName, DisplayOrder) VALUES ('M', 1);`);
  const password = 'PT009@' + randomBytes(12).toString('hex');
  const customers = [];
  for (let i = 1; i <= 2; i++) {
    const email = 'pt009.customer' + i + '@test.local';
    expect(await api('POST', '/api/auth/register', {
      email, password, confirmPassword: password, fullName: i === 1 ? 'Khách thứ nhất' : 'Khách thứ hai',
      phoneNumber: '090000000' + i, captchaToken: 'pt009-isolated-test',
    }), 201, 'Register customer ' + i);
    const auth = expect(await api('POST', '/api/auth/login', {
      email, password, captchaToken: 'pt009-isolated-test',
    }), 200, 'Login customer ' + i);
    const address = expect(await api('POST', '/api/addresses', {
      receiverName: 'PT009 Customer ' + i, receiverPhone: '090000000' + i,
      province: 'TP. Ho Chi Minh', district: 'Quan 1', ward: 'Phuong 1',
      addressDetail: 'PT009 isolated test', isDefault: true,
    }, auth.accessToken), 201, 'Create address ' + i);
    customers.push({ email, token: auth.accessToken, addressId: address.addressId });
  }

  for (let round = 1; round <= 3; round++) {
    const variant = jsonSql(`
INSERT INTO Products (CategoryId, ProductName, Slug, Description, Gender, BasePrice, IsFeatured, IsActive, CreatedAt)
VALUES (1, N'Áo Polo PT009 round ${round}', 'pt009-round-${round}', N'Isolated PT009 concurrency test', 'Unisex', 200000, 0, 1, GETUTCDATE());
DECLARE @productId int = SCOPE_IDENTITY();
INSERT INTO ProductVariants (ProductId, ColorId, SizeId, SKU, StockQuantity, IsActive)
VALUES (@productId, 1, 1, 'PT009-ROUND-${round}', 1, 1);
SELECT CAST(SCOPE_IDENTITY() AS int) AS variantId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;`);
    for (const customer of customers) {
      expect(await api('DELETE', '/api/cart', undefined, customer.token), 200, 'Clear cart');
      expect(await api('POST', '/api/cart/items', { variantId: variant.variantId, quantity: 1 }, customer.token), 201, 'Add last item');
    }
    const before = jsonSql(`SELECT VariantId, StockQuantity FROM ProductVariants WHERE VariantId=${variant.variantId} FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;`);
    const note = 'PT009 round ' + round;
    let release;
    const gate = new Promise(resolve => { release = resolve; });
    const tasks = customers.map(async customer => {
      await gate;
      const sentAtMs = performance.now();
      const result = await api('POST', '/api/orders', {
        addressId: customer.addressId, paymentMethod: 'COD', couponCode: null, note,
      }, customer.token);
      return { customer: customer.email, sentAtMs, durationMs: performance.now() - sentAtMs, ...result };
    });
    release();
    const responses = await Promise.all(tasks);
    const verification = jsonSql(`SELECT
(SELECT StockQuantity FROM ProductVariants WHERE VariantId=${variant.variantId}) AS stock,
(SELECT COUNT(DISTINCT o.OrderId) FROM Orders o JOIN OrderItems i ON i.OrderId=o.OrderId WHERE i.VariantId=${variant.variantId}) AS orderCount,
(SELECT COALESCE(SUM(Quantity),0) FROM OrderItems WHERE VariantId=${variant.variantId}) AS soldQuantity,
JSON_QUERY((SELECT o.OrderId,o.OrderCode,o.UserId,o.Note,i.Quantity FROM Orders o JOIN OrderItems i ON i.OrderId=o.OrderId WHERE i.VariantId=${variant.variantId} FOR JSON PATH)) AS orders
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;`);
    const successCount = responses.filter(x => x.status === 201 && x.body.success === true).length;
    const insufficientStockCount = responses.filter(x => [400, 409].includes(x.status) && /không đủ tồn kho/i.test(x.body.message || '')).length;
    const passed = before.StockQuantity === 1 && successCount === 1 && insufficientStockCount === 1 && verification.stock === 0 && verification.orderCount === 1 && verification.soldQuantity === 1;
    const result = { round, variantId: variant.variantId, before, requestStartGapMs: Math.abs(responses[0].sentAtMs - responses[1].sentAtMs), responses, verification, passed };
    report.rounds.push(result);
    fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify({ round, statuses: responses.map(x => x.status), successCount, insufficientStockCount, ...verification, passed }));
  }
  report.passed = report.rounds.every(round => round.passed);
  if (!report.passed) process.exitCode = 1;
}

main().catch(error => {
  // Do not serialize child_process errors: they can include environment values.
  report.error = error.message.startsWith('Command failed:') ? 'Docker/SQL command failed; see sanitized API log.' : error.message;
  process.exitCode = 2;
}).finally(() => {
  try { fs.writeFileSync(path.join(output, 'api.log'), docker(['logs', container])); } catch {}
  try { docker(['rm', '-f', container]); report.testContainerRemoved = true; } catch { report.testContainerRemoved = false; }
  // Keep only the isolated test database as inspectable SQL evidence.
  report.completedAt = new Date().toISOString();
  fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify(report, null, 2));
  console.log('Report: ' + path.join(output, 'result.json'));
  if (report.error) console.error(report.error);
});
