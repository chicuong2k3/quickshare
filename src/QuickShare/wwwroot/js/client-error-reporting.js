// Captures uncaught browser errors and reports them to the server log (Serilog → Seq).
// Must be loaded before other scripts to catch their startup errors.
(function () {
    'use strict';

    function report(data) {
        try {
            var body = JSON.stringify(data);
            if (navigator.sendBeacon) {
                navigator.sendBeacon('/api/client-errors', new Blob([body], { type: 'application/json' }));
            } else {
                fetch('/api/client-errors', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: body,
                    keepalive: true
                }).catch(function () { });
            }
        } catch (err) {
            // Never recurse into the reporter.
        }
    }

    window.addEventListener('error', function (e) {
        report({
            message: e.message || 'Unknown error',
            stack: e.error && e.error.stack ? e.error.stack : '',
            source: e.filename || '',
            line: e.lineno || 0,
            column: e.colno || 0,
            href: window.location.href
        });
    });

    window.addEventListener('unhandledrejection', function (e) {
        var r = e.reason;
        report({
            message: 'Unhandled promise rejection: ' + (r && r.message ? r.message : String(r)),
            stack: r && r.stack ? r.stack : '',
            source: '',
            line: 0,
            column: 0,
            href: window.location.href
        });
    });
})();
