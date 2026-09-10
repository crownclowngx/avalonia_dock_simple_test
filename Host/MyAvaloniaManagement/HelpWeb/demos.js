(function(root) {
  'use strict';
  const M = root.HelpModels;
  const esc = text => String(text).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const definitions = {
    workspace: { title:'工作围绕一个焦点展开', badge:'产品行为示意', lead:'打开任务、查看摘要、调整布局。此处的操作只影响演示。', steps:[
      ['先看全局','外围 Tool 提供文件与任务摘要，中央区域留给当前工作。'],
      ['打开独立任务','每个 Document 有自己的输入和状态；打开第二个任务不会覆盖第一个任务。'],
      ['切换当前焦点','选择另一个文档标签，上下文仍然保留；无需重新建立全部输入。'],
      ['安排相关信息','把摘要面板移动到右侧，让它与当前内容形成直接对照。'],
      ['收起低频信息','隐藏暂时不用的面板，让中央任务获得更多空间。需要时可重新显示。']
    ]},
    architecture: {title:'从声明到释放的六个时刻',badge:'当前架构主链路',lead:'点击任意阶段，查看谁创建对象、谁持有对象、谁负责释放。',steps:[
      ['发现','Host 读取独立目录中的 plugin.manifest.json，确定唯一插件入口。','Host / Loader'],
      ['校验','核对身份、SDK 区间、入口和依赖；不符合协议的插件被隔离。','Host / Validator'],
      ['注册','插件配置私有 Provider；Host 校验并冻结贡献声明，最终提交注册。','Host / Registry'],
      ['激活','通过贡献激活入口创建模型、View 和 Document Scope，成功后发布到工作区。','Workspace / Activator'],
      ['关闭','先完成保存确认与在途调用协调，再移除对应工作会话。取消关闭时保留原状态。','Workspace / Coordinator'],
      ['释放','回收 View 与 Document Scope；退出时停止生命周期并释放插件 Provider。','各对象的明确所有者']
    ]},
    attention: {title:'看见选择的不确定性',badge:'数学定义＋工程模型',lead:'改变分布，而不仅仅是按钮数量。所有布局数值都是演示值。',steps:[
      ['等概率的 16 个选择','16 个候选等概率时，H = log₂16 = 4 bit。'],
      ['引入任务上下文','只比较当前上下文中的 4 个等概率候选时，H = 2 bit。它不表示时间减半。'],
      ['同样数量，不同偏好','候选数量相同，但概率更集中时，不确定性会降低。'],
      ['零概率也有明确含义','首项概率为零时，该项对熵的贡献为零，其余概率重新归一化。'],
      ['观察布局的另一面','缩短操作距离有收益；增加拥挤也有成本。两部分共同决定此示例的布局代价。']
    ]},
    activity: {title:'把一个需求拆成可管理的目标',badge:'项目方法论',lead:'示例：下载视频，查看下载进度，完成后播放。点击步骤检查分类理由。',steps:[
      ['配置一次下载','有独立目标、URL 和保存位置等交互状态，需要保留一次工作的上下文。','Document','实例状态 · 需要用户决策'],
      ['选择清晰度','需要用户输入，但它属于下载配置这一目标内部的一个步骤。','InternalStep','实例状态 · 从属于父目标'],
      ['查看全局队列','用户需要周期性感知多个任务的摘要并执行少量全局控制。','Tool','全局状态 · 感知与导航'],
      ['执行文件传输','执行本身不需要持续的人机交互，由后台服务处理。','Service','执行状态 · 自动完成']
    ]},
    production: {title:'规则如何转化为生产能力',badge:'研究假说 · 示例指数',lead:'先看每阶段需要的证据，再改变 P、N、A、H。指数不代表实测吞吐量。',steps:[
      ['领域模型','明确对象、状态、规则和不可逆的设计决策。'],
      ['可执行原型','把假设放进可运行系统，验证软件是否描述了正确的问题。'],
      ['现实反馈','收集真实任务中的问题和失败，不以生成代码数量代替学习。'],
      ['规则形式化','把有效边界写进 SDK、类型、约束检查和验收用例。'],
      ['平台固化','收敛兼容规则、生命周期和发布证据，确定允许的变化空间。'],
      ['受约束地扩展','在已明确的边界内并行演进，并持续检查架构漂移与契约冲突。']
    ]},
    forkable: {title:'让低频能力拥有下一次生命',badge:'概念框架与研究假说',lead:'每条路径都需要可检查的前提。点击节点查看需要保存或验证的材料。',steps:[
      ['维护','当前能力持续服务实际需求。保存代码、数据语义和可运行证据。','Host / 插件 / 验证'],
      ['归档','记录可重建环境、依赖版本、样例数据和来源许可。','环境 / 契约 / 许可'],
      ['休眠','暂时停止维护，但保留恢复路径。休眠不保证未来自动兼容。','档案完整性 / 已知限制'],
      ['恢复','旧环境仍可获得时，先重建和验证，再恢复使用。','构建记录 / 回归用例'],
      ['迁移','环境或契约发生变化时，转换实现与数据，并重新确认行为。','迁移规则 / 数据样例 / 验证'],
      ['分叉','在来源与许可允许的条件下，为独立需求建立自己的演进节奏。','来源 / 许可 / 新的需求与责任']
    ]}
  };

  function mount(element, kind) {
    const definition = definitions[kind];
    if (!definition) return {dispose(){},suspend(){}};
    const abort = new AbortController();
    let step = 0, timer = null, disposed = false, visible = true;
    const state = {n:16,preference:1,distance:6,clutter:4,sensitivity:1,productivity:2,count:4,acceptance:.8,attention:2};
    element.innerHTML = `<section class="demo" data-demo="${kind}"><div class="demo-heading"><h2>${definition.title}</h2><span class="evidence-badge">${definition.badge}</span></div><p class="demo-lead">${definition.lead}</p><div class="stage-visual"></div><div class="stage-note" role="status" aria-live="polite"></div><div class="demo-controls"><button data-action="play" class="primary">播放</button><button data-action="previous">上一步</button><button data-action="next">下一步</button><button data-action="restart">重播</button><button data-action="reset">重置</button><span class="demo-counter"></span></div></section>`;
    const wrapper = element.firstElementChild;
    const visual = wrapper.querySelector('.stage-visual');
    const note = wrapper.querySelector('.stage-note');
    const play = wrapper.querySelector('[data-action=play]');
    const control = name => wrapper.querySelector(`[data-action=${name}]`);
    function pause() { if (timer !== null) clearInterval(timer); timer = null; play.textContent = '播放'; wrapper.classList.remove('playing'); }
    function select(index) {
      step = Math.max(0,Math.min(definition.steps.length-1,index));
      if (kind === 'attention') {
        const preset = [[16,1],[4,1],[4,8],[4,0],[8,1]][step];
        state.n=preset[0];state.preference=preset[1];
      }
      render();
    }
    function begin() {
      if (!visible || disposed) return;
      if (step === definition.steps.length-1) select(0);
      play.textContent='暂停';wrapper.classList.add('playing');
      timer=setInterval(()=>{if(step>=definition.steps.length-1){pause();return;}select(step+1);},2600);
    }
    function field(key,label,min,max,increment) {
      return `<div class="slider-field"><label for="${kind}-${key}">${label}<output data-output="${key}">${state[key]}</output></label><input id="${kind}-${key}" data-param="${key}" type="range" min="${min}" max="${max}" step="${increment}" value="${state[key]}"></div>`;
    }
    function flow() {
      return `<div class="flow-nodes">${definition.steps.map((s,i)=>`<button class="flow-node ${i===step?'active':i<step?'done':''}" data-step="${i}" aria-current="${i===step?'step':'false'}"><span>0${i+1}${i<definition.steps.length-1?' →':''}</span><b>${s[0]}</b>${kind==='architecture'?`<span>${s[2]}</span>`:''}</button>`).join('')}</div>`;
    }
    function render() {
      const current=definition.steps[step];
      note.innerHTML=`<strong>${String(step+1).padStart(2,'0')} / ${current[0]}</strong>${current[1]}`;
      wrapper.querySelector('.demo-counter').textContent=`${step+1} / ${definition.steps.length}`;
      control('previous').disabled=step===0;control('next').disabled=step===definition.steps.length-1;
      if(kind==='workspace'){
        visual.innerHTML=`<div class="workspace ${step===3?'right':''} ${step===4?'hidden':''}"><aside class="mock-tool ${step===0?'focus-ring':''}"><b>工具摘要</b><span>文件系统</span><span>任务队列 · 2</span><span>插件状态 · 就绪</span></aside><div class="mock-document focus-ring"><div class="mock-tabs"><div class="mock-tab ${step!==2?'selected':''}">任务 A</div>${step>=1?`<div class="mock-tab ${step===2?'selected':''}">任务 B</div>`:''}</div><div class="mock-body"><h3>${step===2?'任务 B · 独立输入':'任务 A · 保留上下文'}</h3><div class="mock-line"></div><div class="mock-line short"></div><div class="mock-meter"><span style="width:${step===2?35:70}%"></span></div></div></div></div><div class="demo-controls"><button data-step="1">打开示例任务</button><button data-step="2">切换任务</button><button data-step="3">移到右侧</button><button data-step="${step===4?0:4}">${step===4?'显示工具':'隐藏工具'}</button></div>`;
      }else if(kind==='attention'){
        visual.innerHTML=`<div class="sliders">${field('n','候选数量 n',1,32,1)}${field('preference','首项相对权重',0,16,.5)}</div><svg class="prob-chart" viewBox="0 0 560 160" role="img" aria-label="候选概率分布"></svg><div class="metric-row"><div class="metric"><label>选择不确定性 H</label><strong data-metric="entropy"></strong><small> bit</small></div><div class="metric"><label>概率总和</label><strong data-metric="sum"></strong></div></div><div class="formula-live" data-live="entropy"></div><h3 class="demo-subtitle">布局代价：关系距离与拥挤共同作用</h3><svg class="layout-visual" viewBox="0 0 560 135" role="img" aria-label="上下文操作距离示例"></svg><div class="sliders">${field('distance','操作距离 d',1,10,.5)}${field('clutter','拥挤代价 C',0,10,.5)}${field('sensitivity','拥挤敏感度 λ',0,5,.5)}</div><div class="formula-live" data-live="layout"></div>`;
        updateNumbers();
      }else if(kind==='activity'){
        visual.innerHTML=`${flow()}<div class="classification" style="margin-top:18px"><div><small>检查交互与所有权</small>${current[3]}<p>目标：${current[0]}</p></div><div class="result"><small>此示例的分类结果</small><strong>Φ(g) = ${current[2]}</strong><p>${step===1?'归入父 Document':step===0?'拥有独立工作会话':step===2?'提供跨任务摘要':'执行具体业务动作'}</p></div></div>`;
      }else if(kind==='production'){
        visual.innerHTML=`${flow()}<h3 class="demo-subtitle">生产能力关系式 · 无量纲示例</h3><div class="sliders">${field('productivity','单体相对能力 P',.5,5,.5)}${field('count','安全并行数量 N',1,16,1)}${field('acceptance','自动验收比例 A',0,1,.05)}${field('attention','人类注意力需求 H',.1,5,.1)}</div><div class="metric-row"><div class="metric"><label>示例生产能力指数 Q</label><strong data-metric="throughput"></strong></div></div><div class="formula-live" data-live="throughput"></div>`;
        updateNumbers();
      }else{
        visual.innerHTML=flow()+(kind==='forkable'?`<h3 class="demo-subtitle">这个阶段需要保留或验证的材料</h3><div class="material-list">${current[2].split(' / ').map(t=>`<span>${esc(t)}</span>`).join('')}</div>`:'');
      }
    }
    function updateNumbers(){
      visual.querySelectorAll('[data-output]').forEach(o=>o.value=String(Number(state[o.dataset.output].toFixed(2))));
      if(kind==='attention'){
        const probabilities=M.probabilities(state.n,state.preference), h=M.entropy(probabilities);
        const barWidth=520/probabilities.length;
        visual.querySelector('.prob-chart').innerHTML=probabilities.map((p,i)=>`<rect x="${20+i*barWidth+2}" y="${132-p*112}" width="${Math.max(1,barWidth-4)}" height="${p*112}" rx="3"><title>候选 ${i+1}：${(p*100).toFixed(2)}%</title></rect>${probabilities.length<=16?`<text x="${20+(i+.5)*barWidth}" y="154" text-anchor="middle">${i+1}</text>`:''}`).join('');
        visual.querySelector('[data-metric=entropy]').textContent=h.toFixed(3);
        visual.querySelector('[data-metric=sum]').textContent=probabilities.reduce((a,b)=>a+b,0).toFixed(3);
        visual.querySelector('[data-live=entropy]').textContent=`H(A) = −Σ pᵢ log₂ pᵢ = ${h.toFixed(3)} bit`;
        const model=M.layout(state.distance,state.clutter,state.sensitivity),x=130+state.distance*25;
        visual.querySelector('.layout-visual').innerHTML=`<line x1="110" y1="55" x2="${x}" y2="55"/><rect x="20" y="26" width="90" height="58"/><text x="65" y="61" text-anchor="middle">文档 A</text><rect x="${x}" y="26" width="90" height="58"/><text x="${x+45}" y="61" text-anchor="middle">工具 B</text><text x="280" y="120" text-anchor="middle">示例关联权重 w = 2 · 距离 d = ${state.distance}</text>`;
        visual.querySelector('[data-live=layout]').textContent=`L = 2 × ${state.distance} + ${state.sensitivity} × ${state.clutter} = ${model.total.toFixed(2)}（关系 ${model.relation.toFixed(2)} + 拥挤 ${model.noise.toFixed(2)}）`;
      }else if(kind==='production'){
        const q=M.throughput(state.productivity,state.count,state.acceptance,state.attention);
        visual.querySelector('[data-metric=throughput]').textContent=q.toFixed(2);
        visual.querySelector('[data-live=throughput]').textContent=`Q ≈ (${state.productivity} × ${state.count} × ${state.acceptance.toFixed(2)}) ÷ ${state.attention.toFixed(1)} = ${q.toFixed(2)}`;
      }
    }
    wrapper.addEventListener('click',event=>{
      const target=event.target.closest('button');if(!target)return;
      if(target.dataset.step!==undefined){pause();select(Number(target.dataset.step));return;}
      switch(target.dataset.action){
        case 'play':if(timer===null)begin();else pause();break;
        case 'previous':pause();select(step-1);break;
        case 'next':pause();select(step+1);break;
        case 'restart':pause();select(0);begin();break;
        case 'reset':pause();Object.assign(state,{n:16,preference:1,distance:6,clutter:4,sensitivity:1,productivity:2,count:4,acceptance:.8,attention:2});select(0);break;
      }
    },{signal:abort.signal});
    wrapper.addEventListener('input',event=>{const key=event.target.dataset.param;if(!key)return;pause();state[key]=Number(event.target.value);updateNumbers();},{signal:abort.signal});
    const observer=new IntersectionObserver(entries=>{visible=entries[0].isIntersecting;if(!visible)pause();},{threshold:.1});observer.observe(wrapper);
    render();
    return {suspend(value){if(value)pause();},dispose(){if(disposed)return;disposed=true;pause();observer.disconnect();abort.abort();element.replaceChildren();},get playing(){return timer!==null;}};
  }
  root.HelpDemos=Object.freeze({mount});
})(globalThis);
