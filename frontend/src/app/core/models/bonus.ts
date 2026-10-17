export interface Employee {
  departmentCode: string;
  employeeNumber: number;
  fullName: string;
  hireDate: string;
  monthlySalary: string;
  taxRate: string;
}

export interface Bonus {
  bonusId: number;
  departmentCode: string;
  departmentName: string;
  employeeNumber: number;
  fullName: string;
  period: string;
  bonusTypeId: number;
  bonusTypeDescription: string;
  value: string;
  reasonCode: string;
  reasonDescription: string;
  stateCode: string;
  stateDescription: string;
  grossAmount: string;
  taxAmount: string;
  netAmount: string;
}

export interface BonusFilter {
  departmentCode?: string;
  employeeNumber?: number;
  period?: string;
  stateCode?: string;
}

export interface RegisterBonusRequest {
  departmentCode: string;
  employeeNumber: number;
  period: string;
  bonusTypeId: number;
  value: string;
  reasonCode: string;
}
