(function(root) {
  'use strict';
  const finite = (v, fallback) => Number.isFinite(Number(v)) ? Number(v) : fallback;
  const clamp = (v, min, max) => Math.min(max, Math.max(min, finite(v, min)));
  function probabilities(count, preference = 1) {
    count = Math.round(clamp(count, 1, 32)); preference = clamp(preference, 0, 16);
    if (count === 1) return [1];
    const total = preference + count - 1;
    return Array.from({length: count}, (_, i) => (i === 0 ? preference : 1) / total);
  }
  function entropy(values) {
    const weights = values.map(v => Math.max(0, finite(v, 0)));
    const total = weights.reduce((a, b) => a + b, 0);
    if (!total) return 0;
    return Math.max(0, -weights.reduce((sum, value) => { const p = value / total; return sum + (p ? p * Math.log2(p) : 0); }, 0));
  }
  function layout(distance, clutter, sensitivity) {
    const relation = 2 * clamp(distance, 1, 10);
    const noise = clamp(clutter, 0, 10) * clamp(sensitivity, 0, 5);
    return { relation, noise, total: relation + noise };
  }
  function throughput(productivity, count, acceptance, attention) {
    return clamp(productivity, .1, 10) * Math.round(clamp(count, 1, 16)) * clamp(acceptance, 0, 1) / clamp(attention, .1, 10);
  }
  root.HelpModels = Object.freeze({probabilities, entropy, layout, throughput, clamp});
})(globalThis);
