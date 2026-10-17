import { BonusTypes } from '../../core/models/catalog';

const scale = 10n ** 14n;

function decimal(value: string): bigint | null {
  if (!/^\d{1,14}(\.\d{1,14})?$/.test(value)) return null;
  const [whole, fraction = ''] = value.split('.');
  return BigInt(whole) * scale + BigInt(fraction.padEnd(14, '0'));
}

function round(numerator: bigint, denominator: bigint): bigint {
  return (numerator + denominator / 2n) / denominator;
}

function fixed(cents: bigint): string {
  return `${cents / 100n}.${(cents % 100n).toString().padStart(2, '0')}`;
}

export function previewBonus(salary: string, tax: string, type: number, value: string): string[] | null {
  const amount = decimal(value), monthlySalary = decimal(salary), taxRate = decimal(tax);
  if (amount === null || monthlySalary === null || taxRate === null || amount <= 0n || monthlySalary <= 0n ||
      taxRate > 100n * scale || (type !== BonusTypes.FixedAmount && type !== BonusTypes.SalaryPercentage)) return null;
  if (amount > (type === BonusTypes.FixedAmount ? monthlySalary : 100n * scale)) return null;
  const gross = type === BonusTypes.FixedAmount
    ? round(amount * 100n, scale)
    : round(monthlySalary * amount, scale * scale);
  const withheld = round(gross * taxRate, 100n * scale);
  return [fixed(gross), fixed(withheld), fixed(gross - withheld)];
}

export function currentPeriod(now = new Date()): string {
  const parts = new Intl.DateTimeFormat('en', { timeZone: 'America/Lima', year: 'numeric', month: '2-digit' }).formatToParts(now);
  return `${parts.find(p => p.type === 'year')!.value}-${parts.find(p => p.type === 'month')!.value}`;
}

export function money(value: string): string {
  const amount = decimal(value);
  if (amount === null) return '—';
  const [whole, fraction] = fixed(round(amount * 100n, scale)).split('.');
  return `${whole.replace(/\B(?=(\d{3})+(?!\d))/g, ',')}.${fraction}`;
}
