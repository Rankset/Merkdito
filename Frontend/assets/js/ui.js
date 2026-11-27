/*
 * Camada comum de feedback da interface do cliente Merkdito.
 * Padroniza notificações acessíveis, mensagens HTTP e estado visual de botões.
 * Não substitui validações nem decisões de autorização da API.
 */
(function () {
    "use strict";

    const timers = new WeakMap();
    const BUSY_MARKUP = new WeakMap();

    function statusMessage(status, fallback) {
        switch (Number(status)) {
            case 400: return fallback || "Confira os dados informados e tente novamente.";
            case 401: return "Sua sessão expirou ou não é mais válida. Entre novamente.";
            case 403: return "Sua conta não tem permissão para realizar esta operação.";
            case 404: return "O recurso solicitado não foi encontrado.";
            case 409: return "A operação conflita com dados existentes. Confira o cadastro e tente novamente.";
            case 413: return "O arquivo ou os dados enviados ultrapassam o limite permitido.";
            case 422: return "Os dados enviados não passaram pela validação. Revise os campos e tente novamente.";
            case 429: return "Muitas solicitações em pouco tempo. Aguarde um instante e tente novamente.";
            default:
                if (Number(status) >= 500) return "A API encontrou um erro interno. Tente novamente mais tarde.";
                return fallback || (status ? `Não foi possível concluir a operação (HTTP ${status}).` : "Não foi possível concluir a operação.");
        }
    }


    function resolveElement(target) {
        if (typeof target === "string") return document.getElementById(target);
        return target instanceof HTMLElement ? target : null;
    }

    function installFeedbackStyles() {
        if (document.getElementById("merkditoFeedbackStyles")) return;
        const style = document.createElement("style");
        style.id = "merkditoFeedbackStyles";
        style.textContent = `
            .merkdito-feedback { box-sizing:border-box; display:block; margin:10px 0; padding:12px 14px; border:1px solid transparent; border-radius:10px; font-size:.92rem; line-height:1.5; white-space:pre-wrap; overflow-wrap:anywhere; }
            .merkdito-feedback:not(.visible):not(.is-visible) { display:none !important; }
            .merkdito-feedback--info { background:#f2f9fc !important; border-color:#b9ddec !important; color:#245878 !important; }
            .merkdito-feedback--success { background:#f0faf3 !important; border-color:#a9d9b7 !important; color:#247245 !important; }
            .merkdito-feedback--warning { background:#fff9e8 !important; border-color:#ead39a !important; color:#76550c !important; }
            .merkdito-feedback--error { background:#fff4f4 !important; border-color:#efb6b6 !important; color:#9a2e2e !important; }
            button[aria-busy="true"] { cursor:progress; }
        `;
        (document.head || document.documentElement).appendChild(style);
    }

    function showInline(target, message, type) {
        const element = resolveElement(target);
        if (!element) return;
        installFeedbackStyles();
        const kind = ["success", "error", "warning", "info"].includes(type) ? type : "info";
        element.textContent = String(message == null ? "" : message);
        element.classList.add("merkdito-feedback", "visible", "is-visible");
        element.classList.remove("merkdito-feedback--success", "merkdito-feedback--error", "merkdito-feedback--warning", "merkdito-feedback--info", "is-success", "is-error", "is-info", "success", "error", "warning", "info");
        element.classList.add(`merkdito-feedback--${kind}`);
        element.dataset.merkditoFeedback = kind;
        element.style.display = "block";
        element.setAttribute("role", kind === "error" ? "alert" : "status");
        element.setAttribute("aria-live", kind === "error" ? "assertive" : "polite");
    }

    function clearInline(target) {
        const element = resolveElement(target);
        if (!element) return;
        element.textContent = "";
        element.classList.remove("merkdito-feedback", "visible", "is-visible", "merkdito-feedback--success", "merkdito-feedback--error", "merkdito-feedback--warning", "merkdito-feedback--info", "is-success", "is-error", "is-info", "success", "error", "warning", "info");
        delete element.dataset.merkditoFeedback;
        element.style.display = "none";
    }

    function inlineStatus(target, message, type) {
        if (message == null || String(message) === "") clearInline(target);
        else showInline(target, message, type || "info");
    }

    function flattenValidationErrors(errors) {
        if (!errors || typeof errors !== "object") return "";
        const messages = [];
        for (const value of Object.values(errors)) {
            if (Array.isArray(value)) messages.push(...value.map(String));
            else if (value !== null && value !== undefined) messages.push(String(value));
        }
        return messages.filter(Boolean).slice(0, 8).join(" ");
    }

    async function responseMessage(response, fallback) {
        if (!response) return fallback || "Não foi possível obter uma resposta da API.";
        let raw = "";
        try { raw = await response.text(); } catch { /* resposta sem corpo legível */ }
        let payload = null;
        if (raw) {
            try { payload = JSON.parse(raw); } catch { /* corpo não JSON */ }
        }

        let detail = "";
        if (typeof payload === "string") detail = payload;
        else if (payload && typeof payload === "object") {
            detail = payload.mensagem || payload.Mensagem || payload.message || payload.Message ||
                payload.detail || payload.Detail || payload.title || payload.Title ||
                flattenValidationErrors(payload.errors || payload.Errors) || "";
        }
        if (!detail && raw && !/^\s*</.test(raw)) detail = raw.trim().slice(0, 500);
        if (detail) return String(detail).slice(0, 700);
        return statusMessage(response.status, fallback);
    }

    function ensureToastRegion() {
        let region = document.getElementById("merkditoToastRegion");
        if (region) return region;
        region = document.createElement("div");
        region.id = "merkditoToastRegion";
        region.setAttribute("aria-label", "Notificações");
        region.style.cssText = [
            "position:fixed", "top:18px", "right:18px", "z-index:10000",
            "display:flex", "flex-direction:column", "gap:10px",
            "width:min(420px,calc(100vw - 36px))", "pointer-events:none"
        ].join(";");
        document.body.appendChild(region);
        return region;
    }

    function notify(message, type, options) {
        if (!document.body) return null;
        const kind = ["success", "error", "warning", "info"].includes(type) ? type : "info";
        const settings = options && typeof options === "object" ? options : {};
        const duration = Number.isFinite(Number(settings.duration)) ? Math.max(0, Number(settings.duration)) : (kind === "error" ? 9000 : 5500);
        const region = ensureToastRegion();
        const toast = document.createElement("div");
        toast.className = `merkdito-toast merkdito-toast-${kind}`;
        toast.setAttribute("role", kind === "error" ? "alert" : "status");
        toast.setAttribute("aria-live", kind === "error" ? "assertive" : "polite");
        toast.style.cssText = [
            "pointer-events:auto", "display:flex", "align-items:flex-start", "gap:12px",
            "padding:14px 14px 14px 16px", "border-radius:12px", "border:1px solid",
            "box-shadow:0 10px 30px rgba(27,39,36,.16)", "background:#fff",
            "font:500 14px/1.45 Outfit,Arial,sans-serif", "color:#2c3a47"
        ].join(";");

        const palette = {
            success: { border: "#a9d9b7", accent: "#247245", background: "#f0faf3", icon: "✓" },
            error: { border: "#efb6b6", accent: "#9a2e2e", background: "#fff4f4", icon: "!" },
            warning: { border: "#ead39a", accent: "#76550c", background: "#fff9e8", icon: "⚠" },
            info: { border: "#b9ddec", accent: "#245878", background: "#f2f9fc", icon: "i" }
        }[kind];
        toast.style.borderColor = palette.border;
        toast.style.backgroundColor = palette.background;

        const icon = document.createElement("span");
        icon.textContent = palette.icon;
        icon.setAttribute("aria-hidden", "true");
        icon.style.cssText = `flex:0 0 22px;width:22px;height:22px;display:grid;place-items:center;border-radius:50%;font-weight:800;color:${palette.accent};background:${palette.border}`;
        const text = document.createElement("div");
        text.textContent = String(message || "");
        text.style.cssText = "flex:1;overflow-wrap:anywhere";
        const close = document.createElement("button");
        close.type = "button";
        close.textContent = "×";
        close.setAttribute("aria-label", "Fechar notificação");
        close.style.cssText = "border:0;background:transparent;color:#52616a;font-size:22px;line-height:1;cursor:pointer;padding:0 2px";
        close.addEventListener("click", () => dismiss(toast));
        toast.append(icon, text, close);
        region.appendChild(toast);

        while (region.children.length > 4) dismiss(region.firstElementChild);
        if (duration > 0) timers.set(toast, window.setTimeout(() => dismiss(toast), duration));
        return toast;
    }

    function dismiss(toast) {
        if (!toast) return;
        const timer = timers.get(toast);
        if (timer) window.clearTimeout(timer);
        timers.delete(toast);
        toast.remove();
        const region = document.getElementById("merkditoToastRegion");
        if (region && !region.children.length) region.remove();
    }

    // Mostra uma indicação temporária e restaura o texto original no finally.
    // Não restaura a propriedade disabled de propósito: a tela pode recalculá-la
    // de acordo com a quantidade de linhas válidas ao término da operação.
    function setBusy(button, label) {
        if (!(button instanceof HTMLElement)) return function () {};
        if (BUSY_MARKUP.has(button)) return BUSY_MARKUP.get(button).restore;
        const originalHtml = button.innerHTML;
        const originalBusy = button.getAttribute("aria-busy");
        const originalTitle = button.getAttribute("title");
        button.disabled = true;
        button.setAttribute("aria-busy", "true");
        button.title = String(label || "Processando...");
        const spinner = document.createElement("span");
        spinner.className = "fa-solid fa-spinner fa-spin";
        spinner.setAttribute("aria-hidden", "true");
        const text = document.createElement("span");
        text.textContent = ` ${String(label || "Processando...")}`;
        button.replaceChildren(spinner, text);
        const restore = function () {
            if (!BUSY_MARKUP.has(button)) return;
            button.innerHTML = originalHtml;
            if (originalBusy === null) button.removeAttribute("aria-busy");
            else button.setAttribute("aria-busy", originalBusy);
            if (originalTitle === null) button.removeAttribute("title");
            else button.setAttribute("title", originalTitle);
            BUSY_MARKUP.delete(button);
        };
        BUSY_MARKUP.set(button, { restore });
        return restore;
    }

    window.MerkditoUI = {
        notify,
        dismiss,
        setBusy,
        showInline,
        clearInline,
        inlineStatus,
        responseMessage,
        statusMessage,
        isSessionRedirectError(error) {
            return Boolean(window.MerkditoAuth && window.MerkditoAuth.isSessionRedirectError && window.MerkditoAuth.isSessionRedirectError(error));
        }
    };
})();
