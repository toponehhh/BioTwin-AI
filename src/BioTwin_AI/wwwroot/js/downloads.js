(function () {
    window.bioTwinDownloads = {
        async downloadFileFromStream(fileName, contentType, streamReference) {
            const arrayBuffer = await streamReference.arrayBuffer();
            const blob = new Blob([arrayBuffer], { type: contentType || "application/octet-stream" });
            const url = URL.createObjectURL(blob);
            const anchor = document.createElement("a");

            anchor.href = url;
            anchor.download = fileName || "download";
            anchor.style.display = "none";
            document.body.appendChild(anchor);
            anchor.click();
            anchor.remove();
            URL.revokeObjectURL(url);
        }
    };
})();
