/* Navegação unificada da área do cliente. Agrupa funções por módulo e marca a rota ativa. */
(function () {
    "use strict";

    function basename(href) {
        try { return new URL(href, window.location.href).pathname.split("/").pop().toLowerCase(); }
        catch { return ""; }
    }

    function initializeNavigation() {
        const current = (window.location.pathname.split("/").pop() || "dashboard.html").toLowerCase();
        const links = Array.from(document.querySelectorAll(".nav-menu a[data-nav-link]"));
        let activeLink = null;

        links.forEach(link => {
            const isActive = basename(link.href) === current;
            link.classList.toggle("active", isActive);
            if (isActive) {
                link.setAttribute("aria-current", "page");
                activeLink = link;
            } else {
                link.removeAttribute("aria-current");
            }
        });

        document.querySelectorAll("[data-nav-group]").forEach(group => {
            const button = group.querySelector(".nav-group-toggle");
            const submenu = group.querySelector(".nav-submenu");
            if (!button || !submenu) return;

            const containsActive = Boolean(activeLink && submenu.contains(activeLink));
            group.classList.toggle("is-open", containsActive);
            button.classList.toggle("active", containsActive);
            button.setAttribute("aria-expanded", containsActive ? "true" : "false");
            submenu.hidden = !containsActive;

            button.addEventListener("click", () => {
                const opening = submenu.hidden;
                // Mantém um só submenu aberto por vez, inclusive no celular.
                document.querySelectorAll("[data-nav-group]").forEach(other => {
                    if (other === group) return;
                    const otherButton = other.querySelector(".nav-group-toggle");
                    const otherSubmenu = other.querySelector(".nav-submenu");
                    other.classList.remove("is-open");
                    if (otherButton) otherButton.setAttribute("aria-expanded", "false");
                    if (otherSubmenu) otherSubmenu.hidden = true;
                });
                group.classList.toggle("is-open", opening);
                submenu.hidden = !opening;
                button.setAttribute("aria-expanded", opening ? "true" : "false");
            });

            button.addEventListener("keydown", event => {
                if (event.key === "Escape") {
                    group.classList.remove("is-open");
                    submenu.hidden = true;
                    button.setAttribute("aria-expanded", "false");
                    button.focus();
                }
            });
        });
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initializeNavigation, { once: true });
    else initializeNavigation();
})();
