// Password breach check (issue #280): k-anonymity against the Have I Been Pwned range
// API — only the SHA-1 prefix (5 hex chars) leaves the browser, never the full hash or
// the password itself. Returns the breach count, 0 if clean, null if unreachable (fail-open).
(function () {
    async function sha1Hex(input: string): Promise<string> {
        const data = new TextEncoder().encode(input);
        const digest = await crypto.subtle.digest('SHA-1', data);
        const bytes = new Uint8Array(digest);
        let hex = '';
        for (let i = 0; i < bytes.length; i++) hex += bytes[i].toString(16).padStart(2, '0');
        return hex;
    }

    async function checkPasswordBreach(password: string): Promise<number | null> {
        if (!password) return null;
        try {
            const hash = await sha1Hex(password);
            const prefix = hash.substring(0, 5);
            const suffix = hash.substring(5).toUpperCase();
            const resp = await fetch('https://api.pwnedpasswords.com/range/' + prefix);
            if (!resp.ok) return null;
            const text = await resp.text();
            for (const line of text.split('\n')) {
                const sep = line.indexOf(':');
                if (sep === -1) continue;
                if (line.substring(0, sep).trim() === suffix) {
                    const count = parseInt(line.substring(sep + 1).trim(), 10);
                    return isNaN(count) ? 0 : count;
                }
            }
            return 0;
        } catch (_) {
            return null;
        }
    }

    function generateStrongPassword(length?: number): string {
        const len = length || 16;
        const upper = 'ABCDEFGHJKLMNPQRSTUVWXYZ';
        const lower = 'abcdefghijkmnopqrstuvwxyz';
        const digits = '23456789';
        const symbols = '!@#$%^&*';
        const all = upper + lower + digits + symbols;
        const bytes = new Uint32Array(len);
        crypto.getRandomValues(bytes);
        let result = '';
        result += upper[bytes[0] % upper.length];
        result += lower[bytes[1] % lower.length];
        result += digits[bytes[2] % digits.length];
        result += symbols[bytes[3] % symbols.length];
        for (let i = 4; i < len; i++) result += all[bytes[i] % all.length];
        return result;
    }

    (window as unknown as {
        checkPasswordBreach: typeof checkPasswordBreach;
        generateStrongPassword: typeof generateStrongPassword;
    }).checkPasswordBreach = checkPasswordBreach;
    (window as unknown as {
        generateStrongPassword: typeof generateStrongPassword;
    }).generateStrongPassword = generateStrongPassword;
})();
