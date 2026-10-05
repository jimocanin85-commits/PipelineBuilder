// Light and dark. Loaded in the page head, so a theme chosen earlier is applied before the page is drawn.
// It is a file, not a script written into the page: the page's Content-Security-Policy only runs script files.
(function () {
  var key = 'pipelinebuilder-theme';
  var root = document.documentElement;
  try {
    var saved = localStorage.getItem(key);
    if (saved === 'light' || saved === 'dark') { root.dataset.theme = saved; }
  } catch (e) { /* no storage: follow the system */ }

  // The button in the header switches, starting from what the system uses, and the choice is remembered.
  document.addEventListener('click', function (event) {
    if (!(event.target instanceof Element) || !event.target.closest('.theme-toggle')) { return; }
    var dark = root.dataset.theme
      ? root.dataset.theme === 'dark'
      : window.matchMedia('(prefers-color-scheme: dark)').matches;
    root.dataset.theme = dark ? 'light' : 'dark';
    try { localStorage.setItem(key, root.dataset.theme); } catch (e) { /* not remembered */ }
  });
})();
