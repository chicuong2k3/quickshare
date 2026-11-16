/**
 * <dialog> shim for Safari 15.0-15.3 (HTMLDialogElement shipped in 15.4) for Unpoly.Blazor.Shadcn.
 * Vendored as a library _content asset; loaded before ui.js in both demo heads.
 *
 * Without it <dialog> is an unknown element there: show()/showModal()/close() and .open do not
 * exist, so every Dialog, AlertDialog, Sheet, Drawer and CommandDialog is inert (ui.js called
 * showModal() on an element that has none). With it, those four methods, the `open` and
 * `returnValue` properties and the `cancel`/`close`/`toggle` events behave as the platform's do,
 * on the same element — nothing is moved, wrapped or replaced, which matters because Blazor owns
 * the surrounding DOM in the MAUI head.
 *
 * What a script cannot reproduce, and this does not pretend to:
 *   - the top layer. A modal dialog is position:fixed with a very high z-index, so an ancestor
 *     that makes a stacking context (z-index, transform, isolation) caps it.
 *   - an inert background. Tab is trapped inside the dialog, the backdrop (below) swallows pointer
 *     input, and the rest of the page is aria-hidden while a modal is open (restored on close).
 *     Anything else that moves focus out of the dialog by script is not stopped.
 *
 * The frame, the Escape key, Tab trapping, focus on open and focus restored on close live here;
 * the position, the size and the backdrop live in ui.safari15.css, which must load with it. The
 * backdrop is the dialog's own ::before rather than a sibling element, so a click on it targets
 * the dialog exactly as a click on ::backdrop does — ui.js relies on that to dismiss.
 *
 * Feature-detected: where showModal exists this file runs nothing past the first check.
 * IIFE-wrapped per repo rule: classic <script src> shares one global scope.
 */
(function () {
  'use strict';

  if (typeof document === 'undefined') return;
  var probe = document.createElement('dialog');
  if (typeof probe.showModal === 'function') return;

  // Where the methods go: whatever prototype an unrecognised <dialog> inherits from (normally
  // HTMLUnknownElement.prototype). Every method checks localName, so another element sharing
  // that prototype sees nothing but an undefined `open`.
  var proto = Object.getPrototypeOf(probe);
  var states = new WeakMap();   // dialog -> { modal, returnValue, previous }
  var modals = [];              // open modal dialogs, topmost last

  var FOCUSABLE = 'a[href], area[href], button, input:not([type="hidden"]), select, textarea, ' +
    'summary, [tabindex], [contenteditable=""], [contenteditable="true"]';

  function isDialog(element) { return !!element && element.localName === 'dialog'; }

  function stateOf(dialog) {
    var state = states.get(dialog);
    if (!state) {
      state = { modal: false, returnValue: '', previous: null };
      states.set(dialog, state);
    }
    return state;
  }

  function invalidState(message) { return new DOMException(message, 'InvalidStateError'); }

  function assertDialog(element) {
    if (!isDialog(element)) throw new TypeError('Illegal invocation');
  }

  function focusable(root) {
    return Array.prototype.filter.call(root.querySelectorAll(FOCUSABLE), function (element) {
      return !element.disabled && element.getAttribute('tabindex') !== '-1' &&
        !element.closest('[inert]') &&
        (element.offsetWidth || element.offsetHeight || element.getClientRects().length);
    });
  }

  function focusInto(dialog) {
    var target = dialog.querySelector('[autofocus], [data-autofocus]') || focusable(dialog)[0];
    if (!target) {
      // Nothing to land on: the dialog itself, as the platform does.
      if (!dialog.hasAttribute('tabindex')) dialog.setAttribute('tabindex', '-1');
      target = dialog;
    }
    try { target.focus({ preventScroll: true }); } catch (e) { /* not focusable after all */ }
  }

  // The platform queues these as tasks, so listeners run after the call that caused them.
  function queueEvent(dialog, type, transition) {
    setTimeout(function () {
      var event = new Event(type);
      if (transition) { event.oldState = transition[0]; event.newState = transition[1]; }
      dialog.dispatchEvent(event);
    }, 0);
  }

  // No `inert` below 15.4 either, so while a modal is open everything outside it is taken out of
  // the accessibility tree instead: every sibling along the path from the TOPMOST modal up to
  // <body> gets aria-hidden. The path itself stays exposed — hiding an ancestor would hide the
  // dialog — and an element the page had already marked aria-hidden is left alone.
  //
  // It is recomputed from nothing on every open and close, never patched: a modal opened over
  // another (an alert dialog over a dialog) must be exposed even though the first modal hid it
  // as "the rest of the page", and closing it must hide the first one's surroundings again.
  // It is a screen-reader fence, not an input one: Tab is trapped below.
  var fenced = [];

  function fence() {
    fenced.forEach(function (element) { element.removeAttribute('aria-hidden'); });
    fenced = [];
    var top = modals[modals.length - 1];
    if (!top) return;
    for (var node = top; node !== document.body && node.parentElement; node = node.parentElement) {
      Array.prototype.forEach.call(node.parentElement.children, function (sibling) {
        if (sibling === node || sibling.hasAttribute('aria-hidden') ||
            /^(script|style|link|template|noscript)$/.test(sibling.localName)) return;
        sibling.setAttribute('aria-hidden', 'true');
        fenced.push(sibling);
      });
    }
  }

  function openDialog(dialog, modal) {
    var state = stateOf(dialog);
    state.previous = document.activeElement;
    dialog.setAttribute('data-dialog-polyfill', modal ? 'modal' : 'modeless');
    dialog.setAttribute('open', '');
    if (modal) {
      state.modal = true;
      dialog.setAttribute('aria-modal', 'true');
      modals.push(dialog);
    }
    focusInto(dialog);
    // After focus has moved in: hiding the subtree that still holds focus is what browsers warn about.
    if (modal) fence();
    queueEvent(dialog, 'toggle', ['closed', 'open']);
  }

  function define(name, descriptor) {
    descriptor.configurable = true;
    descriptor.enumerable = true;
    Object.defineProperty(proto, name, descriptor);
  }

  define('open', {
    get: function () { return isDialog(this) ? this.hasAttribute('open') : undefined; },
    set: function (value) {
      if (!isDialog(this)) return;
      if (value) this.setAttribute('open', '');
      else this.removeAttribute('open');
    }
  });

  define('returnValue', {
    get: function () { return isDialog(this) ? stateOf(this).returnValue : undefined; },
    set: function (value) { if (isDialog(this)) stateOf(this).returnValue = String(value); }
  });

  define('show', {
    writable: true,
    value: function show() {
      assertDialog(this);
      if (this.hasAttribute('open')) {
        if (stateOf(this).modal) {
          throw invalidState("The element already has an 'open' attribute, and therefore cannot be opened non-modally.");
        }
        return;
      }
      openDialog(this, false);
    }
  });

  define('showModal', {
    writable: true,
    value: function showModal() {
      assertDialog(this);
      if (this.hasAttribute('open')) {
        if (stateOf(this).modal) return;
        throw invalidState("The element already has an 'open' attribute, and therefore cannot be opened modally.");
      }
      if (!this.isConnected) throw invalidState('The element is not in a document.');
      openDialog(this, true);
    }
  });

  define('close', {
    writable: true,
    value: function close(returnValue) {
      assertDialog(this);
      if (!this.hasAttribute('open')) return;
      var state = stateOf(this);
      if (returnValue !== undefined) state.returnValue = String(returnValue);
      this.removeAttribute('open');
      this.removeAttribute('data-dialog-polyfill');
      this.removeAttribute('aria-modal');
      var wasModal = state.modal;
      state.modal = false;
      var at = modals.indexOf(this);
      if (at >= 0) modals.splice(at, 1);

      // Before focus goes back: it is returning to an element this was hiding.
      if (wasModal) fence();

      var previous = state.previous;
      state.previous = null;
      if (previous && previous !== document.body && previous.isConnected &&
          typeof previous.focus === 'function') {
        try { previous.focus({ preventScroll: true }); } catch (e) { /* gone or unfocusable */ }
      }
      queueEvent(this, 'toggle', ['open', 'closed']);
      queueEvent(this, 'close');
    }
  });

  // ---- keyboard: Escape and the Tab trap, for the topmost modal --------------------------
  // Capture phase, so an open popover is still open when this looks: Escape belongs to the
  // popover first (a select's list closes, the dialog stays), exactly as it does when the
  // popover is in the real top layer above the dialog.
  document.addEventListener('keydown', function (event) {
    var top = modals[modals.length - 1];
    if (!top) return;

    if (event.key === 'Escape' || event.key === 'Esc') {
      if (event.defaultPrevented || document.querySelector('.\\:popover-open')) return;
      if (top.dispatchEvent(new Event('cancel', { cancelable: true }))) top.close();
      return;
    }

    if (event.key !== 'Tab') return;
    var items = focusable(top);
    if (items.length === 0) { event.preventDefault(); top.focus(); return; }
    var first = items[0];
    var last = items[items.length - 1];
    var active = document.activeElement;
    if (!top.contains(active)) {
      event.preventDefault();
      (event.shiftKey ? last : first).focus();
    } else if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }, true);

  // ---- <form method="dialog"> -----------------------------------------------------------
  // Closes the form's dialog instead of submitting. Safari before 15.4 has no SubmitEvent, so
  // the button that was pressed is remembered from its click (Safari does not focus a button
  // on click, so document.activeElement cannot answer it).
  var pressed = null;
  document.addEventListener('click', function (event) {
    pressed = event.target.closest ? event.target.closest('button, input[type="submit"], input[type="image"]') : null;
    setTimeout(function () { pressed = null; }, 0);
  }, true);

  document.addEventListener('submit', function (event) {
    var form = event.target;
    var submitter = event.submitter || (pressed && pressed.form === form ? pressed : null);
    var method = (submitter && submitter.getAttribute('formmethod')) || form.getAttribute('method');
    if (!method || method.toLowerCase() !== 'dialog') return;
    var dialog = form.closest('dialog');
    if (!dialog || !dialog.hasAttribute('open')) return;
    event.preventDefault();
    dialog.close(submitter ? submitter.value : undefined);
  });
})();
