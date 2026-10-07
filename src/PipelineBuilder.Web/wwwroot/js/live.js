// The file beside the form: when a setting changes it, bring the first changed line into view.
// The page marks those lines with the class "changed"; this only scrolls the pane, never the page.
(function () {
  var pending = false;
  function show() {
    pending = false;
    var first = document.querySelector('.yaml-live .line.changed');
    var pane = first && first.closest('.wizard-side');
    if (!pane || getComputedStyle(pane).position !== 'sticky') { return; } // on a phone the pane is part of the page
    var calm = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    pane.scrollTo({ top: Math.max(0, first.offsetTop - pane.clientHeight / 3), behavior: calm ? 'auto' : 'smooth' });
  }
  new MutationObserver(function (changes) {
    if (pending) { return; }
    for (var i = 0; i < changes.length; i++) {
      var target = changes[i].target;
      if (target instanceof Element && target.closest('.yaml-live')) {
        pending = true;
        requestAnimationFrame(show);
        return;
      }
    }
  }).observe(document.documentElement, { childList: true, subtree: true });
})();
