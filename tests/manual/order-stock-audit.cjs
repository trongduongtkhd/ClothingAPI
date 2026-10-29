// Audit real HTTP concurrency against the current build and a disposable SQL Server.
// First: dotnet publish ClothingAPI.csproj -c Release --no-restore -o tests/manual/.publish /p:UseAppHost=false
// Then: node tests/manual/order-stock-audit.cjs
// L01 regression + checkout guards only: node tests/manual/order-stock-audit.cjs --l01-only
// No production containers, credentials, volumes or databases are used.
const { execFileSync } = require('child_process');
const { randomBytes, randomUUID } = require('crypto');
const { performance } = require('perf_hooks');
const http = require('http');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '../..');
const run = 'stockaudit-' + new Date().toISOString().replace(/\D/g, '').slice(0, 14) + '-' + randomBytes(3).toString('hex');
const dbContainer = run + '-db', apiContainer = run + '-api', secondaryContainer = run + '-api2', image = run + ':local';
const database = 'StockAudit';
const ephemeral = process.argv.includes('--ephemeral');
const output = path.join(__dirname, 'results', run);
if(!ephemeral) fs.mkdirSync(output, { recursive: true });
const report = { run, startedAt: new Date().toISOString(), method: 'Unmodified current source; independent HTTP requests released together; SQL Server 2022 persisted-state assertions; disposable containers without volumes.', cases: [] };
const password = 'Audit@' + randomBytes(24).toString('hex');
let base, serial = 0, admin, appEnv;
const customers = [];
function docker(args, options = {}) {
  try { return execFileSync('docker', args, { cwd: root, encoding: 'utf8', stdio: ['pipe','pipe','pipe'], timeout: 180000, maxBuffer: 32 * 1024 * 1024, ...options }).trim(); }
  catch (e) { throw new Error('Docker ' + args[0] + ' failed: ' + String(e.stderr || e.stdout || e.message || '').split(password).join('[redacted]').slice(0, 1500)); }
}
function sql(query, db = database) {
  return docker(['exec','-i',dbContainer,'sh','-c',
    'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d "$1" -y 0 -w 65535',
    'audit',db], { input: 'SET NOCOUNT ON;\n' + query + '\nGO\n' });
}
const jsonSql = query => JSON.parse(sql(query));
const save = () => { if(!ephemeral) fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify(report, null, 2)); };
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
function api(method, endpoint, body, customer, apiBase=base) {
  if(method==='POST'&&endpoint==='/api/orders'&&body&&body.requestId===undefined) body={requestId:randomUUID(),...body};
  const sent = performance.now();
  return new Promise((resolve, reject) => {
    const payload = body === undefined ? undefined : JSON.stringify(body);
    const request = http.request(apiBase + endpoint, { method, headers: {
      'Content-Type':'application/json', ...(payload ? {'Content-Length':Buffer.byteLength(payload)} : {}),
      ...(customer ? {Authorization:'Bearer ' + customer.token} : {})
    }}, response => {
      const chunks = [];
      response.on('data', c => chunks.push(c));
      response.on('end', () => {
        const raw = Buffer.concat(chunks).toString('utf8');
        let parsed; try { parsed = JSON.parse(raw); } catch { parsed = {message: raw.slice(0, 300)}; }
        // Persist only relevant evidence; never authentication tokens or full customer records.
        resolve({status:response.statusCode, durationMs: Math.round((performance.now()-sent)*100)/100,
          message:parsed.message, success:parsed.success, data:parsed.data, errors:parsed.errors});
      });
    });
    request.setTimeout(35000, () => request.destroy(new Error('HTTP timeout')));
    request.on('error', reject); request.end(payload);
  });
}
function expect(response, status, step) {
  if (response.status !== status || response.success === false) throw new Error(step + ': HTTP ' + response.status + ' ' + (response.message || JSON.stringify(response.errors)));
  return response.data;
}
const ok = r => [200,201].includes(r.status) && r.success === true;
const stockRejected = r => [400,409].includes(r.status) && /không đủ tồn kho|hết hàng/i.test(r.message || '');
async function parallel(tasks) {
  let release;
  const gate = new Promise(resolve => release = resolve);
  const pending = tasks.map(async task => { await gate; const sentAtMs = performance.now(); const response = await task(); return {...response, sentAtMs}; });
  release(); return Promise.all(pending);
}
function compact(responses) { return responses.map(({data,...r}) => ({...r, orderId:data?.orderId, purchaseOrderId:data?.purchaseOrderId})); }
function record(name, responses, observed, expected, passed) {
  const times = responses.map(r => r.sentAtMs).filter(x => x !== undefined);
  const item = { name, passed, requestStartGapMs:times.length ? Math.max(...times)-Math.min(...times) : 0,
    responses:compact(responses), observed, expected };
  report.cases.push(item); save();
  console.log(JSON.stringify({name, passed, statuses:responses.map(r => r.status), observed}));
}
function variant(stock, productId) {
  const id = ++serial;
  return jsonSql(`${productId ? `DECLARE @p int=${productId};` : `INSERT INTO Products(CategoryId,ProductName,Slug,Description,Gender,BasePrice,IsFeatured,IsActive,CreatedAt) VALUES(1,N'Audit ${id}','audit-${id}',N'Isolated audit','Unisex',200000,0,1,GETUTCDATE()); DECLARE @p int=SCOPE_IDENTITY();`}
    INSERT INTO ProductVariants(ProductId,ColorId,SizeId,SKU,StockQuantity,IsActive,AverageCostPrice) VALUES(@p,1,${productId ? 2 : 1},'AUDIT-${id}',${stock},1,100000);
    SELECT CAST(SCOPE_IDENTITY() AS int) AS variantId,@p AS productId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`);
}
function state(v) {
  return jsonSql(`SELECT (SELECT StockQuantity FROM ProductVariants WHERE VariantId=${v.variantId}) stock,
    (SELECT AverageCostPrice FROM ProductVariants WHERE VariantId=${v.variantId}) averageCost,
    (SELECT COUNT(*) FROM OrderItems WHERE VariantId=${v.variantId}) orderCount,
    (SELECT COALESCE(SUM(i.Quantity),0) FROM OrderItems i JOIN Orders o ON o.OrderId=i.OrderId WHERE i.VariantId=${v.variantId} AND o.OrderStatus NOT IN ('Cancelled','Returned')) sold,
    JSON_QUERY((SELECT o.OrderId,o.UserId,o.OrderStatus,o.PaymentStatus,i.Quantity,i.UnitCost FROM Orders o JOIN OrderItems i ON i.OrderId=o.OrderId WHERE i.VariantId=${v.variantId} FOR JSON PATH)) orders
    FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`);
}
const orderBody = (c,v,q=1, extra={}) => ({requestId:randomUUID(),addressId:c.addressId,paymentMethod:'COD',buyNowItem:{variantId:v.variantId,quantity:q},...extra});
const order = (c,v,q=1,extra={}) => api('POST','/api/orders',orderBody(c,v,q,extra),c);
async function clear(c) { expect(await api('DELETE','/api/cart',undefined,c),200,'clear'); }
async function add(c,v,q=1) { return api('POST','/api/cart/items',{variantId:v.variantId,quantity:q},c); }
const purchase = (v,q,unitCost=100000) => api('POST','/api/admin/purchase-orders',{supplierId:1,items:[{variantId:v.variantId,quantity:q,unitCost}]},admin);
async function checkoutScenario(name,stock,count,q,mode) {
  const v = variant(stock), cs = customers.slice(0,count);
  if(mode==='cart') for(const c of cs) { await clear(c); expect(await add(c,v,q),201,'add cart'); }
  const responses = await parallel(cs.map(c => () => mode==='cart'
    ? api('POST','/api/orders',{addressId:c.addressId,paymentMethod:'COD'},c) : order(c,v,q)));
  const observed = state(v), accepted = responses.filter(ok).length, rejected = responses.filter(stockRejected).length;
  const wanted = Math.min(count,Math.floor(stock/q));
  record(name,responses,{...observed,accepted,rejected},{accepted:wanted,stock:stock-wanted*q,sold:wanted*q},
    accepted===wanted && rejected===count-wanted && observed.orderCount===wanted && observed.stock===stock-wanted*q && observed.sold===wanted*q);
}
async function setup() {
  report.source = {orderServiceSha256:require('crypto').createHash('sha256').update(fs.readFileSync(path.join(root,'Services/Implementations/OrderService.cs'))).digest('hex')};
  console.log('Building current published source for isolated test');
  docker(['build','-f','tests/manual/Dockerfile','-t',image,'.']);
  docker(['network','create',run]);
  const dbEnv={ACCEPT_EULA:'Y',MSSQL_SA_PASSWORD:password,MSSQL_PID:'Developer'};
  docker(['run','-d','--name',dbContainer,'--network',run,'--network-alias','audit-db',
    ...Object.keys(dbEnv).flatMap(k=>['--env',k]),'mcr.microsoft.com/mssql/server:2022-latest'],{env:{...process.env,...dbEnv}});
  let ready=false;
  for(let i=0;i<120;i++){ try {sql('SELECT 1','master');ready=true;break;}catch{} await delay(500); }
  if(!ready) throw new Error('SQL Server startup timeout');
  const env=appEnv={ASPNETCORE_ENVIRONMENT:'Docker',ASPNETCORE_URLS:'http://+:8080',
    ConnectionStrings__DefaultConnection:`Server=audit-db;Database=${database};User Id=sa;Password=${password};TrustServerCertificate=True;`,
    JwtSettings__SecretKey:randomBytes(48).toString('hex'),JwtSettings__Issuer:'StockAudit',JwtSettings__Audience:'StockAudit',JwtSettings__ExpirationMinutes:'60',
    TwoFactorSettings__RecoveryCodePepper:randomBytes(48).toString('hex'),TurnstileSettings__Enabled:'false',RefreshTokenSettings__CookieSecure:'false',
    AdminSeedSettings__Email:'admin.audit@test.local',AdminSeedSettings__Password:password,VnpaySettings__Enabled:'false',
    'Logging__LogLevel__Microsoft.EntityFrameworkCore':'Warning','Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore':'Warning'};
  docker(['run','-d','--name',apiContainer,'--network',run,'-p','127.0.0.1::8080',
    ...Object.keys(env).flatMap(k=>['--env',k]),image],{env:{...process.env,...env}});
  const port=docker(['port',apiContainer,'8080/tcp']).split(':').pop(); base='http://127.0.0.1:'+port;
  ready=false;
  for(let i=0;i<120;i++){try{if((await api('GET','/api/products')).status===200){ready=true;break;}}catch{}await delay(500);}
  if(!ready) throw new Error('API startup timeout');
  report.server=jsonSql("SELECT @@VERSION version,(SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name=DB_NAME()) readCommittedSnapshot FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;");
  sql("INSERT INTO Categories(CategoryName,Slug,IsActive,CreatedAt) VALUES(N'Audit','audit',1,GETUTCDATE()); INSERT INTO Colors(ColorName,ColorCode) VALUES(N'Audit','#123456'); INSERT INTO Sizes(SizeName,DisplayOrder) VALUES('M',1),('L',2); INSERT INTO Suppliers(SupplierName,IsActive,CreatedAt) VALUES(N'Audit supplier',1,GETUTCDATE());");
  const login=async email=>expect(await api('POST','/api/auth/login',{email,password,captchaToken:'audit'},undefined),200,'login');
  const auth=await login('admin.audit@test.local'); admin={token:auth.accessToken,userId:auth.user?.userId};
  for(let i=0;i<5;i++) {
    const email=`customer${i}@audit.test`;
    expect(await api('POST','/api/auth/register',{email,password,confirmPassword:password,fullName:'Khách kiểm thử',phoneNumber:'090000000'+i,captchaToken:'audit'}),201,'register');
    const a=await login(email), c={token:a.accessToken};
    const address=expect(await api('POST','/api/addresses',{receiverName:'Khách kiểm thử',receiverPhone:'090000000'+i,addressDetail:'Số 1 đường Kiểm Thử',ward:'Phường 1',district:'Quận 1',province:'TP Hồ Chí Minh',isDefault:true},c),201,'address');
    c.addressId=address.addressId;
    c.userId=jsonSql(`SELECT UserId userId FROM Users WHERE Email='${email}' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`).userId;
    customers.push(c);
  }
}
async function main() {
  await setup();
  // Warm up the full create/cancel paths before collecting measurements.
  const warm=variant(2), w=expect(await order(customers[0],warm),201,'warmup');
  expect(await api('PUT',`/api/orders/${w.orderId}/cancel`,{reason:'Audit warmup'},customers[0]),200,'warmup cancel');
  if(process.argv.includes('--l02-only')) { await restoration(); return; }
  if(process.argv.includes('--l03-only')) { await warehouse(); return; }
  if(process.argv.includes('--remaining-only')) { await remaining(); return; }
  if(process.argv.includes('--extended-only')) { await extended(); return; }
  for(let round=1;round<=3;round++) for(const mode of ['cart','buyNow']) {
    for(const [stock,count,q] of [[10,2,1],[10,2,5],[10,2,6],[2,5,1],[1,2,1]])
      await checkoutScenario(`${mode}:stock=${stock},customers=${count},qty=${q}:r${round}`,stock,count,q,mode);
  }
  if(!process.argv.includes('--l01-only')) {
  for(let round=1;round<=3;round++) {
    const c=customers[0], v=variant(10); await clear(c); expect(await add(c,v,1),201,'same cart');
    const r=await parallel([1,2].map(()=>()=>api('POST','/api/orders',{addressId:c.addressId,paymentMethod:'COD'},c)));
    const s=state(v);record(`duplicate-cart:r${round}`,r,s,{orderCount:1,stock:9},s.orderCount===1&&s.stock===9&&r.filter(ok).length===1&&r.every(x=>x.status<500));
    const v2=variant(10); const body=orderBody(c,v2);
    const r2=await parallel([1,2].map(()=>()=>api('POST','/api/orders',body,c)));
    const s2=state(v2);record(`identical-buyNow-retry:r${round}`,r2,s2,{orderCount:1,stock:9,policy:'Identical retry should be idempotent if it represents one customer action.'},s2.orderCount===1&&s2.stock===9);
    const v3=variant(10);const first=expect(await order(c,v3,2),201,'cancel seed');const second=expect(await order(customers[1],v3,3),201,'cancel seed2');
    const r3=await parallel([()=>api('PUT',`/api/orders/${first.orderId}/cancel`,{reason:'Audit'},c),()=>api('PUT',`/api/orders/${second.orderId}/cancel`,{reason:'Audit'},customers[1])]);
    const s3=state(v3);record(`cancel-two-orders-one-variant:r${round}`,r3,s3,{stock:10,sold:0},r3.every(ok)&&s3.stock===10&&s3.sold===0);
    const v4=variant(10);const one=expect(await order(c,v4,2),201,'same cancel seed');
    const r4=await parallel([1,2].map(()=>()=>api('PUT',`/api/orders/${one.orderId}/cancel`,{reason:'Audit'},c)));
    const s4=state(v4);record(`cancel-same-order:r${round}`,r4,s4,{stock:10,accepted:1},s4.stock===10&&r4.filter(ok).length===1&&r4.every(x=>x.status<500));
    const v5=variant(10); const r5=await parallel([()=>purchase(v5,3),()=>purchase(v5,4)]);const s5=state(v5);
    record(`concurrent-stock-in:r${round}`,r5,s5,{stock:17},r5.every(ok)&&s5.stock===17);
    const v6=variant(10); const r6=await parallel([()=>purchase(v6,5),()=>order(c,v6,2)]);const s6=state(v6);
    record(`stock-in-vs-sale:r${round}`,r6,s6,{stock:13,sold:2},r6.every(ok)&&s6.stock===13&&s6.sold===2);
    const v7=variant(10); const old=expect(await order(c,v7,2),201,'sale cancel seed');
    const r7=await parallel([()=>api('PUT',`/api/orders/${old.orderId}/cancel`,{reason:'Audit'},c),()=>order(customers[1],v7,3)]);const s7=state(v7);
    record(`cancel-vs-sale:r${round}`,r7,s7,{stock:7,sold:3},r7.every(ok)&&s7.stock===7&&s7.sold===3);
    const code='AUDITLIMIT'+round;
    sql(`INSERT INTO Coupons(Code,Name,DiscountType,DiscountValue,MinOrderAmount,StartDate,EndDate,UsageLimit,UsedCount,IsActive,CreatedByUserId,CreatedAt) SELECT '${code}',N'Audit','FixedAmount',10000,0,DATEADD(day,-1,GETUTCDATE()),DATEADD(day,1,GETUTCDATE()),1,0,1,UserId,GETUTCDATE() FROM Users WHERE Email='admin.audit@test.local';`);
    const vs=[variant(10),variant(10)];const r8=await parallel(vs.map((v,i)=>()=>order(customers[i],v,1,{couponCode:code})));
    const s8=jsonSql(`SELECT UsedCount usedCount,(SELECT COUNT(*) FROM CouponUsages u WHERE u.CouponId=c.CouponId) actualUsages FROM Coupons c WHERE Code='${code}' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`);
    record(`coupon-global-limit:r${round}`,r8,s8,{usedCount:1,actualUsages:1},r8.filter(ok).length===1&&r8.every(x=>x.status<500)&&s8.usedCount===1&&s8.actualUsages===1);
    const v9=variant(10);await clear(c); const r9=await parallel([1,2].map(()=>()=>add(c,v9,1)));
    const s9=jsonSql(`SELECT COUNT(*) [rows],COALESCE(SUM(Quantity),0) quantity FROM CartItems WHERE UserId=${c.userId} AND VariantId=${v9.variantId} FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`);
    record(`concurrent-cart-add-new:r${round}`,r9,s9,{quantity:2,rows:1},r9.every(ok)&&s9.rows===1&&s9.quantity===2);
    const r10=await parallel([1,2].map(()=>()=>add(c,v9,1)));
    const s10=jsonSql(`SELECT COALESCE(SUM(Quantity),0) quantity FROM CartItems WHERE UserId=${c.userId} AND VariantId=${v9.variantId} FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`);
    record(`concurrent-cart-add-existing:r${round}`,r10,s10,{quantity:s9.quantity+2},r10.every(ok)&&s10.quantity===s9.quantity+2);
    const v10=variant(10);const qr=expect(await order(c,v10,2,{paymentMethod:'MockQR'}),201,'QR seed');
    const r11=await parallel([()=>api('PUT',`/api/orders/${qr.orderId}/cancel`,{reason:'Audit'},c),()=>api('POST',`/api/orders/${qr.orderId}/mock-qr/confirm`,{},c)]);
    const s11=state(v10), row=s11.orders[0];
    const valid=row.OrderStatus==='Cancelled' ? row.PaymentStatus!=='Paid'&&s11.stock===10 : row.PaymentStatus==='Paid'&&s11.stock===8;
    record(`mock-payment-vs-cancel:r${round}`,r11,s11,{consistent:true,accepted:1},valid&&r11.filter(ok).length===1&&r11.every(x=>x.status<500));
  }
  }
  const c=customers[0];
  for(const quantity of [-1,0,101]) {
    const v=variant(200);const r=await order(c,v,quantity);const s=state(v);
    record(`invalid-buyNow-quantity:${quantity}`,[r],s,{status:400,stock:200,orderCount:0},r.status===400&&s.stock===200&&s.orderCount===0);
  }
  const a=variant(10),b=variant(2,a.productId);
  const r=await parallel([()=>order(c,a,3),()=>order(customers[1],b,2)]);const states=[state(a),state(b)];
  record('different-variants-same-product',r,states,{stocks:[7,0]},r.every(ok)&&states[0].stock===7&&states[1].stock===0);
  await clear(c);const good=variant(10),bad=variant(2);expect(await add(c,good,1),201,'rollback good');expect(await add(c,bad,2),201,'rollback bad');
  sql(`UPDATE ProductVariants SET StockQuantity=1 WHERE VariantId=${bad.variantId};`);
  const failed=await api('POST','/api/orders',{addressId:c.addressId,paymentMethod:'COD'},c);
  const rollback={good:state(good),bad:state(bad),cart:jsonSql(`SELECT COUNT(*) items,SUM(Quantity) quantity FROM CartItems WHERE UserId=${c.userId} FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`)};
  record('multi-item-shortage-rolls-back',[failed],rollback,{goodStock:10,badStock:1,cartQuantity:3},stockRejected(failed)&&rollback.good.stock===10&&rollback.bad.stock===1&&rollback.good.orderCount===0&&rollback.cart.quantity===3);
  const noStock=variant(0);const soldOut=await order(c,noStock,1);record('already-sold-out',[soldOut],state(noStock),{status:400},stockRejected(soldOut)&&state(noStock).orderCount===0);
}
async function completedReturn(c,v,q) {
  const o=expect(await order(c,v,q),201,'return seed');
  for(const status of ['Confirmed','Shipping','Completed'])
    expect(await api('PUT',`/api/admin/orders/${o.orderId}/status`,{status},admin),200,'complete seed');
  expect(await api('POST',`/api/orders/${o.orderId}/return-request`,{reason:'Sản phẩm không phù hợp kích cỡ'},c),200,'request return');
  return o;
}
async function restoration() {
  const c=customers[0], d=customers[1];
  const cancel=(o,customer,asAdmin=false)=>asAdmin
    ?api('PUT',`/api/admin/orders/${o.orderId}/status`,{status:'Cancelled',note:'Audit'},admin)
    :api('PUT',`/api/orders/${o.orderId}/cancel`,{reason:'Audit'},customer);
  const approve=o=>api('PUT',`/api/admin/orders/${o.orderId}/return-request/approve`,{note:'Audit'},admin);
  for(let round=1;round<=3;round++) {
    for(const [name,firstAdmin,secondAdmin] of [['customers',false,false],['admins',true,true],['mixed',false,true]]) {
      const v=variant(10),a=expect(await order(c,v,2),201,'cancel seed'),b=expect(await order(d,v,3),201,'cancel seed');
      const r=await parallel([()=>cancel(a,c,firstAdmin),()=>cancel(b,d,secondAdmin)]),s=state(v);
      record(`l02-cancel-two-${name}:r${round}`,r,s,{stock:10,sold:0},r.every(ok)&&s.stock===10&&s.sold===0&&s.orders.every(o=>o.OrderStatus==='Cancelled'));
    }
    for(const asAdmin of [false,true]) {
      const v=variant(10),a=expect(await order(c,v,2),201,'sale cancel seed');
      const r=await parallel([()=>cancel(a,c,asAdmin),()=>order(d,v,3)]),s=state(v);
      record(`l02-cancel-${asAdmin?'admin':'customer'}-vs-sale:r${round}`,r,s,{stock:7,sold:3},r.every(ok)&&s.stock===7&&s.sold===3);
    }
    const v=variant(10),a=expect(await order(c,v,2),201,'same cancel seed');
    const r=await parallel([()=>cancel(a,c),()=>cancel(a,c,true)]),s=state(v);
    record(`l02-cancel-same-order-mixed:r${round}`,r,s,{stock:10,accepted:1},r.filter(ok).length===1&&r.every(x=>x.status===200||x.status===400)&&s.stock===10&&s.orders[0].OrderStatus==='Cancelled');
    const v2=variant(10),a2=await completedReturn(c,v2,2),b2=await completedReturn(d,v2,3);
    const r2=await parallel([()=>approve(a2),()=>approve(b2)]),s2=state(v2);
    record(`l02-return-two-orders:r${round}`,r2,s2,{stock:10,sold:0},r2.every(ok)&&s2.stock===10&&s2.sold===0&&s2.orders.every(o=>o.OrderStatus==='Returned'&&o.PaymentStatus==='Refunded'));
    const v3=variant(10),a3=await completedReturn(c,v3,2);
    const r3=await parallel([()=>approve(a3),()=>order(d,v3,3)]),s3=state(v3);
    record(`l02-return-vs-sale:r${round}`,r3,s3,{stock:7,sold:3},r3.every(ok)&&s3.stock===7&&s3.sold===3);
    const v4=variant(10),a4=await completedReturn(c,v4,2);
    const r4=await parallel([()=>approve(a4),()=>approve(a4)]),s4=state(v4);
    record(`l02-return-same-order:r${round}`,r4,s4,{stock:10,accepted:1},r4.filter(ok).length===1&&r4.every(x=>x.status===200||x.status===400)&&s4.stock===10&&s4.orders[0].OrderStatus==='Returned');
    const v5=variant(10),a5=await completedReturn(c,v5,2),b5=expect(await order(d,v5,3),201,'mixed return cancel seed');
    const r5=await parallel([()=>approve(a5),()=>cancel(b5,d,true)]),s5=state(v5);
    record(`l02-return-vs-admin-cancel:r${round}`,r5,s5,{stock:10,sold:0},r5.every(ok)&&s5.stock===10&&s5.sold===0);
  }
}
async function warehouse() {
  const c=customers[0];
  const close=(a,b)=>Math.abs(a-b)<=0.011;
  const edit=(po,v,q,unitCost=100000)=>api('PUT',`/api/admin/purchase-orders/${po.purchaseOrderId}`,{supplierId:1,items:[{variantId:v.variantId,quantity:q,unitCost}]},admin);
  const cancel=po=>api('POST',`/api/admin/purchase-orders/${po.purchaseOrderId}/cancel`,{reason:'Audit'},admin);
  const poState=po=>jsonSql(`SELECT p.Status status,p.TotalAmount total,(SELECT SUM(Quantity) FROM PurchaseOrderItems WHERE PurchaseOrderId=p.PurchaseOrderId) quantity FROM PurchaseOrders p WHERE p.PurchaseOrderId=${po.purchaseOrderId} FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`);
  const costPair=(s,pairs)=>pairs.some(([avg,cost])=>close(s.averageCost,avg)&&close(s.orders[0]?.UnitCost,cost));
  for(let round=1;round<=3;round++) {
    const v=variant(10),r=await parallel([()=>purchase(v,3,200000),()=>purchase(v,4,300000)]),s=state(v);
    record(`l03-two-receipts-cost:r${round}`,r,s,{stock:17,averageCost:164705.88},r.every(ok)&&s.stock===17&&close(s.averageCost,164705.88));

    const v2=variant(10),r2=await parallel([()=>purchase(v2,5,200000),()=>order(c,v2,2)]),s2=state(v2);
    record(`l03-stock-in-vs-sale-cost:r${round}`,r2,s2,{stock:13,sold:2,costPairs:[[138461.54,100000],[133333.33,133333.33]]},r2.every(ok)&&s2.stock===13&&s2.sold===2&&costPair(s2,[[138461.54,100000],[133333.33,133333.33]]));

    const v3=variant(10),p3=expect(await purchase(v3,5,200000),201,'cancel sale seed');
    const r3=await parallel([()=>cancel(p3),()=>order(c,v3,2)]),s3=state(v3);
    record(`l03-cancel-receipt-vs-sale:r${round}`,r3,{...s3,receipt:poState(p3)},{stock:8,sold:2,status:'Cancelled'},r3.every(ok)&&s3.stock===8&&s3.sold===2&&poState(p3).status==='Cancelled'&&costPair(s3,[[100000,100000],[91666.66,133333.33]]));

    const v4=variant(10),p4=expect(await purchase(v4,5,200000),201,'edit sale seed');
    const r4=await parallel([()=>edit(p4,v4,8,200000),()=>order(c,v4,2)]),s4=state(v4);
    record(`l03-edit-receipt-vs-sale:r${round}`,r4,{...s4,receipt:poState(p4)},{stock:16,sold:2,receiptQuantity:8},r4.every(ok)&&s4.stock===16&&s4.sold===2&&poState(p4).quantity===8&&costPair(s4,[[144444.44,144444.44],[145833.33,133333.33]]));

    const v5=variant(10),p5=expect(await purchase(v5,3),201,'two cancel seed'),p6=expect(await purchase(v5,4),201,'two cancel seed');
    const r5=await parallel([()=>cancel(p5),()=>cancel(p6)]),s5=state(v5);
    record(`l03-cancel-two-receipts:r${round}`,r5,s5,{stock:10,averageCost:100000},r5.every(ok)&&s5.stock===10&&close(s5.averageCost,100000)&&poState(p5).status==='Cancelled'&&poState(p6).status==='Cancelled');

    const v6=variant(10),p7=expect(await purchase(v6,5),201,'same cancel seed');
    const r6=await parallel([()=>cancel(p7),()=>cancel(p7)]),s6=state(v6);
    record(`l03-cancel-same-receipt:r${round}`,r6,s6,{stock:10,accepted:1},r6.filter(ok).length===1&&r6.every(x=>[200,400].includes(x.status))&&s6.stock===10&&poState(p7).status==='Cancelled');

    const v7=variant(10),p8=expect(await purchase(v7,5),201,'same edit seed');
    const r7=await parallel([()=>edit(p8,v7,7,120000),()=>edit(p8,v7,9,140000)]),s7=state(v7),ps7=poState(p8);
    const expectedCost=(1000000+ps7.total)/(10+ps7.quantity);
    record(`l03-edit-same-receipt:r${round}`,r7,{...s7,receipt:ps7},{stock:'10 + current receipt quantity',cost:'weighted current receipt'},r7.every(ok)&&[7,9].includes(ps7.quantity)&&s7.stock===10+ps7.quantity&&Math.abs(s7.averageCost-expectedCost)<=0.02);

    const v8=variant(10),p9=expect(await purchase(v8,5),201,'edit cancel seed');
    const r8=await parallel([()=>edit(p9,v8,7),()=>cancel(p9)]),s8=state(v8);
    record(`l03-edit-vs-cancel-same-receipt:r${round}`,r8,{...s8,receipt:poState(p9)},{stock:10,status:'Cancelled'},r8[1].status===200&&[200,400].includes(r8[0].status)&&s8.stock===10&&poState(p9).status==='Cancelled');

    const v9=variant(10),p10=expect(await purchase(v9,3),201,'edit two seed'),p11=expect(await purchase(v9,4),201,'edit two seed');
    const r9=await parallel([()=>edit(p10,v9,5),()=>edit(p11,v9,6)]),s9=state(v9);
    record(`l03-edit-two-receipts:r${round}`,r9,s9,{stock:21,averageCost:100000},r9.every(ok)&&s9.stock===21&&close(s9.averageCost,100000));

    const v10=variant(10),o10=expect(await order(c,v10,2),201,'restore seed');
    const r10=await parallel([()=>purchase(v10,5),()=>api('PUT',`/api/orders/${o10.orderId}/cancel`,{reason:'Audit'},c)]),s10=state(v10);
    record(`l03-stock-in-vs-order-cancel:r${round}`,r10,s10,{stock:15,sold:0},r10.every(ok)&&s10.stock===15&&s10.sold===0);

    const v11=variant(10),o11=await completedReturn(c,v11,2);
    const r11=await parallel([()=>purchase(v11,5),()=>api('PUT',`/api/admin/orders/${o11.orderId}/return-request/approve`,{},admin)]),s11=state(v11);
    record(`l03-stock-in-vs-return:r${round}`,r11,s11,{stock:15,sold:0},r11.every(ok)&&s11.stock===15&&s11.sold===0);

    const va=variant(10),vb=variant(10);
    const body=ids=>({supplierId:1,items:ids.map(v=>({variantId:v.variantId,quantity:2,unitCost:100000}))});
    const r12=await parallel([()=>api('POST','/api/admin/purchase-orders',body([va,vb]),admin),()=>api('POST','/api/admin/purchase-orders',body([vb,va]),admin)]),ss12=[state(va),state(vb)];
    record(`l03-opposite-variant-order:r${round}`,r12,ss12,{stocks:[14,14]},r12.every(ok)&&ss12.every(s=>s.stock===14&&close(s.averageCost,100000)));

    const v13=variant(0),p13=expect(await purchase(v13,2),201,'shortage race seed');
    const r13=await parallel([()=>cancel(p13),()=>order(c,v13,1)]),s13=state(v13),ps13=poState(p13);
    const valid13=(r13[0].status===200&&stockRejected(r13[1])&&s13.stock===0&&s13.sold===0&&ps13.status==='Cancelled')
      ||(r13[0].status===400&&ok(r13[1])&&s13.stock===1&&s13.sold===1&&ps13.status==='Completed');
    record(`l03-cancel-receipt-insufficient-race:r${round}`,r13,{...s13,receipt:ps13},{consistent:true},valid13);
  }
  const overflow=variant(10),ro=await purchase(overflow,2147483647),so=state(overflow);
  record('l03-stock-overflow-rejected',[ro],so,{status:400,stock:10},ro.status===400&&so.stock===10);
  const amount=variant(0),ra=await purchase(amount,2,5000000000000000),sa=state(amount);
  record('l03-money-overflow-rejected',[ra],sa,{status:400,stock:0},ra.status===400&&sa.stock===0);
}
async function remaining() {
  docker(['run','-d','--name',secondaryContainer,'--network',run,'-p','127.0.0.1::8080',
    ...Object.keys(appEnv).flatMap(k=>['--env',k]),image],{env:{...process.env,...appEnv}});
  const other='http://127.0.0.1:'+docker(['port',secondaryContainer,'8080/tcp']).split(':').pop();
  let ready=false;
  for(let i=0;i<90;i++){try{if((await api('GET','/api/products',undefined,undefined,other)).status===200){ready=true;break;}}catch{}await delay(300);}
  if(!ready)throw new Error('Second API startup timeout');
  const c=customers[0],d=customers[1],v=variant(10),payload=orderBody(c,v);
  const r=await parallel([0,1,2,3,4].map(i=>()=>api('POST','/api/orders',payload,c,i%2?other:base)));
  const s=state(v),id=r.find(ok)?.data.orderId;
  record('remaining-idempotency-two-api-instances',r,s,{stock:9,orderCount:1,sameOrderId:true},r.every(ok)&&r.every(x=>x.data.orderId===id)&&s.stock===9&&s.orderCount===1);
  const mismatch=await api('POST','/api/orders',{...payload,note:'changed'},c);
  record('remaining-idempotency-payload-conflict',[mismatch],state(v),{status:409,stock:9},mismatch.status===409&&state(v).stock===9);
  const hidden=await api('GET','/api/orders/by-request/'+payload.requestId,undefined,d);
  record('remaining-idempotency-customer-isolation',[hidden],{status:hidden.status},{status:404},hidden.status===404);
  const otherUser=await api('POST','/api/orders',{...payload,addressId:d.addressId},d,other);
  record('remaining-idempotency-key-scoped-to-user',[otherUser],state(v),{stock:8,orderCount:2},ok(otherUser)&&otherUser.data.orderId!==id&&state(v).stock===8);
  const fresh=await api('POST','/api/orders',{...payload,requestId:randomUUID()},c);
  record('remaining-identical-new-intent-allowed',[fresh],state(v),{stock:7,orderCount:3},ok(fresh)&&fresh.data.orderId!==id&&state(v).stock===7);
  docker(['restart',apiContainer]);ready=false;
  // Docker can reassign an ephemeral published port when the container restarts.
  base='http://127.0.0.1:'+docker(['port',apiContainer,'8080/tcp']).split(':').pop();
  for(let i=0;i<90;i++){try{if((await api('GET','/api/products')).status===200){ready=true;break;}}catch{}await delay(300);}
  if(!ready)throw new Error('API restart timeout');
  const replay=await api('POST','/api/orders',payload,c);
  record('remaining-replay-after-api-restart',[replay],state(v),{stock:7,orderCount:3},ok(replay)&&replay.data.orderId===id&&state(v).stock===7&&state(v).orderCount===3);
  await clear(c);const cv=variant(10);expect(await add(c,cv),201,'cart replay seed');const cp={requestId:randomUUID(),addressId:c.addressId,paymentMethod:'COD'};
  const cr=await parallel([()=>api('POST','/api/orders',cp,c),()=>api('POST','/api/orders',cp,c,other)]),cs=state(cv);
  record('remaining-cart-replay-after-cart-deletion',cr,cs,{stock:9,orderCount:1},cr.every(ok)&&cr[0].data.orderId===cr[1].data.orderId&&cs.stock===9&&cs.orderCount===1);
  const failedVariant=variant(0),fp=orderBody(c,failedVariant),first=await api('POST','/api/orders',fp,c);
  sql(`UPDATE ProductVariants SET StockQuantity=1 WHERE VariantId=${failedVariant.variantId};`);
  const retry=await api('POST','/api/orders',fp,c,other);
  record('remaining-failed-request-can-retry',[first,retry],state(failedVariant),{statuses:[400,201],stock:0,orderCount:1},first.status===400&&ok(retry)&&state(failedVariant).stock===0&&state(failedVariant).orderCount===1);
  const cancelled=await api('PUT',`/api/orders/${id}/cancel`,{reason:'Audit'},c);expect(cancelled,200,'cancel replay seed');
  const again=await api('POST','/api/orders',payload,c);
  record('remaining-replay-cancelled-order',[again],state(v),{stock:8,orderCount:3,status:'Cancelled'},ok(again)&&again.data.orderId===id&&[5,'Cancelled'].includes(again.data.orderStatus)&&state(v).stock===8);
  await clear(c);const limit=variant(200);expect(await add(c,limit,99),201,'cart bound seed');
  const lr=await parallel([()=>add(c,limit),()=>api('POST','/api/cart/items',{variantId:limit.variantId,quantity:1},c,other)]);
  const quantity=jsonSql(`SELECT SUM(Quantity) quantity FROM CartItems WHERE UserId=${c.userId} FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`).quantity;
  record('remaining-cart-concurrent-limit',lr,{quantity},{quantity:100,accepted:1},quantity===100&&lr.filter(ok).length===1&&lr.every(x=>[201,400].includes(x.status)));
  await clear(c);const ca=variant(10),cb=variant(10);expect(await add(c,ca),201,'cart mutation seed');
  const mr=await parallel([()=>api('POST','/api/orders',{requestId:randomUUID(),addressId:c.addressId,paymentMethod:'COD'},c),()=>add(c,cb)]);
  const ms=state(cb),cartQty=jsonSql(`SELECT COALESCE(SUM(Quantity),0) quantity FROM CartItems WHERE UserId=${c.userId} AND VariantId=${cb.variantId} FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`).quantity;
  record('remaining-cart-add-vs-checkout',mr,{...ms,cartQty},{conservedQuantity:1},mr.every(ok)&&ms.sold+cartQty===1&&ms.stock===10-ms.sold&&state(ca).stock===9);
  const qr=variant(10),qo=expect(await order(c,qr,2,{paymentMethod:'MockQR'}),201,'admin QR seed');
  const qrR=await parallel([()=>api('POST',`/api/orders/${qo.orderId}/mock-qr/confirm`,{},c),()=>api('PUT',`/api/admin/orders/${qo.orderId}/status`,{status:'Cancelled'},admin)]),qs=state(qr);
  record('remaining-mock-payment-vs-admin-cancel',qrR,qs,{stock:10,notPaid:true},qrR.every(x=>[200,400].includes(x.status))&&qrR[1].status===200&&qs.stock===10&&qs.orders[0].PaymentStatus!=='Paid');
  const du=variant(10),dou=expect(await order(c,du,1,{paymentMethod:'MockQR'}),201,'double payment seed');
  const dr=await parallel([()=>api('POST',`/api/orders/${dou.orderId}/mock-qr/confirm`,{},c),()=>api('POST',`/api/orders/${dou.orderId}/mock-qr/confirm`,{},c,other)]);
  record('remaining-mock-payment-once',dr,state(du),{accepted:1,stock:9},dr.filter(ok).length===1&&dr.every(x=>[200,400].includes(x.status))&&state(du).stock===9&&state(du).orders[0].PaymentStatus==='Paid');
  const bad=await api('POST','/api/orders',{...payload,requestId:null},c);
  record('remaining-missing-request-id-rejected',[bad],{status:bad.status},{status:400},bad.status===400);
}
async function extended() {
  const c=customers[0];
  for(let round=1;round<=3;round++) {
    const v=variant(10);
    const a=await completedReturn(c,v,2), b=await completedReturn(customers[1],v,3);
    const rr=await parallel([a,b].map(o=>()=>api('PUT',`/api/admin/orders/${o.orderId}/return-request/approve`,{note:'Audit approval'},admin)));
    const sr=state(v);record(`return-two-orders-one-variant:r${round}`,rr,sr,{stock:10,sold:0},rr.every(ok)&&sr.stock===10&&sr.sold===0);
    const v2=variant(10), same=await completedReturn(c,v2,2);
    const r2=await parallel([1,2].map(()=>()=>api('PUT',`/api/admin/orders/${same.orderId}/return-request/approve`,{},admin)));
    const s2=state(v2);record(`return-same-order:r${round}`,r2,s2,{stock:10,accepted:1},s2.stock===10&&r2.filter(ok).length===1&&r2.every(x=>x.status<500));
    const v3=variant(10), po=expect(await purchase(v3,5),201,'purchase seed');
    const r3=await parallel([()=>api('POST',`/api/admin/purchase-orders/${po.purchaseOrderId}/cancel`,{reason:'Audit cancel'},admin),()=>order(c,v3,2)]);
    const s3=state(v3);record(`cancel-stock-in-vs-sale:r${round}`,r3,s3,{stock:8,sold:2},r3.every(ok)&&s3.stock===8&&s3.sold===2);
    const code='AUDITUSER'+round;
    sql(`INSERT INTO Coupons(Code,Name,DiscountType,DiscountValue,MinOrderAmount,StartDate,EndDate,UsageLimitPerUser,UsedCount,IsActive,CreatedByUserId,CreatedAt) SELECT '${code}',N'Audit','FixedAmount',10000,0,DATEADD(day,-1,GETUTCDATE()),DATEADD(day,1,GETUTCDATE()),1,0,1,UserId,GETUTCDATE() FROM Users WHERE Email='admin.audit@test.local';`);
    const variants=[variant(10),variant(10)];
    const r4=await parallel(variants.map(v=>()=>order(c,v,1,{couponCode:code})));
    const s4=jsonSql(`SELECT UsedCount usedCount,(SELECT COUNT(*) FROM CouponUsages u WHERE u.CouponId=cp.CouponId AND UserId=${c.userId}) actualUserUsages FROM Coupons cp WHERE Code='${code}' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`);
    record(`coupon-per-user-limit:r${round}`,r4,s4,{actualUserUsages:1},r4.filter(ok).length===1&&r4.every(x=>x.status<500)&&s4.actualUserUsages===1&&s4.usedCount===1);
    const unlimited='AUDITRESTORE'+round;
    sql(`INSERT INTO Coupons(Code,Name,DiscountType,DiscountValue,MinOrderAmount,StartDate,EndDate,UsedCount,IsActive,CreatedByUserId,CreatedAt) SELECT '${unlimited}',N'Audit','FixedAmount',10000,0,DATEADD(day,-1,GETUTCDATE()),DATEADD(day,1,GETUTCDATE()),0,1,UserId,GETUTCDATE() FROM Users WHERE Email='admin.audit@test.local';`);
    const orders=[];
    for(let i=0;i<2;i++)orders.push(expect(await order(customers[i],variant(10),1,{couponCode:unlimited}),201,'coupon cancel seed'));
    const r5=await parallel(orders.map((o,i)=>()=>api('PUT',`/api/orders/${o.orderId}/cancel`,{reason:'Audit'},customers[i])));
    const s5=jsonSql(`SELECT UsedCount usedCount,(SELECT COUNT(*) FROM CouponUsages u WHERE u.CouponId=cp.CouponId) actualUsages FROM Coupons cp WHERE Code='${unlimited}' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`);
    record(`coupon-restore-concurrent-cancel:r${round}`,r5,s5,{usedCount:0,actualUsages:0},r5.every(ok)&&s5.usedCount===0&&s5.actualUsages===0);
  }
  const over=variant(10);const overflow=await purchase(over,2147483647);const so=state(over);
  record('stock-in-integer-overflow',[overflow],so,{rejected:true,stock:10},[400,409].includes(overflow.status)&&so.stock===10);
}
main().catch(e=>{ report.error=e.message.split(password).join('[redacted]');console.error(report.error);process.exitCode=2; }).finally(()=>{
  // Collect sanitized diagnostic logs, then delete ALL test data with the dedicated containers.
  if(!ephemeral) try { const log=docker(['logs',apiContainer],{maxBuffer:64*1024*1024});fs.writeFileSync(path.join(output,'api.log'),log.split(password).join('[redacted]')); }catch{}
  report.cleanup={};
  for(const name of [secondaryContainer,apiContainer,dbContainer]) {
    if(name===secondaryContainer&&!process.argv.includes('--remaining-only'))continue;
    try {docker(['rm','-f','-v',name]);report.cleanup[name]=true;}catch{report.cleanup[name]=false;}
  }
  try{docker(['network','rm',run]);report.cleanup.network=true;}catch{report.cleanup.network=false;}
  try{docker(['image','rm',image]);report.cleanup.image=true;}catch{report.cleanup.image=false;}
  report.cleanup.dataDeleted=report.cleanup[apiContainer]&&report.cleanup[dbContainer];
  report.completedAt=new Date().toISOString();
  report.summary={total:report.cases.length,passed:report.cases.filter(c=>c.passed).length,failed:report.cases.filter(c=>!c.passed).length};
  save();console.log(JSON.stringify({report:ephemeral?undefined:path.join(output,'result.json'),summary:report.summary,cleanup:report.cleanup,error:report.error}));
  if(!report.error&&report.summary.failed)process.exitCode=1;
});
