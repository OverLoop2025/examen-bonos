const { test } = require('node:test');
const assert = require('node:assert/strict');
const { parseBonusJson, serializeBonus } = require('../.angular/tests/core/services/bonus-json.js');

test('Lee sueldo y retención exactos sin depender del reviver moderno', () => {
  const employee = parseBonusJson('{"employeeNumber":101,"monthlySalary":99999999999999.99,"taxRate":8.00,"fullName":"Texto \\"grossAmount\\":123"}');
  assert.equal(employee.monthlySalary, '99999999999999.99');
  assert.equal(employee.taxRate, '8.00');
  assert.equal(employee.employeeNumber, 101);
  assert.equal(employee.fullName, 'Texto "grossAmount":123');
});

test('Lee todos los decimales del listado y conserva sus identificadores', () => {
  const page = parseBonusJson('{"totalRecords":1,"data":[{"bonusId":7,"value":50.00000000000001,"grossAmount":50000000000000.00,"taxAmount":4000000000000.00,"netAmount":46000000000000.00}]}');
  assert.deepEqual(page, { totalRecords: 1, data: [{ bonusId: 7, value: '50.00000000000001', grossAmount: '50000000000000.00', taxAmount: '4000000000000.00', netAmount: '46000000000000.00' }] });
});

test('Envía el valor exacto como número JSON sin JSON.rawJSON', () => {
  const body = serializeBonus({ departmentCode: 'TECH1', employeeNumber: 101, period: '2000-01', bonusTypeId: 1, value: '5000.00000000000001', reasonCode: '000001' });
  assert.ok(body.includes('"value":5000.00000000000001'));
  assert.equal(JSON.parse(body).employeeNumber, 101);
});

test('Los demás campos siguen escapándose con JSON estándar', () => {
  const body = serializeBonus({ departmentCode: 'Un "texto"', employeeNumber: 101, period: '2000-01', bonusTypeId: 1, value: '0.125', reasonCode: '000001' });
  assert.equal(JSON.parse(body).departmentCode, 'Un "texto"');
  assert.equal(JSON.parse(body).value, 0.125);
});

test('Rechaza una entrada que no sea un decimal antes de insertarla en JSON', () => {
  for (const value of ['NaN', '1e2', '01', '-1', '1,"stateCode":"0202"'])
    assert.throws(() => serializeBonus({ value }));
});
