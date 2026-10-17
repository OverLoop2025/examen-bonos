import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Bonus, BonusFilter, Employee, RegisterBonusRequest } from '../models/bonus';
import { PagedResponse } from '../models/paged-response';
import { parseBonusJson, serializeBonus } from './bonus-json';

@Injectable({ providedIn: 'root' })
export class BonusApi {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/Bonus`;

  list(filter: BonusFilter, pageIndex: number) {
    return this.read<PagedResponse<Bonus>>(this.url, { ...filter, pageIndex, pageSize: 10 });
  }

  employee(departmentCode: string, employeeNumber: number) {
    return this.read<Employee>(`${this.url}/employee/${encodeURIComponent(departmentCode)}/${employeeNumber}`);
  }

  register(request: RegisterBonusRequest) {
    return this.http.post(this.url, serializeBonus(request), { headers: { 'Content-Type': 'application/json' } });
  }

  cancel(id: number) { return this.http.put<void>(`${this.url}/${id}/cancel`, null); }
  delete(id: number) { return this.http.delete<void>(`${this.url}/${id}`); }
  pay(id: number) { return this.http.post<void>(`${this.url}/${id}/pay`, null); }

  private read<T>(url: string, params: Record<string, string | number> = {}) {
    return this.http.get(url, { params, responseType: 'text' }).pipe(map(text => parseBonusJson<T>(text)));
  }
}
