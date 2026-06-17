// MathJax 3 configuration for Material for MkDocs + pymdownx.arithmatex (generic mode).
// arithmatex wraps math in elements with class "arithmatex"; we restrict typesetting to those
// so MathJax never mangles code blocks that contain '$' or backslashes.
window.MathJax = {
  tex: {
    inlineMath: [["\\(", "\\)"]],
    displayMath: [["\\[", "\\]"]],
    processEscapes: true,
    processEnvironments: true
  },
  options: {
    ignoreHtmlClass: ".*|",
    processHtmlClass: "arithmatex"
  }
};

// Material's `navigation.instant` swaps page content via XHR without a full reload, so MathJax
// must be re-run on every page change or new pages render raw TeX. `document$` is Material's hook.
document$.subscribe(() => {
  if (window.MathJax && MathJax.startup && MathJax.typesetPromise) {
    MathJax.startup.document.clear();
    MathJax.startup.document.updateDocument();
  }
});
