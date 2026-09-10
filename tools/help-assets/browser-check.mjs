import { chromium } from 'playwright';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
const here=path.dirname(fileURLToPath(import.meta.url));
const output=path.resolve(here,'../../artifacts/help-validation');
await mkdir(output,{recursive:true});
const pages=JSON.parse(await readFile(path.join(output,'help-pages.json'),'utf8'));
const entry=path.resolve(process.argv[2]||path.join(here,'../../Host/MyAvaloniaManagement/HelpWeb/index.html'));
const browser=await chromium.launch({channel:'msedge',headless:true});
const context=await browser.newContext({viewport:{width:940,height:800}});
const page=await context.newPage();
const errors=[],external=[];
page.on('pageerror',error=>errors.push(error.message));
page.on('console',message=>{if(message.type()==='error'){errors.push(message.text());console.error(message.text().slice(0,1200));}});
page.on('requestfailed',request=>{const error=request.url()+' '+request.failure()?.errorText;errors.push(error);console.error(error);});
await context.route(/^https?:/,route=>{external.push(route.request().url());return route.abort();});
await page.addInitScript(()=>{window.helpMessages=[];window.invokeCSharpAction=body=>window.helpMessages.push(JSON.parse(body));});
const results=[];
try{
  await page.goto(pathToFileURL(entry).href);
  await page.waitForFunction(()=>window.hostHelp&&window.MathJax?.startup?.promise);
  await page.evaluate(()=>MathJax.startup.promise);
  for(const article of pages){
    const start=Date.now(),errorStart=errors.length;
    await page.evaluate(article=>{window.helpMessages=[];window.hostHelp.openPage({page:article,position:{fullTextExpanded:true,scrollTop:0},query:'',anchor:'',presentation:{dark:false,fontSize:17,reduceMotion:false}});},article);
    await page.waitForFunction(id=>window.helpMessages.some(m=>m.requestId===id&&['rendered','render-error'].includes(m.type)),article.requestId,{timeout:60000});
    const status=await page.evaluate(()=>window.hostHelp.getStatus());
    results.push({id:article.id,...status,elapsedMs:Date.now()-start,errors:errors.slice(errorStart)});
    if(article.demo){
      await page.locator('.demo').scrollIntoViewIfNeeded();
      await page.locator('[data-action=next]').click();
      assert.match(await page.locator('.demo-counter').textContent(),/^2 /);
      await page.locator('[data-action=play]').click();
      await page.evaluate(()=>window.hostHelp.suspend(true));
      assert.equal((await page.evaluate(()=>window.hostHelp.getStatus())).playing,false);
      await page.evaluate(()=>window.hostHelp.suspend(false));
    }
    if(article.id==='attention'){
      await page.locator('[data-action=reset]').click();
      assert.equal(await page.locator('[data-metric=entropy]').textContent(),'4.000');
      await page.locator('[data-param=n]').fill('4');
      assert.equal(await page.locator('[data-metric=entropy]').textContent(),'2.000');
    }
    if(article.id==='references'){
      await page.locator('.math-source.display [data-formula-action=zoom]').first().click();
      assert.equal(await page.locator('#formula-dialog').isVisible(),true);
      await page.locator('#copy-formula').click();
      assert.ok(await page.evaluate(()=>window.helpMessages.some(m=>m.type==='copy'&&m.value.includes('aligned'))));
      await page.locator('#close-formula').click();
    }
    await page.evaluate(()=>{const full=document.getElementById('full-text');if(full)full.open=false;window.scrollTo({top:0,behavior:'instant'});});
    await page.screenshot({path:path.join(output,`${article.id}-light.png`),fullPage:true});
    await page.evaluate(()=>window.hostHelp.present({dark:true}));
    await page.waitForTimeout(250);
    await page.screenshot({path:path.join(output,`${article.id}-dark.png`),fullPage:true});
    console.log(JSON.stringify(results.at(-1)));
  }
  const attention=pages.find(p=>p.id==='attention');
  await page.evaluate(article=>{window.helpMessages=[];window.hostHelp.openPage({page:article,position:{fullTextExpanded:false},query:'Scalable Fabric',anchor:'',presentation:{dark:true,fontSize:22,reduceMotion:true}});},attention);
  await page.waitForFunction(()=>window.helpMessages.some(m=>m.type==='rendered'));
  assert.equal(await page.locator('#full-text').getAttribute('open'),'');
  assert.ok(await page.locator('mark').count()>0);
  await page.keyboard.press('Control+f');
  assert.ok(await page.evaluate(()=>window.helpMessages.some(m=>m.type==='search')));
  assert.deepEqual(external,[],'All reader dependencies must resolve locally');
  assert.ok(results.every(r=>r.mathErrors===0&&r.diagramErrors===0),'Math/diagram failures: '+JSON.stringify(results));
  assert.deepEqual(errors,[],'Browser errors');
}finally{
  await writeFile(path.join(output,'browser-results.json'),JSON.stringify({entry,results,errors,external},null,2));
  await browser.close();
}
