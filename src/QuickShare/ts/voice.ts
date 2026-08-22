// Voice commands (#277). One global object, loaded from the layout.
//
// It drives the command palette (#153) rather than owning a registry of its own: the palette
// already holds every action, already localises its labels, and a second list would drift from
// the first the day somebody adds a page. Speech here is an alternative *input* to the same
// menu, never a second menu.
//
// Nothing runs without being shown first. Recognition mishears, and a mishearing that navigates
// silently is worse than one that says "going to Billing — cancel?" and gives you a moment.

interface VoiceStrings {
    listening: string;
    heard: string;
    noMatch: string;
    running: string;
    cancelled: string;
    unsupported: string;
    denied: string;
}

window.quickShareVoice = (function () {
    const BAR = 'voice-status';
    const BUTTON = 'voice-trigger';
    const LIST = '#command-palette-list li';
    /** Long enough to read the line and say no; short enough to still feel hands-free. */
    const CONFIRM_MS = 2500;
    const CANCEL_WORDS = ['cancel', 'stop', 'huỷ', 'hủy', 'dừng'];

    let pending: number | null = null;

    function strings(): VoiceStrings {
        const el = document.getElementById('voice-i18n');
        return el ? JSON.parse(el.textContent || '{}') : ({} as VoiceStrings);
    }

    function say(message: string): void {
        const bar = document.getElementById(BAR);
        if (!bar) return;
        bar.textContent = message;
        bar.hidden = false;
    }

    function recognizer(): any {
        const w = window as any;
        const Ctor = w.SpeechRecognition || w.webkitSpeechRecognition;
        return Ctor ? new Ctor() : null;
    }

    /**
     * Best palette entry for what was heard.
     *
     * Deliberately the same text the palette filters on — label plus keywords — so a command
     * that works when typed works when spoken. Longest match wins, because "send" is a prefix of
     * plenty and the more specific phrase is the one the speaker meant.
     */
    function match(transcript: string): HTMLAnchorElement | null {
        const spoken = transcript.toLowerCase().trim();
        if (!spoken) return null;

        let best: HTMLAnchorElement | null = null;
        let bestLength = 0;

        document.querySelectorAll(LIST).forEach(function (li) {
            const link = li.querySelector('a') as HTMLAnchorElement | null;
            if (!link) return;

            const label = (link.textContent || '').toLowerCase().trim();
            const keywords = (link.getAttribute('data-keywords') || '').toLowerCase();

            const hit = (label && spoken.indexOf(label) !== -1)
                || keywords.split(' ').some(function (word) { return word.length > 2 && spoken.indexOf(word) !== -1; });

            if (hit && label.length >= bestLength) {
                best = link;
                bestLength = label.length;
            }
        });

        return best;
    }

    function cancel(): void {
        if (pending === null) return;
        window.clearTimeout(pending);
        pending = null;
        say(strings().cancelled);
    }

    function handle(transcript: string): void {
        const text = strings();
        say(text.heard.replace('{0}', transcript));

        if (CANCEL_WORDS.some(function (word) { return transcript.toLowerCase().indexOf(word) !== -1; })) {
            cancel();
            return;
        }

        const link = match(transcript);
        if (!link) {
            say(text.noMatch.replace('{0}', transcript));
            return;
        }

        const label = (link.textContent || '').trim();
        say(text.running.replace('{0}', label));

        // Shown, then run. Escape or another "cancel" stops it — the undo for an action that has
        // not happened yet is simply not doing it.
        pending = window.setTimeout(function () {
            pending = null;
            window.location.href = link.getAttribute('href') || '/';
        }, CONFIRM_MS);
    }

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') cancel();
    });

    return {
        /** Wired to the mic button; hides itself when the browser cannot listen. */
        start: function (): void {
            const text = strings();
            const speech = recognizer();

            if (!speech) {
                say(text.unsupported);
                return;
            }

            speech.lang = document.documentElement.lang || 'en';
            speech.interimResults = false;
            speech.maxAlternatives = 1;

            speech.onresult = function (event: any) {
                const result = event.results && event.results[0] && event.results[0][0];
                handle(result ? String(result.transcript) : '');
            };
            speech.onerror = function (event: any) {
                say(event && event.error === 'not-allowed' ? text.denied : text.noMatch.replace('{0}', ''));
            };

            say(text.listening);
            speech.start();
        },

        cancel: cancel,

        /** Exposed for the browser test and for anything that wants to know what a phrase maps to. */
        match: match,

        /** Hides the trigger where speech recognition does not exist, rather than offering a dead button. */
        init: function (): void {
            if (recognizer()) return;
            const button = document.getElementById(BUTTON);
            if (button) button.hidden = true;
        },
    };
})();

// Immediately, not on DOMContentLoaded: this script is loaded after the layout markup, and a
// streamed page can leave that event later than the user's first look at the button.
window.quickShareVoice.init();
