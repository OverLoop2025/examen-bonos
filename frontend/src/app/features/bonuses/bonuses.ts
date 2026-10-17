import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { catchError, finalize, of, startWith, Subject, switchMap } from 'rxjs';
import { Bonus, BonusFilter } from '../../core/models/bonus';
import { BonusStates, BonusTypes, MasterStateCodes } from '../../core/models/catalog';
import { PagedResponse } from '../../core/models/paged-response';
import { BonusApi } from '../../core/services/bonus-api';
import { CatalogApi } from '../../core/services/catalog-api';
import { Notification } from '../../core/services/notification';
import { money } from './bonus-preview';
import { RegisterBonus } from './register-bonus';

@Component({
  selector: 'app-bonuses',
  imports: [ReactiveFormsModule, RegisterBonus],
  templateUrl: './bonuses.html',
  styleUrl: './bonuses.css',
})
export class Bonuses {
  private readonly catalogApi = inject(CatalogApi);
  private readonly api = inject(BonusApi);
  private readonly notification = inject(Notification);
  private readonly requests = new Subject<number>();
  private applied: BonusFilter = {};
  protected readonly form = inject(FormBuilder).nonNullable.group({
    departmentCode: '', employeeNumber: ['', [Validators.pattern(/^[1-9]\d*$/), Validators.max(2147483647)]],
    period: '', stateCode: '',
  });
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly registering = signal(false);
  protected readonly page = signal<PagedResponse<Bonus> | null>(null);
  protected readonly money = money;
  protected readonly states = BonusStates;
  protected readonly types = BonusTypes;
  protected percent(value: string) { return value.replace(/(\.\d*?)0+$/, '$1').replace(/\.$/, ''); }

  protected readonly departments = toSignal(this.catalogApi.getDepartments().pipe(catchError(() => of([]))), { initialValue: [] });
  protected readonly bonusStates = toSignal(this.catalogApi.getStates(MasterStateCodes.Bonus).pipe(catchError(() => of([]))), { initialValue: [] });

  constructor() {
    this.requests.pipe(startWith(1), switchMap(page => {
      this.loading.set(true);
      this.failed.set(false);
      this.page.set(null);
      return this.api.list(this.applied, page).pipe(
        catchError(() => { this.failed.set(true); return of(null); }),
        finalize(() => this.loading.set(false)),
      );
    }), takeUntilDestroyed()).subscribe(page => {
      if (page && page.pageNumber > Math.max(page.totalPages, 1)) this.load(Math.max(page.totalPages, 1));
      else this.page.set(page);
    });
  }

  protected search() {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;
    const values = this.form.getRawValue();
    this.applied = Object.fromEntries(Object.entries({ ...values,
      employeeNumber: values.employeeNumber ? Number(values.employeeNumber) : undefined,
    }).filter(([, value]) => value !== '' && value !== undefined));
    this.load(1);
  }

  protected clear() { this.form.reset(); this.search(); }
  protected load(page: number) { this.requests.next(page); }
  protected closeRegistration(saved: boolean) { this.registering.set(false); if (saved) this.load(1); }

  protected change(bonus: Bonus, cancel: boolean) {
    if (this.busy() || this.loading() || bonus.stateCode !== BonusStates.Registered) return;
    if (!window.confirm(`¿${cancel ? 'Anular' : 'Eliminar'} el bono de ${bonus.fullName} del periodo ${bonus.period}?`)) return;
    this.busy.set(true);
    (cancel ? this.api.cancel(bonus.bonusId) : this.api.delete(bonus.bonusId)).pipe(
      finalize(() => this.busy.set(false)),
    ).subscribe({
      next: () => { this.notification.show('success', cancel ? 'Bono anulado.' : 'Bono eliminado.'); this.load(this.page()?.pageNumber ?? 1); },
      error: () => this.load(this.page()?.pageNumber ?? 1),
    });
  }
}
