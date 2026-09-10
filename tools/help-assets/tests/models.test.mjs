import test from 'node:test';
import assert from 'node:assert/strict';
import '../../../Host/MyAvaloniaManagement/HelpWeb/demo-models.js';
const {probabilities,entropy,layout,throughput}=globalThis.HelpModels;
test('Entropy uses normalized probabilities and the zero-probability convention',()=>{
  assert.equal(entropy(probabilities(16)),4);
  assert.equal(entropy(probabilities(4)),2);
  assert.equal(entropy([1,0]),0);
  assert.equal(entropy([0,0]),0);
  assert.equal(entropy([2,2]),1);
  assert.ok(entropy(probabilities(4,8))<2);
  for(let n=1;n<=32;n++)assert.ok(Math.abs(probabilities(n,0).reduce((a,b)=>a+b,0)-1)<1e-12);
});
test('Layout sensitivity and distance affect their own terms',()=>{
  assert.deepEqual(layout(6,4,1),{relation:12,noise:4,total:16});
  assert.equal(layout(6,10,0).noise,0);
  assert.equal(layout(1,4,1).total,6);
});
test('Illustrative throughput handles zero acceptance and invalid denominators',()=>{
  assert.equal(throughput(2,4,.8,2),3.2);
  assert.equal(throughput(2,4,0,2),0);
  assert.ok(Number.isFinite(throughput(2,4,.8,0)));
  assert.ok(Number.isFinite(throughput(NaN,Infinity,NaN,NaN)));
});
