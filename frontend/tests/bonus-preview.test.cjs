const { test } = require('node:test');
const assert = require('node:assert/strict');
const { previewBonus, currentPeriod, money } = require('../.angular/tests/features/bonuses/bonus-preview.js');

for (const [salary, tax, type, value, expected] of [
  ['5000.00', '8', 2, '10', ['500.00', '40.00', '460.00']],
  ['5000.00', '8', 1, '0.125', ['0.13', '0.01', '0.12']],
  ['5000.00', '8', 2, '0.0001', ['0.01', '0.00', '0.01']],
  ['2500.00', '0', 1, '2500', ['2500.00', '0.00', '2500.00']],
  ['5000.00', '8', 2, '100', ['5000.00', '400.00', '4600.00']],
  ['99999999999999.99', '8', 2, '50.00000000000001', ['50000000000000.00', '4000000000000.00', '46000000000000.00']],
]) test(`Vista previa ${salary}, tipo ${type}, valor ${value}`, () => {
  assert.deepEqual(previewBonus(salary, tax, type, value), expected);
});

test('Valores inválidos no generan una vista previa', () => {
  for (const [type, value] of [[1, '0'], [1, '-1'], [1, '5000.00000000000001'], [2, '100.01'], [2, '1e2'], [2, '0.000000000000001'], [3, '10']])
    assert.equal(previewBonus('5000.00', '8', type, value), null);
});
test('Mes actual usa Lima antes y después de medianoche, incluso al cambiar de año', () => {
  assert.equal(currentPeriod(new Date('2026-11-01T04:59:59Z')), '2026-10');
  assert.equal(currentPeriod(new Date('2026-11-01T05:00:00Z')), '2026-11');
  assert.equal(currentPeriod(new Date('2027-01-01T04:59:59Z')), '2026-12');
  assert.equal(currentPeriod(new Date('2027-01-01T05:00:00Z')), '2027-01');
});
test('Formato monetario conserva céntimos fuera del rango exacto de number', () => {
  assert.equal(money('99999999999999.99'), '99,999,999,999,999.99');
});
