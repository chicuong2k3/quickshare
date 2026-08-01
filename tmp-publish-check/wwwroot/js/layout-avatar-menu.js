(function () {
    'use strict';

    var initFlag = '__layoutAvatarMenuInitialized';
    if (window[initFlag]) {
        return;
    }

    window[initFlag] = true;

    function closeAllMenus() {
        var menus = document.querySelectorAll('.db-dropdown.is-open');
        for (var i = 0; i < menus.length; i++) {
            menus[i].classList.remove('is-open');
        }
    }

    document.addEventListener('click', function (event) {
        var trigger = event.target.closest('[data-avatar-menu-trigger="true"]');
        if (trigger) {
            var dropdown = trigger.closest('.db-dropdown');
            if (!dropdown) {
                return;
            }

            var willOpen = !dropdown.classList.contains('is-open');
            closeAllMenus();
            if (willOpen) {
                dropdown.classList.add('is-open');
            }
            return;
        }

        closeAllMenus();
    });

    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Escape') {
            return;
        }

        closeAllMenus();
    });
})();
