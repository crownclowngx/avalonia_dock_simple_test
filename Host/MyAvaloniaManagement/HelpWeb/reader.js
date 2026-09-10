(function() {
  'use strict';
  const content=document.getElementById('content'), fullDialog=document.getElementById('formula-dialog');
  let current=null, demo=null, renderTail=Promise.resolve(), latest='', positionTimer=null, toastTimer=null, suspended=false;
  let preferences={dark:false,fontSize:17,reduceMotion:false};
  const sourceFormulas=new WeakMap();
  const send=(type,extra={})=>{
    const body=JSON.stringify({type,requestId:current?.page.requestId,...extra});
    if(typeof window.invokeCSharpAction==='function')window.invokeCSharpAction(body);
    else if(window.chrome?.webview)window.chrome.webview.postMessage(body);
    // A standalone browser preview uses this event; production uses the native message bridge.
    else window.dispatchEvent(new CustomEvent('help-message',{detail:JSON.parse(body)}));
  };
  const notice=text=>{const toast=document.getElementById('toast');toast.textContent=text;toast.classList.add('visible');clearTimeout(toastTimer);toastTimer=setTimeout(()=>toast.classList.remove('visible'),1800);};
  function present(next){
    preferences={...preferences,...next};
    document.documentElement.classList.toggle('dark',preferences.dark);
    document.documentElement.classList.toggle('reduce-motion',preferences.reduceMotion);
    document.documentElement.style.setProperty('--font-size',`${Math.min(26,Math.max(14,preferences.fontSize))}px`);
    if(preferences.reduceMotion)demo?.suspend(true);
  }
  function position(){
    if(!current)return;
    let anchor='';for(const heading of content.querySelectorAll('h1[id],h2[id],h3[id]')){
      if(heading.getClientRects().length&&heading.getBoundingClientRect().top<=40)anchor=heading.id;
    }
    send('position',{scrollTop:window.scrollY,anchor,fullTextExpanded:!!document.getElementById('full-text')?.open});
    const total=document.documentElement.scrollHeight-window.innerHeight;
    document.getElementById('progress').style.width=`${total>0?100*window.scrollY/total:100}%`;
  }
  async function renderMath(scope){
    await MathJax.startup.promise;
    const elements=[...scope.querySelectorAll('.math-source:not([data-rendered])')];
    for(const element of elements){
      if(!element.isConnected)return;
      const tex=element.dataset.tex,display=element.dataset.display==='true';
      try{
        const svg=await MathJax.tex2svgPromise(tex,{display});
        if(!element.isConnected)return;
        element.replaceChildren(svg);element.dataset.rendered='true';element.setAttribute('aria-label',tex);sourceFormulas.set(element,tex);
        if(display){
          const tools=document.createElement('div');tools.className='formula-tools';
          const number=[...content.querySelectorAll('.math-source.display')].indexOf(element)+1;
          element.id ||= `formula-${number}`;
          const label=document.createElement('a');label.href=`#${element.id}`;label.textContent=`式 ${number}`;tools.append(label);
          for(const [action,text] of [['zoom','放大'],['copy','复制 LaTeX']]){const b=document.createElement('button');b.type='button';b.dataset.formulaAction=action;b.textContent=text;tools.append(b);}
          element.append(tools);
        }else{
          const b=document.createElement('button');b.type='button';b.dataset.formulaAction='zoom';b.className='math-inline-button';b.textContent='↗';b.setAttribute('aria-label','放大行内公式');element.append(b);
        }
      }catch(error){element.classList.add('math-error');element.textContent=`公式暂未排版：${tex}`;element.dataset.rendered='error';console.error('HELP_MATH_FAILED',error);}
    }
  }
  async function renderDiagrams(scope){
    for(const code of scope.querySelectorAll('pre > code.language-mermaid, .mermaid:not([data-processed])')){
      if(code.closest('.mermaid-figure'))continue;
      if(!code.isConnected)return;
      const source=code.textContent,figure=document.createElement('div');figure.className='mermaid-figure';
      const graph=document.createElement('div');graph.className='mermaid';graph.textContent=source;
      const details=document.createElement('details');details.className='diagram-source';const summary=document.createElement('summary');summary.textContent='查看图示源码';details.append(summary);const pre=document.createElement('pre');pre.textContent=source;details.append(pre);
      figure.append(graph,details);(code.tagName==='CODE'?code.parentElement:code).replaceWith(figure);
      try{await mermaid.run({nodes:[graph],suppressErrors:false});}
      catch(error){graph.textContent='此图示暂未排版，可展开源码阅读。';details.open=true;figure.dataset.error='true';console.error('HELP_DIAGRAM_FAILED',error);}
    }
  }
  async function fullText(){
    const details=document.getElementById('full-text');if(!details?.open)return;
    await renderMath(details);await renderDiagrams(details);
  }
  function toc(){
    const nav=document.getElementById('toc');nav.replaceChildren();const used=new Set();let count=0;
    for(const heading of content.querySelectorAll('h1,h2,h3')){
      let id=heading.id||`section-${++count}`,suffix=1;while(used.has(id))id=`${heading.id||'section'}-${suffix++}`;
      used.add(id);heading.id=id;const link=document.createElement('a');link.href=`#${id}`;link.textContent=heading.textContent;link.className=`level-${heading.tagName.slice(1)}`;nav.append(link);
    }
    document.getElementById('toc-count').textContent=`${nav.children.length} 个章节`;
  }
  async function jump(anchor){
    let target=document.getElementById(anchor);
    if(!target){target=[...content.querySelectorAll('[id]')].find(e=>e.id.toLowerCase()===anchor.toLowerCase());}
    if(!target){notice('未找到此章节，已保留当前文章');return;}
    const details=target.closest('details');if(details){details.open=true;await fullText();}
    target.scrollIntoView({behavior:'auto',block:'start'});position();
  }
  function highlight(query){
    if(!query)return null;
    const walker=document.createTreeWalker(content,NodeFilter.SHOW_TEXT,{acceptNode(node){
      return node.parentElement.closest('script,style,mjx-container,.math-source,.demo')?NodeFilter.FILTER_REJECT:NodeFilter.FILTER_ACCEPT;
    }});
    const nodes=[];while(walker.nextNode())nodes.push(walker.currentNode);let first=null;
    for(const node of nodes){
      const text=node.textContent,lower=text.toLocaleLowerCase(),needle=query.toLocaleLowerCase();let index=lower.indexOf(needle);if(index<0)continue;
      const fragment=document.createDocumentFragment();let start=0;
      while(index>=0){fragment.append(text.slice(start,index));const mark=document.createElement('mark');mark.textContent=text.slice(index,index+query.length);fragment.append(mark);first ||= mark;start=index+query.length;index=lower.indexOf(needle,start);}
      fragment.append(text.slice(start));node.replaceWith(fragment);
    }
    return first;
  }
  function openPage(payload){
    latest=payload.page.requestId;
    renderTail=renderTail.catch(()=>{}).then(async()=>{
      if(payload.page.requestId!==latest)return;
      demo?.dispose();demo=null;clearTimeout(positionTimer);current=payload;
      present(payload.presentation);
      mermaid.initialize({startOnLoad:false,securityLevel:'strict',theme:preferences.dark?'dark':'default',flowchart:{htmlLabels:false},maxTextSize:100000});
      document.getElementById('article-title').textContent=payload.page.title;
      document.getElementById('article-description').textContent=payload.page.description;
      content.innerHTML=payload.page.html;window.scrollTo({top:0,behavior:'instant'});
      for(const table of content.querySelectorAll('table')){
        const scroll=document.createElement('div');scroll.className='table-scroll';table.replaceWith(scroll);scroll.append(table);
      }
      const details=document.getElementById('full-text');
      if(details)details.open=!!payload.position.fullTextExpanded||!!payload.query||!!payload.anchor;
      const guide=content.querySelector(':scope>section[data-source]');
      if(guide){await renderMath(guide);await renderDiagrams(guide);}
      if(details?.open)await fullText();
      if(payload.page.requestId!==latest)return;
      if(payload.page.demo)demo=HelpDemos.mount(document.getElementById('help-demo'),payload.page.demo);
      demo?.suspend(suspended||document.hidden);
      toc();
      if(details)details.addEventListener('toggle',()=>{
        if(current!==payload)return;
        if(details.open){renderTail=renderTail.catch(()=>{}).then(fullText).then(position);}else position();
      });
      const first=highlight(payload.query);
      if(payload.anchor)await jump(payload.anchor);
      else if(first)first.scrollIntoView({behavior:'auto',block:'center'});
      else window.scrollTo({top:payload.position.scrollTop||0,behavior:'instant'});
      requestAnimationFrame(()=>{if(current===payload)position();});
      send('rendered');
    }).catch(error=>{console.error('HELP_PAGE_FAILED',error);if(current?.page.requestId===latest)send('render-error');});
  }
  document.addEventListener('click',async event=>{
    const action=event.target.closest('[data-formula-action]');
    if(action){
      const element=action.closest('.math-source'),tex=element.dataset.tex;
      if(action.dataset.formulaAction==='copy'){send('copy',{value:tex});notice('已请求复制 LaTeX');}
      else{const clone=element.querySelector('mjx-container').cloneNode(true);document.getElementById('formula-large').replaceChildren(clone);document.getElementById('formula-source').textContent=tex;fullDialog.showModal();}
      return;
    }
    const link=event.target.closest('a[href]');if(!link)return;
    event.preventDefault();const href=link.getAttribute('href');
    if(href.startsWith('#'))await jump(decodeURIComponent(href.slice(1)));
    else send('link',{value:href,source:link.closest('[data-source]')?.dataset.source||''});
  });
  document.getElementById('close-formula').addEventListener('click',()=>fullDialog.close());
  document.getElementById('copy-formula').addEventListener('click',()=>{send('copy',{value:document.getElementById('formula-source').textContent});notice('已请求复制 LaTeX');});
  window.addEventListener('scroll',()=>{clearTimeout(positionTimer);positionTimer=setTimeout(position,80);},{passive:true});
  window.addEventListener('keydown',event=>{if(event.ctrlKey&&event.key.toLowerCase()==='f'){event.preventDefault();send('search');}});
  document.addEventListener('visibilitychange',()=>{if(document.hidden){demo?.suspend(true);position();}});
  window.addEventListener('pagehide',()=>{position();demo?.dispose();clearTimeout(positionTimer);clearTimeout(toastTimer);});
  window.hostHelp=Object.freeze({openPage,present,suspend(value){suspended=value;if(value){demo?.suspend(true);position();}},
    getStatus(){return {article:current?.page.id,requestId:current?.page.requestId,formulas:content.querySelectorAll('.math-source[data-rendered=true]').length,mathErrors:content.querySelectorAll('.math-error,merror,[data-mml-node=merror]').length,diagramErrors:content.querySelectorAll('.mermaid-figure[data-error]').length,diagrams:content.querySelectorAll('.mermaid svg').length,playing:demo?.playing||false};}});
  send('ready');
})();
