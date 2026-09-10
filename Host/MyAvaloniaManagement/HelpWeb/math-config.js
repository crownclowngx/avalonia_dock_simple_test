window.MathJax = {
  loader: { load: ['input/tex', 'output/svg'], paths: {
    mathjax: new URL('vendor/mathjax', document.baseURI).href,
    'mathjax-newcm': new URL('vendor/fonts/mathjax-newcm-font', document.baseURI).href
  } },
  output: { font: 'mathjax-newcm', fontPath: new URL('vendor/fonts/%%FONT%%', document.baseURI).href.replace('%25%25FONT%25%25', '%%FONT%%') },
  svg: { fontCache: 'local' },
  // Load only TeX and SVG components. The default combined bundle also starts a
  // speech worker whose module fetch is unavailable on file://.
  startup: { typeset: false }
};
