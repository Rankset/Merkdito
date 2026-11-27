/*
 * Autenticação central da interface do cliente Merkdito.
 *
 * Responsabilidades:
 * - manter o JWT de cliente em sessionStorage;
 * - anexar Authorization: Bearer a chamadas protegidas;
 * - rejeitar destinos fora da API configurada;
 * - detectar expiração local pelo claim JWT `exp` (apenas para UX);
 * - em qualquer 401 de uma chamada autenticada, limpar a sessão e redirecionar
 *   para o login com motivo=sessao-expirada;
 * - deixar a API como autoridade final sobre validade, pwdv e estado da conta.
 *
 * A decodificação local do JWT NÃO valida assinatura. A segurança efetiva deve
 * continuar no backend, incluindo a validação JWT e a verificação do cliente ativo.
 */
(function () {
    "use strict";

    const API_BASE_URL = String(window.MERKDITO_API_URL || "http://localhost:5102/api")
        .replace(/\/+$/, "");
    const TOKEN_KEY = "merkdito_cliente_token";
    const PROFILE_KEY = "merkdito_cliente_perfil";
    const NAME_KEY = "merkdito_cliente_nome";
    const EMAIL_KEY = "merkdito_cliente_email";
    const MAX_TIMEOUT_MS = 2147483647;

    let expiryTimer = null;
    let redirectingToLogin = false;

    class SessionRedirectError extends Error {
        constructor(message) {
            super(message);
            this.name = "SessionRedirectError";
            this.code = "MERKDITO_SESSION_REDIRECT";
        }
    }

    function readMessage(payload, fallback) {
        if (!payload || typeof payload !== "object") return fallback;
        return payload.mensagem || payload.message || payload.title || fallback;
    }

    async function readJsonSafely(response) {
        const text = await response.text();
        if (!text) return null;
        try { return JSON.parse(text); } catch { return null; }
    }

    function stopExpiryTimer() {
        if (expiryTimer !== null) {
            window.clearTimeout(expiryTimer);
            expiryTimer = null;
        }
    }

    function clearSession() {
        stopExpiryTimer();
        sessionStorage.removeItem(TOKEN_KEY);
        sessionStorage.removeItem(PROFILE_KEY);
        sessionStorage.removeItem(NAME_KEY);
        sessionStorage.removeItem(EMAIL_KEY);
    }

    function redirectToLogin(reason) {
        if (redirectingToLogin) return;
        redirectingToLogin = true;
        clearSession();

        const destination = new URL("index.html", window.location.href);
        destination.searchParams.set("motivo", reason || "autenticacao");
        window.location.replace(destination.toString());
    }

    function raiseSessionRedirect(reason, message) {
        redirectToLogin(reason);
        throw new SessionRedirectError(message || "Sua sessão não é mais válida. Entre novamente.");
    }

    function getJwtPayload(token) {
        if (!token || typeof token !== "string") return null;
        const parts = token.split(".");
        if (parts.length < 2) return null;

        try {
            const base64 = parts[1].replace(/-/g, "+").replace(/_/g, "/");
            const padded = base64.padEnd(base64.length + ((4 - base64.length % 4) % 4), "=");
            const binary = window.atob(padded);
            const bytes = Uint8Array.from(binary, char => char.charCodeAt(0));
            const json = new TextDecoder().decode(bytes);
            const payload = JSON.parse(json);
            return payload && typeof payload === "object" ? payload : null;
        } catch {
            return null;
        }
    }

    function getExpirationTime(token) {
        const payload = getJwtPayload(token);
        const exp = payload && Number(payload.exp);
        return Number.isFinite(exp) && exp > 0 ? exp * 1000 : null;
    }

    function tokenIsLocallyExpired(token) {
        const expiresAt = getExpirationTime(token);
        // Tokens sem `exp` não são aceitos/rejeitados com base apenas no frontend;
        // o backend fará a validação autoritativa.
        return expiresAt !== null && expiresAt <= Date.now();
    }

    function scheduleExpiryCheck(token) {
        stopExpiryTimer();
        if (!token) return;

        function check() {
            expiryTimer = null;
            if (sessionStorage.getItem(TOKEN_KEY) !== token) return;

            const expiresAt = getExpirationTime(token);
            if (expiresAt === null) return;

            const remaining = expiresAt - Date.now();
            if (remaining <= 0) {
                redirectToLogin("sessao-expirada");
                return;
            }

            expiryTimer = window.setTimeout(check, Math.min(remaining, MAX_TIMEOUT_MS));
        }

        check();
    }

    function isClientAuthenticated() {
        const token = sessionStorage.getItem(TOKEN_KEY);
        if (!token || sessionStorage.getItem(PROFILE_KEY) !== "CLIENTE") return false;

        if (tokenIsLocallyExpired(token)) {
            redirectToLogin("sessao-expirada");
            return false;
        }

        scheduleExpiryCheck(token);
        return true;
    }

    async function login(email, senha) {
        if (!String(email || "").trim() || !String(senha || "")) {
            return { sucesso: false, mensagem: "Informe o e-mail e a senha." };
        }

        let response;
        try {
            // Login usa fetch direto de propósito: um 401 aqui significa credenciais
            // recusadas, não uma sessão autenticada que precise ser redirecionada.
            response = await fetch(`${API_BASE_URL}/Auth/login`, {
                method: "POST",
                headers: { "Content-Type": "application/json", "Accept": "application/json" },
                body: JSON.stringify({ email: String(email).trim(), senha })
            });
        } catch {
            return {
                sucesso: false,
                mensagem: "Não foi possível conectar à API. Confira se ela está ativa e se a URL configurada em assets/js/config.js corresponde à porta em execução."
            };
        }

        const payload = await readJsonSafely(response);
        if (!response.ok) {
            return {
                sucesso: false,
                mensagem: readMessage(payload, "Não foi possível entrar. Verifique o e-mail e a senha.")
            };
        }

        const token = payload && (payload.token || payload.Token);
        const perfil = payload && (payload.perfil || payload.Perfil);
        if (!token || !perfil) {
            clearSession();
            return { sucesso: false, mensagem: "A resposta da API não contém os dados de autenticação esperados." };
        }

        const perfilNormalizado = String(perfil).toUpperCase();
        if (!["CLIENTE", "FUNCIONARIO"].includes(perfilNormalizado)) {
            clearSession();
            return {
                sucesso: false,
                mensagem: "Este portal aceita somente contas de cliente ou funcionário."
            };
        }

        if (tokenIsLocallyExpired(token)) {
            clearSession();
            return { sucesso: false, mensagem: "A API retornou um token expirado. Tente entrar novamente e, se persistir, contate o administrador." };
        }

        clearSession();
        redirectingToLogin = false;
        sessionStorage.setItem(TOKEN_KEY, token);
        sessionStorage.setItem(PROFILE_KEY, perfilNormalizado);
        sessionStorage.setItem(NAME_KEY, payload.nome || payload.Nome || "");
        sessionStorage.setItem(EMAIL_KEY, payload.email || payload.Email || String(email).trim());
        scheduleExpiryCheck(token);
        return { sucesso: true, mensagem: "Login realizado com sucesso." };
    }

    function isEmployeeAuthenticated() {
        const token = sessionStorage.getItem(TOKEN_KEY);
        if (!token || sessionStorage.getItem(PROFILE_KEY) !== "FUNCIONARIO") return false;
        if (tokenIsLocallyExpired(token)) {
            redirectToLogin("sessao-expirada");
            return false;
        }
        scheduleExpiryCheck(token);
        return true;
    }

    function requireClient() {
        const token = sessionStorage.getItem(TOKEN_KEY);
        const profile = sessionStorage.getItem(PROFILE_KEY);
        if (!token || profile !== "CLIENTE") {
            redirectToLogin("autenticacao");
            return false;
        }
        if (tokenIsLocallyExpired(token)) {
            redirectToLogin("sessao-expirada");
            return false;
        }
        scheduleExpiryCheck(token);
        return true;
    }

    function requireEmployee() {
        const token = sessionStorage.getItem(TOKEN_KEY);
        const profile = sessionStorage.getItem(PROFILE_KEY);
        if (!token || profile !== "FUNCIONARIO") {
            redirectToLogin("autenticacao-funcionario");
            return false;
        }
        if (tokenIsLocallyExpired(token)) {
            redirectToLogin("sessao-expirada");
            return false;
        }
        scheduleExpiryCheck(token);
        return true;
    }

    function logout() {
        redirectToLogin("logout");
    }

    async function applyEmployeeNavigation() {
        if (!isEmployeeAuthenticated()) return;
        const checks = [
            ["funcionario_vendas.html", ["funcionario/vendas/estabelecimentos"]],
            ["funcionario_produtos.html", ["funcionario/produtos/estabelecimentos"]],
            ["funcionario_estoque.html", ["funcionario/estoque/estabelecimentos"]],
            ["funcionario_relatorios.html", ["funcionario/relatorios/estabelecimentos"]],
            ["funcionario_compras.html", [
                "funcionario/operacoes/estabelecimentos-fornecedores",
                "funcionario/operacoes/estabelecimentos-compras"
            ]]
        ];

        await Promise.all(checks.map(async ([page, endpoints]) => {
            let allowed = false;
            for (const endpoint of endpoints) {
                try {
                    const response = await apiFetchEmployee(endpoint);
                    if (!response.ok) continue;
                    const payload = await readJsonSafely(response);
                    if (Array.isArray(payload) && payload.length > 0) {
                        allowed = true;
                        break;
                    }
                } catch (error) {
                    if (error && error.code === "MERKDITO_SESSION_REDIRECT") return;
                }
            }
            document.querySelectorAll(`a[href="${page}"]`).forEach(link => {
                const item = link.closest("li");
                if (item) item.hidden = !allowed;
                else link.hidden = !allowed;
            });
        }));
    }

    async function apiFetchForProfile(endpoint, options, expectedProfile) {
        const token = sessionStorage.getItem(TOKEN_KEY);
        if (!token || sessionStorage.getItem(PROFILE_KEY) !== expectedProfile) {
            raiseSessionRedirect("autenticacao", `Não há uma sessão ${expectedProfile === "FUNCIONARIO" ? "de funcionário" : "de cliente"} válida. Entre novamente.`);
        }
        if (tokenIsLocallyExpired(token)) {
            raiseSessionRedirect("sessao-expirada", "Sua sessão expirou. Entre novamente.");
        }
        scheduleExpiryCheck(token);

        let url;
        try {
            const endpointText = String(endpoint || "");
            url = new URL(/^https?:\/\//i.test(endpointText)
                ? endpointText
                : `${API_BASE_URL}/${endpointText.replace(/^\/+/, "")}`);
            const base = new URL(API_BASE_URL);
            const basePath = base.pathname.replace(/\/+$/, "");
            if (url.origin !== base.origin ||
                !(url.pathname === basePath || url.pathname.startsWith(`${basePath}/`))) {
                throw new Error("Destino fora da API configurada.");
            }
        } catch {
            throw new Error("O endereço solicitado não pertence à API configurada.");
        }

        const config = { ...(options || {}) };
        const headers = new Headers(config.headers || {});
        headers.set("Authorization", `Bearer ${token}`);
        headers.set("Accept", "application/json");
        config.headers = headers;

        let response;
        try {
            response = await fetch(url.toString(), config);
        } catch {
            throw new Error("Não foi possível comunicar com a API. Verifique se a API está ativa, o endereço configurado e se a origem do frontend está liberada no CORS da API.");
        }
        if (response.status === 401) {
            raiseSessionRedirect("sessao-expirada", "Sua sessão expirou ou seu acesso foi alterado. Entre novamente.");
        }
        return response;
    }

    function apiFetch(endpoint, options) {
        return apiFetchForProfile(endpoint, options, "CLIENTE");
    }

    function apiFetchEmployee(endpoint, options) {
        return apiFetchForProfile(endpoint, options, "FUNCIONARIO");
    }

    window.MerkditoAuth = {
        apiBaseUrl: API_BASE_URL,
        login,
        apiFetch,
        isClientAuthenticated,
        isEmployeeAuthenticated,
        requireClient,
        requireEmployee,
        apiFetchEmployee,
        applyEmployeeNavigation,
        clearSession,
        logout,
        isSessionRedirectError(error) {
            return Boolean(error && error.code === "MERKDITO_SESSION_REDIRECT");
        },
        get isRedirectingToLogin() { return redirectingToLogin; },
        updateCachedIdentity(nome, email) {
            if (nome !== undefined && nome !== null) sessionStorage.setItem(NAME_KEY, String(nome));
            if (email !== undefined && email !== null) sessionStorage.setItem(EMAIL_KEY, String(email));
        },
        get nome() { return sessionStorage.getItem(NAME_KEY) || ""; },
        get email() { return sessionStorage.getItem(EMAIL_KEY) || ""; }
    };

    document.addEventListener("DOMContentLoaded", function () {
        if (document.body && document.body.dataset.requireClient === "true") requireClient();
        if (document.body && document.body.dataset.requireEmployee === "true" && requireEmployee()) {
            applyEmployeeNavigation();
        }
    });

    // Ajuda quando uma aba ficou suspensa em segundo plano e voltou depois do prazo.
    window.addEventListener("focus", function () {
        if (document.body && document.body.dataset.requireClient === "true") requireClient();
        if (document.body && document.body.dataset.requireEmployee === "true") requireEmployee();
    });
    document.addEventListener("visibilitychange", function () {
        if (!document.hidden && document.body && document.body.dataset.requireClient === "true") requireClient();
        if (!document.hidden && document.body && document.body.dataset.requireEmployee === "true") requireEmployee();
    });
})();
