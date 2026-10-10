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
  // 无侵入健康检查：只确认视口内仍有足够大的、正在渲染的 video；不 resize、不改播放器定位。
  // 判定不依赖 feed-active-video 标记，也不要求画面覆盖视口中心：推荐流是虚拟列表，滚动时
  // 该标记会被回收；打开评论面板时抖音会缩小播放器或让它让出空间，画面仍完整可用，
  // 只是不再居中，用中心点判定会把抖音自己的交互面板误判成布局损坏。
  var H='__boniuImmersiveHealthGuard';
  var HK='__boniuImmersiveHealthHooks';
  var healthEvents=['scroll','wheel','touchmove'];
  var stopHealthGuard=function(){
    if(window[H]){clearInterval(window[H]);window[H]=0;}
    if(window[HK]){window[HK].forEach(function(h){healthEvents.forEach(function(e){removeEventListener(e,h,true);});});window[HK]=0;}
  };
  stopHealthGuard();
  if(" + (immersive ? "true" : "false") + @"){
    var misses=0;
    // 健康标准是“可见画面占视口的比例”，不是“画面是否覆盖中心”。窗口默认只有 420x760，
    // 评论面板一展开播放器就会被推离中心甚至压窄，而真实损坏（被压成窄条、移出视口）比例接近 0。
    var MIN_VISIBLE_RATIO=0.25;
    var visibleRatio=function(el){
      if(!el)return 0;
      var cs=getComputedStyle(el);
      if(cs.display==='none'||cs.visibility==='hidden')return 0;
      var r=el.getBoundingClientRect();
      if(r.width<=0||r.height<=0)return 0;
      var left=Math.max(0,r.left),top=Math.max(0,r.top);
      var right=Math.min(innerWidth,r.right),bottom=Math.min(innerHeight,r.bottom);
      if(right<=left||bottom<=top)return 0;
      return ((right-left)*(bottom-top))/(innerWidth*innerHeight);
    };
    // 抖音自己的评论面板展开时会重排播放器，这是页面交互而不是注入样式损坏：命中即清零计数。
    // 面板本身必须是一块可见的大区域，避免把列表里的单个评论项当成面板。
    var overlaySelectors=['[data-e2e=""comment-list""]','[data-e2e=""comment-panel""]','[data-e2e=""video-comment""]',
      '[class*=""commentPanel""]','[class*=""comment-panel""]','[class*=""CommentPanel""]','[class*=""commentContainer""]'];
    var overlayOpen=function(){
      for(var i=0;i<overlaySelectors.length;i++){
        var nodes=document.querySelectorAll(overlaySelectors[i]);
        for(var j=0;j<nodes.length;j++){
          var cs=getComputedStyle(nodes[j]);
          if(cs.display==='none'||cs.visibility==='hidden'||cs.opacity==='0')continue;
          var r=nodes[j].getBoundingClientRect();
          if(r.width>innerWidth*.25&&r.height>innerHeight*.4)return true;
        }
      }
      return false;
    };
    var reset=function(){misses=0;};
    healthEvents.forEach(function(e){addEventListener(e,reset,true);});
    window[HK]=[reset];
    window[H]=setInterval(function(){
      var s=document.getElementById(S);
      if(!s||s.getAttribute('data-immersive')!=='1'){stopHealthGuard();return;}
      var videos=document.querySelectorAll('video');
      if(!videos.length)return;
      // 取所有正在渲染的 video 里最大的可见比例：推荐页主播放器、视频详情页播放器、
      // 从设置返回后的首帧都满足；只有画面确实被压坏时比例才会掉到阈值以下。
      var best=0,rendering=false;
      for(var i=0;i<videos.length;i++){
        var box=videos[i].getBoundingClientRect(),style=getComputedStyle(videos[i]);
        if(style.display==='none'||style.visibility==='hidden'||box.width<=0||box.height<=0)continue;
        rendering=true;
        var ratio=visibleRatio(videos[i]);
        if(ratio>best)best=ratio;
        if(best>=MIN_VISIBLE_RATIO)break;
      }
      if(!rendering)return;
      if(overlayOpen()){misses=0;return;}
      misses=best>=MIN_VISIBLE_RATIO?0:misses+1;
      // 连续 3 次（约 4.5 秒）异常才判定布局损坏，给滚动与视频切换留足过渡时间。
      if(misses<3)return;
      s.textContent='';s.setAttribute('data-fallback','1');
      stopHealthGuard();
      // 带上实测值：真实页面再出现误回退时，run.log 能直接说明当时的画面比例与视口尺寸。
      try{window.chrome.webview.postMessage('boniu:immersive-fallback:'+revision+'|best='+best.toFixed(2)
        +' vw='+innerWidth+' vh='+innerHeight+' videos='+videos.length);}catch(e){}
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
