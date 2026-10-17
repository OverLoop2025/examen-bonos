import { AfterViewInit, Component, computed, DestroyRef, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { catchError, finalize, forkJoin, of, Subscription } from 'rxjs';
import { Employee } from '../../core/models/bonus';
import { BonusTypes, Department } from '../../core/models/catalog';
import { BonusApi } from '../../core/services/bonus-api';
import { CatalogApi } from '../../core/services/catalog-api';
import { Notification } from '../../core/services/notification';
import { currentPeriod, money, previewBonus } from './bonus-preview';

@Component({
  selector: 'app-register-bonus',
  imports: [ReactiveFormsModule],
  templateUrl: './register-bonus.html',
  styleUrl: './register-bonus.css',
})
export class RegisterBonus implements AfterViewInit {
  readonly departments = input.required<Department[]>();
  readonly closed = output<boolean>();
  private readonly api = inject(BonusApi);
  private readonly catalog = inject(CatalogApi);
  protected readonly notification = inject(Notification);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder).nonNullable;
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private lookupRequest?: Subscription;
  protected readonly lookup = this.fb.group({
    departmentCode: ['', Validators.required],
    employeeNumber: ['', [Validators.required, Validators.pattern(/^[1-9]\d*$/), Validators.max(2147483647)]],
  });
  protected readonly form = this.fb.group({
    period: [currentPeriod(), [Validators.required, Validators.pattern(/^\d{4}-(0[1-9]|1[0-2])$/)]],
    bonusTypeId: [BonusTypes.SalaryPercentage as number, Validators.required],
    value: ['', Validators.required], reasonCode: ['', Validators.required],
  });
  private readonly values = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly employee = signal<Employee | null>(null);
  protected readonly searching = signal(false);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  protected readonly money = money;
  protected readonly currentPeriod = currentPeriod;
  protected readonly types = BonusTypes;
  protected readonly catalogs = toSignal(forkJoin({
    types: this.catalog.getBonusTypes(), reasons: this.catalog.getBonusReasons(),
  }).pipe(catchError(() => of({ types: [], reasons: [] }))), { initialValue: null });
  protected readonly preview = computed(() => {
    this.values();
    const employee = this.employee(), { bonusTypeId, value } = this.form.getRawValue();
    return employee ? previewBonus(employee.monthlySalary, employee.taxRate, bonusTypeId, this.normalize(value)) : null;
  });
  protected get invalidPeriod() {
    const period = this.form.controls.period.value;
    return this.form.controls.period.invalid || period < '0001-01' || period > currentPeriod();
  }

  constructor() {
    this.notification.clear();
    this.lookup.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.lookupRequest?.unsubscribe();
      this.employee.set(null);
      this.searching.set(false);
    });
  }

  ngAfterViewInit() { this.dialog().nativeElement.showModal(); }

  protected search() {
    this.lookup.markAllAsTouched();
    if (this.lookup.invalid || this.searching() || this.saving()) return;
    this.notification.clear();
    this.employee.set(null);
    this.searching.set(true);
    const { departmentCode, employeeNumber } = this.lookup.getRawValue();
    this.lookupRequest = this.api.employee(departmentCode, Number(employeeNumber)).pipe(
      finalize(() => this.searching.set(false)), takeUntilDestroyed(this.destroyRef),
    ).subscribe({ next: employee => this.employee.set(employee), error: () => {} });
  }

  protected save() {
    this.submitted.set(true);
    this.form.markAllAsTouched();
    const employee = this.employee(), catalogs = this.catalogs();
    if (this.saving() || this.searching() || this.form.invalid || this.invalidPeriod || !this.preview() || !employee ||
        !catalogs?.reasons.some(reason => reason.reasonCode === this.form.controls.reasonCode.value)) return;
    const { period, bonusTypeId, value, reasonCode } = this.form.getRawValue();
    this.notification.clear();
    this.saving.set(true);
    this.api.register({ departmentCode: employee.departmentCode, employeeNumber: employee.employeeNumber,
      period, bonusTypeId, value: this.normalize(value), reasonCode,
    }).pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.notification.show('success', 'Bono registrado.'); this.close(true); },
      error: () => {},
    });
  }

  protected close(saved = false) {
    if (this.saving() && !saved) return;
    this.dialog().nativeElement.close();
    this.closed.emit(saved);
  }

  protected escape(event: Event) { event.preventDefault(); this.close(); }
  private normalize(value: string) { return value.trim().replace(',', '.').replace(/^0+(?=\d)/, ''); }
}
