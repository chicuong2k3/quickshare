// shadcn/ui for Unpoly.Blazor — the behaviour layer.
// =============================================================================================
// Radix builds its components in React and keeps their state in React. Nothing here can: the
// server renders once and Unpoly swaps fragments in and out underneath. So each interactive
// component is either
//
//   (a) a native element that already owns the state — <dialog>, <details>, [popover],
//       <select> — dressed in shadcn's classes, or
//   (b) an `up.compiler`, which runs on every DOM insertion (first load AND every fragment
//       swap) and MUST return a destructor. A compiler that leaves residue behind does not
//       break this swap; it breaks the next one, which is why every one below tears down.
//
// Tailwind only emits classes it can see in a scanned file, so this file must be listed in the
// head's `@source` — otherwise everything built here renders unstyled. That line is in each
// head's app.css next to the component globs.

// Everything below runs inside an IIFE, and that is load-bearing rather than tidy.
// A <script src> without type=module shares one global scope with every other script on the
// page — so `const el` here and `function el` in a head's own app.js are a redeclaration, and
// the browser throws a SyntaxError that kills THIS ENTIRE FILE before a single compiler is
// registered. Nothing logs at the call site, nothing fails on the server, and every dialog,
// toast and dropdown is simply absent on the one head that happened to pick the same name.
// The two exports at the bottom are the only things this file puts on window, deliberately.
;(function () {
  // ---- config --------------------------------------------------------------------------------
  // The only strings this library produces from JavaScript rather than from a Razor parameter.
  // Read lazily at call time, not captured at load, so a head can set window.shadcnUi from a
  // script that happens to load after this one.
  //
  //   window.shadcnUi = {
  //     confirmText: 'Đồng ý',
  //     cancelText: 'Huỷ',
  //     datePickerLocale: { days: [...], months: [...], today: 'Hôm nay', clear: 'Xoá', ... },
  //   }
  const config = () => window.shadcnUi || {}

  // ---- platform driver — the one place this file knows which renderer it is on --------------
  // This library ships identical Razor components to two hosts:
  //   (a) a Blazor static SSR web head running Unpoly — navigation and the fragments it swaps,
  //   (b) a MAUI Blazor Hybrid app — Blazor renders into a WebView and there is no Unpoly at all.
  // In (a) every compiler is registered with `up.compiler`, which runs it on first load AND on
  // every fragment swap, and detaches it when the swap is torn down. In (b) the same compilers
  // must run when Blazor inserts DOM inside the WebView and be torn down when Blazor removes it.
  //
  // So `shadcnCompiler`/`shadcnOn` are the ONLY names the 40+ compilers below are allowed to
  // call. When Unpoly is present they just pass through to it — a web head sees exactly what it
  // saw before this shim existed. When it is not (MAUI), a MutationObserver drives the same
  // compilers against the same selectors, honouring the destructor contract: a compiler returns
  // a teardown, and the teardown runs when its element leaves the DOM.
  //
  // The lazy-asset compilers (AirDatepicker, Prism, QRCode, image-compare) may return no teardown
  // because they bail ("asset not loaded yet"). Under Unpoly a bail is retried on the next swap;
  // here the observer re-runs a compiler that produced no teardown on every further mutation so
  // a bail is retried, not lost.
  const usingUnpoly = typeof window.up !== 'undefined' && typeof window.up.compiler === 'function'

  // Compilers keyed by the selector string they were registered with, plus a WeakMap of the
  // elements each compiler already owns (so the same element is not compiled twice).
  const compilerRegistry = new Map()
  const compiledElements = new WeakMap()

  const shadcnCompiler = (selector, compile) => {
    if (usingUnpoly) return window.up.compiler(selector, compile)
    const set = compilerRegistry.get(selector) || new Set()
    set.add(compile)
    compilerRegistry.set(selector, set)
    // An element already in the DOM that the observer has not visited yet (e.g. the first paint
    // happens before the observer starts) is compiled now.
    for (const el of document.querySelectorAll(selector)) runCompiler(el, compile, selector)
  }

  const shadcnOn = (name, handler) => {
    if (usingUnpoly) return window.up.on(name, handler)
    // In the hybrid host there is no `up.on` channel. The two events this library listens for
    // are dispatched on the global window by whichever code owns the underlying feature; the
    // handler here is registered on `document` so it works for both `window` and any target.
    document.addEventListener(name, (e) => handler(e.detail ?? e), true)
  }

  // Run one compiler on one element. Returns true if it produced a teardown (it "took" the
  // element); false if it bailed and should be retried on a later mutation.
  const runCompiler = (el, compile, selector) => {
    const owned = compiledElements.get(el)
    if (owned && owned.has(compile)) return true
    const teardown = compile(el)
    let set = compiledElements.get(el)
    if (!set) { set = new Set(); compiledElements.set(el, set) }
    if (typeof teardown === 'function') { set.add(compile); el.__shadcnTeardown = teardown }
    // No teardown (a bail, or a compiler that attaches state to global structure) → not owned.
    // The observer will retry it on the next mutation; this is what makes a lazy-asset bail work
    // without swapping fragments.
    return typeof teardown === 'function'
  }

  if (!usingUnpoly) {
    // A compiler may inject DOM that matches another registered selector (a select builds a
    // popover, a tooltip builds a panel). Each injection is itself a mutation the observer sees,
    // so the observer MUST stay connected — a MAUI render inserts components at any time and each
    // must be compiled. To avoid re-compiling the same element forever, `compiledElements` marks
    // what each compiler already owns; `runCompiler` is a no-op on an owned element. A compiler
    // that returned a teardown is owned and never re-runs; one that bailed is re-tried each pass.
    const observer = new MutationObserver((mutations) => {
      const scan = (node) => {
        if (!(node instanceof Element)) return
        for (const [selector, set] of compilerRegistry) {
          if (node.matches?.(selector)) for (const c of set) runCompiler(node, c, selector)
          for (const m of node.querySelectorAll(selector)) for (const c of set) runCompiler(m, c, selector)
        }
      }
      // Blazor can report a parent and hundreds of its descendants in the same batch.
      // Scanning each one walks that subtree once per compiler per descendant (quadratic
      // on a page of calendar day inputs). A live added ancestor already covers its children.
      const added = new Set()
      for (const { addedNodes } of mutations)
        for (const node of addedNodes)
          if (node instanceof Element && node.isConnected) added.add(node)
      for (const node of added) {
        let covered = false
        for (let parent = node.parentElement; parent; parent = parent.parentElement) {
          if (added.has(parent)) { covered = true; break }
        }
        if (!covered) scan(node)
      }
      for (const { removedNodes } of mutations) {
        for (const node of removedNodes) {
          // A node moved within the document (for example a wrapped select) is not
          // disposed. Unwrapping it here creates an infinite mutation/recompile loop.
          if (!(node instanceof Element) || node.isConnected) continue
          const teardown = node.__shadcnTeardown
          if (typeof teardown === 'function') { teardown(); delete node.__shadcnTeardown }
          compiledElements.delete(node)
        }
      }
    })
    observer.observe(document.body, { subtree: true, childList: true })
    // The first paint's elements that arrived before the observer above started.
    for (const [selector, set] of compilerRegistry)
      for (const el of document.querySelectorAll(selector)) for (const c of set) runCompiler(el, c, selector)
  }

  // ---- lazy assets — only when a component that needs them is actually on the page -----------
  // SSR + Unpoly renders HTML on the server. CodeBlock's Shiki bundle and QRCode
  // are requested only when their components are present on the page.
  // of blocking download on a marketing page that ships no code at all. Each compiler below
  // calls `loadScript` on first use and caches the promise, so a request that never renders the
  // component never fetches the file — and a request that renders ten blocks fetches it once.
  const loaded = new Set()
  const loading = new Map()
  const loadScript = (src) => {
    if (loaded.has(src)) return Promise.resolve()
    // The SAME in-flight promise for every caller: the previous version returned an
    // already-resolved Promise to the second caller (the script element already exists),
    // so its .then ran on the next microtask — BEFORE the script had executed. Every
    // compiler that touched a lazy library raced the load and lost: with 14 QRs on a
    // page, 13 rendered nothing. A cached promise makes all callers wait for onload.
    if (loading.has(src)) return loading.get(src)
    const promise = new Promise((resolve, reject) => {
      const s = document.createElement('script')
      s.src = src
      s.defer = true
      s.onload = () => { loaded.add(src); loading.delete(src); resolve() }
      s.onerror = () => { loading.delete(src); reject() }
      document.head.appendChild(s)
    })
    loading.set(src, promise)
    return promise
  }
  const ensureShiki = () => loadScript('_content/Unpoly.Blazor.Shadcn/shiki/shiki.js')
  const ensureQrcode = () => {
    if (typeof QRCode !== 'undefined') return Promise.resolve()
    return loadScript('_content/Unpoly.Blazor.Shadcn/qrcodejs/qrcode.min.js').catch(() => {})
  }
  // Motion (motion.dev, MIT) — framer-motion's own engine without React: springs, motion values
  // and transforms. 48 KB gzipped, so it is fetched only by a component that animates with it.
  const ensureMotion = () => {
    if (typeof window.Motion !== 'undefined') return Promise.resolve(window.Motion)
    return loadScript('_content/Unpoly.Blazor.Shadcn/motion/motion.js').then(() => window.Motion)
  }

  // ---- cn ------------------------------------------------------------------------------------
  const cn = (...parts) => parts.filter(Boolean).join(' ')

  const el = (tag, className, props = {}) =>
    Object.assign(document.createElement(tag), { className, ...props })

  // ---- shared class strings ------------------------------------------------------------------
  // Only the JS-built components need these; the Razor components carry their own inline, exactly
  // as shadcn does. They sit at the top rather than beside their use because a `const` read before
  // its declaration is a temporal-dead-zone error, and a compiler callback is easy to reorder.

  let selectSeq = 0

  // The page must not scroll behind a takeover — a dialog, alert dialog, sheet, modal drawer,
  // or an open select list (the list is the takeover: the reader is choosing, not browsing).
  // The platform makes a modal's background inert to clicks and to the keyboard, but the wheel
  // still reaches it, so the panel sits pinned while the page slides past behind it.
  //
  // Locking sets overflow:hidden on the root, which retires the page scrollbar — and on a
  // machine with classic scrollbars that 17px gutter vanish re-lays out the whole page under
  // the panel, one visible jump every open. scrollbar-gutter:stable reserves the strip ahead
  // of time, so flipping overflow only stops the scrolling; the layout does not move. On
  // overlay-scrollbar platforms (macOS, mobile) the gutter is empty and nothing shows.
  //
  // Counted rather than toggled: dialogs nest, a select opens inside a dialog, and the first
  // one to close must not unlock the page while another is still holding it.
  document.documentElement.style.scrollbarGutter = 'stable'
  let locks = 0
  const lockScroll = (on) => {
    locks = Math.max(0, locks + (on ? 1 : -1))
    document.documentElement.style.overflow = locks > 0 ? 'hidden' : ''
  }

  const SELECT_TRIGGER =
    'border-input flex h-control w-full items-center justify-between gap-2 rounded-md border ' +
    'bg-transparent px-3 py-2 text-control whitespace-nowrap shadow-xs ' +
    'transition-[color,box-shadow] outline-none focus-visible:border-ring ' +
    'focus-visible:ring-ring/50 focus-visible:ring-[3px] disabled:cursor-not-allowed ' +
    'disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-destructive/20 ' +
    'data-[placeholder]:text-muted-foreground data-[size=sm]:h-8 dark:bg-input/30 ' +
    'dark:hover:bg-input/50 dark:aria-invalid:ring-destructive/40 [&_svg]:pointer-events-none ' +
    "[&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4 " +
    "[&_svg:not([class*='text-'])]:text-muted-foreground"

  const SELECT_CONTENT =
    'bg-popover text-popover-foreground z-50 max-h-60 min-w-[8rem] overflow-x-hidden ' +
    'overflow-y-auto rounded-md border p-1 shadow-md'

  const SELECT_ITEM =
    'relative flex w-full cursor-default items-center gap-2 rounded-sm py-1.5 pr-8 pl-2 text-sm ' +
    'outline-hidden select-none hover:bg-accent hover:text-accent-foreground ' +
    'data-[active]:bg-accent data-[active]:text-accent-foreground ' +
    'data-[disabled]:pointer-events-none data-[disabled]:opacity-50 ' +
    "[&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4 " +
    "[&_svg:not([class*='text-'])]:text-muted-foreground"

  const SELECT_LABEL = 'px-2 py-1.5 text-xs text-muted-foreground'
  const SELECT_SEPARATOR = 'pointer-events-none -mx-1 my-1 h-px bg-border'
  const SELECT_INDICATOR = 'absolute right-2 flex size-3.5 items-center justify-center rtl:right-auto rtl:left-2'
  const SELECT_SCROLL_BUTTON = 'flex cursor-default items-center justify-center py-1'

  // The two icons the drawn select needs. Everywhere else an <Icon> is rendered by Razor from the
  // committed sheet; here the whole control is built in script, so these are the only two markup
  // strings in this file that draw a picture.
  const svg = (paths) =>
    `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" ` +
    `fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" ` +
    `stroke-linejoin="round" aria-hidden="true">${paths}</svg>`
  const CHEVRON_DOWN = svg('<path d="m6 9 6 6 6-6"/>')
  const CHEVRON_UP = svg('<path d="m18 15-6-6-6 6"/>')
  const CHECK = svg('<path d="M20 6 9 17l-5-5"/>')

  const BUTTON_BASE =
    'inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md text-control ' +
    'font-medium transition-all disabled:pointer-events-none disabled:opacity-50 shrink-0 ' +
    'outline-none focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px] ' +
    'h-control px-4 py-2'

  const BUTTON = {
    default: cn(BUTTON_BASE, 'bg-primary text-primary-foreground shadow-xs hover:bg-primary/90'),
    destructive: cn(BUTTON_BASE, 'bg-destructive text-white shadow-xs hover:bg-destructive/90'),
    outline: cn(BUTTON_BASE, 'border bg-background shadow-xs hover:bg-accent hover:text-accent-foreground'),
    ghost: cn(BUTTON_BASE, 'hover:bg-accent hover:text-accent-foreground'),
  }

  // =============================================================================================
  // Sonner — toast()
  // =============================================================================================
  // Toastify-js underneath (vendored under /toastify, MIT). It owns the stack, the slide, the
  // timer and the dismissal; we supply the wording and the slot the CSS dresses.
  //
  // The server already knows what happened and says so with `Ctx.UpEmit("sonner:toast", …)`.

  function toast(text, options = {}) {
    if (!text) return
    if (typeof Toastify !== 'function') { console.warn('[ui] Toastify not loaded:', text); return }

    const type = options.type || 'success'
    Toastify({
      text,
      duration: options.duration ?? 4000,
      close: true,
      // Keep transient feedback clear of the Windows taskbar and checkout controls.
      gravity: 'top',
      position: 'right',
      // Theme comes from CSS, not inline style, so every token stays in one place.
      className: 'sonner-toast',
      style: { background: '' },
      stopOnFocus: true,
      escapeMarkup: true,
      ariaLive: type === 'error' ? 'assertive' : 'polite',
      callback: undefined,
      onClick: options.onClick,
    }).showToast()

    // Toastify has no hook for arbitrary attributes, and [data-slot] is what ui.css styles.
    const node = document.querySelector('.toastify:not([data-slot])')
    if (!node) return
    node.dataset.slot = 'sonner-toast'
    node.dataset.type = type

    // The glyph shadcn passes into Sonner as `icons`. Prepended rather than styled in, because
    // the toast is built by Toastify and the icon is the one part of its shape that is ours.
    const glyph = TOAST_ICONS[type]
    if (glyph) node.insertAdjacentHTML('afterbegin', svg(glyph))

    // Sonner's `action`, which this used to accept and silently drop: the option was read for
    // its type and its duration and never for this, so a toast written with an Undo showed the
    // words and no way to undo anything.
    //
    // `label` is sonner's name for it; `text` is accepted too, because that is what this port's
    // own examples had been writing against a feature that did not exist.
    const action = options.action
    if (action) {
      const button = el('button', '', { type: 'button', textContent: action.label ?? action.text ?? 'Undo' })
      button.dataset.slot = 'sonner-action'
      button.addEventListener('click', (event) => {
        event.stopPropagation()
        action.onClick?.(event)
        // A toast whose action has been taken has nothing left to say.
        node.remove()
      })
      node.insertBefore(button, node.querySelector('.toast-close'))
    }
  }

  // shadcn's five, and the same five lucide glyphs.
  const TOAST_ICONS = {
    success: '<path d="M21.801 10A10 10 0 1 1 17 3.335"/><path d="m9 11 3 3L22 4"/>',
    info: '<circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/>',
    warning: '<path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3"/><path d="M12 9v4"/><path d="M12 17h.01"/>',
    error: '<path d="m16.24 3.56 4.2 4.2a2 2 0 0 1 .59 1.42v5.64a2 2 0 0 1-.59 1.42l-4.2 4.2a2 2 0 0 1-1.42.59H9.18a2 2 0 0 1-1.42-.59l-4.2-4.2A2 2 0 0 1 3 14.82V9.18a2 2 0 0 1 .59-1.42l4.2-4.2A2 2 0 0 1 9.18 3h5.64a2 2 0 0 1 1.42.56z"/><path d="m15 9-6 6"/><path d="m9 9 6 6"/>',
    loading: '<path d="M21 12a9 9 0 1 1-6.219-8.56"/>',
  }

  toast.success = (text, o) => toast(text, { ...o, type: 'success' })
  toast.error = (text, o) => toast(text, { ...o, type: 'error' })
  // info and warning were missing, so a page calling toast.info threw and showed nothing at all.
  toast.info = (text, o) => toast(text, { ...o, type: 'info' })
  toast.warning = (text, o) => toast(text, { ...o, type: 'warning' })
  toast.loading = (text, o) => toast(text, { ...o, type: 'loading', duration: o?.duration ?? -1 })

  shadcnOn('sonner:toast', (event) => toast(event.text, { type: event.flavor || event.type || 'success' }))
  // The cart event carries its own wording and predates the toast channel.
  shadcnOn('cart:changed', (event) => toast(event.text))

  // =============================================================================================
  // Opening and closing a <dialog>
  // =============================================================================================
  // Safari before 15.4 has no <dialog>: show(), showModal(), close() and .open do not exist, and
  // calling them threw mid-click while a trigger that checked first just did nothing — a palette
  // that never opens, with nothing in the console. compat/dialog-shim.js supplies all of them, and
  // a head that loads it never reaches the fallback below. A head that does not is told so, once,
  // and the dialog still opens (as the `open` attribute, unpositioned) rather than not at all.
  // `open` is read as the attribute for the same reason: the property is what is missing.

  let warnedNoDialog = false
  const isOpen = (dialog) => dialog.hasAttribute('open')

  const showDialog = (dialog, modal = true) => {
    const method = modal ? 'showModal' : 'show'
    if (typeof dialog[method] === 'function') {
      dialog[method]()
      const focusTarget = dialog.querySelector('[autofocus], [data-autofocus]')
      if (focusTarget && typeof focusTarget.focus === 'function') {
        try { focusTarget.focus({ preventScroll: true }) } catch { focusTarget.focus() }
      }
      return
    }
    if (!warnedNoDialog) {
      warnedNoDialog = true
      console.warn('[ui] <dialog>.' + method + '() is missing (Safari before 15.4). Load ' +
        '_content/Unpoly.Blazor.Shadcn/compat/dialog-shim.js before ui.js. Falling back to the open attribute.')
    }
    dialog.setAttribute('open', '')
  }

  const closeDialog = (dialog) => {
    if (typeof dialog.close === 'function') dialog.close()
    else dialog.removeAttribute('open')
  }

  // =============================================================================================
  // AlertDialog — a destructive confirmation
  // =============================================================================================
  // [up-confirm] calls window.confirm, and Unpoly's hook for it (up.browser.assertConfirmed) is
  // called synchronously and never awaited, so a promise returned from an override is ignored — a
  // modal cannot be substituted there. Hence a separate attribute Unpoly does not know about:
  // the click is caught in the capture phase before Unpoly sees it, and re-issued once answered.
  //
  // [data-confirm] is NOT a free name — Unpoly reads it as an alias of [up-confirm].

  const ALERT_DIALOG_PANEL =
    'bg-background rounded-lg border p-6 shadow-lg w-[min(28rem,calc(100vw-2rem))] ' +
    'flex flex-col gap-4'

  function alertDialog(message, options = {}) {
    const confirmText = options.confirmText ?? config().confirmText ?? 'OK'
    const cancelText = options.cancelText ?? config().cancelText ?? 'Cancel'
    return new Promise((resolve) => {
      const dialog = el('dialog', '', { })
      dialog.dataset.slot = 'alert-dialog'

      // The <dialog> is the frame; this is the box shadcn calls AlertDialogContent. Same split
      // as the Razor <Dialog>, so one CSS rule dresses both.
      const panel = el('div', ALERT_DIALOG_PANEL)
      panel.dataset.slot = 'alert-dialog-content'
      const header = el('div', 'flex flex-col gap-2 text-center sm:text-left')
      header.dataset.slot = 'alert-dialog-header'
      const title = el('p', 'text-muted-foreground text-sm', { textContent: message })
      title.dataset.slot = 'alert-dialog-description'
      header.append(title)

      const footer = el('div', 'flex flex-col-reverse gap-2 sm:flex-row sm:justify-end')
      footer.dataset.slot = 'alert-dialog-footer'

      // data-ask-yes / data-ask-no name the two answers. Both buttons carry data-slot="button"
      // and differ only by variant, so without these a test — or a keyboard shortcut — would have
      // to tell them apart by their Vietnamese labels.
      const cancel = el('button', BUTTON.outline, { type: 'button', textContent: cancelText })
      cancel.dataset.slot = 'button'
      cancel.dataset.variant = 'outline'
      cancel.dataset.askNo = ''
      const confirm = el('button', BUTTON.destructive, { type: 'button', textContent: confirmText })
      confirm.dataset.slot = 'button'
      confirm.dataset.variant = 'destructive'
      confirm.dataset.askYes = ''
      footer.append(cancel, confirm)

      panel.append(header, footer)
      dialog.append(panel)
      document.body.append(dialog)

      const answer = (ok) => { closeDialog(dialog); dialog.remove(); resolve(ok) }
      confirm.addEventListener('click', () => answer(true))
      cancel.addEventListener('click', () => answer(false))
      // Escape and the backdrop both mean no. <dialog> fires `cancel` for Escape by itself.
      dialog.addEventListener('cancel', (e) => { e.preventDefault(); answer(false) })
      dialog.addEventListener('click', (e) => { if (e.target === dialog) answer(false) })

      showDialog(dialog)         // native: focus trap, inert background, top layer
      cancel.focus()              // the safe answer, not the destructive one
    })
  }

  shadcnCompiler('[data-ask]', (element) => {
    const onClick = async (event) => {
      // A second click — the one this handler issues itself after a yes.
      if (element.dataset.asked) return

      event.preventDefault()
      event.stopImmediatePropagation()      // Unpoly's own click handler must not run yet

      const message = element.dataset.ask
      if (!message || await alertDialog(message)) {
        element.dataset.asked = '1'
        element.click()                     // now it means: follow the link, submit the form
        delete element.dataset.asked
      }
    }

    element.addEventListener('click', onClick, true)
    return () => element.removeEventListener('click', onClick, true)
  })

  // =============================================================================================
  // Dialog / Sheet — <dialog> does the hard parts
  // =============================================================================================
  // showModal() gives the focus trap, the inert background, Escape and the top layer. None of
  // that is worth reimplementing, and all of it is what Radix spends most of its bytes on.

  // alert-dialog-trigger belongs here too. It was missing, and an attribute selector is exact:
  // [data-slot="dialog-trigger"] does not match alert-dialog-trigger, so every AlertDialog in
  // the library was inert — the button rendered, nothing bound to it, and no error said so.
  shadcnCompiler('[data-slot="dialog-trigger"], [data-slot="sheet-trigger"], '
            + '[data-slot="alert-dialog-trigger"], '
            + '[data-slot="attachment-trigger"][data-target]', (trigger) => {
    const open = (event) => {
      const target = document.getElementById(trigger.dataset.target)
      if (!target) return
      event.preventDefault()
      showDialog(target)
    }
    trigger.addEventListener('click', open)
    return () => trigger.removeEventListener('click', open)
  })

  // A close button anywhere inside the dialog, including the corner X. [data-dialog-close] is
  // the attribute a component sets when it wants this behaviour under a different slot name —
  // AlertDialogCancel does, and its own comment said so while nothing bound it.
  // drawer-close is on this list because it was not, and nothing else bound it: the button
  // relied on formmethod="dialog", which only acts on a submit button that HAS a form owner, and
  // no drawer in the demo is inside a form. So every Close and Cancel in a drawer did nothing.
  shadcnCompiler('[data-slot="dialog-close"], [data-slot="sheet-close"], ' +
              '[data-slot="drawer-close"], [data-dialog-close]',
              (button) => {
    const close = (event) => { event.preventDefault(); const dialog = button.closest('dialog'); if (dialog) closeDialog(dialog) }
    button.addEventListener('click', close)
    return () => button.removeEventListener('click', close)
  })

  // The page must not scroll behind a modal. The platform makes the background inert to CLICKS
  // and to the keyboard, but the wheel still scrolls it — so the dialog sat pinned while the
  // page slid past behind it, which nothing built on Radix does because Radix locks the scroll.
  // Counted rather than toggled: drawers nest, and the first one to close must not unlock the
  // page while another is still open.
  shadcnCompiler('dialog[data-slot="dialog"], dialog[data-slot="alert-dialog"], ' +
              'dialog[data-slot="command-dialog"], dialog[data-slot="sheet"], ' +
              'dialog[data-slot="drawer"]', (dialog) => {
    const onToggle = () => {
      // A non-modal drawer does not lock anything: staying usable is the whole point of it.
      if (dialog.dataset.modal === 'false') return
      lockScroll(isOpen(dialog))
    }

    const watch = new MutationObserver(onToggle)
    watch.observe(dialog, { attributes: true, attributeFilter: ['open'] })
    if (isOpen(dialog)) onToggle()

    return () => {
      watch.disconnect()
      if (isOpen(dialog) && dialog.dataset.modal !== 'false') lockScroll(false)
    }
  })

  // Dismissable rides on data-dismissable, which the component always renders as "true" or
  // "false" (raw booleans cannot be used: Blazor drops a false attribute from the DOM, so an
  // absent attribute would be indistinguishable from "attribute not supported"). The <dialog>
  // element IS the backdrop area, so a click whose target is the dialog itself — rather than
  // the panel inside it — landed outside. A non-dismissable dialog must be answered: neither
  // the backdrop nor Escape closes it, only its buttons.
  shadcnCompiler('dialog[data-dismissable]', (dialog) => {
    const dismissable = () => !/^false$/i.test(dialog.dataset.dismissable || '')
    const onClick = (event) => { if (dismissable() && event.target === dialog) closeDialog(dialog) }
    const onCancel = (event) => { if (!dismissable()) event.preventDefault() }
    dialog.addEventListener('click', onClick)
    dialog.addEventListener('cancel', onCancel)
    return () => {
      dialog.removeEventListener('click', onClick)
      dialog.removeEventListener('cancel', onCancel)
    }
  })

  // =============================================================================================
  // Tabs
  // =============================================================================================
  // No request, no history: this is a local view switch. A tab that should be linkable is a link
  // with [up-target] instead, and needs none of this.

  // Which physical edge "start" and "end" mean. Everything below places by reading order, not by
  // screen order — a dropdown aligned to the start of its trigger hangs off the RIGHT edge in
  // Arabic, and aligning it left there is the same bug as writing ml-auto in a stylesheet.
  const rtl = (element) => getComputedStyle(element).direction === 'rtl'

  // The box position:fixed is laid out in, in viewport coordinates. Measured, because nothing
  // reports it: clientWidth and innerWidth both still count the reserved scrollbar gutter.
  function fixedBox() {
    const probe = document.createElement('div')
    probe.style.cssText = 'position:fixed;inset:0;visibility:hidden;pointer-events:none'
    document.body.appendChild(probe)
    const box = probe.getBoundingClientRect()
    probe.remove()
    return box
  }

  // A panel's laid-out size. Not getBoundingClientRect(): an opening panel is mid-animation,
  // scaled from 0.96, so its rectangle is four percent short — a menu opening upward covered
  // the bottom of its own trigger by exactly that much, and one aligned by subtracting its
  // width came out short by the same amount on the other side.
  const sizeOf = (panel) => ({ width: panel.offsetWidth, height: panel.offsetHeight })

  // A [popover] gets `inset: 0; margin: auto` from the UA stylesheet, so setting only `left`
  // leaves it over-constrained — and which of the two the browser then ignores depends on the
  // writing direction. In LTR it drops `right` and the panel lands where you asked; in RTL it
  // drops LEFT, and every menu in the library sat against the far edge of the window instead of
  // against its trigger. Releasing the other three insets is what makes the number mean
  // something. This was invisible in English, which is why it survived.
  //
  // Every caller works in viewport coordinates — the numbers getBoundingClientRect() gives — and
  // passes a left edge. put() moves them into the box position:fixed is measured against, which
  // is not the viewport: scrollbar-gutter:stable (set above, for the scroll lock) takes the
  // gutter out of it, on the right in LTR and on the left in RTL. A panel pinned by `right`
  // against clientWidth landed a gutter's width short of its trigger.
  function put(panel, edge, top) {
    const box = fixedBox()
    panel.style.insetInline = 'auto'
    panel.style.insetBlock = 'auto'
    if (edge.left !== undefined) panel.style.left = `${Math.round(edge.left - box.left)}px`
    if (edge.right !== undefined) panel.style.right = `${Math.round(edge.right)}px`
    panel.style.top = `${Math.round(top - box.top)}px`

    // The native top layer uses the viewport as the fixed-position containing block.
    // On Safari 15 the popover polyfill marks the open panel with this class but
    // leaves it in the DOM: a transformed ancestor can then become its containing
    // block. CSS left/top still report the requested viewport coordinates while
    // the actual rectangle is offset by that ancestor (sidebar previews do this).
    // Correct the measured displacement only on the polyfilled path, preserving
    // native positioning and the existing left/right alignment logic.
    if (panel.classList.contains(':popover-open')) {
      const actual = panel.getBoundingClientRect()
      const wantedLeft = edge.left !== undefined
        ? Math.round(edge.left)
        : document.documentElement.clientWidth - Math.round(edge.right) - actual.width
      const dx = actual.left - wantedLeft
      const dy = actual.top - Math.round(top)
      if (Math.abs(dx) > 0.5) {
        if (edge.left !== undefined) panel.style.left = `${Math.round(parseFloat(panel.style.left) - dx)}px`
        else panel.style.right = `${Math.round(parseFloat(panel.style.right) + dx)}px`
      }
      if (Math.abs(dy) > 0.5) panel.style.top = `${Math.round(parseFloat(panel.style.top) - dy)}px`
    }
  }

  let tabsSeq = 0

  shadcnCompiler('[data-slot="tabs"]', (root) => {
    // Tabs nest: this documentation page puts every example inside its own Preview/Code tabs, so
    // an example that is itself a tab strip has one Tabs inside another. Both compilers see the
    // same bubbled event, so each one has to check that the trigger is ITS OWN — mine, not merely
    // somewhere beneath me. Without that check the outer strip handled the inner strip's arrow
    // key, could not find the trigger in its own list, and moved focus to its own first tab.
    const seq = ++tabsSeq
    const mine = (el) => el?.closest('[data-slot="tabs"]') === root
    const triggers = [...root.querySelectorAll('[data-slot="tabs-trigger"]')].filter(mine)
    const panels = [...root.querySelectorAll('[data-slot="tabs-content"]')].filter(mine)
    if (triggers.length === 0) return

    // A tab and its panel have to name each other, or a screen reader can reach the strip and
    // never reach what it opens. The ids are generated because the pairing is already known here
    // and a caller should not have to invent two of them per tab.
    triggers.forEach((trigger, i) => {
      const panel = panels.find((p) => p.dataset.value === trigger.dataset.value)
      if (!panel) return
      // Numbered per strip, not per page: without the counter a second Tabs with no id of
      // its own minted tabs-tab-0 all over again, and aria-controls pointed into the first.
      trigger.id ||= `${root.id || 'tabs-' + seq}-tab-${i}`
      panel.id ||= `${root.id || 'tabs-' + seq}-panel-${i}`
      trigger.setAttribute('aria-controls', panel.id)
      panel.setAttribute('aria-labelledby', trigger.id)
    })

    const show = (value) => {
      for (const t of triggers) t.dataset.state = t.dataset.value === value ? 'active' : 'inactive'
      for (const t of triggers) t.setAttribute('aria-selected', String(t.dataset.value === value))
      for (const t of triggers) t.tabIndex = t.dataset.value === value ? 0 : -1
      for (const p of panels) p.hidden = p.dataset.value !== value
    }

    const onClick = (event) => {
      const trigger = event.target.closest('[data-slot="tabs-trigger"]')
      if (mine(trigger)) show(trigger.dataset.value)
    }

    // Arrow keys move between tabs, which is the part of the APG pattern people actually notice.
    const onKey = (event) => {
      const trigger = event.target.closest('[data-slot="tabs-trigger"]')
      if (!mine(trigger)) return
      const forward = rtl(root) ? 'ArrowLeft' : 'ArrowRight'
      const back = rtl(root) ? 'ArrowRight' : 'ArrowLeft'
      const step = { [back]: -1, [forward]: 1, Home: -Infinity, End: Infinity }[event.key]
      if (step === undefined) return
      event.preventDefault()
      const from = triggers.indexOf(trigger)
      const to = Math.max(0, Math.min(triggers.length - 1, step === Infinity ? triggers.length - 1
        : step === -Infinity ? 0 : from + step))
      // Activate first, then focus: show() rewrites every tabIndex, and setting -1 on the element
      // that is being focused in the same tick is how the focus ended up on the document.
      show(triggers[to].dataset.value)
      triggers[to].focus()
    }

    root.addEventListener('click', onClick)
    root.addEventListener('keydown', onKey)
    show(root.dataset.value || triggers[0].dataset.value)

    return () => {
      root.removeEventListener('click', onClick)
      root.removeEventListener('keydown', onKey)
    }
  })

  // =============================================================================================
  // data-state on a [popover]
  // =============================================================================================
  // The same problem as <dialog>, and the same fix. shadcn animates every menu and popover with
  // data-[state=open] and data-[state=closed], which Radix sets as it mounts. A [popover] has an
  // open state but no attribute for it, so those classes matched nothing and panels appeared and
  // vanished with no transition at all.
  //
  // The trigger gets it too, because a chevron that turns when its menu opens is written
  // `group-data-[state=open]:rotate-180` — the group is the trigger, not the panel.

  shadcnCompiler('[popover][data-slot]', (panel) => {
    const trigger = document.querySelector(`[popovertarget="${panel.id}"]`)

    const onToggle = (event) => {
      const open = event.newState === 'open'
      panel.dataset.state = open ? 'open' : 'closed'
      if (trigger) trigger.dataset.state = open ? 'open' : 'closed'
    }

    if (panel.matches(':popover-open')) panel.dataset.state = 'open'
    panel.addEventListener('toggle', onToggle)
    return () => panel.removeEventListener('toggle', onToggle)
  })

  // =============================================================================================
  // DropdownMenu / Popover — the popover API does the hard parts
  // =============================================================================================
  // [popover] gives the top layer, light dismiss and Escape. What it does not give portably yet is
  // anchor positioning (`position-area` is not everywhere), so that much is measured here.

  function place(panel, anchor, align = 'start', side = 'bottom', offset = 4) {
    const a = anchor.getBoundingClientRect()

    // What the panel's own class string is written against, and it has to be set BEFORE the
    // panel is measured or the placement below works from a width that is about to change.
    // shadcn sizes a combobox list with `w-(--anchor-width)` and caps menus with
    // `max-h-(--available-height)`; Base UI's positioner supplies those, and nothing here did —
    // so the class resolved to nothing, the list kept whatever width it happened to have, and it
    // stood out past the side of the box it belongs to.
    const viewport = fixedBox()
    const room = viewport.right
    panel.style.setProperty('--anchor-width', `${Math.round(a.width)}px`)
    panel.style.setProperty('--available-width', `${Math.round(viewport.width) - 16}px`)
    panel.style.setProperty('--available-height',
      `${Math.max(120, Math.round(Math.max(a.top, window.innerHeight - a.bottom) - offset - 8))}px`)

    const p = sizeOf(panel)
    const flip = rtl(anchor)
    const floor = viewport.left

    // inline-start and inline-end are sides too. They fell through to the bottom branch in
    // silence, which is what the RTL samples ask for and what they were not getting.
    if (side === 'inline-start') side = flip ? 'right' : 'left'
    else if (side === 'inline-end') side = flip ? 'left' : 'right'

    // Beside, rather than above or below. Only top and bottom were handled, so a panel asking
    // for left or right silently got bottom -- the side was in the markup, in the docs and in
    // the data attribute, and nothing anywhere did it.
    if (side === 'left' || side === 'right') {
      const wantsLeft = side === 'left'
      let top = Math.max(8, Math.min(a.top + (a.height - p.height) / 2,
                                     window.innerHeight - p.height - 8))
      // Flip to the other side when the chosen one has no room, as every popover library does.
      const fitsLeft = a.left - p.width - offset >= floor + 8
      const fitsRight = a.right + p.width + offset <= room - 8
      const left = (wantsLeft && fitsLeft) || (!wantsLeft && !fitsRight)
      panel.dataset.placedSide = left ? 'left' : 'right'
      put(panel, { left: left ? a.left - offset - p.width : a.right + offset }, top)
      return
    }

    let top = side === 'top' ? a.top - p.height - offset : a.bottom + offset
    // Where it ENDED UP, which is not always where it asked to be. data-side stays the request,
    // because every caller reads it back on the next open; the arrow needs the outcome.
    panel.dataset.placedSide = side === 'top' ? 'top' : 'bottom'
    if (top + p.height > window.innerHeight - 8) {
      top = Math.max(8, a.top - p.height - offset)
      panel.dataset.placedSide = 'top'
    }
    // And the other edge. Only the bottom was checked, so a panel asking for the top near the
    // top of the window was given a negative offset and drawn off the screen entirely.
    if (top < 8) {
      top = Math.min(a.bottom + offset, window.innerHeight - p.height - 8)
      panel.dataset.placedSide = 'bottom'
    }

    // The width is the laid-out one (see sizeOf), with --anchor-width already applied above, so
    // an end-aligned edge can be derived from it: the right edge of the trigger, less the panel.
    const toStart = flip ? a.right - p.width : a.left
    const toEnd = flip ? a.left : a.right - p.width
    const want = align === 'center' ? a.left + (a.width - p.width) / 2
      : align === 'end' ? toEnd : toStart

    // Stay on screen. A menu half off an edge is a menu with unreachable items.
    put(panel, { left: Math.max(floor + 8, Math.min(want, room - p.width - 8)) }, top)
  }

  // Every trigger that opens a panel anchored to itself: a dropdown, a popover, a submenu row,
  // and a menubar's own buttons. They differ in where the panel goes, not in how it opens.
  shadcnCompiler('[data-slot="dropdown-menu-trigger"], [data-slot="popover-trigger"], ' +
              '[data-slot="dropdown-menu-sub-trigger"], [data-slot="menubar-trigger"], ' +
              '[data-slot="menubar-sub-trigger"], [data-slot="context-menu-sub-trigger"], ' +
              // The navigation menu was missing from this list, and nothing else placed it: its
              // panel fell back to the popover's own idea of where to be, which was full width
              // and two hundred pixels below the bar it belongs to.
              '[data-slot="navigation-menu-trigger"], ' +
              // A sidebar row that opens a menu is the most common sidebar pattern there is —
              // the workspace switcher and the account menu are both one — and it was not on
              // this list, so both opened wherever the popover's own default put them.
              '[data-slot="sidebar-menu-button"][data-target]',
              (trigger) => {
    const panel = document.getElementById(trigger.dataset.target)
    if (!panel) return

    // Radix lets a popover be positioned against something other than its trigger. Same idea:
    // the nearest [data-slot=popover-anchor] wins if there is one.
    const anchor = trigger.closest('[data-slot="popover"]')
      ?.querySelector('[data-slot="popover-anchor"]') || trigger

    const onToggle = (event) => {
      if (event.newState !== 'open') { trigger.setAttribute('aria-expanded', 'false'); return }
      trigger.setAttribute('aria-expanded', 'true')
      // A submenu opens BESIDE its row, not below it — otherwise it lands on top of the parent
      // menu and the row you came from disappears underneath it.
      const settle = () => {
        if (trigger.getAttribute('data-slot').endsWith('sub-trigger')) placeBeside(panel, trigger)
        else place(panel, anchor, panel.dataset.align, panel.dataset.side,
                   Number(panel.dataset.sideOffset || 4))
      }

      settle()
      // Again next frame. A panel whose width answers a media query — the navigation menu is
      // w-full below md and w-auto above it — is still full width when the first measurement is
      // taken, so the clamp that keeps it on screen slid it to the left edge of the window.
      // Placing twice costs one layout read and needs no guess about which panels do that.
      requestAnimationFrame(settle)
      panel.focus({ preventScroll: true })
    }

    // A dropdown pinned to a trigger that has scrolled away is worse than one that closed — but
    // only a scroll that MOVED the trigger counts. The listener is on the window with capture,
    // so it hears every element's scroll, and a chat transcript scrolling in the corner of the
    // page was closing menus that had nothing to do with it.
    const onScroll = (event) => {
      if (!panel.matches(':popover-open')) return
      const scroller = event?.target
      const moved = !scroller || scroller === document || scroller === window
        || (scroller.contains?.(trigger) && scroller !== panel && !panel.contains(scroller))
      if (moved) panel.hidePopover()
    }

    panel.addEventListener('toggle', onToggle)
    window.addEventListener('scroll', onScroll, { passive: true, capture: true })
    window.addEventListener('resize', onScroll, { passive: true })

    return () => {
      panel.removeEventListener('toggle', onToggle)
      window.removeEventListener('scroll', onScroll, true)
      window.removeEventListener('resize', onScroll)
    }
  })

  // A site header opens on hover. Clicking one is the behaviour of a menu you are choosing FROM;
  // a navigation bar is one you are reading ACROSS, and requiring a click there costs a second
  // click to get out of it again. shadcn opens on hover and this did not, which made every panel
  // on the bar two interactions away instead of none.
  //
  // The panel is rendered inside its <li>, so it is a DOM descendant of the bar even while it is
  // painted in the top layer — which is what lets one pair of listeners on the bar cover both the
  // trigger and the panel, and what makes moving from one into the other not a leave at all.
  shadcnCompiler('[data-slot="navigation-menu"]', (root) => {
    const triggers = () => [...root.querySelectorAll('[data-slot="navigation-menu-trigger"]')]
    const panelOf = (trigger) => document.getElementById(trigger.dataset.target)
    const openPanels = () => triggers().map(panelOf).filter((p) => p?.matches(':popover-open'))
    let opening, closing

    const over = (event) => {
      clearTimeout(closing)
      const trigger = event.target.closest('[data-slot="navigation-menu-trigger"]')
      if (!trigger) {
        // Something in the bar with no panel of its own — a plain link, the gap between two
        // items. Arriving there is leaving whatever is open, and it used to leave it open with
        // the pointer nowhere near it. Not while inside a panel: that is where the pointer goes
        // to use the thing it just opened.
        if (!event.target.closest('[data-slot="navigation-menu-content"]')) {
          clearTimeout(opening)
          openPanels().forEach((panel) => panel.hidePopover())
        }
        return
      }
      clearTimeout(opening)
      const panel = panelOf(trigger)
      if (!panel || panel.matches(':popover-open')) return
      // Instant while a panel is already open, delayed while none is. Crossing the bar on the way
      // to somewhere else should not flash five panels; moving ALONG it should not stutter.
      opening = setTimeout(() => panel.showPopover(), openPanels().length ? 0 : 150)
    }

    // Leaving for something outside the bar closes it, after long enough to cross the gap between
    // a trigger and the panel under it — a menu that shuts in that gap is a menu you cannot use.
    //
    // :hover rather than relatedTarget alone. Crossing from a trigger into its panel is a move
    // into the top layer, and a browser may report that with no relatedTarget at all — which read
    // as "left the bar" and closed the panel the pointer had just entered. The panel is a DOM
    // child of the bar however it is painted, so asking the bar whether it is still hovered is
    // the question that has the same answer in every browser.
    const out = (event) => {
      if (event.relatedTarget && root.contains(event.relatedTarget)) return
      clearTimeout(opening)
      clearTimeout(closing)
      closing = setTimeout(() => {
        if (root.matches(':hover')) return
        openPanels().forEach((p) => p.hidePopover())
      }, 200)
    }

    root.addEventListener('pointerover', over)
    root.addEventListener('pointerout', out)

    return () => {
      clearTimeout(opening)
      clearTimeout(closing)
      root.removeEventListener('pointerover', over)
      root.removeEventListener('pointerout', out)
    }
  })

  // A submenu opens away from the parent along the reading direction, and falls back to the
  // other side when there is no room. Both halves flip under RTL: opening to the right in
  // Arabic puts the submenu on top of the menu it came from.
  function placeBeside(panel, row) {
    const a = row.getBoundingClientRect()
    const p = sizeOf(panel)
    const room = fixedBox().right
    const flip = rtl(row)
    let left = flip ? a.left - p.width + 4 : a.right - 4
    if (flip) {
      if (left < 8) left = Math.min(a.right - 4, room - p.width - 8)
    } else if (left + p.width > room - 8) {
      left = Math.max(8, a.left - p.width + 4)
    }
    put(panel, { left }, Math.min(a.top - 4, window.innerHeight - p.height - 8))
  }

  // Keyboard within an open menu, for all three families. Radix roves focus; so does this.
  // The key that opens a submenu is the one pointing along the reading direction — Right in
  // English, Left in Arabic — and the other one closes it. The APG says so, and hard-coding
  // Right leaves an RTL user opening a submenu with the key that visually points back at the
  // menu they came from.
  shadcnCompiler('[data-slot="dropdown-menu-content"], [data-slot="context-menu-content"], ' +
              '[data-slot="menubar-content"], [data-slot="dropdown-menu-sub-content"], ' +
              '[data-slot="context-menu-sub-content"], [data-slot="menubar-sub-content"]',
              (panel) => {
    const onKey = (event) => {
      const forward = rtl(panel) ? 'ArrowLeft' : 'ArrowRight'
      const back = rtl(panel) ? 'ArrowRight' : 'ArrowLeft'
      if (event.key === forward && document.activeElement?.matches('[aria-haspopup=menu]')) {
        event.preventDefault()
        document.activeElement.click()
        return
      }
      if (event.key === back && panel.getAttribute('data-slot').includes('sub-content')) {
        event.preventDefault()
        panel.hidePopover()
        return
      }
      const items = [...panel.querySelectorAll('[data-slot$="-item"]:not([data-disabled]), ' +
                                               '[data-slot$="-sub-trigger"]')]
      if (items.length === 0) return
      const from = items.indexOf(document.activeElement)
      if (event.key === 'ArrowDown') { event.preventDefault(); items[(from + 1) % items.length].focus() }
      else if (event.key === 'ArrowUp') { event.preventDefault(); items[(from - 1 + items.length) % items.length].focus() }
      else if (event.key === 'Home') { event.preventDefault(); items[0].focus() }
      else if (event.key === 'End') { event.preventDefault(); items[items.length - 1].focus() }
    }
    // Hovering a sub-trigger opens its submenu, as Radix does — a menu is a pointer surface,
    // and needing a CLICK to open a submenu reads as broken. Hovering a plain item closes any
    // submenu hanging off this panel, so moving down the list retracts what you passed.
    let hoverTimer
    // Where the pointer actually is when the retract timer fires. Crossing the parent's other
    // rows on the way to an open submenu is the ordinary diagonal path, and it was enough to
    // retract the submenu out from under the pointer just before it arrived — the report was
    // "moving into the submenu loses the menu", and it is this.
    let at = { x: 0, y: 0 }
    const owned = () => [...panel.querySelectorAll('[data-slot$="-sub-trigger"]')]
      .map((trigger) => document.getElementById(trigger.dataset.target))
      .filter((child) => child && child.matches(':popover-open'))
    const pointerInSubmenu = () => {
      const under = document.elementFromPoint(at.x, at.y)
      return !!under && owned().some((child) => child.contains(under))
    }

    const onOver = (event) => {
      at = { x: event.clientX, y: event.clientY }
      const sub = event.target.closest('[data-slot$="-sub-trigger"]')
      clearTimeout(hoverTimer)
      if (sub && panel.contains(sub)) {
        hoverTimer = setTimeout(() => {
          const child = document.getElementById(sub.dataset.target)
          if (child && !child.matches(':popover-open')) sub.click()
        }, 100)
        return
      }
      const item = event.target.closest('[data-slot$="-item"]')
      // Focus follows the pointer, as Radix's data-highlighted does: the row lights up under
      // the mouse, and the arrow keys continue from wherever the pointer left off. Without
      // this only Tab and the arrows highlighted anything, and the menu read as inert.
      if (item && panel.contains(item) && !item.hasAttribute('data-disabled')) item.focus()
      if (item && item.parentElement?.closest('[data-slot]') !== null) {
        hoverTimer = setTimeout(() => {
          // Not if the pointer has already reached the submenu. It is a sibling in the top
          // layer rather than a descendant of this panel, so no containment check on the event
          // can see it — where the pointer IS can.
          if (pointerInSubmenu()) return
          for (const child of owned()) child.hidePopover()
        }, 150)
      }
    }

    // Leaving the menu unlights the row, as Radix does. Highlighting here IS focus, so the row
    // keeps it until something else takes it -- which left a menu you had merely passed through
    // showing a lit row with the pointer nowhere near it. Focus goes back to the panel, so the
    // arrow keys still start from the top and nothing traps it outside the menu.
    const onLeave = (event) => {
      if (event.relatedTarget && panel.contains(event.relatedTarget)) return
      // Leaving INTO this panel's own submenu is not leaving the menu: the row that opened it
      // has to stay lit, or the trail back to where you came from goes out behind you.
      if (event.relatedTarget && owned().some((child) => child.contains(event.relatedTarget))) return
      const item = panel.contains(document.activeElement) ? document.activeElement : null
      if (item && item !== panel) panel.focus({ preventScroll: true })
    }

    // Somewhere for focus to go when the pointer leaves. A [popover] is not focusable on its
    // own, and focus with nowhere to land falls to <body> -- which closes the menu, because
    // light dismiss treats that as focus leaving.
    panel.tabIndex = -1

    panel.addEventListener('keydown', onKey)
    panel.addEventListener('pointerover', onOver)
    panel.addEventListener('pointerleave', onLeave)
    return () => {
      panel.removeEventListener('keydown', onKey)
      panel.removeEventListener('pointerover', onOver)
      panel.removeEventListener('pointerleave', onLeave)
      clearTimeout(hoverTimer)
    }
  })

  // Choosing an item closes the menu it is in, and any menu that opened it. Nothing did this:
  // the panel stayed open after a click, so picking "CSV" left the menu sitting there and read
  // as "the dropdown will not let me choose". Radix closes on select and so does every other
  // menu; a sub-trigger is the exception, because its whole job is to open the next panel.
  shadcnCompiler('[data-slot="dropdown-menu-content"], [data-slot="context-menu-content"], ' +
              '[data-slot="menubar-content"], [data-slot="dropdown-menu-sub-content"], ' +
              '[data-slot="context-menu-sub-content"], [data-slot="menubar-sub-content"]',
              (panel) => {
    const onClick = (event) => {
      const item = event.target.closest('[data-slot$="-item"]')
      if (!item || item.closest('[data-slot$="-sub-trigger"]') || item.hasAttribute('data-disabled')) return
      // A row that carries STATE is not a row that performs an action: ticking one is meant to
      // be repeatable, and closing the menu after each tick made "select several" mean "open the
      // menu three times". Base UI's CheckboxItem and RadioItem default to closeOnClick: false
      // for the same reason.
      if (item.matches('[data-slot$="-checkbox-item"], [data-slot$="-radio-item"]')) return
      // The chain, not just this panel: an item in a submenu closes the menu it hangs off too.
      // Every family listed by NAME — the first version said [data-slot$="menu-content"], and
      // "menubar-content" does not end in "menu-content", so a menubar item never closed its
      // menu. An ends-with shortcut that reads as covering a family is worse than a list.
      for (const open of document.querySelectorAll(
             '[data-slot="dropdown-menu-content"]:popover-open, '
             + '[data-slot="context-menu-content"]:popover-open, '
             + '[data-slot="menubar-content"]:popover-open, '
             + '[data-slot$="sub-content"]:popover-open')) {
        open.hidePopover()
      }
    }
    panel.addEventListener('click', onClick)
    return () => panel.removeEventListener('click', onClick)
  })

  // =============================================================================================
  // Calendar range — two ticks, and everything between them
  // =============================================================================================
  // With scripting off a range calendar is a grid of checkboxes: tick the first day and the last,
  // and the server takes the min and the max. That already works, and this only makes it behave
  // the way a range picker should — a third click starts a new range, and the band between the
  // two ends fills in as you go. The boxes stay the state; nothing here remembers anything.

  shadcnCompiler('[data-slot="calendar"][data-mode="range"]', (calendar) => {
    const boxes = () => [...calendar.querySelectorAll('input[type=checkbox]')]
    const ticked = () => boxes().filter((b) => b.checked)
    const day = (box) => box.value

    const paint = () => {
      const on = ticked().map(day).sort()
      const [from, to] = [on[0], on[on.length - 1]]
      for (const box of boxes()) {
        const cell = box.closest('td')
        const inside = from && to && from !== to && day(box) > from && day(box) < to
        const end = from && to && from !== to && (day(box) === from || day(box) === to)
        cell.classList.toggle('bg-accent', Boolean(inside || end))
        cell.classList.toggle('rounded-s-md', day(box) === from && from !== to)
        cell.classList.toggle('rounded-e-md', day(box) === to && from !== to)
        // The days between are shown, not selected: only the two ends post.
        cell.toggleAttribute('data-in-range', Boolean(inside))
      }
    }

    const onChange = (event) => {
      const box = event.target
      if (!box.matches('input[type=checkbox]')) return
      const on = ticked()
      // A third tick starts again from the day just clicked, which is what every range picker
      // does and what a bare checkbox grid would not.
      if (box.checked && on.length > 2) {
        for (const other of on) if (other !== box) other.checked = false
      }
      paint()
    }

    calendar.addEventListener('change', onChange)
    paint()

    return () => calendar.removeEventListener('change', onChange)
  })

  // =============================================================================================
  // Select all — one box that owns a column of boxes
  // =============================================================================================
  // React keeps a Set in state and derives both directions from it. Here the boxes ARE the
  // state: the header box writes them, and reads itself back from them, so nothing can drift.
  // Indeterminate is a property rather than an attribute, which is why it is set here and not
  // in the markup — the "some but not all" state has no HTML spelling.

  shadcnCompiler('[data-select-all]', (master) => {
    const scope = master.closest('form, table, [data-select-scope]') || document
    const boxes = () => [...scope.querySelectorAll(
      `input[type=checkbox][name="${master.dataset.selectAll}"]`)].filter((b) => !b.disabled)

    const sync = () => {
      const all = boxes()
      const on = all.filter((b) => b.checked).length
      master.checked = on > 0 && on === all.length
      master.indeterminate = on > 0 && on < all.length
    }

    const onMaster = () => {
      for (const box of boxes()) box.checked = master.checked
      master.indeterminate = false
    }

    const onOne = (event) => { if (event.target.name === master.dataset.selectAll) sync() }

    master.addEventListener('change', onMaster)
    scope.addEventListener('change', onOne)
    sync()

    return () => {
      master.removeEventListener('change', onMaster)
      scope.removeEventListener('change', onOne)
    }
  })

  // =============================================================================================
  // Chart tooltip — the card Recharts draws on a canvas, drawn as an element instead
  // =============================================================================================
  // The chart is a table, so every number is already in the DOM. This adds the pointer view: one
  // card per chart, filled from the data attributes of whatever the pointer is over. Nothing is
  // measured and no series data lives in JavaScript — the markup carries it.

  shadcnCompiler('[data-slot="chart"]:has([data-slot="chart-tooltip"])', (chart) => {
    const card = chart.querySelector('[data-slot="chart-tooltip"]')

    const rows = (point) => {
      const series = (point.dataset.chartSeries || '').split(',').map((s) => s.trim())
      const values = (point.dataset.chartValue || '').split(',').map((s) => s.trim())
      const labels = (point.dataset.chartName || '').split(',').map((s) => s.trim())
      const indicator = card.dataset.indicator || 'dot'

      return series.map((key, i) => {
        // The swatch reads the same --color-KEY the bar is painted with, so a theme change moves
        // both at once and neither can be told the wrong colour.
        const shape = indicator === 'dot' ? 'h-2.5 w-2.5'
          : indicator === 'line' ? 'w-1'
          : 'w-0 border-[1.5px] border-dashed bg-transparent'
        const swatch = `<div data-chart-tooltip-indicator class="shrink-0 rounded-[2px] `
          + `${shape}" style="background:var(--color-${key});border-color:var(--color-${key})"></div>`
        const value = Number(values[i])
        const shown = Number.isFinite(value) ? value.toLocaleString() : (values[i] ?? '')
        return `<div class="flex w-full flex-wrap items-center gap-2">${swatch}`
          + `<div class="flex flex-1 items-center justify-between leading-none">`
          + `<span class="text-muted-foreground">${labels[i] || key}</span>`
          + `<span class="font-mono font-medium text-foreground tabular-nums">${shown}</span>`
          + `</div></div>`
      }).join('')
    }

    // Follows the POINTER, not the bar. Anchoring it to the middle of the column meant the card
    // sat a long way from the cursor on a wide bar and did not move as you swept across — which
    // reads as a tooltip belonging to something else. Recharts follows the cursor and so does
    // this; it flips to the other side near an edge so it never leaves the window.
    const show = (point, x, y) => {
      card.innerHTML = `<div data-chart-tooltip-label class="font-medium">`
        + `${point.dataset.chartLabel ?? ''}</div>`
        + `<div class="grid gap-1.5">${rows(point)}</div>`
      card.hidden = false
      const own = card.getBoundingClientRect()
      const gap = 12
      const left = x + gap + own.width > window.innerWidth - 8 ? x - gap - own.width : x + gap
      const top = y - own.height - gap < 8 ? y + gap : y - own.height - gap
      card.style.left = `${Math.round(Math.max(8, left))}px`
      card.style.top = `${Math.round(Math.max(8, top))}px`
    }

    const onOver = (event) => {
      const point = event.target.closest('[data-chart-point]')
      if (point && chart.contains(point)) show(point, event.clientX, event.clientY)
      else card.hidden = true
    }
    const onLeave = () => { card.hidden = true }

    chart.addEventListener('pointermove', onOver)
    chart.addEventListener('pointerleave', onLeave)

    return () => {
      chart.removeEventListener('pointermove', onOver)
      chart.removeEventListener('pointerleave', onLeave)
    }
  })

  // =============================================================================================
  // ContextMenu — the right button, and the two other ways to ask for the same thing
  // =============================================================================================
  // One `contextmenu` listener covers the right button, the context-menu key and a long press on
  // touch, because the platform fires the same event for all three. A right-click handler bolted
  // onto a div gets only the first, which is how context menus end up unreachable by keyboard.

  shadcnCompiler('[data-slot="context-menu"]', (root) => {
    const panel = document.getElementById(root.dataset.target)
    const trigger = root.querySelector('[data-slot="context-menu-trigger"]')
    if (!panel || !trigger) return

    const onContextMenu = (event) => {
      event.preventDefault()
      if (panel.matches(':popover-open')) panel.hidePopover()
      panel.showPopover()

      // The keyboard route carries no pointer position, and 0,0 from a key press would put the
      // menu in the corner of the screen rather than on the thing it belongs to.
      const box = sizeOf(panel)
      const room = fixedBox().right
      const side = panel.dataset.side

      if (side) {
        // side chooses which side of the CURSOR the menu opens on — it still belongs to the
        // click, exactly where the pointer is, and only the direction it grows in changes.
        // The first version anchored it to the trigger box, which put the menu nowhere near
        // the mouse: a context menu that opens away from the click reads as someone else's.
        const px = event.clientX || trigger.getBoundingClientRect().left
        const py = event.clientY || trigger.getBoundingClientRect().bottom
        // Two pixels put the pointer ON the first row, which opened the menu with that row
        // already highlighted and one flick away from being chosen. The gap is the panel's own
        // padding, so the pointer lands on the frame and the list starts unhighlighted.
        const gap = 6
        // Flip to the opposite side when the chosen one has no room. Clamping instead slid the
        // panel back UNDER the cursor, which is the one place it must not be: the row beneath
        // the pointer lights up the moment the menu appears, and a menu that opens with a row
        // already chosen is a menu one twitch away from choosing it.
        let at = side
        if (at === 'left' && px - box.width - gap < 8) at = 'right'
        else if (at === 'right' && px + box.width + gap > room - 8) at = 'left'
        else if (at === 'top' && py - box.height - gap < 8) at = 'bottom'
        else if (at === 'bottom' && py + box.height + gap > window.innerHeight - 8) at = 'top'

        const across = at === 'left' || at === 'right'
        const top = across ? Math.max(8, Math.min(py - box.height / 2, window.innerHeight - box.height - 8))
          : at === 'top' ? py - box.height - gap : py + gap
        const edge = at === 'left' ? { left: px - gap - box.width }
          : at === 'right' ? { left: px + gap }
          : { left: Math.max(8, Math.min(px - box.width / 2, room - box.width - 8)) }
        put(panel, edge, top)
        panel.dataset.placedSide = at
        trigger.setAttribute('aria-expanded', 'true')
        panel.focus({ preventScroll: true })
        return
      }

      const fromKeyboard = event.clientX === 0 && event.clientY === 0
      const at = fromKeyboard ? trigger.getBoundingClientRect() : null
      const x = at ? at.left : event.clientX
      const y = at ? at.bottom : event.clientY

      const left = rtl(trigger) ? x - box.width - 2 : x + 2
      put(panel, { left: Math.max(8, Math.min(left, room - box.width - 8)) },
          Math.min(y + 2, window.innerHeight - box.height - 8))
      trigger.setAttribute('aria-expanded', 'true')
      panel.focus({ preventScroll: true })
    }

    const onToggle = (event) => {
      // The page must not scroll under an open context menu — the panel is fixed, so scrolling
      // detaches it from the thing it belongs to. Radix locks the page; so does this.
      document.documentElement.style.overflow = event.newState === 'open' ? 'hidden' : ''
      if (event.newState !== 'open') trigger.setAttribute('aria-expanded', 'false')
    }

    trigger.addEventListener('contextmenu', onContextMenu)
    panel.addEventListener('toggle', onToggle)

    return () => {
      trigger.removeEventListener('contextmenu', onContextMenu)
      panel.removeEventListener('toggle', onToggle)
    }
  })

  // =============================================================================================
  // Menubar — what makes a bar of dropdowns a menubar
  // =============================================================================================
  // Two behaviours, and they are the whole difference. Left and Right move along the bar rather
  // than into the page. And once ANY menu is open, hovering another opens that one instead of
  // doing nothing — the thing that makes a menubar feel like one, and the only part of it that
  // is not free.

  shadcnCompiler('[data-slot="menubar"]', (bar) => {
    const triggers = () => [...bar.querySelectorAll('[data-slot="menubar-trigger"]')]
    const panelFor = (trigger) => document.getElementById(trigger.dataset.target)

    const openMenuFor = (trigger) => {
      const panel = panelFor(trigger)
      if (!panel || panel.matches(':popover-open')) return
      for (const other of triggers()) {
        if (other !== trigger) panelFor(other)?.hidePopover()
      }
      panel.showPopover()
    }

    const anyOpen = () => triggers().some((t) => panelFor(t)?.matches(':popover-open'))

    const onOver = (event) => {
      const trigger = event.target.closest?.('[data-slot="menubar-trigger"]')
      if (trigger && anyOpen()) openMenuFor(trigger)
    }

    const onKey = (event) => {
      const all = triggers()
      const at = all.indexOf(event.target.closest('[data-slot="menubar-trigger"]'))
      if (at < 0) return
      const forward = rtl(bar) ? 'ArrowLeft' : 'ArrowRight'
      const back = rtl(bar) ? 'ArrowRight' : 'ArrowLeft'
      if (event.key === forward) { event.preventDefault(); all[(at + 1) % all.length].focus() }
      else if (event.key === back) { event.preventDefault(); all[(at - 1 + all.length) % all.length].focus() }
      else if (event.key === 'ArrowDown') { event.preventDefault(); openMenuFor(all[at]) }
    }

    bar.addEventListener('pointerover', onOver)
    bar.addEventListener('keydown', onKey)

    return () => {
      bar.removeEventListener('pointerover', onOver)
      bar.removeEventListener('keydown', onKey)
    }
  })

  // =============================================================================================
  // Drawer — the one thing a sheet cannot do
  // =============================================================================================
  // Everything else about a drawer is <Sheet>: a native <dialog> for the top layer, the focus
  // trap, Escape and ::backdrop. What vaul adds, and the only reason it exists, is that you can
  // drag it shut — which on a phone is the gesture people reach for before they look for a close
  // button. Past a third of the panel it closes; short of that it springs back.
  //
  // Pointer events, not touch events: the same code then works for a mouse drag and a stylus,
  // and setPointerCapture means letting go outside the panel still ends the drag.

  shadcnCompiler('[data-slot="drawer-trigger"]', (trigger) => {
    const onClick = () => {
      const dialog = document.getElementById(trigger.dataset.open)
      if (!dialog) return
      // show(), not showModal(), when the drawer says it is not modal: no backdrop, no focus
      // trap, and the page behind stays usable. That is the whole of upstream's modal={false},
      // and it was written off as impossible because "showModal is modal by definition" — which
      // is true, and is why this calls the other method.
      showDialog(dialog, dialog.dataset.modal !== 'false')
    }
    trigger.addEventListener('click', onClick)
    return () => trigger.removeEventListener('click', onClick)
  })

  shadcnCompiler('dialog[data-slot="drawer"]', (dialog) => {
    const panel = dialog.querySelector('[data-slot="drawer-content"]')
    if (!panel) return

    const vertical = dialog.dataset.direction !== 'left' && dialog.dataset.direction !== 'right'
    const towards = dialog.dataset.direction === 'top' || dialog.dataset.direction === 'left' ? -1 : 1
    let from = null

    // Snap points, in pixels, smallest first. A bare number is a fraction of the viewport along
    // the axis the drawer travels — upstream's ["31rem", 1] is "a peek, then nearly all of it".
    // Resolved by asking the browser what the length means rather than parsing units here.
    const axis = () => (vertical ? window.innerHeight : window.innerWidth)
    const points = (dialog.dataset.snapPoints || '').split(/\s+/).filter(Boolean)

    // Measured on demand, against the BODY. Inside the dialog the probe measured zero, because a
    // closed <dialog> is display:none and nothing in it has a layout — so the panel opened one
    // pixel tall. And on demand rather than once, so a resized window still snaps to the right
    // fractions.
    const snaps = () => points.map((point) => {
      const asNumber = Number(point)
      if (!Number.isNaN(asNumber)) return asNumber * axis()
      const probe = document.createElement('div')
      probe.style.cssText =
        `position:absolute;visibility:hidden;pointer-events:none;${vertical ? 'height' : 'width'}:${point}`
      document.body.append(probe)
      const px = vertical ? probe.offsetHeight : probe.offsetWidth
      probe.remove()
      return px
    }).sort((a, b) => a - b)

    // The panel opens at the first snap point, and every later rest is written to the same
    // property — so the size is one number the drag moves, not a transform to unwind afterwards.
    const restAt = (px) => {
      panel.style[vertical ? 'height' : 'width'] = `${Math.round(px)}px`
      panel.style[vertical ? 'maxHeight' : 'maxWidth'] = 'none'
    }
    const onOpen = () => { if (points.length) restAt(snaps()[0]) }
    if (points.length) {
      dialog.addEventListener('close', () => { panel.style.height = panel.style.width = '' })
    }

    // Escape belongs to a MODAL dialog; a non-modal one is left to the page, so a drawer opened
    // with show() ignored it. The key is the same key either way to whoever is pressing it.
    const onEscape = (event) => {
      if (event.key === 'Escape' && isOpen(dialog) && dialog.dataset.modal === 'false') closeDialog(dialog)
    }
    if (dialog.dataset.modal === 'false') document.addEventListener('keydown', onEscape)

    const offset = (event) => (vertical ? event.clientY : event.clientX)

    const onDown = (event) => {
      // Not on a control: dragging must not steal a press meant for a button or a text field.
      if (event.target.closest('button, a, input, select, textarea, [contenteditable]')) return
      from = offset(event)
      grabbed = vertical ? panel.offsetHeight : panel.offsetWidth
      panel.setPointerCapture(event.pointerId)
      panel.style.transition = 'none'
    }

    let grabbed = 0

    const onMove = (event) => {
      if (from === null) return
      const moved = (offset(event) - from) * towards
      // With snap points the drag RESIZES the panel — both ways, because pulling it further open
      // is half the point of having them. Without them it can only be pushed shut.
      if (points.length) {
        const stops = snaps()
        restAt(Math.max(24, Math.min(grabbed - moved, stops[stops.length - 1])))
        return
      }
      if (moved <= 0) return                       // dragging further in does nothing
      panel.style.transform = vertical ? `translateY(${moved * towards}px)`
                                       : `translateX(${moved * towards}px)`
    }

    const onUp = (event) => {
      if (from === null) return
      const moved = (offset(event) - from) * towards
      const span = vertical ? panel.offsetHeight : panel.offsetWidth
      from = null
      panel.style.transition = ''

      if (points.length) {
        const stops = snaps()
        const now = vertical ? panel.offsetHeight : panel.offsetWidth
        // Below half the smallest rest, the gesture was "put it away" rather than "make it
        // smaller" — every drawer with snap points still has to be closeable by dragging.
        if (now < stops[0] / 2) { closeDialog(dialog); return }
        restAt(stops.reduce((best, point) =>
          Math.abs(point - now) < Math.abs(best - now) ? point : best, stops[0]))
        return
      }

      panel.style.transform = ''
      if (moved > span / 3) closeDialog(dialog)
    }

    // The first snap point is where it opens, so the panel is never full height for a frame
    // before shrinking to its rest.
    const watch = new MutationObserver(() => { if (isOpen(dialog)) onOpen() })
    watch.observe(dialog, { attributes: true, attributeFilter: ['open'] })
    if (isOpen(dialog)) onOpen()

    panel.addEventListener('pointerdown', onDown)
    panel.addEventListener('pointermove', onMove)
    panel.addEventListener('pointerup', onUp)
    panel.addEventListener('pointercancel', onUp)

    return () => {
      watch.disconnect()
      document.removeEventListener('keydown', onEscape)
      panel.removeEventListener('pointerdown', onDown)
      panel.removeEventListener('pointermove', onMove)
      panel.removeEventListener('pointerup', onUp)
      panel.removeEventListener('pointercancel', onUp)
    }
  })

  // =============================================================================================
  // Combobox — filter, choose, and post what was chosen
  // =============================================================================================
  // The value lives in a real <input type="hidden">, so the form posts it, a fragment swap cannot
  // lose it, and the server reads it with no JSON anywhere. Everything below is presentation over
  // that one fact.

  shadcnCompiler('[data-slot="combobox"]', (root) => {
    const panel = document.getElementById(root.dataset.target)
    if (!panel) return

    const multiple = root.dataset.multiple === 'true'
    // Free-form tags: text that matches no row becomes a chip of its own. Only meaningful with
    // chips, so a creatable root without Multiple is read as an ordinary combobox.
    const creatable = multiple && root.dataset.creatable === 'true'
    const input = root.querySelector('[data-slot="combobox-chip-input"], input[type="text"]')
    const trigger = root.querySelector('[data-slot="combobox-trigger"]')
    const value = root.querySelector('[data-slot="combobox-value"]')
    const clear = root.querySelector('[data-slot="combobox-clear"]')
    const hidden = root.querySelector('input[type="hidden"][data-combobox-value]')
    // Captured now, because the multiple branch REMOVES unchosen inputs -- once the last one is
    // gone there is nowhere left to read the field's name from.
    const listName = hidden?.name || ''
    const empty = panel.querySelector('[data-slot="combobox-empty"]')
    const items = () => [...panel.querySelectorAll('[data-slot="combobox-item"]')]

    const shown = () => items().filter((i) => !i.hidden)

    // Set while a choice is closing the list, so the focus that closing returns to the input
    // cannot open it again. See onFocus.
    let justChose = false

    // data-label when the row carries one, because textContent on a row with an avatar and a
    // description returns all three run together — and that string is what the trigger shows.
    const label = (item) => item.dataset.label || item.textContent.replace(/\s+/g, ' ').trim()

    // Read the state rather than remember it. The button was unhidden only by choose(), so a
    // value the SERVER rendered — the ordinary case after a fragment swap, or after a form comes
    // back with errors — left a chosen combobox with no way to clear it, and the multiple branch
    // never unhid it at all.
    const syncClear = () => {
      if (!clear) return
      clear.hidden = !([...root.querySelectorAll('input[type="hidden"][data-combobox-value]')].some((p) => p.value)
        || root.querySelector('[data-slot="combobox-chip"]'))
    }

    // The hidden input is the field the form actually posts and the server actually reads, so a
    // choice has to reach it as a real field event. A form that watches itself -- Unpoly's
    // up-autosubmit, a filter bar that reloads as you change it -- listens for change on the
    // form, and without this the page simply sat there until something unrelated happened.
    // Dispatched on the post itself so it bubbles from the field the reader actually filled.
    const announce = (field) => {
      if (!field) return
      field.dispatchEvent(new Event('input', { bubbles: true }))
      field.dispatchEvent(new Event('change', { bubbles: true }))
    }

    const valuePosts = () => [...root.querySelectorAll('input[type="hidden"][data-combobox-value]')]

    const announceAll = () => { for (const post of valuePosts()) announce(post) }

    const filter = () => {
      const q = fold((input?.value || '').trim())
      let count = 0
      for (const item of items()) {
        const hay = fold(item.textContent + ' ' + (item.dataset.keywords || ''))
        item.hidden = q ? !hay.includes(q) : false
        if (!item.hidden) count++
      }
      for (const group of panel.querySelectorAll('[data-slot="combobox-group"]')) {
        group.hidden = !group.querySelector('[data-slot="combobox-item"]:not([hidden])')
      }
      if (empty) empty.hidden = count > 0
    }

    const choose = (item) => {
      if (!multiple) {
        for (const other of items()) {
          const on = other === item
          other.dataset.selected = on ? 'true' : ''
          if (!on) delete other.dataset.selected
          other.setAttribute('aria-selected', String(on))
        }
        if (hidden) hidden.value = item.dataset.value
        announce(hidden)
        if (value) value.textContent = label(item)
        if (input && !input.matches('[data-slot="combobox-chip-input"]')) {
          // A search box inside the panel resets; with no ComboboxValue the text box IS the
          // display and takes the label. Either way the filter must not survive the choice --
          // a list opened tomorrow filtered by yesterday's label reads as items having
          // vanished. (The popup pattern has BOTH: the trigger shows the value, the panel
          // holds the search, and each gets its own line above.)
          input.value = panel.contains(input) ? '' : (value ? input.value : label(item))
          for (const other of items()) other.hidden = false
          for (const group of panel.querySelectorAll('[data-slot="combobox-group"]')) group.hidden = false
          if (empty) empty.hidden = true
        }
        syncClear()
        justChose = true
        panel.hidePopover()
        trigger?.focus()
        // Cleared once that focus has been and gone. Clicking the box later is a new gesture and
        // opens the list as usual.
        setTimeout(() => { justChose = false }, 0)
        return
      }

      // Multiple: one hidden input per chosen value, all under the same name, which is what the
      // server reads as a list. Choosing an already-chosen row removes it.
      const chips = root.querySelector('[data-slot="combobox-chips"]')
      if (!chips) {
        // Multiple without chips: the trigger keeps a count and the list keeps the ticks. The
        // hidden inputs still toggle one per value -- this branch used to return without doing
        // anything, which read as "multiple select is broken", because it was.
        const posts = () => [...root.querySelectorAll('input[type="hidden"][data-combobox-value]')]
        const existing = posts().find((p) => p.value === item.dataset.value)
        if (existing) {
          existing.remove()
          delete item.dataset.selected
          item.setAttribute('aria-selected', 'false')
        } else {
          const post = document.createElement('input')
          post.type = 'hidden'
          post.name = listName
          post.value = item.dataset.value
          post.dataset.comboboxValue = ''
          root.prepend(post)
          item.dataset.selected = 'true'
          item.setAttribute('aria-selected', 'true')
        }
        const count = posts().length
        announceAll()
        if (value) {
          value.textContent = count
            ? (value.dataset.countLabel || '{n} selected').replace('{n}', count)
            : (value.dataset.placeholder || '')
        }
        syncClear()
        return
      }
      const existing = chipFor(item.dataset.value)
      if (existing) { existing.remove(); delete item.dataset.selected; syncClear(); announceAll(); return }
      if (!addChip(chips, item.dataset.value, label(item))) return
      item.dataset.selected = 'true'
      if (input) input.value = ''
      syncClear()
      announceAll()
      filter()
    }

    // Found by comparing values rather than by an attribute selector: a created tag is whatever
    // the reader typed, and a quote or a bracket in it would make `[data-value="…"]` a syntax
    // error. Case-folded only when asked — a row's value is exact, a typed duplicate is not.
    const chipFor = (v, anyCase = false) => [...root.querySelectorAll('[data-slot="combobox-chip"]')]
      .find((chip) => anyCase ? chip.dataset.value?.toLowerCase() === v.toLowerCase() : chip.dataset.value === v)

    // Cloned from the template the component renders, not built here. Built here it wore
    // whatever class name the call site remembered to pass — usually none — so a chip added
    // by clicking was bare text next to a server-rendered one that was a proper badge with a
    // remove button. The template has all of it, including the button and its label. A created
    // tag goes through the same path, so it posts under the same name and looks the same.
    const addChip = (chips, v, text) => {
      const template = chips.querySelector('[data-combobox-chip-template]')
      const chip = template?.content.firstElementChild?.cloneNode(true)
      if (!chip) return false
      chip.dataset.value = v
      const post = chip.querySelector('input[type="hidden"]')
      if (post) post.value = v
      const remove = chip.querySelector('[data-slot="combobox-chip-remove"]')
      if (remove) remove.setAttribute('aria-label', `Remove ${text}`)
      chip.insertBefore(document.createTextNode(text), chip.firstChild)
      chips.insertBefore(chip, input && input.parentNode === chips ? input : null)
      return true
    }

    // Creatable: Enter or a comma turns the typed text into a chip, Backspace in an empty box
    // takes the last one back. Text that names a visible row chooses that row instead, so typing
    // "react" and pressing Enter gives the catalogue's React chip rather than a lookalike.
    const onCreateKey = (event) => {
      if (event.isComposing) return
      const chips = root.querySelector('[data-slot="combobox-chips"]')
      if (!chips) return
      const text = input.value.trim()
      if (event.key === 'Enter' || event.key === ',') {
        // A comma is a separator, never part of a tag; Enter here means "add", and letting it
        // through would submit the form with the tag still sitting in the box.
        if (event.key === ',' || text) event.preventDefault()
        if (!text) return
        const needle = text.toLowerCase()
        const match = shown().find((i) => !i.disabled
          && (label(i).toLowerCase() === needle || (i.dataset.value || '').toLowerCase() === needle))
        if (match) {
          // choose() toggles, and a reader typing a name that is already a chip means "add it",
          // not "remove it" — so an existing chip only clears the box.
          if (!chipFor(match.dataset.value)) choose(match)
        } else if (!chipFor(text, true)) {
          addChip(chips, text, text)
        }
        input.value = ''
        syncClear()
        filter()
      } else if (event.key === 'Backspace' && !input.value) {
        const chip = [...chips.querySelectorAll('[data-slot="combobox-chip"]')].pop()
        if (!chip) return
        event.preventDefault()
        const item = items().find((i) => i.dataset.value === chip.dataset.value)
        if (item) { delete item.dataset.selected; item.setAttribute('aria-selected', 'false') }
        chip.remove()
        syncClear()
      }
    }

    const onClick = (event) => {
      // Wired to the panel AND the root, because the panel may be rendered outside the root --
      // but in the usual markup it is inside, so one click reaches both listeners. For a toggle
      // that is add-then-remove in the same gesture, which looked exactly like "nothing
      // happened". One handling per event.
      if (event.comboboxHandled) return
      event.comboboxHandled = true
      const remove = event.target.closest('[data-slot="combobox-chip-remove"]')
      if (remove) {
        const chip = remove.closest('[data-slot="combobox-chip"]')
        const item = items().find((i) => i.dataset.value === chip?.dataset.value)
        if (item) delete item.dataset.selected
        chip?.remove()
        syncClear()
        announceAll()
        return
      }
      const item = event.target.closest('[data-slot="combobox-item"]')
      if (item && !item.disabled) choose(item)
    }

    const onKey = (event) => {
      const list = shown()
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault()
        if (!panel.matches(':popover-open')) panel.showPopover()
        const at = list.indexOf(document.activeElement)
        const next = event.key === 'ArrowDown' ? at + 1 : at - 1
        list[(next + list.length) % list.length]?.focus()
      } else if (event.key === 'Escape' && panel.matches(':popover-open')) {
        panel.hidePopover()
      }
    }

    const onInput = () => {
      if (!panel.matches(':popover-open')) panel.showPopover()
      filter()
    }

    // The recipe styles data-highlighted and nothing set it: the list never responded to the
    // mouse. One attribute follows the pointer; focus stays in the input, as Radix keeps it.
    const onHighlight = (event) => {
      const item = event.target.closest('[data-slot="combobox-item"]')
      if (!item || item.disabled) return
      for (const other of items()) delete other.dataset.highlighted
      item.dataset.highlighted = 'true'
    }

    // Focusing the box opens the list. Only typing did, so a chips combobox showed nothing at
    // all until you had already typed a character — and the two examples built on it looked
    // completely inert. Every combobox opens on focus; this one had a trigger for the button
    // form and nothing for the input form.
    const onFocus = () => {
      // Not straight after choosing: closing the list hands focus back to the input, and that
      // focus would open it again. Chrome sent it to the body and hid the loop; Opera sends it
      // to the input, and the list reopened the instant it closed — which reads as "choosing
      // does nothing at all".
      if (justChose) return
      // A click on the input fires focus and click, and the trigger button inside the same
      // frame may open the panel between them -- showPopover() during another show throws
      // "Invalid to show a popover during another show operation", which killed the rest of the
      // handler. Ask the browser, and let the one that got there first win.
      if (!panel.matches(':popover-open')) { try { panel.showPopover() } catch { /* already opening */ } }
      if (input && !panel.contains(input) && input.value) input.select()
    }

    const onClear = () => {
      for (const item of items()) { delete item.dataset.selected; item.setAttribute('aria-selected', 'false') }
      for (const chip of root.querySelectorAll('[data-slot="combobox-chip"]')) chip.remove()
      if (multiple) for (const post of root.querySelectorAll('input[type="hidden"][data-combobox-value]')) post.remove()
      else if (hidden) hidden.value = ''
      if (input) input.value = ''
      if (value) value.textContent = value.dataset.placeholder || ''
      syncClear()
      announceAll()
      filter()
    }

    const onToggle = (event) => {
      const open = event.newState === 'open'
      trigger?.setAttribute('aria-expanded', String(open))
      input?.setAttribute('aria-expanded', String(open))
      // The FRAME is the anchor, not the box inside it: an input-group's chevron and icon are
      // part of the control the reader sees, and a chips field is the whole bordered area. Left
      // to the inner input the list started at the text and stood out past the frame's edge.
      if (open) {
        const frame = (trigger || input)?.closest(
          '[data-slot="input-group"], [data-slot="combobox-chips"]') || trigger || root
        place(panel, frame, 'start', 'bottom', 4)
      }
      // The popup pattern: the panel opened from a button and the search box lives inside it.
      // Focus goes straight there, which is what makes it a search box rather than an ornament.
      if (open && input && panel.contains(input)) input.focus()
    }

    panel.addEventListener('click', onClick)
    root.addEventListener('click', onClick)
    root.addEventListener('keydown', onKey)
    panel.addEventListener('keydown', onKey)
    input?.addEventListener('input', onInput)
    if (creatable) input?.addEventListener('keydown', onCreateKey)
    input?.addEventListener('focus', onFocus)
    // click, not pointerdown: opening on pointerdown puts the panel in the top layer before the
    // click completes, and the browser's own light-dismiss then sees a click outside it and
    // closes it again in the same gesture. The list flashed and vanished.
    input?.addEventListener('click', onFocus)
    clear?.addEventListener('click', onClear)
    panel.addEventListener('toggle', onToggle)
    panel.addEventListener('pointerover', onHighlight)
    // A display input showing the chosen label must not pre-filter the list to that one label --
    // reopening would show a list of one. The filter belongs to typing, not to arriving.
    if (input && !panel.contains(input) && input.value && hidden?.value) {
      if (empty) empty.hidden = true
    } else {
      filter()
    }
    syncClear()

    return () => {
      panel.removeEventListener('click', onClick)
      root.removeEventListener('click', onClick)
      root.removeEventListener('keydown', onKey)
      panel.removeEventListener('keydown', onKey)
      input?.removeEventListener('input', onInput)
      input?.removeEventListener('keydown', onCreateKey)
      input?.removeEventListener('focus', onFocus)
      input?.removeEventListener('click', onFocus)
      clear?.removeEventListener('click', onClear)
      panel.removeEventListener('toggle', onToggle)
      panel.removeEventListener('pointerover', onHighlight)
    }
  })

  // =============================================================================================
  // Sidebar — the open state, and where it is kept
  // =============================================================================================
  // In a `sidebar_state` cookie, which is also where shadcn keeps it. That is the whole design:
  // the server reads the cookie, renders the right state on the first paint, and there is no
  // flash of a sidebar opening itself. This only has to keep the two in step afterwards.
  //
  // The trigger and the rail ship hidden and are revealed here. With no JavaScript there is
  // nothing to toggle, and a control that does nothing is worse than one that is absent — the
  // sidebar stays open and every link in it is still reachable.

  shadcnCompiler('[data-sidebar-toggle]', (control) => {
    const wrapper = control.closest('[data-slot="sidebar-wrapper"]') || document.body

    const sidebars = () => [...wrapper.querySelectorAll('[data-slot="sidebar"]')]

    const onClick = () => {
      const open = wrapper.dataset.state !== 'expanded'
      wrapper.dataset.state = open ? 'expanded' : 'collapsed'

      for (const sidebar of sidebars()) {
        sidebar.dataset.state = open ? 'expanded' : 'collapsed'
        // data-collapsible carries the MODE only while collapsed, which is what upstream does
        // and what every group-data-[collapsible=icon] selector expects.
        sidebar.dataset.collapsible = open ? '' : (sidebar.dataset.mode || 'offcanvas')
      }

      // A year, path-wide, and SameSite=Lax so it survives an ordinary navigation from elsewhere
      // without being sent on a cross-site request.
      document.cookie = `sidebar_state=${open}; path=/; max-age=31536000; samesite=lax`
      control.setAttribute('aria-expanded', String(open))
    }

    // data-mode is rendered by the component and says which mode this sidebar collapses INTO,
    // whether or not it is collapsed right now. This used to read data-collapsible instead —
    // which is empty while expanded — so an icon sidebar that had never been collapsed fell back
    // to offcanvas and slid away rather than narrowing.
    for (const sidebar of sidebars()) {
      if (!sidebar.dataset.mode && sidebar.dataset.collapsible) {
        sidebar.dataset.mode = sidebar.dataset.collapsible
      }
    }

    // ctrl+b, or cmd+b on a Mac, which is the shortcut shadcn documents. Bound once per
    // toggle rather than once per page, and guarded so it does not fire while someone is typing
    // b in a field — a shortcut that eats a letter is worse than no shortcut.
    const onShortcut = (event) => {
      if (event.key !== 'b' || !(event.metaKey || event.ctrlKey) || event.altKey) return
      const el = document.activeElement
      if (el?.matches('input, textarea, select, [contenteditable=""], [contenteditable="true"]')) return
      // Only the first toggle inside a wrapper acts, or a page with a trigger AND a rail would
      // toggle twice and end up where it started.
      if (wrapper.querySelector('[data-sidebar-toggle]') !== control) return
      event.preventDefault()
      onClick()
    }

    control.hidden = false
    control.setAttribute('aria-expanded', String(wrapper.dataset.state === 'expanded'))
    control.addEventListener('click', onClick)
    document.addEventListener('keydown', onShortcut)

    return () => {
      control.removeEventListener('click', onClick)
      document.removeEventListener('keydown', onShortcut)
    }
  })

  // =============================================================================================
  // MessageScroller — stay at the newest message, unless the reader is reading
  // =============================================================================================
  // Every chat needs this and most get it wrong in one direction or the other: either it yanks
  // you to the bottom while you are reading something from ten minutes ago, or it silently stops
  // following and you miss the reply.
  //
  // The rule is simple once stated. Follow the newest message while the viewport is already at
  // the bottom; stop the moment it is not; and show a button that says so. `overflow-anchor: auto`
  // in the CSS does the first half without any script at all — the browser holds the scroll
  // position against content inserted above the anchor — and this adds the button.

  shadcnCompiler('[data-slot="message-scroller"]', (root) => {
    const viewport = root.querySelector('[data-slot="message-scroller-viewport"]')
    if (!viewport) return

    const buttons = () => [...root.querySelectorAll('[data-slot="message-scroller-button"]')]
    const margin = () => Number(root.dataset.scrollMargin || 24)
    const follows = () => root.dataset.autoScroll !== 'false'
    const anchors = () => [...viewport.querySelectorAll('[data-scroll-anchor="true"]')]

    // Not === 0: a fractional scroll height never reaches the exact bottom, and a scroller that
    // is one pixel off is a scroller that never follows anything again.
    const atEnd = () => viewport.scrollHeight - viewport.scrollTop - viewport.clientHeight < 8
    const atStart = () => viewport.scrollTop < 8

    // Where the thread can still go, published as state so a footer can say "you are at the
    // top" in CSS. This is upstream's useMessageScrollerScrollable, which is two booleans
    // wearing a hook's clothes.
    // Which anchored turn the reader is on: the last one whose top has passed the margin. It is
    // upstream's useMessageScrollerVisibility, and it is published two ways — on the root for CSS,
    // and as data-current on anything that named the same turn, because an outline row and a
    // dash beside the card both want to light up and CSS cannot compare two attributes.
    const anchorNow = () => {
      let current = null
      for (const anchor of anchors()) {
        if (anchor.offsetTop - viewport.scrollTop <= margin() + 4) current = anchor
      }
      // Above the first anchor, the first anchor is still the one you are on — nothing has been
      // passed yet, and an outline with no row lit reads as broken rather than as scrolled up.
      return (current || anchors()[0])?.dataset.messageId || null
    }

    let announced = null
    const announce = () => {
      const id = anchorNow()
      if (id === announced) return
      announced = id
      if (id) root.dataset.currentAnchor = id
      else delete root.dataset.currentAnchor
      for (const mark of document.querySelectorAll('[data-anchor-mark]')) {
        mark.dataset.current = String(mark.dataset.anchorMark === id)
      }
    }

    const sync = () => {
      const end = atEnd()
      root.dataset.following = end ? 'true' : 'false'
      root.dataset.canScrollStart = viewport.scrollTop > 8 ? 'true' : 'false'
      root.dataset.canScrollEnd = end ? 'false' : 'true'
      announce()
      for (const button of buttons()) {
        const done = button.dataset.direction === 'start' ? atStart() : end
        button.hidden = done
        if (done) delete button.dataset.active
        else button.dataset.active = 'true'
      }
    }

    // A programmatic scroll hides the scrollbar while it runs, which is what the viewport's own
    // data-autoscrolling:scrollbar-none class has always been written against and what nothing
    // was setting.
    // `chase` is the reason this is not one line. Every item carries content-visibility: auto,
    // so an item that has never been on screen has an ESTIMATED height until it is — which means
    // scrollHeight changes WHILE a smooth scroll to the bottom is running, and the scroll stops
    // at a target that was true when it started. Pressing "jump to newest" landed twenty pixels
    // short and the thread did not resume following, because twenty pixels short is not the end.
    //
    // So a scroll that asked for the end checks when it settles, and finishes the job.
    let settle
    let chaseTimer
    let chaseStop
    let chasing = false
    const cancelChase = () => {
      clearInterval(chaseTimer)
      clearTimeout(chaseStop)
      chaseTimer = chaseStop = undefined
      chasing = false
    }
    // A reader who takes control must not be pulled back down by a pending jump.
    const interruptChase = () => {
      if (!chasing) return
      clearTimeout(settle)
      cancelChase()
      delete viewport.dataset.autoscrolling
      sync()
    }
    viewport.addEventListener('wheel', interruptChase, { passive: true })
    viewport.addEventListener('touchstart', interruptChase, { passive: true })
    const glide = (top, behavior = 'smooth', chase = false) => {
      cancelChase()
      chasing = chase
      viewport.dataset.autoscrolling = 'true'
      viewport.scrollTo({ top, behavior })
      clearTimeout(settle)
      settle = setTimeout(() => {
        if (chase) {
          // Revealing an estimated content-visibility item can change scrollHeight
          // *after* the first correction. Follow the target briefly until layout
          // settles, rather than chasing once at a guessed 400 ms mark.
          const finish = () => {
            viewport.scrollTop = viewport.scrollHeight
            sync()
          }
          finish()
          chaseTimer = setInterval(finish, 100)
          chaseStop = setTimeout(() => {
            cancelChase()
            delete viewport.dataset.autoscrolling
            sync()
          }, 1600)
        } else {
          delete viewport.dataset.autoscrolling
          sync()
        }
      }, 400)
    }

    // An anchored turn sits at the TOP of the viewport with a peek of what came before it,
    // rather than the thread scrolling to its own bottom. That is the whole point of the
    // component: when a long answer arrives you are left looking at the question that caused it.
    const toAnchor = (behavior = 'smooth') => {
      const last = anchors().pop()
      if (!last) return false
      glide(last.offsetTop - margin(), behavior)
      return true
    }

    const toEnd = (behavior = 'smooth') => glide(viewport.scrollHeight, behavior, true)
    const toStart = (behavior = 'smooth') => glide(0, behavior)

    const onButton = (event) => {
      const button = event.target.closest('[data-slot="message-scroller-button"]')
      if (button) (button.dataset.direction === 'start' ? toStart : toEnd)()
    }

    // Anything on the page can ask for a turn by name — a jump menu, an outline, a link. It is
    // the markup half of upstream's scrollToMessage, and it needs no state to work.
    const onJump = (event) => {
      const jump = event.target.closest('[data-scroll-to-message]')
      if (!jump) return
      const item = viewport.querySelector(
        '[data-message-id="' + CSS.escape(jump.dataset.scrollToMessage) + '"]')
      if (!item) return
      event.preventDefault()
      glide(item.offsetTop - margin())
    }

    // New content follows only when the reader is at the bottom AND the thread was told to
    // follow. Content that arrives ABOVE them is different: prepending older messages grows the
    // scroll height above the reader, and following that is how "load older" throws away the
    // place they were reading. Then the scroll position moves by exactly what was added, so the
    // page under their eyes does not move at all.
    let height = viewport.scrollHeight
    let top = viewport.scrollTop
    const watch = new MutationObserver(() => {
      const grew = viewport.scrollHeight - height
      const prepended = grew > 0 && viewport.scrollTop === top && top > 0
      height = viewport.scrollHeight
      if (prepended) {
        viewport.scrollTop = top + grew
      } else if (follows() && root.dataset.following !== 'false') {
        if (!toAnchor('auto')) viewport.scrollTop = viewport.scrollHeight
      }
      top = viewport.scrollTop
      sync()
    })
    watch.observe(viewport, { childList: true, subtree: true })

    const onScroll = () => { top = viewport.scrollTop; sync() }
    viewport.addEventListener('scroll', onScroll, { passive: true })
    root.addEventListener('click', onButton)
    document.addEventListener('click', onJump)

    // Where it opens: the end, the start, or the last anchored turn — which is where a reader
    // coming back to a long thread left off.
    // Placed twice, for the same reason `chase` exists: at the moment the compiler runs, every
    // item below the fold is an ESTIMATE of its height, so an anchored turn's offsetTop is a
    // guess and the thread opens near the right place rather than at it. The second pass runs
    // once layout has settled and lands on the real number.
    const openAt = root.dataset.openAt || 'end'
    const place = () => {
      if (openAt === 'start') viewport.scrollTop = 0
      else if (openAt !== 'anchor' || !toAnchor('auto')) viewport.scrollTop = viewport.scrollHeight
    }
    place()
    requestAnimationFrame(() => { place(); height = viewport.scrollHeight; top = viewport.scrollTop; sync() })
    height = viewport.scrollHeight
    top = viewport.scrollTop
    sync()

    return () => {
      watch.disconnect()
      clearTimeout(settle)
      cancelChase()
      viewport.removeEventListener('wheel', interruptChase)
      viewport.removeEventListener('touchstart', interruptChase)
      viewport.removeEventListener('scroll', onScroll)
      root.removeEventListener('click', onButton)
      document.removeEventListener('click', onJump)
    }
  })

  // =============================================================================================
  // Tooltip
  // =============================================================================================
  // Hover AND focus, because a tooltip only reachable by mouse is a tooltip half the users never
  // see. Escape closes it, which the APG asks for and most implementations forget.

  // [data-tooltip-target] is on this list because a tooltip is not always a component of its
  // own: a sidebar row IS the trigger, and it already has a data-slot saying what it is. The
  // attribute is the general hook — anything can name a tip and get the whole behaviour.
  shadcnCompiler('[data-slot="tooltip-trigger"], [data-tooltip-target]', (trigger) => {
    const panel = document.getElementById(trigger.dataset.target || trigger.dataset.tooltipTarget)
    if (!panel) return

    // "collapsed" means: only while the sidebar this row is in has narrowed to icons. That is
    // upstream's rule, and it is the only moment the label is not already on screen — a tip
    // repeating a word the reader can see is noise.
    const wanted = () => trigger.dataset.tooltipWhen !== 'collapsed'
      || trigger.closest('[data-slot="sidebar"]')?.dataset.state === 'collapsed'
    let timer

    // Clicking dismisses the tip, and it stays dismissed until the pointer leaves and comes
    // back. Without this the click's own focus event reopened it a millisecond later, so the
    // dismissal was real and invisible.
    let dismissed = false

    const open = () => {
      if (dismissed || !wanted()) return
      clearTimeout(timer)
      timer = setTimeout(() => {
        panel.showPopover()
        place(panel, trigger, 'center', panel.dataset.side || 'top',
              Number(panel.dataset.sideOffset || 6))
        panel.dataset.state = 'delayed-open'
      }, Number(trigger.dataset.delay ?? 0))
    }

    const close = () => {
      clearTimeout(timer)
      panel.dataset.state = 'closed'
      if (panel.matches(':popover-open')) panel.hidePopover()
    }

    const onKey = (event) => { if (event.key === 'Escape') close() }

    // The pointer may travel INTO the tip. Radix calls this hoverable content and leaves it on
    // by default; without it a tip holding a <Kbd> or a link cannot be reached at all, because
    // leaving the trigger closed it before the pointer arrived. The hover card three hundred
    // lines down already did this.
    const leave = (event) => {
      const to = event.relatedTarget
      if (to && (panel.contains(to) || trigger.contains(to))) return
      dismissed = false
      close()
    }
    const stay = () => clearTimeout(timer)

    trigger.addEventListener('pointerenter', open)
    trigger.addEventListener('pointerleave', leave)
    panel.addEventListener('pointerenter', stay)
    panel.addEventListener('pointerleave', leave)
    // Clicking the thing dismisses its label: whatever the click does next, the tip is now
    // sitting on top of the result.
    const dismiss = () => { dismissed = true; close() }
    trigger.addEventListener('pointerdown', dismiss)
    trigger.addEventListener('focus', open)
    trigger.addEventListener('blur', close)
    document.addEventListener('keydown', onKey)

    return () => {
      close()
      panel.removeEventListener('pointerenter', stay)
      panel.removeEventListener('pointerleave', leave)
      trigger.removeEventListener('pointerdown', dismiss)
      trigger.removeEventListener('pointerenter', open)
      trigger.removeEventListener('pointerleave', leave)
      trigger.removeEventListener('focus', open)
      trigger.removeEventListener('blur', close)
      document.removeEventListener('keydown', onKey)
    }
  })

  // =============================================================================================
  // Snippet — tabs are driven by the shared tabs compiler; copy reads the visible panel.
  shadcnCompiler('[data-slot="snippet"]', (root) => {
    const button = root.querySelector('[data-slot="snippet-copy"]')
    if (!button) return
    if (!navigator.clipboard?.writeText) return
    button.hidden = false
    let revert
    const copy = async () => {
      const code = [...root.querySelectorAll('[data-slot="tabs-content"]')]
        .find(panel => !panel.hidden)?.querySelector('[data-slot="snippet-copy-value"]')?.textContent
      if (!code) return
      try { await navigator.clipboard.writeText(code) }
      catch (error) {
        root.dispatchEvent(new CustomEvent('snippet:error', { bubbles: true, detail: { error } }))
        return
      }
      button.dataset.copied = ''
      root.dispatchEvent(new CustomEvent('snippet:copy', { bubbles: true, detail: { value: code } }))
      clearTimeout(revert)
      revert = setTimeout(() => delete button.dataset.copied, Math.max(0, Number(button.dataset.timeout) || 2000))
    }
    button.addEventListener('click', copy)
    return () => { clearTimeout(revert); button.removeEventListener('click', copy) }
  })

  // CodeBlock — copy
  // =============================================================================================
  // The button ships hidden and this reveals it. A copy button that silently does nothing because
  // the page is not on a secure origin is worse than no button at all, so it stays absent unless
  // the clipboard is actually there.

  shadcnCompiler('[data-slot="code-block-copy"]', (button) => {
    if (!navigator.clipboard?.writeText) return
    button.hidden = false

    const root = button.closest('[data-slot="code-block"]')
    if (!root?.querySelector('[data-slot="code-block-code"]')) return

    let revert
    const onClick = async () => {
      try {
        const code = [...root.querySelectorAll('[data-slot="code-block-pre"]')]
          .find(pre => !pre.hidden)?.querySelector('[data-slot="code-block-code"]')
        if (!code) return
        await navigator.clipboard.writeText(code.dataset.copySource ?? code.textContent ?? '')
      } catch {
        // Denied, or no permission. Say nothing rather than claiming success.
        return
      }
      // Confirmed on the button itself: a toast for something this small is a toast people
      // learn to ignore.
      button.dataset.copied = ''
      clearTimeout(revert)
      revert = setTimeout(() => delete button.dataset.copied, 1500)
    }

    button.addEventListener('click', onClick)
    return () => {
      clearTimeout(revert)
      button.removeEventListener('click', onClick)
    }
  })

  // =============================================================================================
  // CodeBlock — Shiki with Kibo's themes and notation transformers, lazy-loaded.
  // A plain escaped <code> stays usable while the bundle loads or if loading fails.
  let codeBlockSeq = 0
  shadcnCompiler('[data-slot="code-block"]', (root) => {
    const codes = [...root.querySelectorAll('[data-slot="code-block-code"]')]
    if (!codes.length) return
    let disposed = false
    for (const code of codes) {
      const original = code.textContent ?? ''
      code.dataset.copySource = original
      if (code.dataset.highlight === 'false') {
        const pre = code.closest('[data-slot="code-block-pre"]')
        if (pre?.dataset.lineNumbers) {
          code.classList.add('shiki-code', 'shiki-line-numbers')
          const lines = original.replace(/\r\n/g, '\n').split('\n')
          if (lines.length > 1 && lines.at(-1) === '') lines.pop()
          code.replaceChildren(...lines.map(text => {
            const line = document.createElement('span')
            line.className = 'line'
            line.textContent = text || '\u200b'
            return line
          }))
        }
        continue
      }
      const lang = code.dataset.language || 'typescript'
      ensureShiki().then(() => window.shadcnShikiHighlight(
        original, lang, code.dataset.notations !== 'false'
      )).then(html => {
        if (disposed || !root.isConnected) return
        const doc = new DOMParser().parseFromString(html, 'text/html')
        const pre = code.closest('[data-slot="code-block-pre"]')
        const shiki = doc.querySelector('pre.shiki')
        if (!shiki || !pre) return
        code.replaceChildren(...shiki.querySelector('code').childNodes)
        code.classList.add('shiki-code')
        if (pre.dataset.lineNumbers) code.classList.add('shiki-line-numbers')
      }).catch(error => console.error('CodeBlock Shiki:', error))
    }
    const tabs = [...root.querySelectorAll('[data-slot="code-block-file"]')]
    const panels = [...root.querySelectorAll('[data-slot="code-block-pre"]')]
    const seq = ++codeBlockSeq
    tabs.forEach((tab, index) => {
      tab.id ||= `${root.id || 'code-block-' + seq}-tab-${index}`
      panels[index].id ||= `${root.id || 'code-block-' + seq}-panel-${index}`
      tab.setAttribute('aria-controls', panels[index].id)
      panels[index].setAttribute('role', 'tabpanel')
      panels[index].setAttribute('aria-labelledby', tab.id)
    })
    const show = (index) => {
      tabs.forEach((tab, i) => {
        tab.setAttribute('aria-selected', String(i === index))
        tab.tabIndex = i === index ? 0 : -1
      })
      panels.forEach((panel, i) => { panel.hidden = i !== index; panel.tabIndex = i === index ? 0 : -1 })
    }
    const onClick = (event) => {
      const tab = event.target.closest('[data-slot="code-block-file"]')
      if (tab && root.contains(tab)) show(tabs.indexOf(tab))
    }
    const onKey = (event) => {
      const tab = event.target.closest('[data-slot="code-block-file"]')
      if (!tab || !root.contains(tab)) return
      const delta = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0
      if (!delta && event.key !== 'Home' && event.key !== 'End') return
      event.preventDefault()
      const index = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1
        : (tabs.indexOf(tab) + delta + tabs.length) % tabs.length
      show(index)
      tabs[index].focus()
    }
    root.addEventListener('click', onClick)
    root.addEventListener('keydown', onKey)
    return () => {
      disposed = true
      root.removeEventListener('click', onClick)
      root.removeEventListener('keydown', onKey)
    }
  })

  // =============================================================================================
  // QR Code — qrcodejs (https://github.com/davidshimjs/qrcodejs)
  // =============================================================================================
  // Static SSR renders <div data-qrcode data-value="..."> empty. This compiler fills it via
  // `new QRCode(el, {text, width, height, colorDark, colorLight, correctLevel})`. On every
  // Unpoly fragment swap the compiler re-runs, so a value change via markup just works.
  // `colorDark`/`colorLight` may be CSS variables (`var(--foreground)`) — resolved via
  // getComputedStyle so dark mode just works when no explicit colors are given.
  // Quiet zone is CSS padding on the generated canvas/img; `data-margin="0"` removes it.
  shadcnCompiler('[data-slot="qr-code"][data-qrcode]', (el) => {
    const cssVar = (name, fallback) => {
      const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim()
      return v || fallback
    }
    const resolveColor = (raw, fallback) => {
      if (!raw) return fallback
      const m = raw.match(/^var\((--[^)]+)\)$/)
      if (m) return cssVar(m[1], fallback)
      return raw
    }
    const levelMap = { L: 1, M: 0, Q: 3, H: 2, '1': 1, '0': 0, '3': 3, '2': 2 }

    const build = () => {
      if (typeof QRCode === 'undefined') return
      const text = el.dataset.value ?? ''
      if (!text) { el.innerHTML = ''; return }

      const size = parseInt(el.dataset.size || '128', 10) || 128
      const rawLevel = (el.dataset.correctLevel || 'M').toUpperCase()
      const correctLevel = levelMap[rawLevel] ?? 0

      const colorDark = resolveColor(el.dataset.colorDark, cssVar('--foreground', '#0a0a0a'))
      const colorLight = resolveColor(el.dataset.colorLight, cssVar('--card', '#ffffff') || '#ffffff')

      const margin = parseInt(el.dataset.margin || '4', 10)
      el.style.padding = Number.isFinite(margin) && margin > 0 ? margin + 'px' : '0'
      el.style.background = colorLight

      el.innerHTML = ''
      new QRCode(el, { text, width: size, height: size, colorDark, colorLight, correctLevel })

      // qrcodejs may append both a canvas and a fallback <img>. The labelled wrapper is
      // the single semantic image, so generated renderers must stay decorative.
      for (const renderer of el.querySelectorAll('canvas, img')) {
        renderer.setAttribute('aria-hidden', 'true')
        if (renderer.tagName === 'IMG') renderer.setAttribute('alt', '')
      }
    }

    const init = () => {
      if (typeof QRCode === 'undefined') {
        ensureQrcode().then(build).catch(() => {})
      } else build()
    }
    init()

    const mo = new MutationObserver((muts) => {
      for (const m of muts) {
        if (m.attributeName === 'data-value' || m.attributeName === 'data-size' ||
            m.attributeName === 'data-correct-level' || m.attributeName === 'data-color-dark' ||
            m.attributeName === 'data-color-light' || m.attributeName === 'data-margin') {
          build(); break
        }
      }
    })
    mo.observe(el, { attributes: true, attributeFilter: ['data-value','data-size','data-correct-level','data-color-dark','data-color-light','data-margin'] })

    // Rebuild on theme switch so cssVar-resolved colors pick up the new palette
    const themeMo = new MutationObserver(build)
    themeMo.observe(document.documentElement, { attributes: true, attributeFilter: ['class','data-theme','style'] })

    return () => {
      mo.disconnect()
      themeMo.disconnect()
      el.innerHTML = ''
    }
  })

  // =============================================================================================
  // ImageCompare — img-comparison-slider WC (sneas, 3KB)
  // =============================================================================================
  // Static SSR renders <img-comparison-slider> with two <img slot="first/second">. This compiler
  // lazy-loads the WC definition only when [data-slot="image-compare"] is on the page, so a landing
  // page with no compare never fetches the 11KB script.
  // The bundled media-chrome IIFE registers custom elements once. The browser upgrades
  // connected controllers automatically; removing one leaves no per-instance listeners here.
  shadcnCompiler('[data-slot="video-player"]', () => {
    if (!customElements.get('media-controller')) {
      loadScript('_content/Unpoly.Blazor.Shadcn/media-chrome/index.js').catch(console.error)
    }
  })

  shadcnCompiler('[data-slot="image-crop"]', (root) => {
    const image = root.querySelector('img')
    if (!image) return
    let disposed = false
    let cropper = null
    const controller = new AbortController()
    const start = () => {
      if (disposed || !root.isConnected || cropper || !window.Cropper) return
      const ratio = Number(root.dataset.aspectRatio)
      cropper = new window.Cropper(image, {
        aspectRatio: root.dataset.circular === 'true' ? 1 : (root.dataset.aspectRatio ? ratio : NaN),
        viewMode: 1, autoCropArea: 0.9, background: false,
        ready() {
          if (root.dataset.circular === 'true') {
            const box = root.querySelector('.cropper-view-box')
            const face = root.querySelector('.cropper-face')
            if (box) box.style.borderRadius = '50%'
            if (face) face.style.borderRadius = '50%'
          }
        }
      })
    }
    if (window.Cropper) start()
    else loadScript('_content/Unpoly.Blazor.Shadcn/cropperjs/cropper.min.js').then(start).catch(console.error)
    const emitCrop = (canvas) => {
      const dataUrl = canvas.toDataURL('image/png')
      const max = Number(root.dataset.maxImageSize) || 0
      if (max && Math.ceil((dataUrl.length - dataUrl.indexOf(',') - 1) * 3 / 4) > max) {
        root.dispatchEvent(new CustomEvent('image-crop:error', { bubbles: true, detail: { reason: 'size' } }))
        return
      }
      root.dispatchEvent(new CustomEvent('image-crop:apply', { bubbles: true, detail: { dataUrl } }))
    }
    root.querySelector('[data-slot="image-crop-apply"]')?.addEventListener('click', () => {
      if (!cropper) return
      const canvas = cropper.getCroppedCanvas()
      if (!canvas) return
      if (root.dataset.circular === 'true') {
        const circle = document.createElement('canvas')
        circle.width = canvas.width; circle.height = canvas.height
        const ctx = circle.getContext('2d')
        ctx.beginPath(); ctx.ellipse(circle.width / 2, circle.height / 2, circle.width / 2, circle.height / 2, 0, 0, Math.PI * 2)
        ctx.clip(); ctx.drawImage(canvas, 0, 0)
        emitCrop(circle)
      } else {
        emitCrop(canvas)
      }
    }, { signal: controller.signal })
    root.querySelector('[data-slot="image-crop-reset"]')?.addEventListener('click', () => cropper?.reset(), { signal: controller.signal })
    return () => { disposed = true; controller.abort(); cropper?.destroy() }
  })

  shadcnCompiler('[data-slot="image-compare"][data-image-compare]', (el) => {
    const slider = el.querySelector('img-comparison-slider')
    if (!slider) return
    const ensure = () => {
      if (customElements.get('img-comparison-slider')) {
        slider.value = el.dataset.position || '50'
        return Promise.resolve()
      }
      return loadScript('_content/Unpoly.Blazor.Shadcn/image-compare/img-comparison-slider.js')
        .then(() => { slider.value = el.dataset.position || '50' }).catch(()=>{})
    }
    ensure()
    const mo = new MutationObserver((muts) => {
      for (const m of muts) if (m.attributeName === 'data-position') { slider.value = el.dataset.position || '50'; break }
    })
    mo.observe(el, { attributes: true, attributeFilter: ['data-position'] })
    return () => mo.disconnect()
  })

  // =============================================================================================
  // Command
  // =============================================================================================
  // Upstream is cmdk, which keeps the filtered list in React state. Here every item is already in
  // the DOM — as a link, usually — and this hides the ones that do not match. For a list the
  // server can render that is the same thing, and the palette still works with scripting off.
  //
  // Matching is on the visible text plus [data-keywords], so "billing" can find "Invoices".
  // Accents are stripped on both sides: someone typing "don" should find "Đơn hàng".

  const fold = (s) => s.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()

  shadcnCompiler('[data-slot="command"], [data-slot="command-dialog"]', (root) => {
    const input = root.querySelector('[data-slot="command-input"]')
    const list = root.querySelector('[data-slot="command-list"]')
    if (!input || !list) return

    // A CommandDialog CONTAINS a Command, so this compiler's selector matched both and attached
    // two controllers to one palette. Each moved the selection on the same bubbled key and they
    // cancelled out, which is why the docs search would not move at all while the inline example
    // was fine. The outer one stands down.
    if (root.dataset.slot === 'command-dialog' && root.querySelector('[data-slot="command"]')) return

    const items = () => [...list.querySelectorAll('[data-slot="command-item"]:not([hidden])')]
    const empty = root.querySelector('[data-slot="command-empty"]')
    const cleanup = []

    // Every item needs an id for aria-activedescendant to point at one.
    let seq = 0
    const number = () => {
      for (const item of list.querySelectorAll('[data-slot="command-item"]')) {
        if (!item.id) item.id = `cmd-${++seq}-${Math.abs(fold(item.textContent).length)}`
      }
    }
    number()
    input.setAttribute('role', 'combobox')
    input.setAttribute('aria-expanded', 'true')
    input.setAttribute('aria-controls', list.id || (list.id = 'cmd-list-' + seq))

    const highlight = (item) => {
      for (const other of list.querySelectorAll('[data-slot="command-item"]')) {
        delete other.dataset.selected
      }
      if (!item) { input.removeAttribute('aria-activedescendant'); return }
      item.dataset.selected = 'true'
      input.setAttribute('aria-activedescendant', item.id)
      // The first filter() runs at compile time, while a CommandDialog is still closed. Scrolling a
      // palette that is not on screen forces a synchronous layout of the whole page for nothing:
      // 139 ms of every boot on a low-end phone, twice per page. Nothing is visible, nothing to scroll.
      // A closed dialog hides its contents through the `open` attribute, not `hidden`, so check both,
      // and note that this compiler also matches the Command *inside* a closed CommandDialog.
      const offScreen = root.closest('[hidden]') || root.closest('dialog:not([open])')
      if (!offScreen) item.scrollIntoView({ block: 'nearest' })
    }

    const filter = () => {
      const q = fold(input.value.trim())
      let shown = 0

      for (const item of list.querySelectorAll('[data-slot="command-item"]')) {
        const hay = fold(item.textContent + ' ' + (item.dataset.keywords || ''))
        const match = !q || hay.includes(q)
        item.hidden = !match
        if (match) shown++
      }

      // A heading over nothing is worse than no heading.
      for (const group of list.querySelectorAll('[data-slot="command-group"]')) {
        group.hidden = !group.querySelector('[data-slot="command-item"]:not([hidden])')
      }
      if (empty) empty.hidden = shown > 0

      highlight(items()[0])
    }

    const onKey = (event) => {
      const shown = items()
      if (shown.length === 0) return
      const at = shown.findIndex((i) => i.dataset.selected === 'true')

      if (event.key === 'ArrowDown') {
        event.preventDefault()
        highlight(shown[(at + 1) % shown.length])
      } else if (event.key === 'ArrowUp') {
        event.preventDefault()
        highlight(shown[(at - 1 + shown.length) % shown.length])
      } else if (event.key === 'Home') {
        event.preventDefault(); highlight(shown[0])
      } else if (event.key === 'End') {
        event.preventDefault(); highlight(shown[shown.length - 1])
      } else if (event.key === 'Enter' && at >= 0) {
        // Let the item BE the click, so a link navigates and Unpoly follows it as usual.
        event.preventDefault()
        shown[at].click()
      }
    }

    input.addEventListener('input', filter)

    // The keys are bound to the ROOT, not to the input. In a command DIALOG the user agent moves
    // focus to the first focusable element when it opens, which is the close button — so every
    // arrow press went to the document, the list never moved, and the page scrolled underneath
    // instead. Binding the palette's keys to the palette is the fix; the input is focused as well,
    // because typing is what you do next.
    root.addEventListener('keydown', onKey)

    if (root.matches('dialog')) {
      const focusInput = () => { if (isOpen(root)) input.focus({ preventScroll: true }) }
      const watch = new MutationObserver(focusInput)
      watch.observe(root, { attributes: true, attributeFilter: ['open'] })
      focusInput()
      cleanup.push(() => watch.disconnect())
    }
    list.addEventListener('pointermove', (e) => {
      const item = e.target.closest('[data-slot="command-item"]')
      if (item && !item.hidden) highlight(item)
    })
    filter()

    // The server-answered palette: <CommandInput> in a GET form with [up-target], and the results
    // swapped into the list while the palette is open. Those rows arrive after the filter ran, so
    // they had no ids for aria-activedescendant, nothing was highlighted, and Enter did nothing.
    // Watching the list's children (not attributes — filtering writes `hidden`, which would loop)
    // numbers and filters whatever arrives, and highlights the first match again.
    const arrivals = new MutationObserver(() => { number(); filter() })
    arrivals.observe(list, { childList: true, subtree: true })
    cleanup.push(() => arrivals.disconnect())

    return () => {
      for (const undo of cleanup) undo()
      input.removeEventListener('input', filter)
      root.removeEventListener('keydown', onKey)
    }
  })

  // The shortcut that opens a palette. `mod` is ⌘ on a Mac and Ctrl everywhere else, which is the
  // distinction every implementation gets wrong in one direction or the other.
  shadcnCompiler('[data-command-open]', (trigger) => {
    // Looked up when clicked, not when compiled: the palette may arrive in a later fragment than
    // its button, and a compiler that bailed for that would leave the button dead for good.
    const onClick = () => {
      const dialog = document.getElementById(trigger.dataset.commandOpen || '')
      if (dialog) isOpen(dialog) ? closeDialog(dialog) : showDialog(dialog)
    }
    trigger.addEventListener('click', onClick)
    return () => trigger.removeEventListener('click', onClick)
  })

  shadcnCompiler('[data-command-key]', (dialog) => {
    const combo = (dialog.dataset.commandKey || 'mod+k').toLowerCase().split('+')
    const key = combo[combo.length - 1]
    const wantsMod = combo.includes('mod') || combo.includes('ctrl') || combo.includes('cmd')
    const wantsShift = combo.includes('shift')

    const onKey = (event) => {
      const mod = event.metaKey || event.ctrlKey
      if (event.key.toLowerCase() !== key) return
      if (wantsMod !== mod || wantsShift !== event.shiftKey) return
      event.preventDefault()
      if (isOpen(dialog)) closeDialog(dialog)
      else showDialog(dialog)
    }

    // Focus the field, not the dialog: a palette that needs a second click to type into is a
    // palette nobody uses twice. Clear it too — the last search is not this one.
    const onOpen = () => {
      const input = dialog.querySelector('[data-slot="command-input"]')
      if (!input) return
      input.value = ''
      input.dispatchEvent(new Event('input', { bubbles: true }))
      input.focus()
    }

    // Choosing an item is the end of a palette's job, so it closes — on the click itself, which
    // is also what Enter produces. Left open, it stayed on screen over the page it had just
    // navigated to; and being a modal <dialog> in the top layer, it sat above any Unpoly overlay
    // the item opened and swallowed every click meant for it.
    const onChoose = (event) => {
      const item = event.target.closest('[data-slot="command-item"]')
      if (!item || !isOpen(dialog) || item.hidden || item.getAttribute('aria-disabled') === 'true') return
      closeDialog(dialog)
    }

    const onToggle = (event) => { if (event.newState === 'open') onOpen() }

    document.addEventListener('keydown', onKey)
    dialog.addEventListener('toggle', onToggle)
    dialog.addEventListener('click', onChoose)
    return () => {
      document.removeEventListener('keydown', onKey)
      dialog.removeEventListener('toggle', onToggle)
      dialog.removeEventListener('click', onChoose)
    }
  })

  // =============================================================================================
  // InputOTP
  // =============================================================================================
  // Six real inputs rather than one hidden input with painted boxes. What that costs is this
  // compiler; what it buys is a field that posts, autofills, and survives a fragment swap with no
  // state of its own.
  //
  // The hidden input is the one the server sees. Keeping it in step here rather than joining the
  // boxes in every handler is the difference between one place to get it right and one per form.

  shadcnCompiler('[data-slot="input-otp"]', (root) => {
    const boxes = [...root.querySelectorAll('[data-slot="input-otp-slot"]')]
    const hidden = root.querySelector('[data-otp-value]')
    if (boxes.length === 0 || !hidden) return

    const sync = () => {
      const next = boxes.map((b) => b.value).join('')
      // An empty set of boxes never overwrites a value the SERVER rendered: the compiler ran
      // once on boot and erased it, so a code that came back with the form was gone before
      // anyone saw it.
      if (!next && hidden.value) return
      if (next === hidden.value) return
      hidden.value = next
      // Filter forms and [up-autosubmit] watch the hidden input, and a programmatic assignment
      // fires nothing on its own.
      hidden.dispatchEvent(new Event('input', { bubbles: true }))
      hidden.dispatchEvent(new Event('change', { bubbles: true }))
    }

    // What counts as a character for THIS field. The filter was a hardcoded non-digit class,
    // so the alphanumeric example -- which advertises letters in its own pattern attribute --
    // blanked every letter typed into it and never advanced. Upstream takes the pattern as a
    // prop for the same reason; here the box already carries one.
    const allowed = (() => {
      const pattern = boxes[0].getAttribute('pattern')
      if (!pattern) return /[^0-9]/g
      try { return new RegExp('[^' + pattern.replace(/^\[|\]$/g, '') + ']', 'g') }
      catch { return /[^0-9]/g }
    })()
    const keep = (text) => text.replace(allowed, '')

    // Pasting the whole code is how most people enter one — from a message, not by typing.
    const spread = (digits, from = 0) => {
      for (let k = 0; k < digits.length && from + k < boxes.length; k++) {
        boxes[from + k].value = digits[k]
      }
      boxes[Math.min(from + digits.length, boxes.length - 1)].focus()
    }

    const onInput = (event) => {
      const box = event.target
      const i = boxes.indexOf(box)
      if (i < 0) return
      // A phone keyboard can deliver more than one character at a time.
      const typed = keep(box.value)
      box.value = typed.slice(0, 1)
      if (typed.length > 1) spread(typed, i)
      else if (box.value && i < boxes.length - 1) boxes[i + 1].focus()
      sync()
    }

    const onKeyDown = (event) => {
      const i = boxes.indexOf(event.target)
      if (i < 0) return
      if (event.key === 'Backspace' && !event.target.value && i > 0) {
        // Retreat AND clear, which is what a code field is expected to do: one backspace per
        // digit, not one to move and another to erase.
        event.preventDefault()
        boxes[i - 1].value = ''
        boxes[i - 1].focus()
        sync()
      } else if (event.key === (rtl(root) ? 'ArrowRight' : 'ArrowLeft') && i > 0) {
        event.preventDefault()
        boxes[i - 1].focus()
      } else if (event.key === (rtl(root) ? 'ArrowLeft' : 'ArrowRight')
                 && i < boxes.length - 1) {
        event.preventDefault()
        boxes[i + 1].focus()
      }
    }

    const onPaste = (event) => {
      const digits = keep(event.clipboardData?.getData('text') || '')
      if (!digits) return
      event.preventDefault()
      spread(digits)
      sync()
    }

    // Clicking box four while two and three are empty is almost never what was meant.
    const onFocus = (event) => {
      const firstEmpty = boxes.find((b) => !b.value && !b.disabled) || boxes[boxes.length - 1]
      if (boxes.indexOf(event.target) > boxes.indexOf(firstEmpty)) { firstEmpty.focus(); return }
      // Select what is there, so typing replaces it. maxlength="1" on a full box otherwise
      // swallows the keystroke silently and the caret never moves on.
      event.target.select?.()
    }

    root.addEventListener('input', onInput)
    root.addEventListener('keydown', onKeyDown)
    root.addEventListener('paste', onPaste)
    root.addEventListener('focusin', onFocus)
    sync()

    return () => {
      root.removeEventListener('input', onInput)
      root.removeEventListener('keydown', onKeyDown)
      root.removeEventListener('paste', onPaste)
      root.removeEventListener('focusin', onFocus)
    }
  })

  // =============================================================================================
  // HoverCard
  // =============================================================================================
  // A popover that opens on hover. It is decoration by definition — anything only reachable by
  // pointing at it is unreachable to a keyboard and to a touch screen — so the trigger stays a
  // real link or button and this only adds the preview. Focus opens it too, and Escape closes
  // it, because a keyboard user should be able to reach the same content the mouse gets.
  //
  // The pointer is allowed to travel from the trigger into the panel without it closing, which
  // is the one thing that makes a hover card usable and the one thing implementations forget.

  // One sweep for every hover card on the page, registered once and owned by nobody.
  //
  // Each trigger's own compiler closes its card, and that is enough right up until the compiler
  // is not there: a fragment swap that replaces the TRIGGER but not the panel runs the
  // destructor and leaves the panel open in the top layer with nothing listening for it. The
  // per-trigger net cannot cover that, because the thing that would have closed it is the thing
  // that went away.
  //
  // Every compiler registers its pointer check (below) against its panel; this interval calls
  // whichever ones it finds. The check is geometric — elementFromPoint at the pointer's last
  // known position — and the sweep must use THAT, not its own :hover test: Chromium does not
  // refresh the hover chain until the pointer moves, so after a wheel scroll under a stationary
  // pointer the old :hover stays true over nothing, the sweep skipped the card, and the card
  // sat on screen for as long as the reader cared to wait. Orphaned panels whose trigger is
  // gone close unconditionally; nobody can be hovering them.
  const hoverCardChecks = new WeakMap()
  setInterval(() => {
    for (const panel of document.querySelectorAll('[data-slot="hover-card-content"]')) {
      if (!panel.matches(':popover-open')) continue
      const checker = hoverCardChecks.get(panel)
      if (checker ? checker() : true) panel.hidePopover()
    }
  }, 500)

  shadcnCompiler('[data-slot="hover-card-trigger"]', (trigger) => {
    const panel = document.getElementById(trigger.dataset.target)
    if (!panel) return
    let openTimer, closeTimer

    const open = () => {
      clearTimeout(closeTimer)
      openTimer = setTimeout(() => {
        panel.showPopover()
        place(panel, trigger, panel.dataset.align, panel.dataset.side,
              Number(panel.dataset.sideOffset || 4))
      }, Number(trigger.dataset.openDelay ?? 600))
    }

    // A grace period, not an immediate close: the gap between the trigger and the panel is a
    // few pixels of nothing, and leaving through it must not count as leaving.
    const close = () => {
      clearTimeout(openTimer)
      closeTimer = setTimeout(() => {
        if (panel.matches(':popover-open')) panel.hidePopover()
      }, Number(trigger.dataset.closeDelay ?? 300))
    }

    const onKey = (event) => { if (event.key === 'Escape') { clearTimeout(openTimer); panel.hidePopover() } }

    // The safety net. pointerleave is the ordinary way this closes, and it is enough right up
    // until it is not: a card that opens over its own trigger, a pointer that leaves the window,
    // an element swapped out from under the cursor. Then no leave event ever arrives and the
    // card sits there with the pointer nowhere near it — which is exactly what gets reported.
    // So ask the document directly: pointer over neither, focus in neither, start closing.
    let at = null
    const elsewhere = () => {
      if (!panel.matches(':popover-open')) return false
      //
      // Decided by GEOMETRY, not by :hover. Chromium does not refresh the hover chain until
      // the pointer itself moves — so after a wheel scroll under a stationary pointer, the
      // page content moves away and the trigger keeps :hover=true over nothing. A sweep built
      // on :hover then keeps the card open forever, which is the report this closes. A hit
      // test against the trigger and panel boxes cannot go stale: it asks where the pointer
      // IS, not where the browser last thought it was. The top-layer panel is included by
      // elementFromPoint like any other element, and a box test survives the boundary the
      // way the old coordinate bookkeeping never did.
      //
      // Focus deliberately does NOT keep the card open. It used to: anything holding focus
      // (trigger :focus-visible, panel :focus-within) was exempt from the sweep — and a real
      // click inside the card (selecting the handle text, following a link) leaves :focus-within
      // set long after the pointer has gone, which held the card open on screen indefinitely.
      // Radix's behaviour is the pointer's: leave both elements and the card closes, focused or
      // not. Focus still OPENS it for the keyboard — and if the reader is mid-Tab and the card
      // folds, Tab or Shift-Tab reopens it from the still-focused trigger.
      if (!at) return false // pointer never seen on this page: keep out of the way entirely

      const over = document.elementFromPoint(at.x, at.y)
      return !over || !(trigger.contains(over) || panel.contains(over))
    }
    hoverCardChecks.set(panel, elsewhere)

    const onMove = (event) => {
      at = { x: event.clientX, y: event.clientY }
      if (elsewhere()) close()
    }

    // A heartbeat, because the check above needs an event to run and a pointer can stop
    // producing them: parked outside the window, resting over browser chrome, or — the one
    // that hid this bug — a wheel scroll under a stationary pointer, which moves the page
    // without a single pointer event. Every 400ms the last known pointer position is hit
    // tested again; scroll, resize and layout shifts are all caught by the same tick.
    const beat = setInterval(() => { if (elsewhere()) close() }, 400)

    // And leaving the window is leaving the card.
    const onOut = (event) => { if (!event.relatedTarget) close() }

    document.addEventListener('pointermove', onMove, { passive: true })
    document.addEventListener('pointerout', onOut, { passive: true })
    window.addEventListener('blur', close)
    // Focus opens it for the KEYBOARD, not for the mouse. A click focuses the trigger too, and
    // then the card stayed open with the pointer long gone — the safety net leaves anything
    // holding focus alone, on purpose, so a card reached by Tab does not vanish while it is
    // being read. :focus-visible is the browser's own answer to "did this focus come from the
    // keyboard", which is exactly the question.
    const onFocus = () => { if (trigger.matches(':focus-visible')) open() }

    trigger.addEventListener('pointerenter', open)
    trigger.addEventListener('pointerleave', close)
    trigger.addEventListener('focus', onFocus)
    trigger.addEventListener('blur', close)
    panel.addEventListener('pointerenter', () => clearTimeout(closeTimer))
    panel.addEventListener('pointerleave', close)
    document.addEventListener('keydown', onKey)

    return () => {
      hoverCardChecks.delete(panel)
      clearTimeout(openTimer)
      trigger.removeEventListener('pointerenter', open)
      trigger.removeEventListener('pointerleave', close)
      trigger.removeEventListener('focus', onFocus)
      trigger.removeEventListener('blur', close)
      clearInterval(beat)
      document.removeEventListener('pointermove', onMove)
      document.removeEventListener('pointerout', onOut)
      window.removeEventListener('blur', close)
      document.removeEventListener('keydown', onKey)
    }
  })

  // =============================================================================================
  // Select
  // =============================================================================================
  // Radix Select is a listbox built from divs and a hidden input. On static SSR the real <select>
  // has to stay in the form — it is what posts, and it is what still works with scripting off. So
  // the native control is kept and hidden, and a shadcn-shaped panel is drawn beside it. Choosing
  // an item writes through to the <select> and dispatches a bubbling change event, so
  // [up-autosubmit] and filter forms behave exactly as they do without this enhancement.

  // <output for="…"> shows what a control currently holds. The element is built for exactly this
  // and the platform still makes you write the assignment, so this writes it once for every
  // output on the page rather than once per demo. It is the whole of shadcn's "controlled"
  // slider: there, the value lives in a state variable and is printed beside the label; here it
  // lives in the input, which is the thing that posts, and the output reads it.
  shadcnCompiler('output[for]', (output) => {
    const source = document.getElementById(output.getAttribute('for'))
    if (!source) return

    const show = () => { output.textContent = source.value }
    source.addEventListener('input', show)
    show()
    return () => source.removeEventListener('input', show)
  })

  shadcnCompiler('input[type="checkbox"][data-toggles]', (box) => {
    const target = document.getElementById(box.dataset.toggles)
    const name = box.dataset.togglesAttribute
    if (!target || !name) return

    const write = () => target.setAttribute(name,
      box.checked ? (box.dataset.togglesOn ?? 'true') : (box.dataset.togglesOff ?? 'false'))

    box.addEventListener('change', write)
    write()
    return () => box.removeEventListener('change', write)
  })

  shadcnCompiler('select[data-slot="select"]', (select) => {
    if (select.multiple || select.closest('[data-slot="select-root"]')) return
    if (select.options.length === 0) return
    // A disabled select is still drawn. Bailing out left the native control on screen wearing no
    // recipe at all, so the one example whose whole subject is the disabled state was the one
    // place the component did not look like shadcn.
    const locked = select.disabled

    // Sizing and layout belong to the visible control, so the caller's own classes move to the
    // wrapper. <Select> hands them over verbatim in data-wrapper-class rather than leaving this to
    // subtract the recipe and guess.
    const wrap = el('span', cn('relative block', select.dataset.wrapperClass))
    wrap.dataset.slot = 'select-root'

    const trigger = el('button', SELECT_TRIGGER, { type: 'button' })
    trigger.dataset.slot = 'select-trigger'
    trigger.dataset.size = select.dataset.size || 'default'
    trigger.setAttribute('aria-haspopup', 'listbox')
    trigger.setAttribute('aria-expanded', 'false')
    if (select.getAttribute('aria-invalid')) trigger.setAttribute('aria-invalid', 'true')
    if (locked) trigger.disabled = true

    const label = el('span', 'line-clamp-1 flex items-center gap-2 text-left')
    label.dataset.slot = 'select-value'
    // A real chevron. This was an empty span with a size and an opacity on it, so the trigger had
    // a gap where shadcn has an arrow — the one part of a select everybody recognises.
    const chevron = el('span', 'flex size-4 shrink-0 items-center justify-center opacity-50',
                       { innerHTML: CHEVRON_DOWN })
    chevron.dataset.slot = 'select-chevron'
    trigger.append(label, chevron)

    // In the top layer, not absolutely positioned inside the wrapper: a select inside a card with
    // overflow-hidden had its list clipped, and a select near the bottom of the window had it cut
    // off. place() is the same anchoring every other panel in this library uses.
    const panel = el('div', cn(SELECT_CONTENT, 'fixed'))
    panel.dataset.slot = 'select-content'
    panel.setAttribute('role', 'listbox')
    panel.setAttribute('popover', 'auto')
    panel.id = (select.id || 'select') + '-content-' + (++selectSeq)

    // The list scrolls, so it gets the two buttons Radix draws for the same reason: at the ends of
    // a long list there is nothing else to say the list continues.
    const scrollUp = el('div', cn(SELECT_SCROLL_BUTTON, 'hidden'), { innerHTML: CHEVRON_UP })
    scrollUp.dataset.slot = 'select-scroll-up-button'
    const scrollDown = el('div', cn(SELECT_SCROLL_BUTTON, 'hidden'), { innerHTML: CHEVRON_DOWN })
    scrollDown.dataset.slot = 'select-scroll-down-button'
    const list = el('div', 'flex flex-col')
    panel.append(scrollUp, list, scrollDown)

    // Walk the select's own children rather than its flat option list, because <optgroup> and <hr>
    // ARE the grouping and the separator — the element has had both all along, and the drawn panel
    // threw them away and rendered one undifferentiated column.
    const items = []

    const drawOption = (option, into) => {
      const item = el('div', SELECT_ITEM, { role: 'option' })
      item.dataset.slot = 'select-item'
      item.tabIndex = -1
      item.append(el('span', 'flex-1 truncate', { textContent: option.text }))
      const indicator = el('span', SELECT_INDICATOR)
      indicator.dataset.slot = 'select-item-indicator'
      item.append(indicator)
      if (option.disabled) item.dataset.disabled = ''
      item.addEventListener('click', () => {
        if (option.disabled) return
        choose(option.value)
        close()
        trigger.focus()
      })
      into.append(item)
      items.push({ option, item, indicator })
    }

    for (const child of select.children) {
      if (child.tagName === 'OPTGROUP') {
        const group = el('div', '', { role: 'group' })
        group.dataset.slot = 'select-group'
        const heading = el('div', SELECT_LABEL, { textContent: child.label })
        heading.dataset.slot = 'select-label'
        group.append(heading)
        for (const option of child.children) drawOption(option, group)
        list.append(group)
      } else if (child.tagName === 'HR') {
        const rule = el('div', SELECT_SEPARATOR)
        rule.dataset.slot = 'select-separator'
        list.append(rule)
      } else if (child.tagName === 'OPTION') {
        drawOption(child, list)
      }
    }

    let index = -1

    function sync() {
      const current = [...select.options].find((o) => o.value === select.value)
      label.textContent = current ? current.text : (select.dataset.placeholder || '')
      if (current) delete trigger.dataset.placeholder
      else trigger.dataset.placeholder = ''
      for (const entry of items) {
        const on = entry.option.value === select.value
        entry.item.dataset.selected = String(on)
        entry.item.setAttribute('aria-selected', String(on))
        // The tick. Radix marks the chosen row and this drew nothing at all, so an open list gave
        // no sign of which row you were already on.
        entry.indicator.innerHTML = on ? CHECK : ''
      }
      index = Math.max(0, items.findIndex((entry) => entry.option.value === select.value))
    }

    function active(i) {
      index = Math.max(0, Math.min(i, items.length - 1))
      items.forEach((entry, k) => k === index ? entry.item.dataset.active = '' : delete entry.item.dataset.active)
      if (items[index]) items[index].item.scrollIntoView({ block: 'nearest' })
      edges()
    }

    // Which end of the list is out of view. Radix hides each button at its own end; so does this.
    function edges() {
      const more = panel.scrollHeight - panel.clientHeight > 1
      scrollUp.classList.toggle('hidden', !more || panel.scrollTop < 4)
      scrollDown.classList.toggle('hidden',
        !more || panel.scrollTop + panel.clientHeight >= panel.scrollHeight - 4)
    }

    function choose(value) {
      if (value !== select.value) {
        select.value = value
        select.dispatchEvent(new Event('change', { bubbles: true }))
      }
      sync()
    }

    function open() {
      if (locked) return
      panel.showPopover()
      trigger.setAttribute('aria-expanded', 'true')
      // At least as wide as the control it belongs to, which is what makes it read as the same
      // object opening rather than a menu appearing near one.
      panel.style.minWidth = Math.round(trigger.getBoundingClientRect().width) + 'px'
      place(panel, trigger, 'start', 'bottom', 4)
      alignItem()
      requestAnimationFrame(alignItem)
      // Lock until the reader chooses — the list is the takeover, and the page must not run
      // off behind it. The lock is gutter-stable (see lockScroll), so hiding the scrollbar
      // does not shift the layout underneath.
      lockScroll(true)
      document.addEventListener('keydown', onKey, true)
    }

    // alignItemWithTrigger, which is on by default upstream: the list opens with the CHOSEN row
    // sitting over the trigger, so the value you already have does not move under the pointer.
    // Off, the list hangs below the control like a menu. place() has already put it below, so
    // this only has to lift it — and only as far as the window allows, because a list taller than
    // the viewport cannot honour the request and must not be dragged off the top to try.
    function alignItem() {
      if (select.dataset.alignItemWithTrigger === 'false') return
      const chosen = items[Math.max(0, index)]
      if (!chosen) return
      const box = trigger.getBoundingClientRect()
      const offsetTop = fixedBox().top
      const panelTop = parseFloat(panel.style.top) + offsetTop
      // Where the row sits inside the panel, from layout rather than from its rectangle, which
      // is scaled with the panel while it opens.
      const lift = panelTop + chosen.item.offsetTop - panel.scrollTop - box.top
      const top = Math.min(
        Math.max(8, panelTop - lift),
        Math.max(8, window.innerHeight - panel.offsetHeight - 8))
      panel.style.top = Math.round(top - offsetTop) + 'px'
      panel.style.bottom = 'auto'
    }

    function close() {
      if (!isOpen()) return
      panel.hidePopover()
      // hidePopover fires toggle synchronously, and that is where the lock is released — doing
      // it here as well would release it twice for one opening.
    }

    const isOpen = () => panel.matches(':popover-open')

    function onKey(e) {
      if (!isOpen()) {
        if (['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(e.key)) { e.preventDefault(); open() }
        return
      }
      if (e.key === 'Escape') { e.preventDefault(); close(); trigger.focus() }
      else if (e.key === 'Tab') close()
      else if (e.key === 'ArrowDown') { e.preventDefault(); active(index + 1) }
      else if (e.key === 'ArrowUp') { e.preventDefault(); active(index - 1) }
      else if (e.key === 'Home') { e.preventDefault(); active(0) }
      else if (e.key === 'End') { e.preventDefault(); active(items.length - 1) }
      else if (e.key === 'Enter') {
        e.preventDefault()
        const hit = items[index]
        if (hit && !hit.option.disabled) { choose(hit.option.value); close(); trigger.focus() }
      }
    }

    // Light dismiss closes the popover without telling the trigger, and an aria-expanded that
    // says "true" over a closed list is a lie a screen reader has no way to check.
    // Every close comes through here — the button, Escape, a choice, and light dismiss, which
    // closes the popover without telling anyone. An aria-expanded reading "true" over a closed
    // list is a lie a screen reader cannot check, and a scroll lock nobody released is a page
    // that never scrolls again.
    const onToggle = (event) => {
      if (event.newState !== 'closed') return
      trigger.setAttribute('aria-expanded', 'false')
      lockScroll(false)
      document.removeEventListener('keydown', onKey, true)
    }

    trigger.addEventListener('click', () => (isOpen() ? close() : open()))
    trigger.addEventListener('keydown', (e) => { if (!isOpen()) onKey(e) })
    panel.addEventListener('scroll', edges, { passive: true })
    panel.addEventListener('toggle', onToggle)
    // The native select is still the value. A reset, a fragment swap or another script can move
    // it, and the drawn face has to follow rather than claim a value the form does not have.
    select.addEventListener('change', sync)
    // Blazor can update a bound <select> during a component render without dispatching change.
    // Its native value is then correct while this drawn trigger keeps showing the old/first row.
    // Watching the rendered value/selected attributes keeps both faces of the same control in sync.
    const renderedValue = new MutationObserver(sync)
    renderedValue.observe(select, {
      attributes: true,
      childList: true,
      subtree: true,
      attributeFilter: ['value', 'selected', 'disabled']
    })

    sync()
    select.before(wrap)
    wrap.append(select, trigger, panel)
    select.classList.add('sr-only')
    select.tabIndex = -1
    return () => {
      if (isOpen()) lockScroll(false)
      panel.remove()
      renderedValue.disconnect()
      select.removeEventListener('change', sync)
      select.classList.remove('sr-only')
      select.tabIndex = 0
      wrap.replaceWith(select)
    }
  })

  // =============================================================================================
  // DatePicker
  // =============================================================================================
  // Air Datepicker (vendored under /airdatepicker, MIT), not flatpickr: flatpickr renders the year
  // as a number input whose ± arrows are 14px wide and opacity:0 until hovered, so in practice the
  // year could not be changed, only stepped, by someone who found the arrows. Air Datepicker's
  // header title is a button — months -> years -> decades — so any year is two or three clicks,
  // and the same navigation works from the keyboard.
  //
  // Air Datepicker ships one locale file per language and none of them are bundled here, so the
  // month and day names are configuration. Unset means its own English default.
  // 📖 https://air-datepicker.com/docs — the shape is theirs, not ours.
  const DEFAULT_DATE_LOCALE = { dateFormat: 'dd/MM/yyyy', timeFormat: 'HH:mm', firstDay: 1 }

  const isoDate = (d) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

  shadcnCompiler('[data-slot="date-picker"]', (input) => {
    if (typeof window.AirDatepicker !== 'function') return

    // What the form posts stays ISO (yyyy-MM-dd), matching the domain's DateOnly parsing, while
    // the field on screen reads d/m/Y. Air Datepicker has no altInput, so the name moves to a
    // hidden twin and the visible field becomes display-only.
    const name = input.getAttribute('name')
    const hidden = el('input', '', { type: 'hidden', name, value: input.value })
    input.removeAttribute('name')
    input.after(hidden)

    const seeded = input.value ? new Date(`${input.value}T00:00:00`) : null
    const valid = seeded && !Number.isNaN(seeded.getTime()) ? seeded : null

    const dp = new window.AirDatepicker(input, {
      locale: { ...DEFAULT_DATE_LOCALE, ...(config().datePickerLocale || {}) },
      dateFormat: config().datePickerLocale?.dateFormat || DEFAULT_DATE_LOCALE.dateFormat,
      selectedDates: valid ? [valid] : [],
      autoClose: true,
      isMobile: false,
      buttons: ['today', 'clear'],
      // Rendered on <body>, not beside the field. Inside an Unpoly overlay the field lives in
      // up-modal-content, which scrolls — and a popup inside a scrollport is clipped by it, so the
      // calendar came up buried under the modal. A global container also puts it above up-modal's
      // z-index:2000 (see --adp-z-index in ui.behavior.css).
      container: document.body,
      onSelect: ({ date }) => {
        const picked = Array.isArray(date) ? date[0] : date
        hidden.value = picked ? isoDate(picked) : ''
        // The hidden input is what filter / up-autosubmit forms watch, and a programmatic value
        // assignment fires nothing on its own.
        hidden.dispatchEvent(new Event('change', { bubbles: true }))
      },
    })

    // readOnly, not disabled: the field still focuses, still tabs, still opens the calendar, but
    // there is no free-typing a date the parser then has to guess at.
    input.readOnly = true

    // The overlay's own scrollport is not the window, so Air Datepicker's window-scroll listener
    // never fires and the calendar would hang where the field used to be. Follow it, or close.
    const scroller = input.closest('up-modal-content, [data-overlay-scroll]')
    const follow = () => { if (dp.visible) dp.hide() }
    scroller?.addEventListener('scroll', follow, { passive: true })

    return () => {
      scroller?.removeEventListener('scroll', follow)
      dp.destroy()
      hidden.remove()
      if (name !== null) input.setAttribute('name', name)
      input.readOnly = false
    }
  })

  // =============================================================================================
  // data-state on a native <dialog>
  // =============================================================================================
  // shadcn animates its overlays with data-[state=open] and data-[state=closed], which Radix
  // sets as it mounts and unmounts. A <dialog> has no such attribute — it has an `open` property
  // and nothing else — so every one of those classes matched nothing and the panels appeared and
  // vanished with no transition at all.
  //
  // The close half has to outlive the close: `closed` is set, the animation is allowed to run,
  // and only then is the attribute cleared. Reading the duration from the element rather than
  // hardcoding one means a theme that slows the animation down does not get it cut short.

  shadcnCompiler('dialog[data-slot]', (dialog) => {
    const panel = dialog.firstElementChild || dialog

    const opened = () => { panel.dataset.state = 'open' }

    const closing = () => {
      panel.dataset.state = 'closed'
      const ms = parseFloat(getComputedStyle(panel).animationDuration) * 1000 || 0
      setTimeout(() => { if (!isOpen(dialog)) delete panel.dataset.state }, ms)
    }

    // showModal() fires no event of its own, so the open state is watched rather than hooked.
    const watch = new MutationObserver(() => (isOpen(dialog) ? opened() : closing()))
    watch.observe(dialog, { attributes: true, attributeFilter: ['open'] })
    if (isOpen(dialog)) opened()

    return () => watch.disconnect()
  })

  // =============================================================================================
  // Slider — the filled part of the track
  // =============================================================================================
  // The input draws its own track and thumb; what it has no element for is the filled portion to
  // the left of the thumb. That is one gradient stop, so this keeps a percentage in a custom
  // property and CSS does the rest. Without it the slider still works — it is the fill that is
  // missing, not the control, which is why this is a compiler and not a requirement.

  shadcnCompiler('input[type="range"][data-slot="slider"]', (input) => {
    const paint = () => {
      const min = Number(input.min || 0)
      const max = Number(input.max || 100)
      const span = max - min
      input.style.setProperty('--slider-fill', span ? `${((input.value - min) / span) * 100}%` : '0%')
    }

    input.addEventListener('input', paint)
    paint()

    return () => input.removeEventListener('input', paint)
  })

  // Several thumbs: the inputs are stacked, so the only rules that need script are the ones
  // BETWEEN them — a thumb never crosses its neighbour, and the band spans the outermost two.
  // The band's percentages arrive inline from the server; this keeps them true while dragging,
  // and mirrors each value into any <output for="..."> pointed at the input, which is the
  // element HTML already has for "the live result of a control".
  shadcnCompiler('[data-slot="slider"][data-range]', (root) => {
    const inputs = [...root.querySelectorAll('input[type="range"]')]
    const min = Number(inputs[0]?.min || 0)
    const span = Number(inputs[0]?.max || 100) - min

    const sync = () => {
      const values = inputs.map((i) => Number(i.value))
      if (span > 0) {
        root.style.setProperty('--slider-from', `${((Math.min(...values) - min) / span) * 100}%`)
        root.style.setProperty('--slider-to', `${((Math.max(...values) - min) / span) * 100}%`)
      }
      for (const input of inputs) {
        const output = input.id && document.querySelector(`output[for="${CSS.escape(input.id)}"]`)
        if (output) output.textContent = input.value
      }
    }

    const order = (event) => {
      const at = inputs.indexOf(event.target)
      const before = inputs[at - 1], after = inputs[at + 1]
      if (before && Number(event.target.value) < Number(before.value)) event.target.value = before.value
      if (after && Number(event.target.value) > Number(after.value)) event.target.value = after.value
      sync()
    }

    root.addEventListener('input', order)
    sync()

    return () => root.removeEventListener('input', order)
  })

  // A calendar inside a [popover] is a date picker, and a date picker closes when you pick.
  // Without this the day was chosen — the radio really was checked, and it really would post —
  // but the panel stayed open over the page and the button still said the old date, so the
  // choice looked like it had not registered at all.
  shadcnCompiler('[popover] [data-slot="calendar"]', (calendar) => {
    const panel = calendar.closest('[popover]')
    if (!panel?.id) return
    const trigger = document.querySelector(`[popovertarget="${panel.id}"]`)
    const range = calendar.dataset.mode === 'range'

    const written = (value) => {
      const [year, month, date] = value.split('-').map(Number)
      return new Date(year, month - 1, date).toLocaleDateString(
        document.documentElement.lang || undefined,
        { day: 'numeric', month: 'short', year: 'numeric' })
    }

    const onChange = (event) => {
      if (!event.target.matches('input[type="radio"], input[type="checkbox"]')) return

      // The label is a span the caller marks, not the button's own text: the button also holds
      // an icon, and writing over its textContent would take that with it. In two of upstream's
      // pickers the date is not on the button at all — it is the value of a text box the button
      // sits inside — so the mark is looked up in the surrounding field as well, and an input
      // takes it as its value rather than its text.
      const label = trigger?.querySelector('[data-date-label]')
        || trigger?.closest('[data-slot="field"], [data-date-field]')
             ?.querySelector('[data-date-label]')
      const on = [...calendar.querySelectorAll('input:checked')].map((i) => i.value).sort()
      if (label && on.length) {
        const text = range && on.length > 1
          ? `${written(on[0])} – ${written(on[on.length - 1])}`
          : written(on[0])
        if (label.tagName === 'INPUT') label.value = text
        else label.textContent = text
        label.removeAttribute('data-empty')
      }

      // A range needs two dates, so picking one is not finishing. A single date is.
      if (range) return
      panel.hidePopover()
      trigger?.focus()
    }

    calendar.addEventListener('change', onChange)
    return () => calendar.removeEventListener('change', onChange)
  })

  // A slider that drives a progress bar. shadcn holds both in one state variable; here the
  // slider is the input that posts and the bar is what it reports, so the link between them is
  // one attribute naming the other — and with scripting off you still get a working slider and a
  // bar showing whatever the server rendered.
  shadcnCompiler('input[type="range"][data-controls]', (slider) => {
    const bar = document.getElementById(slider.dataset.controls)
    const indicator = bar?.querySelector('[data-slot="progress-indicator"]')
    if (!indicator) return

    const paint = () => {
      const min = Number(slider.min || 0)
      const span = Number(slider.max || 100) - min
      const share = span > 0 ? ((Number(slider.value) - min) / span) * 100 : 0
      indicator.style.transform = `translateX(-${100 - share}%)`
      bar.setAttribute('aria-valuenow', String(Math.round(Number(slider.value))))
    }

    slider.addEventListener('input', paint)
    paint()

    return () => slider.removeEventListener('input', paint)
  })

  // =============================================================================================
  // Stepper — the two buttons beside a number field
  // =============================================================================================
  // shadcn has no number stepper, and a bare <input type=number> is not one: its spinners are a
  // different shape in every engine and invisible until hovered. The component is pure styling
  // around a plain <button>/<input>/<button>; this is the behaviour that makes it a stepper —
  // the buttons step the input by its own step, clamp to min/max, and dispatch a change event so
  // the rest of the page (a form, a control bound to it) sees the new value. Nothing here holds
  // state; the input is the state, and with scripting off it is still a working number field.

  shadcnCompiler('[data-slot="stepper"]', (root) => {
    const input = root.querySelector('input[type="number"]')
    if (!input) return

    const step = (dir) => {
      const min = input.min === '' ? null : Number(input.min)
      const max = input.max === '' ? null : Number(input.max)
      const size = Number(input.step) || 1
      const now = Number(input.value) || 0
      let next = now + dir * size
      if (min !== null) next = Math.max(min, next)
      if (max !== null) next = Math.min(max, next)
      if (next === now) return
      input.value = String(next)
      input.dispatchEvent(new Event('input', { bubbles: true }))
      input.dispatchEvent(new Event('change', { bubbles: true }))
    }

    const onButton = (event) => {
      const button = event.target.closest('button')
      if (!button || button.disabled) return
      if (button.hasAttribute('data-qty-plus')) step(1)
      else if (button.hasAttribute('data-qty-minus')) step(-1)
    }

    root.addEventListener('click', onButton)
    return () => root.removeEventListener('click', onButton)
  })

  // =============================================================================================
  // FileUpload — a drop zone that posts the file and keeps the URL
  // =============================================================================================
  // The file goes up on its own, as soon as it is chosen, and what stays behind in the form is a
  // hidden input holding the URL that came back. That is the whole design, and it buys three
  // things a plain <input type="file"> cannot: the surrounding form stays an ordinary form
  // rather than multipart, a chosen image survives a validation failure that re-renders the
  // page, and a field can sit inside a form that is already doing something else.
  //
  // The antiforgery token is read from the surrounding form, because the upload is a POST to the
  // same application and ASP.NET will refuse it otherwise — and the failure is a 400 with no
  // body, which reads like a broken endpoint rather than a missing token.

  shadcnCompiler('[data-slot="file-upload"]', (root) => {
    const zone = root.querySelector('[data-slot="file-upload-zone"]')
    const picker = root.querySelector('[data-slot="file-upload-input"]')
    const value = root.querySelector('[data-slot="file-upload-value"]')
    const preview = root.querySelector('[data-slot="file-upload-preview"]')
    const empty = root.querySelector('[data-slot="file-upload-empty"]')
    const clear = root.querySelector('[data-slot="file-upload-clear"]')
    const status = root.querySelector('[data-slot="file-upload-status"]')
    if (!zone || !picker || !value) return

    const text = () => ({
      uploading: 'Uploading…',
      failed: 'Upload failed.',
      refused: 'That file is not accepted here.',
      tooBig: 'That file is too large.',
      ...(config().fileUploadText || {}),
    })

    const say = (message, tone) => {
      if (!status) return
      status.textContent = message || ''
      status.dataset.tone = tone || ''
    }

    // What the field holds decides what the zone shows. One function, called after every change,
    // rather than each path remembering to update three elements.
    const paint = () => {
      const url = value.value
      zone.dataset.filled = url ? 'true' : 'false'
      if (preview) {
        // Removed rather than emptied: src="" is a request for the CURRENT PAGE, which the
        // browser fetches, fails to decode as an image, and reports in the console.
        //
        // A stored PATH may belong to another origin, so it is resolved against
        // data-preview-base for display only — what the field posts is never rewritten.
        const base = (root.dataset.previewBase || '').replace(/\/$/, '')
        if (url) preview.src = base && url.startsWith('/') ? base + url : url
        else preview.removeAttribute('src')
        preview.hidden = !url
      }
      if (empty) empty.hidden = !!url
      if (clear) clear.hidden = !url
    }

    const accepts = (file) => {
      const accept = root.dataset.accept
      if (!accept || accept === '*/*') return true
      return accept.split(',').map((rule) => rule.trim()).some((rule) =>
        rule.endsWith('/*') ? file.type.startsWith(rule.slice(0, -1))
          : rule.startsWith('.') ? file.name.toLowerCase().endsWith(rule.toLowerCase())
          : file.type === rule)
    }

    const send = async (file) => {
      if (!file) return
      // Checked here as well as in the picker: `accept` is advice the picker gives, and a file
      // dragged onto the zone never went near it.
      if (!accepts(file)) return say(text().refused, 'error')
      const max = Number(root.dataset.maxBytes || 0)
      if (max > 0 && file.size > max) return say(text().tooBig, 'error')

      zone.dataset.uploading = 'true'
      say(text().uploading)

      const body = new FormData()
      body.append('file', file)
      const token = root.closest('form')?.querySelector('input[name="__RequestVerificationToken"]')
      if (token) body.append(token.name, token.value)

      try {
        const response = await fetch(root.dataset.action, {
          method: 'POST', body, headers: { Accept: 'application/json' },
        })
        if (!response.ok) throw new Error(String(response.status))
        const answer = await response.json()
        if (!answer?.url) throw new Error('no url')

        value.value = answer.url
        // An input written by script raises nothing, and a form that is watching its own fields
        // — up-validate, up-autosubmit — would never hear about the one field that changed.
        value.dispatchEvent(new Event('input', { bubbles: true }))
        value.dispatchEvent(new Event('change', { bubbles: true }))
        paint()
        say('')
      } catch {
        say(text().failed, 'error')
      } finally {
        delete zone.dataset.uploading
      }
    }

    const onPick = () => send(picker.files[0])
    const onClick = (event) => { if (!event.target.closest('[data-slot="file-upload-clear"]')) picker.click() }
    const onKey = (event) => {
      if (event.key !== 'Enter' && event.key !== ' ') return
      if (event.target.closest('[data-slot="file-upload-clear"]')) return
      event.preventDefault()
      picker.click()
    }

    const onOver = (event) => { event.preventDefault(); zone.dataset.dragging = 'true' }
    const onLeave = (event) => { if (!zone.contains(event.relatedTarget)) delete zone.dataset.dragging }
    const onDrop = (event) => {
      event.preventDefault()
      delete zone.dataset.dragging
      send(event.dataTransfer?.files?.[0])
    }

    const onClear = (event) => {
      event.preventDefault()
      event.stopPropagation()
      value.value = ''
      value.dispatchEvent(new Event('input', { bubbles: true }))
      value.dispatchEvent(new Event('change', { bubbles: true }))
      picker.value = ''
      paint()
      say('')
    }

    picker.addEventListener('change', onPick)
    zone.addEventListener('click', onClick)
    zone.addEventListener('keydown', onKey)
    zone.addEventListener('dragover', onOver)
    zone.addEventListener('dragleave', onLeave)
    zone.addEventListener('drop', onDrop)
    clear?.addEventListener('click', onClear)
    paint()

    return () => {
      picker.removeEventListener('change', onPick)
      zone.removeEventListener('click', onClick)
      zone.removeEventListener('keydown', onKey)
      zone.removeEventListener('dragover', onOver)
      zone.removeEventListener('dragleave', onLeave)
      zone.removeEventListener('drop', onDrop)
      clear?.removeEventListener('click', onClear)
    }
  })

  // =============================================================================================
  // Carousel — the two arrows, and only the two arrows
  // =============================================================================================
  // Scroll-snap is the carousel. These buttons scroll it by one slide and disable themselves at
  // each end, which is what upstream's canScrollPrev/canScrollNext do. They ship hidden and are
  // revealed here, because an arrow that cannot scroll is worse than no arrow.

  shadcnCompiler('[data-slot="carousel"]', (root) => {
    const scroller = root.querySelector('[data-slot="carousel-content"]')
    if (!scroller) return

    const buttons = [...root.querySelectorAll('[data-carousel-scroll]')]
    const vertical = root.dataset.orientation === 'vertical'

    const step = () => {
      const item = scroller.querySelector('[data-slot="carousel-item"]')
      // One slide, or one viewport when the slides are narrower than it.
      return item ? (vertical ? item.offsetHeight : item.offsetWidth)
                  : (vertical ? scroller.clientHeight : scroller.clientWidth)
    }

    const sign = () => (!vertical && rtl(scroller) ? -1 : 1)

    const sync = () => {
      const pos = vertical ? scroller.scrollTop : scroller.scrollLeft * sign()
      const max = (vertical ? scroller.scrollHeight - scroller.clientHeight
                            : scroller.scrollWidth - scroller.clientWidth)
      for (const button of buttons) {
        const back = button.dataset.carouselScroll === '-1'
        // A fractional scroll position never reaches the exact maximum, so round.
        button.disabled = back ? pos <= 1 : pos >= max - 1
      }
    }

    const onClick = (event) => {
      const button = event.currentTarget
      const by = Number(button.dataset.carouselScroll) * step()
      scroller.scrollBy(vertical ? { top: by, behavior: 'smooth' }
                                 : { left: by * sign(), behavior: 'smooth' })
    }

    for (const button of buttons) {
      button.hidden = false
      button.addEventListener('click', onClick)
    }
    scroller.addEventListener('scroll', sync, { passive: true })
    sync()

    return () => {
      for (const button of buttons) button.removeEventListener('click', onClick)
      scroller.removeEventListener('scroll', sync)
    }
  })

  // =============================================================================================
  // Resizable — dragging the divider, and moving it from the keyboard
  // =============================================================================================
  // The handle rewrites flex-grow on the panel either side of it. Nothing else changes, so a drag
  // costs one style recalculation per pointer move rather than a render.
  //
  // Arrow keys move it too. A divider only a pointer can move is a divider that half the people
  // using it cannot move at all, and role=separator promises otherwise.

  shadcnCompiler('[data-slot="resizable-handle"]', (handle) => {
    const group = handle.closest('[data-slot="resizable-panel-group"]')
    const before = handle.previousElementSibling
    const after = handle.nextElementSibling
    if (!group || !before || !after) return

    const vertical = group.dataset.orientation === 'vertical'
    const min = (el) => Number(el.dataset.minSize || 0.1)

    // The pair's total grow is preserved, so the rest of the group never moves.
    const total = () => Number(getComputedStyle(before).flexGrow) + Number(getComputedStyle(after).flexGrow)

    const apply = (share) => {
      const sum = total()
      const clamped = Math.min(Math.max(share, min(before)), sum - min(after))
      before.style.flexGrow = String(clamped)
      after.style.flexGrow = String(sum - clamped)
      handle.setAttribute('aria-valuenow', String(Math.round((clamped / sum) * 100)))
    }

    const fromPointer = (event) => {
      const box = group.getBoundingClientRect()
      const across = rtl(group) ? (box.right - event.clientX) / box.width
                                : (event.clientX - box.left) / box.width
      const at = vertical ? (event.clientY - box.top) / box.height : across
      apply(at * total())
    }

    const onMove = (event) => { event.preventDefault(); fromPointer(event) }

    const stop = () => {
      delete handle.dataset.dragging
      window.removeEventListener('pointermove', onMove)
      window.removeEventListener('pointerup', stop)
    }

    const onDown = (event) => {
      event.preventDefault()
      handle.dataset.dragging = ''
      handle.focus()
      window.addEventListener('pointermove', onMove)
      window.addEventListener('pointerup', stop)
    }

    const onKey = (event) => {
      const nudge = rtl(group)
        ? { ArrowRight: -1, ArrowUp: -1, ArrowLeft: 1, ArrowDown: 1 }[event.key]
        : { ArrowLeft: -1, ArrowUp: -1, ArrowRight: 1, ArrowDown: 1 }[event.key]
      if (nudge === undefined && event.key !== 'Home' && event.key !== 'End') return
      event.preventDefault()

      const sum = total()
      const now = Number(getComputedStyle(before).flexGrow)
      if (event.key === 'Home') apply(min(before))
      else if (event.key === 'End') apply(sum - min(after))
      else apply(now + nudge * sum * (event.shiftKey ? 0.1 : 0.02))
    }

    handle.setAttribute('aria-valuemin', '0')
    handle.setAttribute('aria-valuemax', '100')
    apply(Number(getComputedStyle(before).flexGrow))

    handle.addEventListener('pointerdown', onDown)
    handle.addEventListener('keydown', onKey)

    return () => {
      stop()
      handle.removeEventListener('pointerdown', onDown)
      handle.removeEventListener('keydown', onKey)
    }
  })

  // =============================================================================================
  // NumberTicker — the count-up, and only the count-up
  // =============================================================================================
  // The final value is already in the markup, because the server wrote it there and a reader with
  // scripting off is owed a real number rather than a zero waiting for a script. This is therefore
  // strictly an enhancement: it rewrites the same text node, once, from the start value to the end
  // value, when the figure first comes into view — and it puts the end value back if it is torn
  // down half way through.
  //
  // `IntersectionObserver` is the platform's own "has this been seen" test, which is all
  // `useInView` is here: no scroll listener, no per-frame hit test, and no work at all for a ticker
  // the reader never scrolls to.
  //
  // The motion is a spring integrated by hand rather than a CSS transition, because what moves
  // here is TEXT. There is no box to transition and no element to animate — writing the number each
  // frame is the whole of it. Upstream's constants are kept, so the timing is the one this
  // component has always had.

  shadcnCompiler('[data-slot="number-ticker"]', (root) => {
    // Re-read at the moment it is needed, never captured once. Blazor re-renders this span's text
    // and its data attributes without telling the compiler, and one that remembered the figure it
    // saw at compile time would count to the OLD number and write it back over the new one on its
    // last frame — so a figure the server changed would visibly go backwards.
    const read = () => {
      const target = Number(root.dataset.tickerValue)
      if (!Number.isFinite(target)) return null
      const start = Number(root.dataset.tickerStart)
      return {
        target,
        from: Number.isFinite(start) ? start : 0,
        places: Number(root.dataset.tickerDecimals) || 0,
        prefix: root.dataset.tickerPrefix || '',
        suffix: root.dataset.tickerSuffix || '',
        down: root.dataset.tickerDirection === 'down',
        delayMs: Math.max(0, (Number(root.dataset.tickerDelay) || 0) * 1000),
      }
    }

    // Fixed decimals and commas spelled out here rather than handed to Intl, because this string
    // reaches the DOM: under a locale whose group separator is a narrow no-break space, a
    // formatter puts a character in the text node that the server never wrote, and nothing anyone
    // copies out of it ever matches. Rounds half away from zero, which is what `toFixed` does and
    // therefore what the C# side does too — so the final frame is character-for-character the
    // number the server had already put there.
    const format = (value, places) => {
      const digits = Math.max(0, Math.min(15, Math.trunc(places) || 0))
      const [whole, fraction] = Math.abs(value).toFixed(digits).split('.')
      return (value < 0 ? '-' : '')
        + whole.replace(/\B(?=(\d{3})+(?!\d))/g, ',')
        + (fraction ? '.' + fraction : '')
    }

    const write = (state, value) => {
      root.textContent = state.prefix + format(value, state.places) + state.suffix
    }

    let frame = 0
    let delayTimer = 0
    let capTimer = 0
    let stopped = false

    // The one thing this element can ever end up saying. Reduced motion, a delay of zero, a
    // figure that is already at its start, an observer that never fired and a teardown all land
    // on the same number.
    const finish = (state) => {
      if (stopped) return
      stopped = true
      // Re-read rather than trust the figure the count started from: a Value the server changed
      // while the spring was still running would otherwise be overwritten by the old one on the
      // last frame, so the number would finish counting to something nobody asked for.
      const fresh = read() || state
      write(fresh, fresh.target)
    }

    const run = (state) => {
      let position = state.from
      let velocity = 0
      const stiffness = 100
      const damping = 60
      let last = performance.now()

      const tick = (now) => {
        if (stopped) return
        // Clamped, because a tab that was in the background hands back one enormous dt, and an
        // unclamped integration step that size goes straight past the target and never returns.
        let dt = Math.min(64, now - last) / 1000
        last = now
        // Semi-implicit Euler on x'' = -k(x - target) - c·x'. Damping well above critical, so the
        // figure eases in and stops rather than overshooting — a number that wobbles past its
        // value and comes back reads as a number that was guessed.
        //
        // In steps of at most 8ms, whatever the frame took. The update multiplies the velocity by
        // (1 - c·dt), which flips sign and grows once c·dt passes 2 — at this damping, any frame
        // slower than 33ms. A phone under load, or a headless browser, then swung the figure
        // through six-digit and negative values before the cap put it right.
        while (dt > 0) {
          const step = Math.min(dt, 0.008)
          velocity += (-stiffness * (position - state.target) - damping * velocity) * step
          position += velocity * step
          dt -= step
        }
        const epsilon = Math.max(1e-4, Math.abs(state.target) * 1e-5)
        if (Math.abs(state.target - position) < epsilon && Math.abs(velocity) < epsilon) {
          finish(state)
          return
        }
        write(state, position)
        frame = requestAnimationFrame(tick)
      }

      frame = requestAnimationFrame(tick)
      // A spring on a surface that never paints — a hidden tab, a backgrounded WebView — never
      // settles, because requestAnimationFrame is paused and the loop is not running at all. The
      // cap is what makes the end state certain rather than probable.
      capTimer = setTimeout(() => finish(state), 4000)
    }

    const begin = () => {
      if (stopped) return
      const state = read()
      if (!state) return
      const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)')
      if (reduce?.matches || state.down || state.target === state.from) { finish(state); return }
      run(state)
    }

    // An engine with no IntersectionObserver still gets the number, and still gets a teardown so
    // the element is owned rather than recompiled on every mutation. A missing observer is a
    // platform gap, not a bail to retry.
    if (typeof IntersectionObserver !== 'function') {
      const state = read()
      if (state) write(state, state.target)
      return () => {}
    }

    const seen = new IntersectionObserver((entries) => {
      if (!entries.some((entry) => entry.isIntersecting)) return
      seen.disconnect()                 // upstream's `once: true`, as an explicit stop
      const state = read()
      if (!state) return
      if (state.delayMs > 0) delayTimer = setTimeout(begin, state.delayMs)
      else begin()
    })
    seen.observe(root)

    return () => {
      stopped = true
      seen.disconnect()
      clearTimeout(delayTimer)
      clearTimeout(capTimer)
      cancelAnimationFrame(frame)
      // Never carry a half-counted number out of a fragment swap: "7,4…" arriving on the next
      // page reads as though that were the value.
      const state = read()
      if (state) write(state, state.target)
    }
  })

  // ---- FloatingDock ------------------------------------------------------------------------
  // Aceternity's dock, with framer-motion's own engine doing what framer-motion did there. Each
  // tile's size follows the pointer's horizontal distance from its centre: 150px away or more it
  // rests at 40px, right over it 80px, and the icon inside goes 20px → 40px. Upstream's spring
  // (mass 0.1, stiffness 150, damping 12) carries every change, which is what makes a sweep
  // across the row look like a wave rather than a step.
  //
  // The distance is taken from clientX, where upstream reads pageX against a client rect: the
  // two agree only while the page has not scrolled sideways.
  const reducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches

  shadcnCompiler('[data-slot="floating-dock-desktop"]', (dock) => {
    if (reducedMotion()) return () => {}
    let disposed = false
    let detach = () => {}

    ensureMotion().then((M) => {
      if (disposed || !M) return
      const tile = M.transform([-150, 0, 150], [40, 80, 40])
      const glyph = M.transform([-150, 0, 150], [20, 40, 20])
      const spring = { mass: 0.1, stiffness: 150, damping: 12 }

      const parts = [...dock.querySelectorAll('[data-slot="floating-dock-tile"]')].map((box) => {
        const icon = box.querySelector('[data-slot="floating-dock-icon"]')
        const size = M.springValue(40, spring)
        const inner = M.springValue(20, spring)
        const stops = [
          size.on('change', (v) => { box.style.width = box.style.height = `${v}px` }),
          inner.on('change', (v) => { if (icon) icon.style.width = icon.style.height = `${v}px` }),
        ]
        return { box, icon, size, inner, stops }
      })

      const follow = (x) => {
        for (const part of parts) {
          const r = part.box.getBoundingClientRect()
          const distance = x - r.x - r.width / 2
          part.size.set(tile(distance))
          part.inner.set(glyph(distance))
        }
      }
      const onMove = (e) => follow(e.clientX)
      const onLeave = () => follow(Infinity)
      dock.addEventListener('mousemove', onMove)
      dock.addEventListener('mouseleave', onLeave)

      detach = () => {
        dock.removeEventListener('mousemove', onMove)
        dock.removeEventListener('mouseleave', onLeave)
        for (const part of parts) {
          for (const stop of part.stops) stop()
          part.size.destroy()
          part.inner.destroy()
          part.box.style.width = part.box.style.height = ''
          if (part.icon) part.icon.style.width = part.icon.style.height = ''
        }
      }
    }).catch(() => {})

    return () => { disposed = true; detach() }
  })

  // The phone menu is a <details>, so it opens and closes on its own. This adds upstream's
  // stagger: the links rise 10px into place, the one nearest the button first, and leave the
  // same way before the menu closes. Hidden before the first frame, not after, or the open
  // menu flashes at full opacity while Motion is still loading — and shown again if Motion
  // never arrives, so a failed request cannot leave the menu open and empty.
  shadcnCompiler('[data-slot="floating-dock-mobile"]', (details) => {
    const summary = details.querySelector(':scope > summary')
    const links = () => [...details.querySelectorAll('[data-slot="floating-dock-item"]')]
    let leaving = false

    const onToggle = () => {
      if (!details.open || reducedMotion()) return
      const list = links()
      for (const a of list) a.style.opacity = '0'
      ensureMotion().then((M) => {
        list.forEach((a, i) => M.animate(a, { opacity: [0, 1], y: [10, 0] }, { delay: (list.length - 1 - i) * 0.05 }))
      }).catch(() => { for (const a of list) a.style.opacity = '' })
    }

    const onClick = (e) => {
      if (!details.open || leaving || reducedMotion() || !window.Motion) return
      e.preventDefault()
      leaving = true
      const list = links()
      Promise.all(list.map((a, i) => window.Motion.animate(a, { opacity: 0, y: 10 }, { delay: i * 0.05, duration: 0.2 }).finished))
        .finally(() => {
          details.open = false
          for (const a of list) { a.style.opacity = ''; a.style.transform = '' }
          leaving = false
        })
    }

    details.addEventListener('toggle', onToggle)
    summary?.addEventListener('click', onClick)
    return () => {
      details.removeEventListener('toggle', onToggle)
      summary?.removeEventListener('click', onClick)
      for (const a of links()) { a.style.opacity = ''; a.style.transform = '' }
    }
  })

  // ---- FloatingNav -------------------------------------------------------------------------
  // Ruixen's sliding pill. The server draws it inside the active item, so it is right before any
  // script runs; this lifts that one element onto the bar and from then on positions it by the
  // item's box, moving on upstream's spring (stiffness 400, damping 30) when the active item
  // changes. Placed without animation on arrival and on resize, as upstream's effect does.
  shadcnCompiler('[data-slot="floating-nav"]', (bar) => {
    const items = () => [...bar.querySelectorAll(':scope > [data-slot="floating-nav-item"]')]
    const pill = bar.querySelector('[data-slot="floating-nav-indicator"]')
    if (!pill) return () => {}
    const home = pill.parentElement
    bar.prepend(pill)

    const boxOf = (item) => {
      const b = bar.getBoundingClientRect()
      const r = item.getBoundingClientRect()
      return { left: r.left - b.left - bar.clientLeft, width: r.width }
    }
    const active = () => items().find((i) => i.dataset.active === 'true') || home
    const snap = () => {
      const { left, width } = boxOf(active())
      Object.assign(pill.style, { left: `${left}px`, width: `${width}px`, right: 'auto' })
    }
    snap()

    let motion = null
    const onClick = (e) => {
      const item = e.target.closest('[data-slot="floating-nav-item"]')
      if (!item || item.parentElement !== bar || item.dataset.active === 'true') return
      for (const other of items()) {
        delete other.dataset.active
        if (other.tagName === 'BUTTON') other.setAttribute('aria-pressed', 'false')
      }
      item.dataset.active = 'true'
      if (item.tagName === 'BUTTON') item.setAttribute('aria-pressed', 'true')
      const { left, width } = boxOf(item)
      if (reducedMotion()) { snap(); return }
      ensureMotion().then((M) => {
        motion?.stop()
        motion = M.animate(pill, { left: `${left}px`, width: `${width}px` },
          { type: 'spring', stiffness: 400, damping: 30 })
      }).catch(snap)
    }

    const resize = typeof ResizeObserver === 'function' ? new ResizeObserver(snap) : null
    resize?.observe(bar)
    bar.addEventListener('click', onClick)
    return () => {
      bar.removeEventListener('click', onClick)
      resize?.disconnect()
      motion?.stop()
      // Back where the server put it, so a recompile after a swap starts from the same markup.
      pill.removeAttribute('style')
      home.prepend(pill)
    }
  })

  // ---- BottomMenu --------------------------------------------------------------------------
  // useLayouts' bottom menu. The panels are auto popovers, so opening, one-at-a-time, outside
  // click and Escape are the platform's. This places the open panel above the bar, keeps each
  // button's aria-expanded true to its panel, and adds upstream's motion: a panel opening from
  // nothing grows out of the bar (0.3s, ease [0.45, 0, 0.25, 1]); a panel replacing another
  // starts at the old one's size, so the box morphs; and the content blurs in (0.25s).
  //
  // The panel is anchored by its bottom edge and its centre once placed, so a size that animates
  // grows upward and to both sides — which is how upstream's absolutely positioned box grows.
  shadcnCompiler('[data-slot="bottom-menu"]', (root) => {
    const bar = root.querySelector(':scope > [data-slot="bottom-menu-bar"]')
    if (!bar) return () => {}
    const panels = [...root.querySelectorAll(':scope > [data-slot="bottom-menu-panel"]')]
    const triggerFor = (panel) => bar.querySelector(`[popovertarget="${CSS.escape(panel.id)}"]`)
    let left = null
    const ease = [0.45, 0, 0.25, 1]

    const anchor = (panel) => {
      place(panel, bar, 'center', 'top', 12)
      const r = panel.getBoundingClientRect()
      Object.assign(panel.style, {
        top: 'auto', bottom: `${Math.round(window.innerHeight - r.bottom)}px`,
        left: `${Math.round(r.left + r.width / 2)}px`, translate: '-50% 0',
      })
    }

    const onBeforeToggle = (e) => {
      if (e.newState !== 'closed') return
      const r = e.target.getBoundingClientRect()
      left = { width: r.width, height: r.height, at: performance.now() }
    }

    const onToggle = (e) => {
      const panel = e.target
      const open = e.newState === 'open'
      triggerFor(panel)?.setAttribute('aria-expanded', String(open))
      if (!open) { panel.style.width = panel.style.height = ''; return }
      anchor(panel)
      const from = left && performance.now() - left.at < 150 ? left : null
      left = null
      if (reducedMotion()) return
      const view = panel.querySelector(':scope > [data-slot="bottom-menu-view"]')
      const r = panel.getBoundingClientRect()
      ensureMotion().then((M) => {
        const size = from
          ? { width: [`${from.width}px`, `${r.width}px`], height: [`${from.height}px`, `${r.height}px`] }
          : { width: ['0px', `${r.width}px`], height: ['0px', `${r.height}px`],
              opacity: [0, 1], scaleX: [0.95, 1], scaleY: [0.9, 1] }
        panel.style.transformOrigin = 'bottom center'
        M.animate(panel, size, { duration: 0.3, ease }).finished
          .then(() => { panel.style.width = panel.style.height = '' })
        if (view) M.animate(view, { opacity: [0, 1], scale: [0.96, 1], filter: ['blur(10px)', 'blur(0px)'] },
          { duration: 0.25, ease: [0.42, 0, 0.58, 1] })
      }).catch(() => {})
    }

    for (const panel of panels) {
      panel.addEventListener('beforetoggle', onBeforeToggle)
      panel.addEventListener('toggle', onToggle)
    }
    return () => {
      for (const panel of panels) {
        panel.removeEventListener('beforetoggle', onBeforeToggle)
        panel.removeEventListener('toggle', onToggle)
      }
    }
  })

  // ---- FrequentlyAskedQuestions -------------------------------------------------------------
  // ScrollX's entrance, once per piece the first time it is scrolled into view, with upstream's
  // timings: the headline word by word out of a 6px blur and 12px below (0.4s, 0.08s apart), the
  // description fading in at 0.4s, the list rising 20px at 0.5s, and each question rising 10px
  // at 0.5s + 0.07s per row. The markup already holds the finished section; the pieces are
  // hidden here, just before they play, and shown again if Motion cannot be loaded.
  shadcnCompiler('[data-slot="frequently-asked-questions"]', (section) => {
    if (reducedMotion()) return () => {}
    const words = [...section.querySelectorAll('[data-slot="faq-word"]')]
    const description = section.querySelector('[data-slot="faq-description"]')
    const list = section.querySelector('[data-slot="faq-list"]')
    const rows = [...section.querySelectorAll('[data-slot="faq-item"]')]
    const all = [...words, description, list, ...rows].filter(Boolean)
    for (const el of all) el.style.opacity = '0'
    const stops = []
    let disposed = false

    ensureMotion().then((M) => {
      if (disposed) return
      const once = (el, play) => {
        const stop = M.inView(el, () => { play(); stop() }, { amount: 0.1 })
        stops.push(stop)
      }
      words.forEach((word, i) => once(word, () => M.animate(word,
        { opacity: [0, 1], filter: ['blur(6px)', 'blur(0px)'], y: [12, 0] },
        { duration: 0.4, delay: i * 0.08, ease: 'easeInOut' })))
      if (description) once(description, () => M.animate(description, { opacity: [0, 1] }, { duration: 0.5, delay: 0.4 }))
      if (list) once(list, () => M.animate(list, { opacity: [0, 1], y: [20, 0] }, { duration: 0.5, delay: 0.5 }))
      rows.forEach((row, i) => once(row, () => M.animate(row,
        { opacity: [0, 1], y: [10, 0] }, { duration: 0.35, delay: 0.5 + i * 0.07, ease: 'easeOut' })))
    }).catch(() => { for (const el of all) el.style.opacity = '' })

    return () => {
      disposed = true
      for (const stop of stops) stop()
      for (const el of all) { el.style.opacity = ''; el.style.filter = ''; el.style.transform = '' }
    }
  })

  // ---- BottomNavBar ------------------------------------------------------------------------
  // The active tab's name slides out beside its icon and the previous one folds away. The CSS
  // already says which name shows — this moves data-active on a click and carries the change on
  // a spring (stiffness 350, damping 32) for the width, with the name fading over 0.19s. Inline
  // sizes are cleared once a fold settles, so the stylesheet is back in charge between clicks.
  shadcnCompiler('[data-slot="bottom-nav-bar"]', (bar) => {
    const items = () => [...bar.querySelectorAll(':scope > [data-slot="bottom-nav-bar-item"]')]
    const labelOf = (item) => item?.querySelector(':scope > [data-slot="bottom-nav-bar-label"]')
    const running = new Map()
    const settle = (label) => { label.style.maxWidth = label.style.opacity = '' }
    const play = (label, animation) => {
      running.get(label)?.stop()
      running.set(label, animation)
      animation.finished.then(() => {
        if (running.get(label) !== animation) return
        running.delete(label)
        settle(label)
      })
    }

    const onClick = (e) => {
      const item = e.target.closest('[data-slot="bottom-nav-bar-item"]')
      if (!item || item.parentElement !== bar || item.dataset.active === 'true') return
      const leaving = labelOf(items().find((i) => i.dataset.active === 'true'))
      const arriving = labelOf(item)
      const from = leaving ? leaving.getBoundingClientRect().width : 0
      for (const other of items()) {
        delete other.dataset.active
        if (other.tagName === 'BUTTON') other.setAttribute('aria-pressed', 'false')
      }
      item.dataset.active = 'true'
      if (item.tagName === 'BUTTON') item.setAttribute('aria-pressed', 'true')
      if (!arriving || reducedMotion()) return

      // Pin both names where they are before the first frame, or the new attribute paints the
      // finished state for a frame while Motion is still loading.
      const to = arriving.scrollWidth
      const now = arriving.getBoundingClientRect().width
      if (leaving) Object.assign(leaving.style, { maxWidth: `${from}px`, opacity: '1' })
      Object.assign(arriving.style, { maxWidth: `${now}px`, opacity: now ? '1' : '0' })
      ensureMotion().then((M) => {
        const timing = { maxWidth: { type: 'spring', stiffness: 350, damping: 32 }, opacity: { duration: 0.19 } }
        if (leaving) play(leaving, M.animate(leaving, { maxWidth: [`${from}px`, '0px'], opacity: [1, 0] }, timing))
        play(arriving, M.animate(arriving, { maxWidth: [`${now}px`, `${to}px`], opacity: [now ? 1 : 0, 1] }, timing))
      }).catch(() => { if (leaving) settle(leaving); settle(arriving) })
    }

    bar.addEventListener('click', onClick)
    return () => {
      bar.removeEventListener('click', onClick)
      for (const [label, animation] of running) { animation.stop(); settle(label) }
      running.clear()
    }
  })

  // ---- LogoCloud ---------------------------------------------------------------------------
  // The marquee and the spotlight are CSS. These are the two variants that need a clock.
  //
  // blur: the first time the cloud is scrolled to, the title sharpens in and then each logo,
  // 0.08s apart, out of a 10px blur and 8px below. Hidden just before, shown again if Motion
  // never arrives — the cloud is already complete in the markup.
  shadcnCompiler('[data-slot="logo-cloud"][data-variant="blur"]', (cloud) => {
    if (reducedMotion()) return () => {}
    const title = cloud.querySelector(':scope > [data-slot="logo-cloud-title"]')
    const logos = [...cloud.querySelectorAll('[data-slot="logo-cloud-item"]')]
    const all = [title, ...logos].filter(Boolean)
    for (const el of all) el.style.opacity = '0'
    let stop = () => {}
    let disposed = false

    ensureMotion().then((M) => {
      if (disposed) return
      stop = M.inView(cloud, () => {
        if (title) M.animate(title, { opacity: [0, 1], filter: ['blur(6px)', 'blur(0px)'] }, { duration: 0.5 })
        logos.forEach((logo, i) => M.animate(logo,
          { opacity: [0, 1], filter: ['blur(10px)', 'blur(0px)'], y: [8, 0] },
          { duration: 0.6, delay: 0.15 + i * 0.08, ease: 'easeOut' }))
        stop()
      }, { amount: 0.3 })
    }).catch(() => { for (const el of all) el.style.opacity = '' })

    return () => {
      disposed = true
      stop()
      for (const el of all) { el.style.opacity = ''; el.style.filter = ''; el.style.transform = '' }
    }
  })

  // swap: the server shows the first Visible logos and renders the rest hidden. Every 2.5s,
  // while the cloud is on screen and the tab is visible, one cell — never the same one twice
  // running — blurs its logo out and the next logo in the queue blurs in where it was. The
  // incoming logo takes the cell through CSS `order` rather than by moving elements, so the DOM
  // a framework rendered stays the DOM it rendered.
  shadcnCompiler('[data-slot="logo-cloud"][data-variant="swap"]', (cloud) => {
    const list = cloud.querySelector(':scope > [data-slot="logo-cloud-list"]')
    const logos = list ? [...list.querySelectorAll(':scope > [data-slot="logo-cloud-item"]')] : []
    const hidden = logos.map((logo) => logo.hidden)
    const cells = logos.filter((logo) => !logo.hidden)
    const queue = logos.filter((logo) => logo.hidden)
    if (!cells.length || !queue.length || reducedMotion()) return () => {}
    cells.forEach((logo, i) => { logo.style.order = String(i) })
    let timer = 0
    let last = -1
    let busy = false
    let disposed = false
    let stopView = () => {}

    const swap = (M) => {
      if (busy) return
      busy = true
      let slot
      do slot = Math.floor(Math.random() * cells.length)
      while (cells.length > 1 && slot === last)
      last = slot
      const leaving = cells[slot]
      const arriving = queue.shift()
      M.animate(leaving, { opacity: [1, 0], filter: ['blur(0px)', 'blur(8px)'], y: [0, -8] }, { duration: 0.3, ease: 'easeIn' })
        .finished.then(() => {
          if (disposed) return
          arriving.style.order = leaving.style.order
          leaving.hidden = true
          for (const p of ['opacity', 'filter', 'transform', 'order']) leaving.style[p] = ''
          arriving.hidden = false
          cells[slot] = arriving
          queue.push(leaving)
          return M.animate(arriving, { opacity: [0, 1], filter: ['blur(8px)', 'blur(0px)'], y: [8, 0] },
            { duration: 0.4, ease: 'easeOut' }).finished
        })
        .finally(() => { busy = false })
    }

    ensureMotion().then((M) => {
      if (disposed) return
      stopView = M.inView(cloud, () => {
        timer = setInterval(() => { if (!document.hidden) swap(M) }, 2500)
        return () => clearInterval(timer)
      })
    }).catch(() => {})

    return () => {
      disposed = true
      stopView()
      clearInterval(timer)
      logos.forEach((logo, i) => {
        logo.hidden = hidden[i]
        for (const p of ['opacity', 'filter', 'transform', 'order']) logo.style[p] = ''
      })
    }
  })

  // ---- PricingTable ------------------------------------------------------------------------
  // The form is the state: the radios hold the plan and the billing, and the CSS shows the
  // checked billing's prices. This adds what a radio cannot say on its own — the chosen plan's
  // column lit in the matrix and its name on the button — and, when the billing changes, counts
  // each newly shown price from the figure it replaced (0.6s, easing out), where upstream used
  // NumberFlow. The text is set to the old figure before Motion loads, so the new one is never
  // flashed first; if Motion cannot load, every price goes straight to its own value.
  shadcnCompiler('[data-slot="pricing-table"]', (table) => {
    const prices = () => [...table.querySelectorAll('[data-slot="pricing-table-price"]')]
    const write = (el, v) => {
      const digits = Number(el.dataset.decimals || 0)
      el.textContent = (el.dataset.prefix || '') +
        v.toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits })
    }
    let counts = []
    const settle = () => {
      for (const count of counts) count.stop()
      counts = []
      for (const el of prices()) write(el, Number(el.dataset.value))
    }

    const onChange = (e) => {
      const input = e.target
      if (!(input instanceof HTMLInputElement) || !input.checked) return
      if (input.closest('[data-slot="pricing-table-plans"]')) {
        for (const cell of table.querySelectorAll('[data-slot="pricing-table-matrix"] [data-plan]'))
          cell.dataset.selected = String(cell.dataset.plan === input.value)
        const choice = table.querySelector('[data-slot="pricing-table-choice"]')
        if (choice) choice.textContent = input.value
        return
      }
      if (!input.closest('[data-slot="pricing-table-billing"]')) return
      settle()
      if (reducedMotion()) return
      const pairs = []
      for (const plan of table.querySelectorAll('[data-slot="pricing-table-plan"]')) {
        const shown = plan.querySelector(`[data-slot="pricing-table-price"][data-billing="${input.value}"]`)
        const replaced = plan.querySelector(`[data-slot="pricing-table-price"]:not([data-billing="${input.value}"])`)
        if (!shown || !replaced) continue
        write(shown, Number(replaced.dataset.value))
        pairs.push([shown, Number(replaced.dataset.value), Number(shown.dataset.value)])
      }
      ensureMotion().then((M) => {
        if (disposed) return
        for (const count of counts) count.stop()
        counts = pairs.map(([el, from, to]) =>
          M.animate(from, to, { duration: 0.6, ease: [0.16, 1, 0.3, 1], onUpdate: (v) => write(el, v) }))
      }).catch(settle)
    }

    let disposed = false
    table.addEventListener('change', onChange)
    return () => {
      disposed = true
      table.removeEventListener('change', onChange)
      settle()
    }
  })

  // ---- ui-layouts' TimelineAnimation -------------------------------------------------------
  // Every [data-timeline] piece of a section waits behind a 20px blur and, the first time the
  // section comes into view, sharpens in over 0.5s, starting at 0.5s × its number — so the
  // pieces arrive one after another in the order the numbers give. Nested pieces multiply, as
  // they do upstream: a card inside the grid's piece waits for both. Hidden just before, shown
  // again if Motion cannot load; the markup is the finished section either way.
  const timeline = (root) => {
    if (reducedMotion()) return () => {}
    const pieces = [...root.querySelectorAll('[data-timeline]')]
    for (const piece of pieces) piece.style.opacity = '0'
    let stop = () => {}
    let disposed = false
    ensureMotion().then((M) => {
      if (disposed) return
      stop = M.inView(root, () => {
        for (const piece of pieces) {
          M.animate(piece, { opacity: [0, 1], filter: ['blur(20px)', 'blur(0px)'] },
            { delay: Number(piece.dataset.timeline) * 0.5, duration: 0.5 })
        }
        stop()
      })
    }).catch(() => { for (const piece of pieces) piece.style.opacity = '' })
    return () => {
      disposed = true
      stop()
      for (const piece of pieces) { piece.style.opacity = ''; piece.style.filter = '' }
    }
  }

  // ---- NumberFlow's roll -------------------------------------------------------------------
  // What @number-flow/react does when a price changes, done to the text the server wrote: the
  // new figure set right-aligned against the old one, character by character. A digit that
  // changes is a column of 0–9 that spins to its new place; a character that appears or goes
  // fades while its width opens or closes; the rest simply take their new width. When it
  // settles the columns are replaced by the plain text, so the DOM ends as the server wrote it.
  const measure = document.createElement('canvas').getContext('2d')
  const rollText = (M, el, from, to) => {
    const cs = getComputedStyle(el)
    const line = parseFloat(cs.lineHeight) || parseFloat(cs.fontSize) * 1.2
    if (measure) measure.font = `${cs.fontStyle} ${cs.fontWeight} ${cs.fontSize} ${cs.fontFamily}`
    const width = (ch) => (ch === ' ' || !measure ? 0 : measure.measureText(ch).width)
    const n = Math.max(from.length, to.length)
    const a = from.padStart(n)
    const b = to.padStart(n)
    const spin = { duration: 0.75, ease: [0.16, 1, 0.3, 1] }
    const fade = { duration: 0.45 }
    const runs = []

    el.textContent = ''
    const label = document.createElement('span')
    label.textContent = to
    Object.assign(label.style, { position: 'absolute', width: '1px', height: '1px', overflow: 'hidden', clip: 'rect(0 0 0 0)', whiteSpace: 'nowrap' })
    el.append(label)
    for (let i = 0; i < n; i++) {
      const x = a[i]
      const y = b[i]
      const cell = document.createElement('span')
      cell.setAttribute('aria-hidden', 'true')
      Object.assign(cell.style, { display: 'inline-block', overflow: 'hidden', verticalAlign: 'top', whiteSpace: 'pre', height: `${line}px`, lineHeight: `${line}px`, width: `${width(x)}px` })
      const digit = (ch) => (/\d/.test(ch) ? Number(ch) : null)
      const dx = digit(x)
      const dy = digit(y)
      if (dx !== null || dy !== null) {
        const column = document.createElement('span')
        column.style.display = 'block'
        for (let d = 0; d <= 9; d++) {
          const row = document.createElement('span')
          row.style.display = 'block'
          row.textContent = String(d)
          column.append(row)
        }
        cell.append(column)
        const start = dx ?? dy
        const end = dy ?? dx
        column.style.transform = `translateY(${-start * line}px)`
        if (start !== end) runs.push(M.animate(column, { y: [-start * line, -end * line] }, spin))
      } else {
        cell.textContent = y === ' ' ? x : y
      }
      el.append(cell)
      runs.push(M.animate(cell, { width: [`${width(x)}px`, `${width(y)}px`] }, spin))
      if (x === ' ' || y === ' ') runs.push(M.animate(cell, { opacity: [x === ' ' ? 0 : 1, y === ' ' ? 0 : 1] }, fade))
    }

    let stopped = false
    Promise.all(runs.map((run) => run.finished)).then(() => { if (!stopped) el.textContent = to })
    return { stop: () => { stopped = true; for (const run of runs) run.stop() } }
  }

  // Rolls every [data-billing-roll] pair when the section's billing control changes. Each pair
  // holds the monthly and the yearly figure; the CSS has already shown the right one, and this
  // rolls it from the figure it replaced. Every figure is put back to the server's text first,
  // so a second change mid-roll starts clean.
  const billingRoll = (root) => {
    const figures = [...root.querySelectorAll('[data-billing-roll] > [data-billing]')]
    const text = new Map(figures.map((f) => [f, f.textContent]))
    let rolls = []
    let generation = 0
    let disposed = false
    const settle = () => {
      for (const roll of rolls) roll.stop()
      rolls = []
      for (const f of figures) f.textContent = text.get(f)
    }

    const onChange = (e) => {
      if (!e.target.closest?.('[data-billing-control]')) return
      settle()
      const token = ++generation
      if (reducedMotion()) return
      const billing = root.querySelector('[data-billing-control] [value="yearly"]')?.checked ? 'yearly' : 'monthly'
      const pairs = []
      for (const pair of root.querySelectorAll('[data-billing-roll]')) {
        const shown = pair.querySelector(`:scope > [data-billing="${billing}"]`)
        const gone = pair.querySelector(`:scope > [data-billing]:not([data-billing="${billing}"])`)
        if (shown && gone && text.get(shown) !== text.get(gone)) pairs.push([shown, text.get(gone), text.get(shown)])
      }
      // The old figure holds until Motion arrives, so the new one is never flashed first.
      for (const [el, from] of pairs) el.textContent = from
      ensureMotion().then((M) => {
        if (disposed || token !== generation) return
        rolls = pairs.map(([el, from, to]) => rollText(M, el, from, to))
      }).catch(settle)
    }

    root.addEventListener('change', onChange)
    return () => {
      disposed = true
      root.removeEventListener('change', onChange)
      settle()
    }
  }

  // ---- GrowthBusiness, SubscriptionDetails -------------------------------------------------
  // ui-layouts' two pricing sections: the timeline entrance and the roll, nothing else. The
  // billing itself is the switch or the radios, and the CSS.
  shadcnCompiler('[data-slot="growth-business"], [data-slot="subscription-details"]', (root) => {
    const stopTimeline = timeline(root)
    const stopRoll = billingRoll(root)
    return () => { stopTimeline(); stopRoll() }
  })

  // Anything outside Unpoly's world — a head's own app.js, a page script — reaches these.
  // `toast` is global on purpose: that is sonner's API, and the call sites read the same.
  window.toast = toast
  window.alertDialog = alertDialog
})()
