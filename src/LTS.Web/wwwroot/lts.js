// Hands a generated file to the browser. Blazor Server builds workbooks on the server, so the
// bytes arrive over the circuit as base64 and are turned into a download here.
window.ltsDownload = (fileName, base64) => {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);

    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }

    const blob = new Blob([bytes], {
        type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
    });

    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);

    // Revoking immediately can cancel the download in some browsers, so give it a moment.
    setTimeout(() => URL.revokeObjectURL(url), 5000);
};
