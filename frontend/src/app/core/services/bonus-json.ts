import { RegisterBonusRequest } from '../models/bonus';

export function parseBonusJson<T>(text: string): T {
  // Conserva el literal decimal antes de que JSON.parse lo convierta a number.
  const decimals = /("(?:monthlySalary|taxRate|value|grossAmount|taxAmount|netAmount)"\s*:\s*)(\d+(?:\.\d+)?)(?=\s*[,}\]])/g;
  return JSON.parse(text.replace(decimals, '$1"$2"')) as T;
}

export function serializeBonus(request: RegisterBonusRequest): string {
  const { value, ...fields } = request;
  if (!/^(0|[1-9]\d*)(\.\d+)?$/.test(value)) throw new Error('El valor decimal no es válido.');
  return `${JSON.stringify(fields).slice(0, -1)},"value":${value}}`;
}
