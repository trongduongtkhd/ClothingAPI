// Audit real HTTP concurrency against the current build and a disposable SQL Server.
// First: dotnet publish ClothingAPI.csproj -c Release --no-restore -o tests/manual/.publish /p:UseAppHost=false
// Then: node tests/manual/order-stock-audit.cjs
// No production containers, credentials, volumes or databases are used.
const { execFileSync } = require('child_process');
const { randomBytes } = require('crypto');
const { performance } = require('perf_hooks');
const http = require('http');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '../..');
const run = 'stockaudit-' + new Date().toISOString().replace(/\D/g, '').slice(0, 14) + '-' + randomBytes(3).toString('hex');
const dbContainer = run + '-db', apiContainer = run + '-api', image = run + ':local';
const database = 'StockAudit';
const output = path.join(__dirname, 'results', run);
fs.mkdirSync(output, { recursive: true });
const report = { run, startedAt: new Date().toISOString(), method: 'Unmodified current source; independent HTTP requests released together; SQL Server 2022 persisted-state assertions; disposable containers without volumes.', cases: [] };
const password = 'Audit@' + randomBytes(24).toString('hex');
let base, serial = 0, admin;
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
const save = () => fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify(report, null, 2));
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
function api(method, endpoint, body, customer) {
  const sent = performance.now();
  return new Promise((resolve, reject) => {
    const payload = body === undefined ? undefined : JSON.stringify(body);
    const request = http.request(base + endpoint, { method, headers: {
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
    (SELECT COUNT(*) FROM OrderItems WHERE VariantId=${v.variantId}) orderCount,
    (SELECT COALESCE(SUM(i.Quantity),0) FROM OrderItems i JOIN Orders o ON o.OrderId=i.OrderId WHERE i.VariantId=${v.variantId} AND o.OrderStatus NOT IN ('Cancelled','Returned')) sold,
    JSON_QUERY((SELECT o.OrderId,o.UserId,o.OrderStatus,o.PaymentStatus,i.Quantity FROM Orders o JOIN OrderItems i ON i.OrderId=o.OrderId WHERE i.VariantId=${v.variantId} FOR JSON PATH)) orders
    FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;`);
}
const orderBody = (c,v,q=1, extra={}) => ({addressId:c.addressId,paymentMethod:'COD',buyNowItem:{variantId:v.variantId,quantity:q},...extra});
const order = (c,v,q=1,extra={}) => api('POST','/api/orders',orderBody(c,v,q,extra),c);
async function clear(c) { expect(await api('DELETE','/api/cart',undefined,c),200,'clear'); }
async function add(c,v,q=1) { return api('POST','/api/cart/items',{variantId:v.variantId,quantity:q},c); }
const purchase = (v,q) => api('POST','/api/admin/purchase-orders',{supplierId:1,items:[{variantId:v.variantId,quantity:q,unitCost:100000}]},admin);
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
  const env={ASPNETCORE_ENVIRONMENT:'Docker',ASPNETCORE_URLS:'http://+:8080',
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
  if(process.argv.includes('--extended-only')) { await extended(); return; }
  for(let round=1;round<=3;round++) for(const mode of ['cart','buyNow']) {
    for(const [stock,count,q] of [[10,2,1],[10,2,5],[10,2,6],[2,5,1],[1,2,1]])
      await checkoutScenario(`${mode}:stock=${stock},customers=${count},qty=${q}:r${round}`,stock,count,q,mode);
  }
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
  try { const log=docker(['logs',apiContainer],{maxBuffer:64*1024*1024});fs.writeFileSync(path.join(output,'api.log'),log.split(password).join('[redacted]')); }catch{}
  report.cleanup={};
  for(const name of [apiContainer,dbContainer]) {
    try {docker(['rm','-f','-v',name]);report.cleanup[name]=true;}catch{report.cleanup[name]=false;}
  }
  try{docker(['network','rm',run]);report.cleanup.network=true;}catch{report.cleanup.network=false;}
  try{docker(['image','rm',image]);report.cleanup.image=true;}catch{report.cleanup.image=false;}
  report.cleanup.dataDeleted=report.cleanup[apiContainer]&&report.cleanup[dbContainer];
  report.completedAt=new Date().toISOString();
  report.summary={total:report.cases.length,passed:report.cases.filter(c=>c.passed).length,failed:report.cases.filter(c=>!c.passed).length};
  save();console.log(JSON.stringify({report:path.join(output,'result.json'),summary:report.summary,cleanup:report.cleanup,error:report.error}));
  if(!report.error&&report.summary.failed)process.exitCode=1;
});
