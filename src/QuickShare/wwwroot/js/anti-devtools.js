// anti-devtools.js — DevTools blocking via the disable-devtool library (v0.3.9).
// Library: https://github.com/theajack/disable-devtool (UMD bundle at /lib/disable-devtool/disable-devtool.min.js)
// Bypassed by ?debug=true URL parameter (dev escape hatch).
(function() {
    'use strict';

    // Escape hatch for developers debugging production issues
    if (window.location.search.includes('debug=true')) return;

    if (typeof DisableDevtool !== 'function') return;

    DisableDevtool({
        interval: 2000, // same check cadence as the old self-written debugger-timing probe
        disableMenu: true, // block right-click context menu
        clearLog: true, // wipe console output on every check
        ondevtoolopen: function() {
            // Parity with previous behavior: silent refresh closes the inspection window
            location.reload();
        }
    });
})();
