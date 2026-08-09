// ad-component.js — Google AdMob rewarded video lifecycle for QuickShareCloud.
// Loaded by HTMX after the AdComponent.razor reward prompt HTML is swapped in.
window.QuickShareAds = (function() {
    'use strict';

    var state = {
        adUnitId: null,
        callbackUrl: null,
        antiForgeryToken: null,
        remainingAds: 0,
        adInstance: null,
        sdkLoaded: false,
        rewardGranted: false
    };

    /**
     * Check if user has granted ad consent cookie.
     * @returns {boolean}
     */
    function hasConsent() {
        return document.cookie.split(';').some(function(c) {
            return c.trim().startsWith('ad-consent=granted');
        });
    }
    /**
     * Initialize AdMob for the reward card.
     * Called from <script> injected alongside the reward prompt HTML.
     * @param {Object} config
     * @param {string} config.adUnitId - Google AdMob ad unit ID
     * @param {string} config.callbackUrl - POST /api/ads/reward-callback
     * @param {string} config.antiForgeryToken - Request verification token
     * @param {number} config.remainingAds - Ads left today
     */
    function init(config) {
        state.adUnitId = config.adUnitId;
        state.callbackUrl = config.callbackUrl;
        state.antiForgeryToken = config.antiForgeryToken;
        state.remainingAds = config.remainingAds;
        state.rewardGranted = false;
        if (!hasConsent()) {
            console.warn('QuickShareAds: ad consent not granted');
            return;
        }
        wireButton();
    }

    function wireButton() {
        var btn = document.getElementById('ad-reward-watch-btn');
        if (!btn) return;

        btn.addEventListener('click', function() {
            showRewardedAd();
        });

        updateButtonState();
    }

    function updateButtonState() {
        var btn = document.getElementById('ad-reward-watch-btn');
        if (!btn) return;

        if (state.remainingAds <= 0) {
            btn.disabled = true;
            btn.textContent = 'Limit reached';
        }
    }

    /**
     * Load AdMob SDK and show rewarded ad.
     */
    function showRewardedAd() {
        if (state.rewardGranted) return;
        if (!hasConsent()) {
            showError('Ad consent required. Please accept cookies to watch ads.');
            return;
        }

        var container = document.getElementById('ad-reward-card');
        if (!container) return;

        // Indicate loading
        var btn = document.getElementById('ad-reward-watch-btn');
        if (btn) {
            btn.disabled = true;
            btn.textContent = 'Loading ad\u2026';
        }

        loadAdMobSdk()
            .then(function() { return createRewardedAd(); })
            .then(function(ad) {
                state.adInstance = ad;
                return ad.show();
            })
            .catch(function(err) {
                console.warn('QuickShareAds: ad load failed', err);
                showError('Ads unavailable right now. Try again later.');
            });
    }

    function loadAdMobSdk() {
        if (state.sdkLoaded) return Promise.resolve();

        return new Promise(function(resolve, reject) {
            // Check if already loaded by another component
            if (window.adsbygoogle && window.adsbygoogle.loaded) {
                state.sdkLoaded = true;
                resolve();
                return;
            }

            var script = document.createElement('script');
            script.src = 'https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js';
            script.async = true;
            script.crossOrigin = 'anonymous';
            script.onload = function() {
                state.sdkLoaded = true;
                resolve();
            };
            script.onerror = function() {
                reject(new Error('AdMob SDK failed to load'));
            };
            document.head.appendChild(script);
        });
    }

    function createRewardedAd() {
        return new Promise(function(resolve, reject) {
            try {
                // AdMob web rewarded ad API
                // Reference: https://developers.google.com/admob/web/rewarded
                var ad = new window.google.ads.admanager.RewardedAd(state.adUnitId);

                ad.onRewardedAdLoaded = function() {
                    // Ad ready to show
                };

                ad.onRewardedAdFailedToLoad = function(error) {
                    reject(new Error('Ad failed to load: ' + error.message));
                };

                ad.onRewardedAdClosed = function() {
                    // User closed without completing — re-enable button
                    var btn = document.getElementById('ad-reward-watch-btn');
                    if (btn && !state.rewardGranted) {
                        btn.disabled = false;
                        btn.textContent = 'Watch Ad for +100MB';
                    }
                };

                ad.onRewardedAdReward = function(reward) {
                    state.rewardGranted = true;
                    var token = reward.rewardToken || reward.token || crypto.randomUUID();
                    verifyReward(token, reward.amount || 1);
                };

                ad.load();
                resolve(ad);
            } catch (err) {
                reject(err);
            }
        });
    }

    /**
     * Send reward verification token to server.
     */
    function verifyReward(token, amount) {
        var btn = document.getElementById('ad-reward-watch-btn');
        if (btn) {
            btn.disabled = true;
            btn.textContent = 'Verifying\u2026';
        }

        fetch(state.callbackUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': state.antiForgeryToken
            },
            body: JSON.stringify({ token: token, rewardAmount: amount })
        })
        .then(function(r) { return r.json(); })
        .then(function(data) {
            if (data.success) {
                showSuccess(data);
            } else {
                showError(data.error || 'Reward verification failed.');
            }
        })
        .catch(function() {
            showError('Could not verify reward. Please try again.');
        });
    }

    function showSuccess(data) {
        var container = document.getElementById('ad-reward-card');
        if (!container) return;

        var p2pNote = '';
        if (data.p2pUploadsUnlocked > 0) {
            p2pNote = '<p class="ad-reward-card__description" style="color:var(--color-success)">+'
                + data.p2pUploadsUnlocked + ' P2P upload unlocked today.</p>';
        }

        container.innerHTML =
            '<div class="ad-reward-card__body">' +
            '<div class="ad-reward-card__title" style="color:var(--color-success)">+'
                + data.quotaAddedMb + 'MB added!</div>' +
            '<p class="ad-reward-card__description">You have <strong>'
                + data.adsRemainingToday + ' ads</strong> remaining today.</p>' +
            p2pNote +
            '<a href="/pricing" class="ad-upgrade-link">'
                + 'Upgrade to Pro for unlimited bandwidth</a>' +
            '</div>';

        // Notify listeners (e.g. P2P send page) that a reward was granted so
        // they can refresh quota / retry an upload.
        window.dispatchEvent(new CustomEvent('quickshare:ad-rewarded', { detail: data }));
    }

    function showError(message) {
        var btn = document.getElementById('ad-reward-watch-btn');
        if (btn) {
            btn.disabled = false;
            btn.textContent = 'Try again';
        }
        var error = document.getElementById('ad-reward-error');
        if (error) {
            error.textContent = message;
            error.style.display = 'block';
        }
    }

    /** Clean up ad instance. */
    function destroy() {
        if (state.adInstance) {
            try { state.adInstance.destroy(); } catch(e) {}
            state.adInstance = null;
        }
    }

    return {
        init: init,
        destroy: destroy,
        showRewardedAd: showRewardedAd,
        verifyReward: verifyReward
    };
})();
