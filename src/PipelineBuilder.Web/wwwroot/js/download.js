// Browser helpers: download and copy for the Result step, and the light/dark button in the header.
window.pipelineBuilder = {
  // Switches between light and dark, starting from what the system uses, and remembers the choice.
  toggleTheme: function () {
    const root = document.documentElement;
    const dark = root.dataset.theme
      ? root.dataset.theme === 'dark'
      : window.matchMedia('(prefers-color-scheme: dark)').matches;
    root.dataset.theme = dark ? 'light' : 'dark';
    try { localStorage.setItem('pipelinebuilder-theme', root.dataset.theme); } catch (e) { }
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
