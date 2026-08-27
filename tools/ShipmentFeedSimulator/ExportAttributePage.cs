namespace ShipmentFeedSimulator;

/// <summary>The export attribute feed's simulator page - one call, pasted, run.</summary>
internal static class ExportAttributePage
{
    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <title>Export Attribute Feed Simulator</title>
        <style>
            body { font-family: system-ui, sans-serif; max-width: 900px; margin: 2rem auto; padding: 0 1rem; color: #1a1a1a; }
            h1 { font-size: 1.3rem; }
            h2 { font-size: 1.05rem; margin-top: 2rem; border-top: 1px solid #ddd; padding-top: 1.25rem; }
            .warning { background: #fff3cd; border: 1px solid #f0c36d; border-radius: 6px; padding: 0.75rem 1rem; margin-bottom: 1.5rem; }
            label { display: block; font-weight: 600; margin: 1rem 0 0.25rem; }
            textarea { width: 100%; box-sizing: border-box; font-family: ui-monospace, Consolas, monospace; font-size: 0.85rem; padding: 0.5rem; border: 1px solid #ccc; border-radius: 4px; height: 220px; resize: vertical; }
            button { margin-top: 1.25rem; padding: 0.6rem 1.5rem; font-size: 1rem; background: #2563eb; color: white; border: none; border-radius: 6px; cursor: pointer; }
            button:disabled { background: #93b4f0; cursor: not-allowed; }
            pre { background: #f5f5f5; border-radius: 6px; padding: 1rem; overflow-x: auto; white-space: pre-wrap; word-break: break-word; margin-top: 1rem; }
            .error { background: #fde8e8; border: 1px solid #f5b5b5; }
            .success { background: #e8f9ee; border: 1px solid #b5e8c8; }
            a { color: #2563eb; }
            code { background: #eee; padding: 0.1rem 0.35rem; border-radius: 4px; }
        </style>
        </head>
        <body>

        <h1>Export Attribute Feed Simulator</h1>
        <p><a href="/">&larr; Shipment Feed Simulator</a></p>
        <div class="warning">
            Writes directly into the real <code>LTS_Integration</code> database (LTS_Shipments) and
            re-runs KPI scoring for the matched shipment - same path the live poller uses, just fed
            from what you paste below instead of a real HTTP call. Only updates a shipment that
            already exists (matched by <code>ExportFileNumber</code> == <code>ReferenceNo</code>) -
            it never creates one.
        </div>

        <h2>GetLTSExportFileDetail</h2>
        <label for="detailJson">Response JSON &mdash; the array (or a single object)</label>
        <textarea id="detailJson" placeholder='[{"ExportFileNumber": "26RUA377", "ArrivalCustoms": "AC001", "ArrivalCustomsDesc": "Moscow", ...}]'></textarea>
        <button id="run">Run Simulation</button>
        <div id="result"></div>

        <script>
            const runButton = document.getElementById('run');

            runButton.addEventListener('click', async () => {
                const resultDiv = document.getElementById('result');
                resultDiv.innerHTML = '';
                runButton.disabled = true;
                runButton.textContent = 'Running...';

                try {
                    const response = await fetch('/simulate-export-attributes', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ detailJson: document.getElementById('detailJson').value })
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
                    runButton.textContent = 'Run Simulation';
                }
            });
        </script>

        </body>
        </html>
        """;
}
