(function () {
    'use strict';
    const AppCommon = {};

    AppCommon.pad2 = n => String(n).padStart(2, '0');
    AppCommon.toDateOnly = d => `${d.getFullYear()}-${AppCommon.pad2(d.getMonth() + 1)}-${AppCommon.pad2(d.getDate())}`;
    AppCommon.parseLocalDate = function (d) {
        if (!d) return null;
        if (d instanceof Date) return new Date(d.getFullYear(), d.getMonth(), d.getDate());
        const s = String(d);
        const m = s.match(/^(\d{4})-(\d{2})-(\d{2})/);
        if (m) return new Date(+m[1], +m[2] - 1, +m[3]);
        const dt = new Date(d);
        return new Date(dt.getFullYear(), dt.getMonth(), dt.getDate());
    };
    AppCommon.fmtShort = d => { const x = AppCommon.parseLocalDate(d); return x ? x.toLocaleDateString('ru-RU', { day: '2-digit', month: '2-digit' }) : ''; };
    AppCommon.fmtLong = d => { const x = AppCommon.parseLocalDate(d); return x ? x.toLocaleDateString('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric' }) : ''; };
    AppCommon.getMonday = function (d) {
        const x = new Date(d.getFullYear(), d.getMonth(), d.getDate());
        const dow = x.getDay();
        x.setDate(x.getDate() + (dow === 0 ? -6 : 1 - dow));
        return x;
    };
    AppCommon.escapeHtml = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    AppCommon.capFirst = s => { const str = String(s ?? ''); return str ? str.charAt(0).toUpperCase() + str.slice(1) : ''; };
    AppCommon.isAbsent = function (status) {
        if (!status) return false;
        const s = String(status).trim().toLowerCase();
        return s === 'н' || s === 'отсутствовал' || s === 'отсутствовала';
    };
    AppCommon.shortAtt = s => {
        if (s === 'отсутствовал' || s === 'отсутствовала' || s === 'н') return 'Н';
        if (s === 'уважительная') return 'У';
        return s;
    };
    AppCommon.cleanToken = raw => raw ? String(raw).replace(/^\uFEFF/, '').replace(/[\u200B-\u200D]/g, '').replace(/\s/g, '').trim() : '';

    AppCommon.requireAuth = function () {
        const t = AppCommon.cleanToken(localStorage.getItem('token'));
        if (!t) { location.replace('/index.html'); return null; }
        return t;
    };

    AppCommon.makeApi = function (prefix, headers) {
        let loggingOut = false;
        return async function api(path, options = {}) {
            const r = await fetch(prefix + path, { headers, ...options });
            if (r.status === 401) {
                if (!loggingOut) { loggingOut = true; localStorage.clear(); location.replace('/index.html'); }
                throw new Error('Сессия истекла');
            }
            if (r.status === 403) throw new Error('Нет доступа');
            if (!r.ok) {
                let msg = 'Ошибка запроса';
                try {
                    const ct = r.headers.get('content-type') || '';
                    if (ct.includes('application/json')) { const j = await r.json(); msg = j.message || j.title || msg; }
                    else { const t = await r.text(); if (t) msg = t; }
                } catch { }
                throw new Error(msg);
            }
            const ct = r.headers.get('content-type') || '';
            return ct.includes('application/json') ? r.json() : r.text();
        };
    };

    AppCommon.toast = function (msg, type = 'success') {
        const c = document.getElementById('toasts');
        if (!c) { console.log('[toast]', msg); return; }
        const el = document.createElement('div');
        el.className = 'toast ' + type;
        el.textContent = msg;
        c.appendChild(el);
        setTimeout(() => el.remove(), 3500);
    };

    AppCommon.getQuarter = (quarters, id) => quarters.find(q => Number(q.id) === Number(id)) || null;
    AppCommon.pickDefaultQuarter = function (quarters) {
        if (!quarters.length) return null;
        const today = new Date(); today.setHours(0, 0, 0, 0);
        const cur = quarters.find(q => {
            const s = AppCommon.parseLocalDate(q.dateStart), e = AppCommon.parseLocalDate(q.dateEnd);
            return s <= today && today <= e;
        });
        if (cur) return cur;
        const past = quarters.filter(q => AppCommon.parseLocalDate(q.dateEnd) < today)
            .sort((a, b) => AppCommon.parseLocalDate(b.dateEnd) - AppCommon.parseLocalDate(a.dateEnd))[0];
        if (past) return past;
        return quarters.slice().sort((a, b) => AppCommon.parseLocalDate(a.dateStart) - AppCommon.parseLocalDate(b.dateStart))[0];
    };
    AppCommon.quarterBounds = function (q) {
        if (!q) return null;
        return {
            minMon: AppCommon.getMonday(AppCommon.parseLocalDate(q.dateStart)),
            maxMon: AppCommon.getMonday(AppCommon.parseLocalDate(q.dateEnd))
        };
    };

    window.AppCommon = AppCommon;
})();