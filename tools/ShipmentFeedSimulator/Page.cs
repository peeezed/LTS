namespace ShipmentFeedSimulator;

/// <summary>The simulator's one page - two fields, one per real endpoint, plus a customer code and a Run button.</summary>
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
            .warning { background: #fff3cd; border: 1px solid #f0c36d; border-radius: 6px; padding: 0.75rem 1rem; margin-bottom: 1.5rem; }
            label { display: block; font-weight: 600; margin: 1rem 0 0.25rem; }
            input, textarea { width: 100%; box-sizing: border-box; font-family: ui-monospace, Consolas, monospace; font-size: 0.85rem; padding: 0.5rem; border: 1px solid #ccc; border-radius: 4px; }
            textarea { height: 220px; resize: vertical; }
            button { margin-top: 1.25rem; padding: 0.6rem 1.5rem; font-size: 1rem; background: #2563eb; color: white; border: none; border-radius: 6px; cursor: pointer; }
            button:disabled { background: #93b4f0; cursor: default; }
            pre { background: #f5f5f5; border-radius: 6px; padding: 1rem; overflow-x: auto; white-space: pre-wrap; word-break: break-word; }
            .error { background: #fde8e8; border: 1px solid #f5b5b5; }
            .success { background: #e8f9ee; border: 1px solid #b5e8c8; }
        </style>
        </head>
        <body>

        <h1>Shipment Feed Simulator</h1>
        <div class="warning">
            Writes directly into the real <code>LTS_Integration</code> database (LTS_Shipments,
            LTS_ShipmentTransfers, LTS_Boxes) - same upsert logic the live poller uses, just fed
            from what you paste below instead of a real HTTP call.
        </div>

        <label for="customerCode">Customer Code (LTS_Countries.CustomerCode)</label>
        <input id="customerCode" placeholder="M001882">

        <label for="listJson">GetInvoiceListByCustomerCode response</label>
        <textarea id="listJson" placeholder='{"IsSuccess": true, "Value": [{"InvoiceNumber": "INV-001", "ExportNumber": "26GE001", ...}], "Message": null}'></textarea>

        <label for="detailJson">GetInvoiceDetailByInvoiceNumber response</label>
        <textarea id="detailJson" placeholder='{"IsSuccess": true, "Value": [{"PackageNumber": "PKG1", "StoreCode": "ST01", "Quantity": 5, ...}], "Message": null}'></textarea>

        <br>
        <button id="run">Run</button>

        <div id="result"></div>

        <script>
            const runButton = document.getElementById('run');
            const resultDiv = document.getElementById('result');

            runButton.addEventListener('click', async () => {
                const body = {
                    customerCode: document.getElementById('customerCode').value,
                    listJson: document.getElementById('listJson').value,
                    detailJson: document.getElementById('detailJson').value
                };

                runButton.disabled = true;
                runButton.textContent = 'Running...';
                resultDiv.innerHTML = '';

                try {
                    const response = await fetch('/simulate', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(body)
                    });

                    const payload = await response.json();
                    const pre = document.createElement('pre');
                    pre.className = response.ok ? 'success' : 'error';
                    pre.textContent = JSON.stringify(payload, null, 2);
                    resultDiv.appendChild(pre);
                } catch (err) {
                    const pre = document.createElement('pre');
                    pre.className = 'error';
                    pre.textContent = String(err);
                    resultDiv.appendChild(pre);
                } finally {
                    runButton.disabled = false;
                    runButton.textContent = 'Run';
                }
            });
        </script>

        </body>
        </html>
        """;
}
