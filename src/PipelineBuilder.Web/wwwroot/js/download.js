// Browser helpers: download and copy, and the wizard kept in this browser between visits.
window.pipelineBuilder = {
  saveState: function (json) {
    try { localStorage.setItem('pipelinebuilder-state', json); } catch (e) { /* not kept */ }
  },
  loadState: function () {
    try { return localStorage.getItem('pipelinebuilder-state'); } catch (e) { return null; }
  },
  downloadText: function (fileName, content, mimeType) {
    const blob = new Blob([content], { type: mimeType || 'text/plain' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
  },
  copyText: async function (text) {
    if (!navigator.clipboard) { return false; }
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      return false;
    }
  }
};
