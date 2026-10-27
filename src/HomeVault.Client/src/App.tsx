import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { ArrowDownToLine, ArrowRight, ArrowUpRight, Box, Camera, Check, CheckCircle2, ChevronDown, CircleDollarSign, FileText, FolderHeart, House, ImagePlus, LayoutGrid, List, LoaderCircle, LogOut, Menu, Package, Pencil, Plus, Search, ShieldCheck, Sofa, Sparkles, Tag, Trash2, X } from 'lucide-react';
import { api, ApiError } from './api';
import { currency, selectItems, summarizeItems, validatePhoto, type SortOrder } from './catalog';
import type { CatalogItem, ItemDraft, Resident, SignupData } from './types';

const errorText = (error: unknown) => error instanceof Error ? error.message : '暫時無法完成操作，請稍後再試。';

function Brand() {
  return <a className="brand" href="/app/" aria-label="HomeVault 首頁"><span className="brand-mark"><House size={23} strokeWidth={1.7} /><span /></span><span>HomeVault<span className="brand-dot">.</span></span></a>;
}

function HomeIllustration({ small = false }: { small?: boolean }) {
  return <svg className={small ? 'home-illustration small' : 'home-illustration'} viewBox="0 0 480 330" fill="none" aria-hidden="true">
    <path d="M59 290V119C59 61 106 14 164 14H316C374 14 421 61 421 119V290H59Z" fill="#e8eadb" />
    <path d="M101 290V145L239 48L377 145V290H101Z" fill="#f7f4e9" />
    <path d="M83 146L239 36L394 146" stroke="#3e7263" strokeWidth="11" strokeLinecap="round" strokeLinejoin="round" />
    <rect x="202" y="95" width="74" height="69" rx="4" fill="#ccdcd1" />
    <path d="M239 95V164M202 129H276" stroke="#f7f4e9" strokeWidth="7" />
    <rect x="150" y="204" width="179" height="70" rx="15" fill="#bdc9af" />
    <rect x="159" y="180" width="79" height="58" rx="13" fill="#d0dac1" />
    <rect x="242" y="180" width="78" height="58" rx="13" fill="#d0dac1" />
    <rect x="139" y="216" width="23" height="57" rx="9" fill="#8da38d" />
    <rect x="317" y="216" width="23" height="57" rx="9" fill="#8da38d" />
    <path d="M158 273V289M320 273V289" stroke="#56705c" strokeWidth="7" strokeLinecap="round" />
    <path d="M373 247V201M373 226C344 225 341 209 345 201C363 199 374 210 373 226ZM373 212C393 209 404 193 400 184C381 182 371 193 373 212Z" stroke="#74957b" strokeWidth="5" strokeLinejoin="round" />
    <path d="M354 243H392L386 289H360L354 243Z" fill="#caa68a" />
    <path d="M59 291H421" stroke="#adc0aa" strokeWidth="3" strokeLinecap="round" />
    <circle cx="105" cy="67" r="7" fill="#dbb980" /><path d="M385 56V73M377 64H393" stroke="#dbb980" strokeWidth="3" strokeLinecap="round" />
  </svg>;
}

function Spinner({ label = '載入中…' }: { label?: string }) {
  return <span className="spinner-label"><LoaderCircle className="spin" size={18} aria-hidden="true" />{label}</span>;
}

function AuthScreen({ onLogin, notice }: { onLogin: (resident: Resident) => void; notice: string }) {
  const [mode, setMode] = useState<'login' | 'signup'>('login');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = Object.fromEntries(new FormData(event.currentTarget)) as unknown as SignupData;
    if (mode === 'signup' && data.password !== data.confirmPassword) { setError('兩次輸入的密碼不一致。'); return; }
    setBusy(true); setError('');
    try {
      if (mode === 'signup') {
        await api.signup(data);
        // Registration creates the account; authentication is always explicit.
        setMode('login');
      }
      onLogin(await api.login(data.username, data.password));
    } catch (failure) { setError(errorText(failure)); }
    finally { setBusy(false); }
  }
  return <main className="auth-page">
    <section className="auth-story">
      <Brand />
      <div className="auth-story-content"><span className="eyebrow"><span /> A LITTLE ORDER, A LOT OF PEACE</span><h1>把家的每一份珍貴，<br />好好收藏。</h1><p>從第一張照片，到一份完整的居家清單。<br />讓每一件重要的物品，都有安心的歸屬。</p><HomeIllustration /><div className="story-note"><ShieldCheck size={19} />你的物品記錄，只屬於你。</div></div>
      <span className="auth-copyright">HOMEVAULT · A PLACE FOR WHAT MATTERS</span>
    </section>
    <section className="auth-form-side">
      <div className="auth-mobile-brand"><Brand /></div>
      <div className="auth-form-wrap"><span className="section-kicker">YOUR HOME, WELL KEPT</span><h2>{mode === 'login' ? '歡迎回家。' : '為你的家，建立一份記錄。'}</h2><p className="muted">{mode === 'login' ? '登入 HomeVault，接著整理你的美好日常。' : '只需幾個步驟，就能開始收藏家的每一件珍貴。'}</p>
        <div className="auth-tabs" role="group" aria-label="帳號操作"><button type="button" className={mode === 'login' ? 'selected' : ''} onClick={() => { setMode('login'); setError(''); }} disabled={busy}>登入</button><button type="button" className={mode === 'signup' ? 'selected' : ''} onClick={() => { setMode('signup'); setError(''); }} disabled={busy}>建立帳號</button></div>
        {(error || notice) && <div className="message error" role="alert">{error || notice}</div>}
        <form onSubmit={submit} className="auth-form" key={mode}>
          {mode === 'signup' && <><label>姓名<input name="residentName" autoComplete="name" maxLength={30} placeholder="怎麼稱呼你？" required disabled={busy} /></label><label>居住地址<input name="residentAddress" autoComplete="street-address" maxLength={50} placeholder="用於你的保險清單" required disabled={busy} /></label><label>電子郵件<input name="email" type="email" autoComplete="email" maxLength={100} placeholder="you@example.com" required disabled={busy} /></label></>}
          <label>使用者名稱<input name="username" autoComplete="username" minLength={3} maxLength={30} placeholder="輸入使用者名稱" required disabled={busy} /></label>
          <label>密碼<input name="password" type="password" autoComplete={mode === 'login' ? 'current-password' : 'new-password'} minLength={mode === 'signup' ? 8 : undefined} maxLength={100} pattern={mode === 'signup' ? '(?=.*[A-Za-z])(?=.*[0-9]).+' : undefined} title={mode === 'signup' ? '至少 8 個字元，包含英文字母與數字' : undefined} placeholder={mode === 'login' ? '輸入密碼' : '至少 8 個字元，包含英文與數字'} required disabled={busy} /></label>
          {mode === 'signup' && <label>確認密碼<input name="confirmPassword" type="password" autoComplete="new-password" maxLength={100} placeholder="再輸入一次密碼" required disabled={busy} /></label>}
          <button className="button primary auth-submit" disabled={busy}>{busy ? <Spinner label={mode === 'login' ? '登入中…' : '建立帳號中…'} /> : <>{mode === 'login' ? '進入我的 HomeVault' : '建立我的 HomeVault'}<ArrowRight size={18} /></>}</button>
        </form>
        {mode === 'login' && <a className="forgot-link" href="/Account/ForgotPassword">忘記密碼？</a>}
        <p className="auth-fineprint"><ShieldCheck size={15} />照片與物品清單皆受到帳號權限保護</p>
      </div>
    </section>
  </main>;
}

function Dialog({ title, children, onClose, busy = false }: { title: string; children: ReactNode; onClose: () => void; busy?: boolean }) {
  const panel = useRef<HTMLElement>(null);
  const close = useRef(onClose);
  close.current = onClose;
  const isBusy = useRef(busy);
  isBusy.current = busy;
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    document.body.classList.add('modal-open');
    panel.current?.querySelector<HTMLElement>('input,button')?.focus();
    const keydown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !isBusy.current) close.current();
      if (event.key === 'Tab') {
        const elements = panel.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), a[href]');
        if (!elements?.length) { event.preventDefault(); return; }
        const first = elements[0], last = elements[elements.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
      }
    };
    document.addEventListener('keydown', keydown);
    return () => { document.body.classList.remove('modal-open'); document.removeEventListener('keydown', keydown); previous?.focus(); };
  }, []);
  return <div className="modal-backdrop" onMouseDown={event => { if (event.target === event.currentTarget && !busy) onClose(); }}><section className="modal" role="dialog" aria-modal="true" aria-labelledby="dialog-title" ref={panel}><header className="modal-header"><div><span className="section-kicker">A PLACE FOR EVERYTHING</span><h2 id="dialog-title">{title}</h2></div><button className="icon-button" onClick={onClose} aria-label="關閉視窗" disabled={busy}><X size={21} /></button></header>{children}</section></div>;
}

export function ItemEditor({ item, categories, onClose, onSave }: { item: CatalogItem | null; categories: string[]; onClose: () => void; onSave: (draft: ItemDraft, file: File | null, removePhoto: boolean) => Promise<void> }) {
  const [file, setFile] = useState<File | null>(null);
  const [removePhoto, setRemovePhoto] = useState(false);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const draft = { itemName: String(data.get('itemName')).trim(), itemType: String(data.get('itemType')).trim(), itemValue: Number(data.get('itemValue')) };
    if (!draft.itemName || !draft.itemType) { setError('請填寫物品名稱和類別。'); return; }
    setError(''); setBusy(true);
    try { await onSave(draft, file, removePhoto); }
    catch (failure) { setError(errorText(failure)); }
    finally { setBusy(false); }
  }
  return <Dialog title={item ? '編輯物品' : '記錄一件珍貴'} onClose={onClose} busy={busy}>
    <form onSubmit={submit} className="item-form">
      <p className="muted">留下物品的基本資料，讓未來的自己更安心。</p>
      {error && <div className="message error" role="alert">{error}</div>}
      <label>物品名稱<input name="itemName" defaultValue={item?.itemName ?? ''} maxLength={30} placeholder="例如：客廳的雙人沙發" required disabled={busy} /></label>
      <div className="form-row"><label>物品類別<input name="itemType" list="item-categories" defaultValue={item?.itemType ?? ''} maxLength={30} placeholder="例如：家具" required disabled={busy} /><datalist id="item-categories">{[...new Set([...categories, '家具', '電子產品', '家電', '收藏品', '珠寶與配件', '其他'])].map(category => <option key={category} value={category} />)}</datalist></label><label>預估價值（USD）<div className="currency-input"><span aria-hidden="true">$</span><input aria-label="預估價值（USD）" name="itemValue" type="number" min={1} max={2147483647} step={1} defaultValue={item?.itemValue ?? ''} placeholder="0" required disabled={busy} /></div></label></div>
      <div className="photo-field"><span className="field-label">物品照片 <span className="optional">選填</span></span><label className="upload-area"><span className="upload-icon"><ImagePlus size={24} /></span><strong>{file ? file.name : item?.imageUrl && !removePhoto ? '更換現有照片' : '為物品加上一張照片'}</strong><span>點擊選擇 JPG、PNG 或 WebP，最大 5 MB</span><input type="file" accept="image/jpeg,image/png,image/webp" aria-label="上傳物品照片" disabled={busy} onChange={event => { const selected = event.target.files?.[0]; if (!selected) return; const invalid = validatePhoto(selected); if (invalid) { setError(invalid); event.target.value = ''; setFile(null); return; } setError(''); setFile(selected); setRemovePhoto(false); }} /></label>
      {file && <button className="text-button" type="button" disabled={busy} onClick={() => setFile(null)}>取消選取照片</button>}
      {item?.imageUrl && !file && <label className="checkbox-label"><input type="checkbox" checked={removePhoto} disabled={busy} onChange={event => setRemovePhoto(event.target.checked)} />移除目前的照片</label>}</div>
      <footer className="modal-footer"><button type="button" className="button secondary" onClick={onClose} disabled={busy}>取消</button><button className="button primary" disabled={busy}>{busy ? <Spinner label="儲存中…" /> : <><Check size={17} />儲存物品</>}</button></footer>
    </form>
  </Dialog>;
}

function DeleteDialog({ item, onClose, onDelete }: { item: CatalogItem; onClose: () => void; onDelete: () => Promise<void> }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  return <Dialog title="移除這件物品？" onClose={onClose} busy={busy}><div className="delete-content"><span className="delete-symbol"><Trash2 size={26} /></span><p>「<strong>{item.itemName}</strong>」和它的照片將從清單中永久移除。</p>{error && <div className="message error" role="alert">{error}</div>}</div><footer className="modal-footer"><button className="button secondary" disabled={busy} onClick={onClose}>保留物品</button><button className="button danger" disabled={busy} onClick={async () => { setBusy(true); setError(''); try { await onDelete(); } catch (failure) { setError(errorText(failure)); } finally { setBusy(false); } }}>{busy ? <Spinner label="移除中…" /> : '確認移除'}</button></footer></Dialog>;
}

function ItemCard({ item, revision, onEdit, onDelete }: { item: CatalogItem; revision: number; onEdit: () => void; onDelete: () => void }) {
  const [imageFailed, setImageFailed] = useState(false);
  useEffect(() => setImageFailed(false), [item.imageUrl, revision]);
  const typeHash = [...item.itemType].reduce((sum, char) => sum + char.charCodeAt(0), 0) % 4;
  const PlaceholderIcon = /家具|furniture|沙發/i.test(item.itemType) ? Sofa : /電子|家電|electronic/i.test(item.itemType) ? Box : Package;
  return <article className="item-card"><button type="button" className={`item-photo tone-${typeHash}`} onClick={onEdit} aria-label={`編輯 ${item.itemName}`}>
    {item.imageUrl && !imageFailed ? <img src={`${item.imageUrl}?v=${revision}`} alt={item.itemName} loading="lazy" onError={() => setImageFailed(true)} /> : <div className="photo-placeholder"><PlaceholderIcon size={57} strokeWidth={1.1} /><span>{imageFailed ? '照片暫時無法顯示' : '留個影，記住它的樣子'}</span></div>}
    <span className="photo-badge"><Tag size={11} />{item.itemType}</span><span className="photo-edit"><Pencil size={15} /></span>
    </button><div className="item-details"><div><span className="item-code">#{item.itemId}</span><h3><button onClick={onEdit}>{item.itemName}</button></h3></div><div className="item-bottom"><span className="item-value">{currency(item.itemValue)}<small>USD</small></span><div className="card-actions"><button className="icon-button" aria-label={`編輯 ${item.itemName} 的資料`} onClick={onEdit}><Pencil size={15} /></button><button className="icon-button delete-action" aria-label={`移除 ${item.itemName}`} onClick={onDelete}><Trash2 size={15} /></button></div></div></div></article>;
}

type ModalState = { kind: 'edit'; item: CatalogItem | null } | { kind: 'delete'; item: CatalogItem } | null;

export default function App() {
  const [resident, setResident] = useState<Resident | null>(null);
  const [initializing, setInitializing] = useState(true);
  const [initializationError, setInitializationError] = useState('');
  const [sessionAttempt, setSessionAttempt] = useState(0);
  const [authNotice, setAuthNotice] = useState('');
  const [items, setItems] = useState<CatalogItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState('');
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('');
  const [sort, setSort] = useState<SortOrder>('name');
  const [view, setView] = useState<'grid' | 'list'>('grid');
  const [modal, setModal] = useState<ModalState>(null);
  const [toast, setToast] = useState('');
  const [actionError, setActionError] = useState('');
  const [downloading, setDownloading] = useState(false);
  const [loggingOut, setLoggingOut] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    let active = true;
    setInitializing(true); setInitializationError('');
    api.me().then(user => { if (active) setResident(user); }).catch(error => { if (active && (!(error instanceof ApiError) || error.status !== 401)) setInitializationError(errorText(error)); }).finally(() => { if (active) setInitializing(false); });
    return () => { active = false; };
  }, [sessionAttempt]);

  function expireSession(failure: unknown): boolean {
    if (failure instanceof ApiError && failure.status === 401) {
      setResident(null); setItems([]); setModal(null); setAuthNotice('登入已逾時，請重新登入後繼續。');
      return true;
    }
    return false;
  }

  async function loadItems() {
    setLoading(true); setLoadError('');
    try { setItems(await api.items()); }
    catch (failure) { if (!expireSession(failure)) setLoadError(errorText(failure)); }
    finally { setLoading(false); }
  }
  useEffect(() => { if (resident) void loadItems(); }, [resident]);
  useEffect(() => { if (!toast) return; const timeout = window.setTimeout(() => setToast(''), 4500); return () => window.clearTimeout(timeout); }, [toast]);
  useEffect(() => { if (category && !items.some(item => item.itemType === category)) setCategory(''); }, [items, category]);

  async function saveItem(draft: ItemDraft, file: File | null, removePhoto: boolean) {
    if (modal?.kind !== 'edit') return;
    let saved: CatalogItem;
    try { saved = modal.item ? await api.updateItem(modal.item.itemId, draft) : await api.createItem(draft); }
    catch (failure) { expireSession(failure); throw failure; }
    setItems(previous => previous.some(item => item.itemId === saved.itemId) ? previous.map(item => item.itemId === saved.itemId ? saved : item) : [...previous, saved]);
    // Keep the new ID if a photo upload fails so retry never creates a duplicate item.
    setModal({ kind: 'edit', item: saved });
    try {
      if (file) { await api.uploadImage(saved.itemId, file); saved = { ...saved, imageUrl: `/api/items/${encodeURIComponent(saved.itemId)}/image` }; }
      else if (removePhoto && saved.imageUrl) { await api.removeImage(saved.itemId); saved = { ...saved, imageUrl: null }; }
    } catch (failure) { expireSession(failure); throw new Error(`物品資料已儲存，但照片更新失敗。${errorText(failure)} 可再按儲存重試。`); }
    setItems(previous => previous.map(item => item.itemId === saved.itemId ? saved : item));
    setRevision(previous => previous + 1); setModal(null); setToast('物品已儲存，家的記錄又完整了一些。');
  }

  async function deleteItem() {
    if (modal?.kind !== 'delete') return;
    try { await api.deleteItem(modal.item.itemId); setItems(previous => previous.filter(item => item.itemId !== modal.item.itemId)); setModal(null); setToast('物品已從清單移除。'); }
    catch (failure) { expireSession(failure); throw failure; }
  }

  async function downloadReport() {
    setDownloading(true); setActionError('');
    try {
      const blob = await api.report();
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a'); anchor.href = url; anchor.download = 'HomeVault-insurance-report.pdf'; document.body.appendChild(anchor); anchor.click(); anchor.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 1000);
      setToast('保險清單已下載。');
    } catch (failure) { if (!expireSession(failure)) setActionError(errorText(failure)); }
    finally { setDownloading(false); }
  }

  async function logout() {
    setLoggingOut(true); setActionError('');
    try { await api.logout(); setResident(null); setItems([]); setSearch(''); setCategory(''); setToast(''); setMenuOpen(false); setAuthNotice(''); }
    catch (failure) { if (!expireSession(failure)) setActionError(errorText(failure)); }
    finally { setLoggingOut(false); }
  }

  if (initializing) return <main className="initial-screen"><Brand /><Spinner label="正在打開你的 HomeVault…" /></main>;
  if (initializationError) return <main className="initial-screen"><Brand /><div className="connection-error"><h1>暫時連不上你的家。</h1><p role="alert">{initializationError}</p><button className="button primary" onClick={() => setSessionAttempt(previous => previous + 1)}>重新連線</button></div></main>;
  if (!resident) return <AuthScreen notice={authNotice} onLogin={user => { setAuthNotice(''); setActionError(''); setResident(user); }} />;

  const summary = summarizeItems(items);
  const visibleItems = selectItems(items, search, category, sort);

  return <div className="app-shell">
    <a className="skip-link" href="#main-content">跳至主要內容</a>
    {menuOpen && <button className="sidebar-backdrop" aria-label="關閉選單" onClick={() => setMenuOpen(false)} />}
    <aside className={`sidebar ${menuOpen ? 'is-open' : ''}`}><Brand /><span className="nav-label">我的空間</span><nav aria-label="主要導覽"><button className="nav-item active" onClick={() => { setCategory(''); setSearch(''); setMenuOpen(false); }}><LayoutGrid size={19} />物品清單<span className="nav-count">{summary.count}</span></button><button className="nav-item" onClick={() => { setMenuOpen(false); void downloadReport(); }} disabled={downloading || loading || !!loadError}><FileText size={19} />保險清單<ArrowUpRight className="nav-arrow" size={14} /></button></nav>
      <div className="sidebar-card"><HomeIllustration small /><h3>一個家，滿滿的故事。</h3><p>從一件物品開始，<br />慢慢記錄你的生活。</p><span><ShieldCheck size={14} />安心收藏，隨時取用</span></div>
      <div className="sidebar-footer"><span className="status-dot" /><span>屬於你的安心空間</span><span className="version">v1.0</span></div>
    </aside>
    <div className="workspace"><header className="topbar"><button className="icon-button mobile-menu" aria-label="開啟選單" aria-expanded={menuOpen} onClick={() => setMenuOpen(true)}><Menu size={22} /></button><div className="breadcrumb"><House size={16} /><span>/</span><span>我的 HomeVault</span></div><div className="profile"><span className="profile-avatar">{(resident.residentName || resident.username).slice(0, 1).toUpperCase()}</span><div><strong>{resident.residentName || resident.username}</strong><span>個人空間</span></div><button className="icon-button logout" title="登出" aria-label="登出" disabled={loggingOut} onClick={() => void logout()}>{loggingOut ? <LoaderCircle size={18} className="spin" /> : <LogOut size={18} />}</button></div></header>
      <main id="main-content" className="main-content"><section className="page-heading"><div><span className="eyebrow"><span /> EVERY LITTLE THING MATTERS</span><h1>家的每一件，都有記錄<span>。</span></h1><p>收藏生活中的珍貴，為你的家多留一份安心。</p></div><button className="button primary add-button" onClick={() => setModal({ kind: 'edit', item: null })}><Plus size={19} />新增物品</button></section>
        <section className="stats-grid" aria-label="物品統計"><article className="stat-card featured"><div className="stat-top"><span>預估總值</span><CircleDollarSign size={21} /></div><div className="stat-number">{loading ? '—' : currency(summary.totalValue)}<span>USD</span></div><div className="stat-caption"><ShieldCheck size={14} />讓珍貴，有一份清楚的記錄</div></article><article className="stat-card"><div className="stat-top"><span>已收藏物品</span><Package size={21} /></div><div className="stat-number">{loading ? '—' : summary.count}<span>件物品</span></div><div className="stat-caption">橫跨 {summary.categories.length} 個生活類別</div></article><article className="stat-card"><div className="stat-top"><span>照片記錄</span><Camera size={21} /></div><div className="stat-number">{loading ? '—' : summary.photoCount}<span>/ {summary.count} 件</span></div><div className="stat-caption">{summary.count > 0 && summary.photoCount === summary.count ? <><CheckCircle2 size={14} />每一件珍貴，都留下了模樣</> : '一張照片，讓回憶與記錄更完整'}</div></article></section>
        {actionError && <div className="message error action-error" role="alert"><span>{actionError}</span><button className="icon-button" aria-label="關閉錯誤訊息" onClick={() => setActionError('')}><X size={16} /></button></div>}
        <section className="collection" aria-labelledby="collection-title"><div className="collection-heading"><div><h2 id="collection-title">我的物品<span>{summary.count}</span></h2><p>每一件，都值得好好照顧。</p></div><button className="button secondary export-button" onClick={() => void downloadReport()} disabled={downloading || loading || !!loadError}>{downloading ? <Spinner label="準備下載…" /> : <><ArrowDownToLine size={16} />匯出保險清單</>}</button></div>
          <div className="collection-toolbar"><label className="search-input"><Search size={18} /><input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="搜尋物品名稱或類別…" aria-label="搜尋物品" /></label><div className="toolbar-right"><label className="sort-select"><span className="sr-only">排序方式</span><select value={sort} onChange={event => setSort(event.target.value as SortOrder)}><option value="name">依名稱排序</option><option value="value-desc">價值：由高到低</option><option value="value-asc">價值：由低到高</option></select><ChevronDown size={14} /></label><div className="view-switch" role="group" aria-label="顯示方式"><button aria-label="卡片檢視" aria-pressed={view === 'grid'} className={view === 'grid' ? 'active' : ''} onClick={() => setView('grid')}><LayoutGrid size={17} /></button><button aria-label="清單檢視" aria-pressed={view === 'list'} className={view === 'list' ? 'active' : ''} onClick={() => setView('list')}><List size={18} /></button></div></div></div>
          <div className="category-filters" role="group" aria-label="依類別篩選"><button className={!category ? 'active' : ''} aria-pressed={!category} onClick={() => setCategory('')}>全部物品<span>{summary.count}</span></button>{summary.categories.map(type => <button key={type} className={category === type ? 'active' : ''} aria-pressed={category === type} onClick={() => setCategory(type)}>{type}<span>{items.filter(item => item.itemType === type).length}</span></button>)}</div>
          {loading ? <div className="catalog-loading" role="status"><Spinner label="正在整理你的物品…" /><div className="skeleton-grid">{[1, 2, 3].map(number => <div className="skeleton-card" key={number} />)}</div></div> : loadError ? <div className="empty-state"><Box size={40} /><h3>物品清單暫時無法載入</h3><p role="alert">{loadError}</p><button className="button secondary" onClick={() => void loadItems()}>重新載入</button></div> : !items.length ? <div className="empty-state first-item"><span className="empty-icon"><FolderHeart size={43} strokeWidth={1.35} /></span><span className="section-kicker">EVERY COLLECTION STARTS WITH ONE</span><h3>你的第一件珍貴，從這裡開始。</h3><p>一張照片、一個名字，為重要的物品留下記錄。<br />沙發、相機，或是那件陪伴你很久的收藏。</p><button className="button primary" onClick={() => setModal({ kind: 'edit', item: null })}><Plus size={17} />新增第一件物品</button></div> : !visibleItems.length ? <div className="empty-state"><Search size={38} /><h3>還沒找到符合的物品</h3><p>換個關鍵字，或查看所有類別試試看。</p><button className="button secondary" onClick={() => { setSearch(''); setCategory(''); }}>清除搜尋與篩選</button></div> : <><div className={`items-grid ${view === 'list' ? 'list-view' : ''}`}>{visibleItems.map(item => <ItemCard key={item.itemId} item={item} revision={revision} onEdit={() => setModal({ kind: 'edit', item })} onDelete={() => setModal({ kind: 'delete', item })} />)}</div><div className="collection-footnote"><span>顯示 {visibleItems.length} 件，共 {summary.count} 件物品</span><span><ShieldCheck size={13} />僅你可以存取這份清單</span></div></>}
        </section><aside className="gentle-tip"><span><Sparkles size={19} /></span><div><strong>給家的小提醒</strong><p>為物品加上照片與預估價值，需要時就能快速匯出一份完整的保險清單。</p></div><span className="tip-decoration"><House size={36} strokeWidth={1} /></span></aside><footer className="page-footer"><span>HomeVault · 讓家的珍貴，有跡可循</span><span>MADE FOR A LITTLE PEACE OF MIND</span></footer>
      </main>
    </div>
    {modal?.kind === 'edit' && <ItemEditor item={modal.item} categories={summary.categories} onClose={() => setModal(null)} onSave={saveItem} />}
    {modal?.kind === 'delete' && <DeleteDialog item={modal.item} onClose={() => setModal(null)} onDelete={deleteItem} />}
    {toast && <div className="toast" role="status"><CheckCircle2 size={19} />{toast}<button aria-label="關閉通知" className="icon-button" onClick={() => setToast('')}><X size={16} /></button></div>}
  </div>;
}
