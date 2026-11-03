'use strict';
// Optional: node + playwright + installed Chromium. Tests the browser illustration only.
const { chromium } = require('playwright');
const path = require('path');
const fs = require('fs');
const http = require('http');
(async () => {
  const options = {headless:true};
  if(process.env.CHROMIUM_EXECUTABLE) options.executablePath=process.env.CHROMIUM_EXECUTABLE;
  const routes={'/':['index.html','text/html; charset=utf-8'],'/style.css':['style.css','text/css'],'/app.js':['app.js','text/javascript']};
  const server=http.createServer((request,response)=>{
    const route=routes[request.url];if(!route){response.writeHead(404);response.end();return;}
    response.writeHead(200,{'Content-Type':route[1]});response.end(fs.readFileSync(path.resolve(__dirname,'../ui/trade-preview',route[0])));
  });
  await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(0,'127.0.0.1',resolve);});
  let browser;
  try {
    browser=await chromium.launch(options);
    for(const viewport of [{width:1920,height:1080},{width:1280,height:720}]) {
      const page=await browser.newPage({viewport});
      await page.goto(`http://127.0.0.1:${server.address().port}/`);
      if(await page.locator('#own .slot').count()!==10 || await page.locator('#other .slot').count()!==10) throw Error('20 slots expected');
      await page.locator('#confirm').click();
      if(!await page.locator('#confirm').isDisabled()) throw Error('Confirmation state');
      await page.locator('#cancel').click();
      if(!(await page.locator('#status').textContent()).includes('отменён')) throw Error('Cancel state');
      await page.locator('#reset').click();
      await page.locator('#own .slot').first().click();
      if(!(await page.locator('#status').textContent()).includes('инвентарь')) throw Error('Own slot state');
      await page.locator('#reset').click();
      if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)) throw Error('Horizontal overflow');
      await page.close();
    }
    console.log('MRE Trade browser preview: 20 slots, own-slot/confirm/cancel/reset, no horizontal overflow at 1920 and 1280 checked. Unity/game not tested.');
  } finally { if(browser)await browser.close();await new Promise(resolve=>server.close(resolve)); }
})().catch(error=>{console.error(error);process.exitCode=1;});
