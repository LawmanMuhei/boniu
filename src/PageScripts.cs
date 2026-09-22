namespace MiniView.WebView2App
{
    internal static class PageScripts
    {
        internal static string BuildVisibility(AppSettings settings, long revision)
        {
            bool hideLeft = settings.HideLeftNav;
            bool hideTop = settings.HideTopBar;
            bool hideRight = settings.HideRightBar;
            bool immersive = settings.ImmersiveMode;
            return @"(function(){
  var revision=" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture) + @";
  var S='__boniuPageStyle';
  if(window.__boniuStyleRevision>revision)return;
  window.__boniuStyleRevision=revision;
  var st=document.getElementById(S);
  if(!st){st=document.createElement('style');st.id=S;document.head.appendChild(st);}
  var css='';
  // 始终隐藏左右切换箭头（无需开关）
  css+='div[aria-label=""上一条""],div[aria-label=""下一条""],';
  css+='[class*=""switch-btn""],[class*=""switchBtn""],';
  css+='[class*=""arrow-left""],[class*=""arrow-right""],';
  css+='[class*=""slideArrow""],[class*=""slide-arrow""]';
  css+='{display:none!important;}';
  // 隐藏左侧导航栏
  if(" + (hideLeft ? "true" : "false") + @"){
    css+='div[data-e2e=""douyin-navigation""],';
    css+='[class*=""SideBar""],[class*=""side-bar""],';
    css+='[class*=""Navigation""],';
    css+='div[class*=""leftSidebar""],div[class*=""left-sidebar""],';
    css+='[data-e2e=""left-sidebar""]';
    css+='{display:none!important;}';
    // 收起左侧后让内容区占满
    css+='[class*=""MainContent""],[class*=""main-content""],';
    css+='div[class*=""ContentLayout""]';
    css+='{margin-left:0!important;padding-left:0!important;width:100%!important;max-width:100%!important;}';
  }
  // 隐藏顶部搜索栏区域
  if(" + (hideTop ? "true" : "false") + @"){
    css+='div[data-e2e=""searchbar""],';
    css+='[class*=""Header""]:not([class*=""HeaderLayout""]),';
    css+='header,[class*=""header""]:not([class*=""player""]):not([class*=""Player""]),';
    css+='[class*=""TopBar""],[class*=""top-bar""],';
    css+='[class*=""SearchBar""],[class*=""search-bar""],';
    css+='[data-e2e=""top-banner""],div[class*=""topBar""]';
    css+='{display:none!important;}';
    // 收起顶部后让内容区上移
    css+='[class*=""MainContent""],[class*=""main-content""],';
    css+='div[class*=""ContentLayout""],';
    css+='div[class*=""FeedContainer""],[class*=""feed-container""]';
    css+='{margin-top:0!important;padding-top:0!important;height:100%!important;}';
  }
  // 隐藏右侧互动区
  if(" + (hideRight ? "true" : "false") + @"){
    css+='[data-e2e=""video-sidebar""],';
    css+='[class*=""SideToolbar""],[class*=""side-toolbar""],';
    css+='[class*=""ActionBar""],[class*=""action-bar""],';
    css+='[class*=""InteractBar""],[class*=""interact-bar""],';
    css+='[class*=""right-bar""],[class*=""RightBar""],';
    css+='[class*=""video-sidebar""],';
    css+='div[class*=""SideToolBar""]';
    css+='{display:none!important;}';
  }
  // 清爽模式只隐藏页面装饰，不接管抖音的播放器、视频素材或虚拟列表布局。
  // 播放器外框比例、内部视频适配和上下切换全部沿用抖音原生实现。
  // 抖音的 CSS-in-JS 在运行时注入到 head 末尾，同特异性的 !important 会靠源码顺序压过我们，
  // 所以这里每条规则都用根节点 #dark 把特异性抬到 id 级。
  if(" + (immersive ? "true" : "false") + @"){
    css+='#dark #douyin-header,#dark header,';
    css+='#dark #douyin-navigation,#dark [data-e2e=""douyin-navigation""],';
    css+='#dark [data-e2e=""searchbar-input""],#dark [data-e2e=""searchbar-button""],';
    css+='#dark [data-e2e=""im-entry""],#dark [data-e2e=""something-button""],';
    css+='#dark [class*=""danmaku""],';
    css+='#dark [data-e2e=""video-info""]';
    css+='{display:none!important;}';
    // 移除顶部栏占位和互动栏额外留白，但保留互动按钮本身；按钮叠放在铺满宽度的视频区内。
    // 不修改播放器、slide 或 video，比例和虚拟列表切换仍由抖音负责。
    css+='#dark #douyin-right-container{padding-top:0!important;}';
    css+='#dark [data-e2e=""slideList""]{padding-right:0!important;height:100%!important;}';
  }
  st.textContent=css;
  st.setAttribute('data-fallback','0');
  // 清理旧版本留下的强制 resize 对齐守卫；原生布局不需要持续干预。
  st.setAttribute('data-immersive'," + (immersive ? "'1'" : "'0'") + @");
  var G='__boniuAlignGuard';
  if(window[G]){clearInterval(window[G]);window[G]=0;}
  // 无侵入健康检查：只观察当前活动视频是否仍覆盖页面中心；不 resize、不改播放器定位。
  // 连续两次异常时清空注入样式，回退抖音原始布局，避免页面改版后黑屏。
  var H='__boniuImmersiveHealthGuard';
  if(window[H]){clearInterval(window[H]);window[H]=0;}
  if(" + (immersive ? "true" : "false") + @"){
    var misses=0;
    var coversCenter=function(el){
      if(!el)return false;
      var r=el.getBoundingClientRect(),cs=getComputedStyle(el);
      return cs.display!=='none'&&cs.visibility!=='hidden'&&r.width>innerWidth*.4&&r.height>innerHeight*.4&&
        r.left<=innerWidth/2&&r.right>=innerWidth/2&&r.top<=innerHeight/2&&r.bottom>=innerHeight/2;
    };
    window[H]=setInterval(function(){
      var s=document.getElementById(S);
      if(!s||s.getAttribute('data-immersive')!=='1'){clearInterval(window[H]);window[H]=0;return;}
      var active=document.querySelector('[data-e2e=""feed-active-video""]');
      var videos=active?active.querySelectorAll('video'):document.querySelectorAll('video');
      if(!videos.length)return;
      var ok=coversCenter(active);
      if(ok){var any=false;videos.forEach(function(v){if(coversCenter(v))any=true;});ok=any;}
      misses=ok?0:misses+1;
      if(misses<2)return;
      s.textContent='';s.setAttribute('data-fallback','1');
      clearInterval(window[H]);window[H]=0;
      try{window.chrome.webview.postMessage('boniu:immersive-fallback:'+revision);}catch(e){}
    },1500);
  }
})()";
        }

        internal const string OpenRecommendScript = @"(() => {
            const nav = document.querySelector('[data-e2e=""douyin-navigation""]');
            const root = nav || document;
            const link = root.querySelector('a[href*=""recommend""]');
            if (link) { link.click(); return 'link ' + link.getAttribute('href'); }
            const nodes = root.querySelectorAll('div,span,li,a,p');
            for (let i = 0; i < nodes.length; i++) {
              const el = nodes[i];
              if ((el.textContent || '').trim() !== '推荐') continue;
              const box = el.getBoundingClientRect();
              if (box.width <= 0 || box.height <= 0) continue;
              el.click();
              return 'text ' + el.tagName;
            }
            return 'none';
          })()";

        internal const string DomProbeScript = @"(() => {
            const pick = (el) => {
              if (!el) return null;
              const r = el.getBoundingClientRect();
              const cs = getComputedStyle(el);
              let cls = '';
              try { cls = String(el.className).slice(0, 90); } catch (e) { cls = ''; }
              return { tag: el.tagName, id: el.id || null, cls: cls,
                e2e: el.getAttribute ? el.getAttribute('data-e2e') : null,
                x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height),
                pos: cs.position, disp: cs.display, of: cs.objectFit,
                mr: cs.marginRight, pr: cs.paddingRight, mb: cs.marginBottom, pb: cs.paddingBottom, z: cs.zIndex,
                tf: cs.transform === 'none' ? '' : cs.transform.slice(0, 40),
                ml: cs.marginLeft, pl: cs.paddingLeft, cl: cs.left, ct: cs.top, ov: cs.overflow };
            };
            const out = {};
            out.viewport = { w: innerWidth, h: innerHeight, dpr: devicePixelRatio, url: location.href };
            const videos = Array.prototype.slice.call(document.querySelectorAll('video'));
            let video = null, best = 0;
            videos.forEach(v => {
              const r = v.getBoundingClientRect();
              if (r.width * r.height > best) { best = r.width * r.height; video = v; }
            });
            out.videoCount = videos.length;
            out.video = pick(video);
            out.intrinsic = video && video.videoWidth
              ? { w: video.videoWidth, h: video.videoHeight, ratio: video.videoWidth / video.videoHeight } : null;
            out.ancestors = [];
            let cur = video;
            for (let i = 0; i < 18 && cur; i++) { cur = cur.parentElement; if (cur) out.ancestors.push(pick(cur)); }
            out.byId = {};
            const darkRoot = document.getElementById('dark');
            out.darkChildren = darkRoot ? Array.prototype.map.call(darkRoot.children, pick) : [];
            const rightRoot = document.getElementById('douyin-right-container');
            out.rightTree = [];
            if (rightRoot) rightRoot.querySelectorAll('*').forEach(el => {
              const p = pick(el);
              if (!p || p.w <= 0 || p.h <= 0 || out.rightTree.length >= 120) return;
              if (p.y <= 60 || p.pr !== '0px' || p.mb !== '0px' || p.pb !== '0px') {
                const parent = el.parentElement;
                p.parent = parent ? ((parent.id ? '#' + parent.id : '') + '.' + String(parent.className || '').slice(0, 60)) : '';
                out.rightTree.push(p);
              }
            });
            ['HeaderLayout', 'ContainerBackgroundLayout', 'LeftBackgroundLayout', 'RightBackgroundLayout',
             'RightPanelLayout', 'PlayerLayout', 'BottomLayout', 'GiftMenuLayout', 'FeedItemLayout']
              .forEach(id => { out.byId[id] = pick(document.getElementById(id)); });
            out.e2e = [];
            document.querySelectorAll('[data-e2e]').forEach(el => {
              const p = pick(el);
              if (p && p.w > 0 && p.h > 0 && out.e2e.length < 45) out.e2e.push(p);
            });
            const at = (x, y) => document.elementsFromPoint(x, y).slice(0, 7).map(pick);
            out.rightEdge = at(innerWidth - 6, innerHeight * 0.5);
            out.midRight = at(innerWidth * 0.88, innerHeight * 0.5);
            out.bottomEdge = at(innerWidth * 0.5, innerHeight - 6);
            out.topEdge = at(innerWidth * 0.5, 6);
            out.center = at(innerWidth * 0.5, innerHeight * 0.45);
            return out;
          })()";

        internal const string LiveProbeScript = @"(() => {
            const live = Boolean(document.querySelector('#PlayerLayout > .__livingPlayer__, [data-e2e=""living-container""] #PlayerLayout'));
            const id = '__boniuLiveStyle';
            let style = document.getElementById(id);
            if (!live) { if (style) style.remove(); return false; }
            if (!style) {
                style = document.createElement('style'); style.id = id;
                style.textContent = `
                    #HeaderLayout,#GiftMenuLayout,#BottomLayout,#RightBackgroundLayout,#RightPanelLayout { display:none!important; }
                    #ContainerBackgroundLayout,#LeftBackgroundLayout,#PlayerLayout {
                        width:100%!important;height:100%!important;max-width:none!important;margin:0!important;
                    }
                    #PlayerLayout > .__livingPlayer__,#PlayerLayout > .__livingPlayer__ > [data-anchor-id=""living-basic-player""] {
                        width:100%!important;height:100%!important;max-width:none!important;padding-top:0!important;
                    }`;
                document.head.appendChild(style);
            }
            return true;
        })()";

    }
}
