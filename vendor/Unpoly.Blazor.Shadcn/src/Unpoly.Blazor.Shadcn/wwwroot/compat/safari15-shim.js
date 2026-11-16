/**
 * Safari 15.0 JS compatibility shim for Unpoly.Blazor.Shadcn
 * Vendored as a library _content asset; loaded before unpoly/ui.js in both demo heads.
 *
 * Inventory (verified with word-boundary grep over unpoly.min.js 3.14.3,
 * ui.js, demo.js, air-datepicker, toastify, echarts and maplibre bundles):
 *   Array.prototype.toReversed  -> 9 uses in unpoly.min.js (layer/form paths)
 *   Array.prototype.at          -> 17 uses in unpoly.min.js
 *   String.prototype.replaceAll -> 2 uses in unpoly.min.js
 *   toSorted / toSpliced / findLast / findLastIndex / structuredClone / Object.hasOwn
 *                               -> 0 uses; NOT shimmed.
 *
 * Plus a :popover-open selector translation (below): the oddbird polyfill
 * deliberately does NOT patch matches()/querySelector* — without it, ui.js's
 * sixteen panel.matches(':popover-open') checks and its
 * querySelectorAll('...:popover-open') menu-closing sweep throw SyntaxError
 * on Safari 15. Bare :popover-open becomes the polyfill's .\:popover-open
 * class check; selectors already carrying the escaped class pass through
 * untouched, so the forgiving :is(:popover-open, .\:popover-open) form keeps
 * working. Only installed when the native Popover API is absent.
 *
 * Each polyfill is feature-detected so modern browsers execute zero shim code.
 * IIFE-wrapped per repo rule: classic <script src> shares one global scope.
 */
(function () {
  'use strict';

  // Array.prototype.toReversed — ES2023
  if (!Array.prototype.toReversed) {
    Array.prototype.toReversed = function toReversed() {
      var len = this.length;
      var result = new Array(len);
      for (var i = 0; i < len; i++) {
        result[i] = this[len - 1 - i];
      }
      return result;
    };
  }

  // Array.prototype.at — ES2022 (missing in Safari 15.0)
  if (!Array.prototype.at) {
    Array.prototype.at = function at(n) {
      var len = this.length;
      var k = n >= 0 ? n : len + n;
      return (k >= 0 && k < len) ? this[k] : undefined;
    };
  }

  // String.prototype.replaceAll — ES2021 (missing in Safari 13.1, marginal in 15)
  if (!String.prototype.replaceAll) {
    String.prototype.replaceAll = function replaceAll(searchValue, replaceValue) {
      if (searchValue === undefined || searchValue === null) {
        throw new TypeError('Cannot call replaceAll on undefined search value');
      }
      var search = String(searchValue);
      var replacement = String(replaceValue);
      // If search is an empty string, insert replacement between every code unit
      // and at the start/end — matching spec behaviour.
      if (search === '') {
        var result = replacement;
        for (var i = 0; i < this.length; i++) {
          result += this[i] + replacement;
        }
        return result;
      }
      var escaped = search.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      var re = new RegExp(escaped, 'g');
      return this.replace(re, replacement);
    };
  }

  // window.reportError — Safari < 16. Unpoly 3.14 refuses to boot without it
  // ("Unpoly cannot boot: Browser doesn't support the reportError API"), so
  // every interaction dies, not just error reporting. Re-throwing on a timer
  // surfaces the error as uncaught, which is the documented semantics.
  if (typeof window !== 'undefined' && typeof window.reportError !== 'function') {
    window.reportError = function reportError(error) {
      window.setTimeout(function () { throw error; }, 0);
    };
  }

  // :popover-open selector translation — Safari < 17 (and the oddbird polyfill,
  // which patches show/hide/toggle but NOT the selector engines). A selector
  // engine that does not know :popover-open throws SyntaxError on it, which
  // would kill ui.js's open-state checks and its menu-closing sweep. Translate
  // the bare pseudo-class to the polyfill's .\:popover-open class; selectors
  // that already carry the escaped class (including the forgiving
// :is(:popover-open, .\:popover-open) form) are left untouched because the
// pattern requires a non-backslash before the colon.
  if (!('popover' in HTMLElement.prototype) || !('showPopover' in HTMLElement.prototype)) {
    var openPattern = /(^|[^\\]):popover-open\b/g;
    var translateOpen = function (selector) {
      if (typeof selector !== 'string' || selector.indexOf(':popover-open') === -1) {
        return selector;
      }
      return selector.replace(openPattern, '$1.\\:popover-open');
    };
    var patchSelectorMethod = function (proto, name, isElement) {
      var original = proto[name];
      if (typeof original !== 'function') return;
      proto[name] = function (selector) {
        return original.call(this, translateOpen(selector));
      };
    };
    patchSelectorMethod(Element.prototype, 'matches');
    patchSelectorMethod(Element.prototype, 'closest');
    if (typeof Document !== 'undefined') {
      patchSelectorMethod(Document.prototype, 'querySelector');
      patchSelectorMethod(Document.prototype, 'querySelectorAll');
      if (typeof DocumentFragment !== 'undefined') {
        patchSelectorMethod(DocumentFragment.prototype, 'querySelector');
        patchSelectorMethod(DocumentFragment.prototype, 'querySelectorAll');
      }
    }
  }
})();
