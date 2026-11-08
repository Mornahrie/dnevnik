(function () {
    'use strict';

    window.exportAnalyticsToPDF = function (a, b, c) {
        let btn, areaId, filename;
        if (typeof a === 'string') {
            areaId = a; filename = b; btn = c;
        } else {
            btn = a; areaId = b; filename = c;
        }
        areaId = areaId || 'analyticsPrintArea';
        filename = filename || 'analytics.pdf';

        const element = document.getElementById(areaId);
        if (!element) {
            if (window.AppCommon) AppCommon.toast('Область печати не найдена', 'error');
            return;
        }
        if (typeof html2pdf === 'undefined') {
            if (window.AppCommon) AppCommon.toast('Библиотека html2pdf не загружена', 'error');
            return;
        }

        const originalText = btn ? btn.innerHTML : null;
        if (btn) { btn.disabled = true; btn.innerHTML = 'Генерация PDF...'; }

        const printWidth = 1060;

        const opt = {
            // Уменьшили поля до 4мм, чтобы дать больше места по высоте
            margin: [4, 4, 4, 4],
            filename: filename,
            image: { type: 'jpeg', quality: 0.98 },
            html2canvas: {
                scale: 2,
                useCORS: true,
                logging: false,
                backgroundColor: '#ffffff',
                windowWidth: printWidth,
                scrollX: 0,
                scrollY: 0,
                x: 0,
                y: 0,
                onclone: function (clonedDoc) {
                    clonedDoc.documentElement.style.cssText = 'margin: 0 !important; padding: 0 !important; width: ' + printWidth + 'px !important; overflow: hidden !important;';
                    clonedDoc.body.style.cssText = 'margin: 0 !important; padding: 0 !important; width: ' + printWidth + 'px !important; overflow: hidden !important; background: #ffffff !important;';

                    const printArea = clonedDoc.getElementById(areaId);
                    if (!printArea) return;

                    const hideSelectors = ['.sidebar', '.header', '.toolbar', '.filters', '.toast-container', 'button', '.btn', '.btn-primary', '.btn-secondary'];
                    hideSelectors.forEach(sel => {
                        clonedDoc.querySelectorAll(sel).forEach(el => el.style.display = 'none');
                    });

                    let parent = printArea.parentElement;
                    while (parent && parent !== clonedDoc.body) {
                        parent.style.setProperty('margin', '0', 'important');
                        parent.style.setProperty('padding', '0', 'important');
                        parent.style.setProperty('transform', 'none', 'important');
                        parent.style.setProperty('left', '0', 'important');
                        parent.style.setProperty('position', 'static', 'important');
                        parent.style.setProperty('width', '100%', 'important');
                        parent.style.setProperty('max-width', '100%', 'important');
                        parent = parent.parentElement;
                    }

                    printArea.style.setProperty('width', '100%', 'important');
                    printArea.style.setProperty('max-width', printWidth + 'px', 'important');
                    printArea.style.setProperty('margin', '0 auto', 'important');
                    printArea.style.setProperty('padding', '10px 15px', 'important');
                    printArea.style.setProperty('box-sizing', 'border-box', 'important');

                    const style = clonedDoc.createElement('style');
                    style.innerHTML = `
                        * {
                            box-sizing: border-box !important;
                        }
                        #${areaId} {
                            /* Уменьшили высоту до 730px для запаса */
                            height: 700px !important; 
                            max-height: 730px !important;
                            display: flex !important;
                            flex-direction: column !important;
                            background: #fff !important;
                            
                            /* Запрещаем перенос на другую страницу */
                            page-break-inside: avoid !important; 
                            break-inside: avoid !important;
                            overflow: hidden !important; /* Обрезаем все, что может вылезти за границы */
                        }
                        
                        #${areaId} .analytics-grid {
                            display: grid !important;
                            grid-template-columns: repeat(12, 1fr) !important;
                            grid-template-rows: auto 1fr 1fr !important;
                            gap: 12px !important;
                            flex: 1 !important;
                            height: 100% !important;
                            width: 100% !important;
                            margin: 0 !important;
                        }
                        
                        #${areaId} .kpi-card {
                            grid-column: span 3 !important;
                            padding: 12px 16px !important;
                            border-radius: 10px !important;
                            width: 100% !important;
                            box-shadow: none !important; /* Убираем тени, чтобы они не расширяли границы canvas */
                        }
                        #${areaId} .kpi-card .kpi-label { font-size: 13px !important; }
                        #${areaId} .kpi-card .kpi-value { font-size: 26px !important; }

                        #${areaId} .chart-card {
                            grid-column: span 6 !important;
                            padding: 12px 16px !important;
                            border-radius: 10px !important;
                            display: flex !important;
                            flex-direction: column !important;
                            width: 100% !important;
                            box-shadow: none !important; /* Убираем тени */
                        }
                        #${areaId} .chart-card h4 {
                            margin: 0 0 10px 0 !important;
                            font-size: 15px !important;
                            flex-shrink: 0 !important;
                        }

                        #${areaId} .bar-chart {
                            flex: 1 !important;
                            display: flex !important;
                            align-items: flex-end !important;
                            padding: 5px 10px 0 !important;
                            gap: 10px !important;
                        }
                        #${areaId} .bar-item { max-width: 75px !important; }
                        #${areaId} .bar-item .bar-value { font-size: 12px !important; top: -18px !important; }
                        #${areaId} .bar-item .bar-label { font-size: 12px !important; margin-top: 6px !important; }

                        #${areaId} #dynamicsChart, #${areaId} #anDynamics {
                            flex: 1 !important;
                            display: flex !important;
                            align-items: center !important;
                            justify-content: center !important;
                        }
                        #${areaId} #dynamicsChart svg, #${areaId} #anDynamics svg {
                            height: 160px !important;
                            width: 100% !important;
                        }

                        #${areaId} .pie-wrap {
                            flex: 1 !important;
                            gap: 20px !important;
                            align-items: center !important;
                            justify-content: center !important;
                        }
                        #${areaId} .pie-wrap svg { 
                            width: 140px !important;
                            height: 140px !important; 
                        }
                        #${areaId} .pie-legend { gap: 10px !important; }
                        #${areaId} .pie-legend-item { font-size: 14px !important; }

                        #${areaId} .hbar-list {
                            flex: 1 !important;
                            display: flex !important;
                            flex-direction: column !important;
                            justify-content: space-evenly !important;
                        }
                        #${areaId} .hbar-item {
                            grid-template-columns: 180px 1fr 30px !important;
                            gap: 10px !important;
                            font-size: 13px !important;
                            margin-bottom: 0 !important;
                        }
                        #${areaId} .hbar-track {
                            height: 14px !important;
                            border-radius: 7px !important;
                        }
                    `;
                    clonedDoc.head.appendChild(style);
                }
            },
            jsPDF: { unit: 'mm', format: 'a4', orientation: 'landscape', compress: true },
            pagebreak: { mode: 'css' }
        };

        html2pdf().set(opt).from(element).save()
            .then(() => {
                if (window.AppCommon) AppCommon.toast('PDF успешно сохранён');
            })
            .catch(err => {
                console.error('PDF Error:', err);
                if (window.AppCommon) AppCommon.toast('Ошибка при создании PDF', 'error');
            })
            .finally(() => {
                if (btn) { btn.disabled = false; btn.innerHTML = originalText; }
            });
    };
})();