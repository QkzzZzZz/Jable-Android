// Jable 安卓播放器：和 Windows 版 player.js 同一套宿主接口（window.JL.*，postMessage 回报），界面改成触屏操作。
// 单击显示/隐藏控制条，双击左右半边快退/快进 10 秒，在画面上左右拖动调进度。
(() => {
  if (window.JL) return;

  const post = (msg) => { try { window.chrome.webview.postMessage(msg); } catch (e) { } };
  const fmt = (s) => {
    if (!isFinite(s) || s < 0) s = 0;
    s = Math.floor(s);
    const h = Math.floor(s / 3600), m = Math.floor(s / 60) % 60, sec = s % 60;
    const mm = String(m).padStart(2, '0'), ss = String(sec).padStart(2, '0');
    return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
  };
  const fmtOffset = (v) => (v >= 0 ? '+' : '−') + Math.abs(v).toFixed(1) + ' 秒';

  const svg = (d) => `<svg viewBox="0 0 24 24" width="24" height="24" fill="currentColor">${d}</svg>`;
  const ICON = {
    play: svg('<path d="M8 5.14v13.72a1 1 0 0 0 1.52.85l10.6-6.86a1 1 0 0 0 0-1.7L9.52 4.29A1 1 0 0 0 8 5.14z"/>'),
    pause: svg('<rect x="6" y="4.5" width="4" height="15" rx="1.2"/><rect x="14" y="4.5" width="4" height="15" rx="1.2"/>'),
    back10: svg('<path d="M12 5V2L7 6l5 4V7a6 6 0 1 1-6 6H4a8 8 0 1 0 8-8z"/><text x="12" y="16.2" font-size="6.5" text-anchor="middle" font-family="sans-serif" font-weight="700">10</text>'),
    fwd10: svg('<path d="M12 5V2l5 4-5 4V7a6 6 0 1 0 6 6h2a8 8 0 1 1-8-8z"/><text x="12" y="16.2" font-size="6.5" text-anchor="middle" font-family="sans-serif" font-weight="700">10</text>'),
    subs: svg('<path d="M4 5h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2zm1 9v2h6v-2H5zm8 0v2h6v-2h-6zm-8-3v2h3v-2H5zm5 0v2h9v-2h-9z"/>'),
    dl: svg('<path d="M11 4h2v8.2l3.3-3.3 1.4 1.4-5.7 5.7-5.7-5.7 1.4-1.4 3.3 3.3V4zM5 18h14v2H5z"/>'),
    close: svg('<path d="M15.4 4.6 8 12l7.4 7.4-1.4 1.4L5.2 12 14 3.2z"/>'),
  };

  const CSS = `
  html, body { margin:0; padding:0; background:#000; height:100%; overflow:hidden; touch-action:none; }
  #jl { position:fixed; inset:0; background:#000; font-family:sans-serif; color:#fff; user-select:none; -webkit-user-select:none; -webkit-tap-highlight-color:transparent; }
  #jl video { position:absolute; inset:0; width:100%; height:100%; object-fit:contain; background:#000; }
  #jl .sub { position:absolute; left:5%; right:5%; bottom:10%; text-align:center; pointer-events:none; transition:bottom .2s; }
  #jl.on .sub { bottom:calc(10% + 64px); }
  #jl .sub span { display:inline; background:rgba(0,0,0,.55); padding:2px 10px; border-radius:4px; line-height:1.6; font-size:clamp(14px, 4.2vh, 30px);
                  white-space:pre-line; text-shadow:0 0 3px #000; box-decoration-break:clone; -webkit-box-decoration-break:clone; }
  #jl .toast { position:absolute; top:16%; left:50%; transform:translateX(-50%); background:rgba(0,0,0,.72); padding:9px 18px; border-radius:20px; font-size:15px; opacity:0; transition:opacity .2s; pointer-events:none; white-space:nowrap; }
  #jl .toast.show { opacity:1; }
  #jl .top { position:absolute; top:0; left:0; right:0; display:flex; align-items:center; gap:6px; padding:10px 16px 28px; background:linear-gradient(rgba(0,0,0,.75),transparent); }
  #jl .top .title { font-size:15px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; flex:1; }
  #jl .bar { position:absolute; left:0; right:0; bottom:0; padding:30px 18px 12px; background:linear-gradient(transparent,rgba(0,0,0,.82)); }
  #jl .top, #jl .bar, #jl .mid { opacity:0; pointer-events:none; transition:opacity .22s; }
  #jl.on .top, #jl.on .bar, #jl.on .mid { opacity:1; pointer-events:auto; }
  #jl .seek { position:relative; height:28px; margin:0 6px; }
  #jl .seek .track, #jl .seek .buf, #jl .seek .done { position:absolute; left:0; top:12px; height:4px; border-radius:2px; }
  #jl .seek .track { right:0; background:rgba(255,255,255,.25); }
  #jl .seek .buf { background:rgba(255,255,255,.45); }
  #jl .seek .done { background:#e50914; }
  #jl .seek .knob { position:absolute; top:6px; width:16px; height:16px; margin-left:-8px; border-radius:50%; background:#e50914; transition:transform .15s; }
  #jl .seek.drag .knob { transform:scale(1.35); }
  #jl .row { display:flex; align-items:center; gap:4px; }
  #jl button { background:none; border:0; color:#fff; font:inherit; font-size:14px; padding:8px 10px; border-radius:18px; white-space:nowrap; display:inline-flex; align-items:center; gap:4px; }
  #jl button:active { background:rgba(255,255,255,.18); }
  #jl button svg { width:24px; height:24px; }
  #jl .time { font-size:13px; opacity:.92; padding:0 6px; font-variant-numeric:tabular-nums; }
  #jl .grow { flex:1; }
  #jl .mid { position:absolute; top:50%; left:50%; transform:translate(-50%,-50%); display:flex; align-items:center; gap:46px; }
  #jl .mid button { width:64px; height:64px; justify-content:center; border-radius:50%; background:rgba(0,0,0,.4); }
  #jl .mid button.big { width:80px; height:80px; }
  #jl .mid button svg { width:34px; height:34px; }
  #jl .mid button.big svg { width:44px; height:44px; }
  #jl.loading .mid .big { visibility:hidden; }
  #jl .spinner { position:absolute; top:50%; left:50%; width:54px; height:54px; margin:-27px 0 0 -27px; border-radius:50%; border:4px solid rgba(255,255,255,.2); border-top-color:#e50914;
                 animation:jlspin .8s linear infinite; display:none; pointer-events:none; }
  #jl.loading .spinner { display:block; }
  @keyframes jlspin { to { transform:rotate(360deg); } }
  #jl .sheet { position:absolute; right:12px; bottom:76px; background:rgba(24,24,26,.96); border-radius:14px; padding:6px; display:none; min-width:190px; max-height:62vh; overflow:auto;
               transform-origin:bottom right; }
  #jl .sheet.open { display:block; animation:jlpop .38s cubic-bezier(.2,1.4,.4,1); }
  @keyframes jlpop { from { transform:scale(.9); opacity:0; } to { transform:scale(1); opacity:1; } }
  #jl .sheet .item { padding:11px 14px; border-radius:10px; font-size:15px; white-space:nowrap; }
  #jl .sheet .item:active { background:rgba(255,255,255,.12); }
  #jl .sheet .item.sel { color:#ff4d55; }
  #jl .sheet .sep { height:1px; background:rgba(255,255,255,.15); margin:5px 0; }
  #jl .sheet .hint { font-size:12.5px; opacity:.6; padding:6px 14px; white-space:normal; max-width:280px; }
  #jl .sheet .btns { display:flex; gap:6px; padding:4px 8px; flex-wrap:wrap; }
  #jl .sheet .btns button { background:rgba(255,255,255,.12); font-size:13px; padding:7px 10px; }
  `;

  const state = { video: null, hls: null, cues: [], offset: 0, subOn: true, tracks: null, track: null, rate: 1, lastPost: 0, hideTimer: 0, root: null, els: {}, toastTimer: 0 };

  function el(tag, cls, html) { const e = document.createElement(tag); if (cls) e.className = cls; if (html != null) e.innerHTML = html; return e; }

  function toast(text) {
    const t = state.els.toast; t.textContent = text; t.classList.add('show');
    clearTimeout(state.toastTimer); state.toastTimer = setTimeout(() => t.classList.remove('show'), 1400);
  }

  function showControls(on = true) {
    const r = state.root; r.classList.toggle('on', on);
    clearTimeout(state.hideTimer);
    if (on) state.hideTimer = setTimeout(() => { if (!state.video.paused && !r.querySelector('.sheet.open')) r.classList.remove('on'); }, 3200);
  }

  function silenceSite() {
    try { window.stop(); } catch (e) { }
    try { if (window.hls && window.hls.destroy) window.hls.destroy(); } catch (e) { }
    try { if (window.player && window.player.destroy) window.player.destroy(); } catch (e) { }
    document.querySelectorAll('video, audio').forEach(v => { try { v.pause(); v.removeAttribute('src'); v.load(); } catch (e) { } });
    const maxId = setTimeout(() => { }, 0);
    for (let i = 0; i <= maxId; i++) { clearTimeout(i); clearInterval(i); }
    window.open = () => null;
  }

  function build(opts) {
    document.head.innerHTML = '<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,user-scalable=no">';
    document.body.innerHTML = '';
    document.body.removeAttribute('class');
    document.body.removeAttribute('style');
    const style = el('style'); style.textContent = CSS; document.head.appendChild(style);

    const r = el('div'); r.id = 'jl'; r.className = 'loading on';
    r.innerHTML = `
      <video playsinline webkit-playsinline></video>
      <div class="sub"><span></span></div>
      <div class="spinner"></div>
      <div class="toast"></div>
      <div class="top"><button data-a="close">${ICON.close}</button><span class="title"></span></div>
      <div class="mid">
        <button data-a="back">${ICON.back10}</button>
        <button data-a="play" class="big">${ICON.play}</button>
        <button data-a="fwd">${ICON.fwd10}</button>
      </div>
      <div class="bar">
        <div class="seek"><div class="track"></div><div class="buf"></div><div class="done"></div><div class="knob"></div></div>
        <div class="row">
          <span class="time">00:00 / 00:00</span>
          <span class="grow"></span>
          <button data-a="rate">1.0×</button>
          <button data-a="subs">${ICON.subs}</button>
          <button data-a="dl">${ICON.dl}</button>
        </div>
      </div>
      <div class="sheet rate-sheet"></div>
      <div class="sheet subs-sheet"></div>`;
    document.body.appendChild(r);
    state.root = r;
    const q = (s) => r.querySelector(s);
    state.els = {
      video: q('video'), sub: q('.sub span'), toast: q('.toast'), title: q('.title'), time: q('.time'),
      seek: q('.seek'), buf: q('.buf'), done: q('.done'), knob: q('.knob'),
      play: q('[data-a=play]'), rateBtn: q('[data-a=rate]'), rateSheet: q('.rate-sheet'), subsSheet: q('.subs-sheet'),
    };
    state.els.title.textContent = opts.title || '';
    state.video = state.els.video;
    wire();
  }

  function wire() {
    const v = state.video, r = state.root, E = state.els;

    // 画面上的手势：单击切换控制条，双击左右快退/快进，水平拖动调进度
    let lastTap = 0, tapTimer = 0, startX = 0, startY = 0, startT = 0, dragging = false, moved = false;
    r.addEventListener('touchstart', (e) => {
      if (e.target.closest('.bar, .top, .sheet, .mid button')) return;
      const t = e.touches[0]; startX = t.clientX; startY = t.clientY; startT = v.currentTime; dragging = true; moved = false;
    }, { passive: true });
    r.addEventListener('touchmove', (e) => {
      if (!dragging || !v.duration) return;
      const t = e.touches[0], dx = t.clientX - startX;
      if (!moved && Math.abs(dx) < 18) return;
      if (!moved && Math.abs(t.clientY - startY) > Math.abs(dx)) { dragging = false; return; }
      moved = true;
      const target = Math.max(0, Math.min(v.duration - 1, startT + dx / window.innerWidth * Math.min(v.duration, 600)));
      toast((dx >= 0 ? '⏩ ' : '⏪ ') + fmt(target) + ' / ' + fmt(v.duration));
      state.pendingSeek = target;
    }, { passive: true });
    r.addEventListener('touchend', (e) => {
      if (!dragging) return;
      dragging = false;
      if (moved) { seekTo(state.pendingSeek); showControls(true); return; }
      const now = Date.now(), x = (e.changedTouches[0] || {}).clientX / window.innerWidth;
      if (now - lastTap < 280) {
        clearTimeout(tapTimer); lastTap = 0;
        if (x < 0.4) seekBy(-10); else if (x > 0.6) seekBy(10); else togglePlay();
        return;
      }
      lastTap = now;
      tapTimer = setTimeout(() => { closeSheets(); showControls(!r.classList.contains('on')); }, 280);
    });

    r.addEventListener('click', (e) => {
      const b = e.target.closest('button'); if (!b || !b.dataset.a) return;
      e.stopPropagation();
      showControls(true);
      switch (b.dataset.a) {
        case 'play': togglePlay(); break;
        case 'back': seekBy(-10); break;
        case 'fwd': seekBy(10); break;
        case 'rate': toggleSheet(E.rateSheet, renderRateSheet); break;
        case 'subs': toggleSheet(E.subsSheet, renderSubsSheet); break;
        case 'dl': post({ type: 'download' }); break;
        case 'close': post({ type: 'close' }); break;
      }
    });

    // 进度条：按住拖动
    const ratio = (x) => { const b = E.seek.getBoundingClientRect(); return Math.min(1, Math.max(0, (x - b.left) / b.width)); };
    E.seek.addEventListener('touchstart', (e) => { E.seek.classList.add('drag'); seekTo(ratio(e.touches[0].clientX) * v.duration); showControls(true); }, { passive: true });
    E.seek.addEventListener('touchmove', (e) => { seekTo(ratio(e.touches[0].clientX) * v.duration); showControls(true); }, { passive: true });
    E.seek.addEventListener('touchend', () => E.seek.classList.remove('drag'));

    v.addEventListener('play', () => { E.play.innerHTML = ICON.pause; showControls(true); });
    v.addEventListener('pause', () => { E.play.innerHTML = ICON.play; showControls(true); reportProgress(true); });
    v.addEventListener('waiting', () => r.classList.add('loading'));
    v.addEventListener('playing', () => r.classList.remove('loading'));
    v.addEventListener('canplay', () => r.classList.remove('loading'));
    v.addEventListener('ended', () => { reportProgress(true); post({ type: 'ended' }); });
    v.addEventListener('timeupdate', () => { renderTime(); renderSub(); reportProgress(false); });
    v.addEventListener('progress', renderTime);
    v.addEventListener('ratechange', () => { state.rate = v.playbackRate; E.rateBtn.textContent = v.playbackRate.toFixed(v.playbackRate % 1 ? 2 : 1).replace(/0$/, '') + '×'; });
    document.addEventListener('visibilitychange', () => { if (document.hidden) reportProgress(true); });
  }

  function togglePlay() { const v = state.video; if (v.paused) v.play().catch(() => { }); else v.pause(); }
  function seekTo(t) { const v = state.video; if (!isFinite(t)) return; v.currentTime = Math.max(0, Math.min(t, (v.duration || t) - 0.5)); renderTime(); renderSub(); }
  function seekBy(d) { seekTo(state.video.currentTime + d); toast((d > 0 ? '快进 ' : '快退 ') + Math.abs(d) + ' 秒'); }
  function setRate(x) { state.video.playbackRate = x; toast('倍速 ' + x + '×'); post({ type: 'rate', value: x }); }

  function renderTime() {
    const v = state.video, E = state.els, d = v.duration || 0, t = v.currentTime || 0;
    E.time.textContent = fmt(t) + ' / ' + fmt(d);
    const p = d ? t / d * 100 : 0;
    E.done.style.width = p + '%'; E.knob.style.left = p + '%';
    try { if (v.buffered.length) E.buf.style.width = (v.buffered.end(v.buffered.length - 1) / d * 100) + '%'; } catch (e) { }
  }

  function reportProgress(force) {
    const v = state.video; if (!v || !v.duration) return;
    const now = Date.now();
    if (!force && now - state.lastPost < 5000) return;
    state.lastPost = now;
    post({ type: 'progress', position: v.currentTime, duration: v.duration });
  }

  // ---------- 字幕 ----------
  function cueAt(t) {
    const c = state.cues; let lo = 0, hi = c.length - 1, found = -1;
    while (lo <= hi) { const mid = (lo + hi) >> 1; if (c[mid].s <= t) { found = mid; lo = mid + 1; } else hi = mid - 1; }
    for (let i = found; i >= 0 && i > found - 5; i--) if (c[i].s <= t && t < c[i].e) return c[i];
    return null;
  }
  function renderSub() {
    const span = state.els.sub;
    if (!state.subOn || !state.cues.length) { span.textContent = ''; span.style.display = 'none'; return; }
    const cue = cueAt(state.video.currentTime - state.offset);
    span.textContent = cue ? cue.t : '';
    span.style.display = cue ? 'inline' : 'none';
  }
  function setOffset(v, silent) {
    state.offset = Math.round(v * 10) / 10;
    renderSub();
    if (!silent) { toast('字幕偏移 ' + fmtOffset(state.offset)); post({ type: 'offset', value: state.offset }); }
    if (state.els.subsSheet.classList.contains('open')) renderSubsSheet();
  }

  // ---------- 弹出面板 ----------
  function closeSheets() { state.root.querySelectorAll('.sheet.open').forEach(m => m.classList.remove('open')); }
  function toggleSheet(m, render) { const open = m.classList.contains('open'); closeSheets(); if (!open) { render(); m.classList.add('open'); } }

  function renderRateSheet() {
    const m = state.els.rateSheet; m.innerHTML = '';
    [0.5, 0.75, 1, 1.25, 1.5, 2, 2.5, 3].forEach(x => {
      const it = el('div', 'item' + (Math.abs(x - state.rate) < 0.01 ? ' sel' : ''), x + '×');
      it.onclick = (e) => { e.stopPropagation(); setRate(x); closeSheets(); };
      m.appendChild(it);
    });
  }

  function renderSubsSheet() {
    const m = state.els.subsSheet; m.innerHTML = '';
    const add = (cls, html, fn) => { const it = el('div', cls, html); if (fn) it.onclick = (e) => { e.stopPropagation(); fn(); }; m.appendChild(it); return it; };
    add('item' + (state.subOn ? ' sel' : ''), state.subOn ? '✓ 显示字幕' : '　显示字幕', () => {
      state.subOn = !state.subOn; renderSub(); renderSubsSheet(); post({ type: 'subsEnabled', value: state.subOn });
    });
    add('sep');
    if (state.tracks === null) add('hint', '正在搜索字幕…');
    else if (!state.tracks.length) add('hint', '没有字幕');
    else state.tracks.forEach(t => add('item' + (state.track === t.id ? ' sel' : ''), t.name, () => { state.track = t.id; post({ type: 'selectTrack', id: t.id }); renderSubsSheet(); }));
    if (state.cues.length) {
      add('sep');
      add('hint', '偏移 ' + fmtOffset(state.offset));
      const btns = add('btns');
      [['−10', -10], ['−1', -1], ['−0.5', -0.5], ['+0.5', 0.5], ['+1', 1], ['+10', 10]].forEach(([l, d]) => {
        const b = el('button', null, l); b.onclick = (e) => { e.stopPropagation(); setOffset(state.offset + d); }; btns.appendChild(b);
      });
      add('item', '⏱ 从这里开始第一句', () => setOffset(state.video.currentTime - state.cues[0].s));
      add('item', '↺ 偏移归零', () => setOffset(0));
    }
  }

  // ---------- 宿主接口 ----------
  function attach(url, startAt) {
    const v = state.video;
    const onReady = () => {
      if (startAt > 5 && (!v.duration || startAt < v.duration - 10)) { v.currentTime = startAt; toast('从 ' + fmt(startAt) + ' 继续播放'); }
      v.play().catch(() => { state.root.classList.remove('loading'); });
    };
    if (window.JLHls && window.JLHls.isSupported()) {
      const hls = new window.JLHls({ maxBufferLength: 45, startLevel: -1, enableWorker: true });
      state.hls = hls;
      hls.on(window.JLHls.Events.MANIFEST_PARSED, onReady);
      hls.on(window.JLHls.Events.ERROR, (_, d) => {
        if (!d.fatal) return;
        if (d.type === window.JLHls.ErrorTypes.NETWORK_ERROR) { toast('网络错误，重试中…'); hls.startLoad(); }
        else if (d.type === window.JLHls.ErrorTypes.MEDIA_ERROR) { hls.recoverMediaError(); }
        else post({ type: 'error', message: '播放失败：' + (d.details || d.type) });
      });
      hls.loadSource(url);
      hls.attachMedia(v);
    } else {
      v.src = url;
      v.addEventListener('loadedmetadata', onReady, { once: true });
    }
  }

  window.JL = {
    start(opts) {
      if (!opts.synthetic) silenceSite();
      build(opts);
      state.rate = opts.rate || 1;
      state.subOn = opts.subtitlesEnabled !== false;
      state.offset = opts.offset || 0;
      state.video.playbackRate = state.rate;
      attach(opts.url, opts.startAt || 0);
      showControls(true);
      post({ type: 'started' });
    },
    setTracks(tracks, selected) { state.tracks = tracks || []; state.track = selected; if (state.els.subsSheet.classList.contains('open')) renderSubsSheet(); },
    setCues(cues, offset) {
      state.cues = (cues || []).slice().sort((a, b) => a.s - b.s);
      if (typeof offset === 'number') setOffset(offset, true);
      renderSub();
      if (state.cues.length) toast('字幕已加载（' + state.cues.length + ' 句）');
      if (state.els.subsSheet.classList.contains('open')) renderSubsSheet();
    },
    toast,
    position() { return state.video ? state.video.currentTime : 0; },
    duration() { return state.video ? state.video.duration || 0 : 0; },
    pause() { if (state.video) state.video.pause(); },
  };
})();
