namespace ShipmentFeedSimulator;

/// <summary>The simulator's one page - the two real endpoints entered one at a time, matching how they're actually called.</summary>
internal static class Page
{
    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <title>Shipment Feed Simulator</title>
        <style>
            body { font-family: system-ui, sans-serif; max-width: 900px; margin: 2rem auto; padding: 0 1rem; color: #1a1a1a; }
            h1 { font-size: 1.3rem; }
            h2 { font-size: 1.05rem; margin-top: 2rem; border-top: 1px solid #ddd; padding-top: 1.25rem; }
            .warning { background: #fff3cd; border: 1px solid #f0c36d; border-radius: 6px; padding: 0.75rem 1rem; margin-bottom: 1.5rem; }
            .step { opacity: 1; transition: opacity 0.15s; }
            .step.disabled { opacity: 0.45; }
            label { display: block; font-weight: 600; margin: 1rem 0 0.25rem; }
            input, textarea, select { width: 100%; box-sizing: border-box; font-family: ui-monospace, Consolas, monospace; font-size: 0.85rem; padding: 0.5rem; border: 1px solid #ccc; border-radius: 4px; }
            textarea { height: 200px; resize: vertical; }
            button { margin-top: 1.25rem; padding: 0.6rem 1.5rem; font-size: 1rem; background: #2563eb; color: white; border: none; border-radius: 6px; cursor: pointer; }
            button:disabled { background: #93b4f0; cursor: not-allowed; }
            pre { background: #f5f5f5; border-radius: 6px; padding: 1rem; overflow-x: auto; white-space: pre-wrap; word-break: break-word; margin-top: 1rem; }
            .error { background: #fde8e8; border: 1px solid #f5b5b5; }
            .success { background: #e8f9ee; border: 1px solid #b5e8c8; }
            .hint { color: #555; font-size: 0.9rem; }
            code { background: #eee; padding: 0.1rem 0.35rem; border-radius: 4px; }
        </style>
        </head>
        <body>

        <h1>Shipment Feed Simulator</h1>
        <p><a href="/export-attributes">Export Attribute Feed Simulator &rarr;</a></p>
        <div class="warning">
            Writes directly into the real <code>LTS_Integration</code> database (LTS_Shipments,
            LTS_ShipmentTransfers, LTS_Boxes) - same upsert logic the live poller uses, just fed
            from what you paste below instead of a real HTTP call.
        </div>

        <div id="step1" class="step">
        <h2>Step 1 &mdash; GetInvoiceListByCustomerCode</h2>
        <label for="customerCode">Customer Code (LTS_Countries.CustomerCode)</label>
        <input id="customerCode" placeholder="M001882">

        <label for="listJson">Response JSON &mdash; just the array from &quot;Value&quot;, not the whole envelope</label>
        <textarea id="listJson" placeholder='[{"InvoiceNumber": "INV-001", "ExportNumber": "26GE001", ...}]'></textarea>
        <button id="loadList">Load List</button>
        <div id="listResult"></div>
        </div>

        <div id="step2" class="step disabled">
        <h2>Step 2 &mdash; Pick a shipment</h2>
        <p class="hint">Loaded from Step 1. Picking one shows the invoice number to fetch detail for.</p>
        <select id="shipmentPicker" disabled><option>Load the list first&hellip;</option></select>
        </div>

        <div id="step3" class="step disabled">
        <h2>Step 3 &mdash; GetInvoiceDetailByInvoiceNumber</h2>
        <p class="hint">Fetch the detail response for invoice number <code id="invoiceNumberHint">-</code>, then paste it below.</p>
        <label for="detailJson">Response JSON &mdash; just the array from &quot;Value&quot;, not the whole envelope</label>
        <textarea id="detailJson" placeholder='[{"PackageNumber": "PKG1", "StoreCode": "ST01", "Quantity": 5, ...}]' disabled></textarea>
        <button id="run" disabled>Run Simulation</button>
        <div id="result"></div>
        </div>

        <script>
            let entries = [];

            const step2 = document.getElementById('step2');
            const step3 = document.getElementById('step3');
            const picker = document.getElementById('shipmentPicker');
            const detailJson = document.getElementById('detailJson');
            const runButton = document.getElementById('run');

            function renderOutcome(container, response, payload) {
                const pre = document.createElement('pre');
                pre.className = response.ok ? 'success' : 'error';
                pre.textContent = JSON.stringify(payload, null, 2);
                container.innerHTML = '';
                container.appendChild(pre);
            }

            document.getElementById('loadList').addEventListener('click', async () => {
                const listResultDiv = document.getElementById('listResult');
                listResultDiv.innerHTML = '';

                try {
                    const response = await fetch('/parse-list', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ listJson: document.getElementById('listJson').value })
                    });
                    const payload = await response.json();

                    if (!response.ok) {
                        renderOutcome(listResultDiv, response, payload);
                        return;
                    }

                    entries = payload.entries;
                    picker.innerHTML = '';
                    entries.forEach(e => {
                        const opt = document.createElement('option');
                        opt.value = e.index;
                        opt.textContent = e.invoiceNumber + '  (ExportNumber: ' + e.exportNumber + ')';
                        picker.appendChild(opt);
                    });
                    picker.disabled = false;
                    step2.classList.remove('disabled');
                    picker.dispatchEvent(new Event('change'));

                    const pre = document.createElement('pre');
                    pre.className = 'success';
                    pre.textContent = 'Loaded ' + entries.length + ' shipment(s). Pick one below.';
                    listResultDiv.appendChild(pre);
                } catch (err) {
                    listResultDiv.innerHTML = '';
                    const pre = document.createElement('pre');
                    pre.className = 'error';
                    pre.textContent = String(err);
                    listResultDiv.appendChild(pre);
                }
            });

            picker.addEventListener('change', () => {
                const idx = Number(picker.value);
                const entry = entries.find(e => e.index === idx);
                document.getElementById('invoiceNumberHint').textContent = entry ? entry.invoiceNumber : '-';
                step3.classList.remove('disabled');
                detailJson.disabled = false;
                runButton.disabled = false;
            });

            runButton.addEventListener('click', async () => {
                const resultDiv = document.getElementById('result');
                resultDiv.innerHTML = '';
                runButton.disabled = true;
                runButton.textContent = 'Running...';

                try {
                    const response = await fetch('/simulate', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            customerCode: document.getElementById('customerCode').value,
                            listJson: document.getElementById('listJson').value,
                            selectedIndex: Number(picker.value),
                            detailJson: detailJson.value
                        })
                    });
                    const payload = await response.json();
                    renderOutcome(resultDiv, response, payload);
                } catch (err) {
                    resultDiv.innerHTML = '';
                    const pre = document.createElement('pre');
                    pre.className = 'error';
                    pre.textContent = String(err);
                    resultDiv.appendChild(pre);
                } finally {
                    runButton.disabled = false;
                    runButton.textContent = 'Run Simulation';
                }
            });
        </script>

        </body>
        </html>
        """;
}
