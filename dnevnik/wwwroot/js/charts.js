(function () {
    'use strict';
    const esc = window.AppCommon ? AppCommon.escapeHtml : s => String(s ?? '');
    const COLORS = ['#4a90e2', '#7ed321', '#f5a623', '#d0021b', '#9013fe'];
    const AppCharts = {};

    AppCharts.bar = function (items, opts = {}) {
        if (!items || !items.length) return '<div class="empty-state">Нет данных</div>';
        const max = opts.max || 10;
        const height = opts.height || 160;
        const valueKey = opts.valueKey || 'average';
        const labelKey = opts.labelKey || 'label';
        const fmt = opts.fmt || (v => Number(v).toFixed(2));
        return `<div class="bar-chart">${items.map(i => {
            const val = Number(i[valueKey]) || 0;
            const h = Math.max(4, (val / max) * height);
            const label = esc(i[labelKey] || i.subjectName || i.className || '');
            return `<div class="bar-item"><div class="bar" style="height:${h}px"><span class="bar-value">${fmt(val)}</span></div><div class="bar-label" title="${label}">${label}</div></div>`;
        }).join('')}</div>`;
    };

    AppCharts.line = function (points, opts = {}) {
        const w = opts.width || 720, h = opts.height || 220, max = opts.max || 10;
        if (!points || !points.length) return '<div class="empty-state">Нет данных</div>';
        if (!points.some(p => (Number(p.average) || 0) > 0)) return '<div class="empty-state">Нет оценок за выбранный период</div>';
        const padL = 44, padR = 16, padT = 24, padB = 34;
        const pw = w - padL - padR, ph = h - padT - padB;
        const stepX = points.length > 1 ? pw / (points.length - 1) : 0;
        const coords = points.map((p, i) => ({
            x: padL + i * stepX,
            y: padT + ph - ((Number(p.average) || 0) / max) * ph,
            label: p.label, avg: Number(p.average) || 0
        }));
        const pathD = coords.map((c, i) => `${i === 0 ? 'M' : 'L'} ${c.x.toFixed(1)} ${c.y.toFixed(1)}`).join(' ');
        let grid = '';
        for (let i = 0; i <= 5; i++) {
            const y = padT + (ph / 5) * i, val = max - (max / 5) * i;
            grid += `<line x1="${padL}" y1="${y}" x2="${padL + pw}" y2="${y}" stroke="#eef2f7"/>`;
            grid += `<text x="${padL - 8}" y="${y + 4}" text-anchor="end" font-size="10" fill="#7c8798">${val.toFixed(1)}</text>`;
        }
        const labels = coords.map(c => `<text x="${c.x.toFixed(1)}" y="${padT + ph + 18}" text-anchor="middle" font-size="11" fill="#7c8798">${esc(c.label)}</text>`).join('');
        const dots = coords.map(c => c.avg > 0
            ? `<circle cx="${c.x.toFixed(1)}" cy="${c.y.toFixed(1)}" r="4" fill="#fff" stroke="#4a90e2" stroke-width="2"/><text x="${c.x.toFixed(1)}" y="${(c.y - 10).toFixed(1)}" text-anchor="middle" font-size="10" font-weight="700" fill="#2a5bd7">${c.avg.toFixed(2)}</text>`
            : '').join('');
        return `<svg width="100%" height="${h}" viewBox="0 0 ${w} ${h}" preserveAspectRatio="xMidYMid meet">${grid}<path d="${pathD}" fill="none" stroke="#4a90e2" stroke-width="2.5" stroke-linejoin="round" stroke-linecap="round"/>${dots}${labels}</svg>`;
    };

    AppCharts.pie = function (items, opts = {}) {
        const size = opts.size || 150;
        const arr = items || [];
        const total = arr.reduce((s, i) => s + (Number(i.count) || 0), 0);
        if (!total) return `<svg width="${size}" height="${size}"></svg>`;
        const cx = size / 2, cy = size / 2, r = size / 2 - 4;
        const nonZero = arr.filter(i => (Number(i.count) || 0) > 0);
        let angle = -Math.PI / 2, paths = '';
        if (nonZero.length === 1) {
            paths += `<circle cx="${cx}" cy="${cy}" r="${r}" fill="${nonZero[0].color || COLORS[0]}"/>`;
        } else {
            arr.forEach((item, i) => {
                const cnt = Number(item.count) || 0;
                if (!cnt) return;
                const slice = (cnt / total) * Math.PI * 2;
                const color = item.color || COLORS[i % COLORS.length];
                const x1 = cx + r * Math.cos(angle), y1 = cy + r * Math.sin(angle);
                const x2 = cx + r * Math.cos(angle + slice), y2 = cy + r * Math.sin(angle + slice);
                const large = slice > Math.PI ? 1 : 0;
                paths += `<path d="M ${cx} ${cy} L ${x1.toFixed(1)} ${y1.toFixed(1)} A ${r} ${r} 0 ${large} 1 ${x2.toFixed(1)} ${y2.toFixed(1)} Z" fill="${color}" stroke="#fff" stroke-width="2"/>`;
                angle += slice;
            });
        }
        const inner = r * 0.55;
        paths += `<circle cx="${cx}" cy="${cy}" r="${inner}" fill="#fff"/>`;
        paths += `<text x="${cx}" y="${cy - 2}" text-anchor="middle" font-size="18" font-weight="700" fill="#1c2b46">${total}</text>`;
        paths += `<text x="${cx}" y="${cy + 14}" text-anchor="middle" font-size="10" fill="#7c8798">всего</text>`;
        return `<svg width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">${paths}</svg>`;
    };

    AppCharts.legend = function (items) {
        const arr = items || [];
        const total = arr.reduce((s, i) => s + (Number(i.count) || 0), 0) || 1;
        return `<div class="pie-legend">${arr.map((item, i) => {
            const color = item.color || COLORS[i % COLORS.length];
            const pct = ((Number(item.count) || 0) / total * 100).toFixed(0);
            return `<div class="pie-legend-item"><span class="pie-legend-color" style="background:${color}"></span><span>${esc(item.label)}</span><span class="pie-legend-count">${item.count || 0} (${pct}%)</span></div>`;
        }).join('')}</div>`;
    };

    AppCharts.hbars = function (items, opts = {}) {
        if (!items || !items.length) return '<div class="empty-state">Нет данных</div>';
        const max = Math.max(...items.map(i => Number(i.count) || 0), 1);
        const nameKey = opts.nameKey || 'teacherName';
        return items.map(i => {
            const w = ((Number(i.count) || 0) / max) * 100;
            return `<div class="hbar-item"><span>${esc(i[nameKey] || '—')}</span><div class="hbar-track"><div class="hbar-fill" style="width:${w}%"></div></div><span style="text-align:right;font-weight:600;color:#1c2b46">${i.count}</span></div>`;
        }).join('');
    };

    window.AppCharts = AppCharts;
})();