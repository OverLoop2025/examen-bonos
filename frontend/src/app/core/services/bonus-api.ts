import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Bonus, BonusFilter, Employee, RegisterBonusRequest } from '../models/bonus';
import { PagedResponse } from '../models/paged-response';

const decimalFields = new Set(['monthlySalary', 'taxRate', 'value', 'grossAmount', 'taxAmount', 'netAmount']);

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
    // Mantiene el decimal escrito como número JSON, sin convertirlo a number.
    const json = JSON as typeof JSON & { rawJSON(value: string): unknown };
    const body = JSON.stringify({ ...request, value: json.rawJSON(request.value) });
    return this.http.post(this.url, body, { headers: { 'Content-Type': 'application/json' } });
  }

  cancel(id: number) { return this.http.put<void>(`${this.url}/${id}/cancel`, null); }
  delete(id: number) { return this.http.delete<void>(`${this.url}/${id}`); }

  private read<T>(url: string, params: Record<string, string | number> = {}) {
    return this.http.get(url, { params, responseType: 'text' }).pipe(map(text =>
      JSON.parse(text, (key, value, context?: { source: string }) =>
        decimalFields.has(key) && typeof value === 'number' ? context!.source : value) as T));
  }
}
