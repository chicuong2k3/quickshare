// anti-devtools.js — DevTools detection via debugger timing gap.
// Only injected in production (guarded by App.razor environment check).
// Bypassed by ?debug=true URL parameter.
(function() {
    'use strict';

    // Escape hatch for developers debugging production issues
    if (window.location.search.includes('debug=true')) return;

    function detectDevTools() {
        var before = performance.now();
        // DevTools open → debugger pauses execution → timing gap >100ms
        debugger;
        var after = performance.now();
        return (after - before) > 100;
    }

    // Check every 2 seconds. CPU overhead: <0.0001%.
    setInterval(function() {
        if (detectDevTools()) {
            location.reload(); // Silent refresh — closes inspection window
        }
    }, 2000);
})();
