import { Component, Input, forwardRef } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

const pad = (n: number) => String(n).padStart(2, '0');

// Selector de fecha + hora sin escribir a mano: calendario (campo de solo
// lectura que abre el picker al hacer click) y una lista de horas cada 30
// minutos. Valor del formulario: "yyyy-MM-ddTHH:mm" en hora LOCAL (mismo
// formato que <input type="datetime-local">, asi notificarEnToIso() lo
// convierte igual), o '' si no hay fecha.
@Component({
  selector: 'app-fecha-hora-picker',
  standalone: true,
  imports: [
    MatFormFieldModule,
    MatInputModule,
    MatDatepickerModule,
    MatSelectModule,
    MatIconModule,
    MatButtonModule,
  ],
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => FechaHoraPickerComponent),
      multi: true,
    },
  ],
  templateUrl: './fecha-hora-picker.component.html',
  styleUrl: './fecha-hora-picker.component.scss',
})
export class FechaHoraPickerComponent implements ControlValueAccessor {
  @Input() label = '';
  @Input() hint = '';

  // Una fecha pasada no tiene sentido para programar un aviso.
  readonly hoy = new Date();
  readonly horas = Array.from({ length: 48 }, (_, i) => `${pad(Math.floor(i / 2))}:${i % 2 ? '30' : '00'}`);

  private static readonly HORA_DEFAULT = '09:00';

  fecha: Date | null = null;
  hora = FechaHoraPickerComponent.HORA_DEFAULT;
  disabled = false;

  private onChange: (v: string) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(value: string | null): void {
    if (!value) {
      this.fecha = null;
      this.hora = FechaHoraPickerComponent.HORA_DEFAULT;
      return;
    }
    const [dia, hora] = value.split('T');
    const [y, m, d] = dia.split('-').map(Number);
    this.fecha = new Date(y, m - 1, d);
    this.hora = hora?.slice(0, 5) || FechaHoraPickerComponent.HORA_DEFAULT;
  }

  // Fecha+hora ya vencida: el backend la trata como aviso inmediato (ver
  // TasksController.AvisarAsignacionAsync), asi que se avisa antes de crear.
  get enPasado(): boolean {
    if (!this.fecha) return false;
    const [h, m] = this.hora.split(':').map(Number);
    const f = this.fecha;
    return new Date(f.getFullYear(), f.getMonth(), f.getDate(), h, m).getTime() <= Date.now();
  }

  registerOnChange(fn: (v: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
  }

  onFecha(fecha: Date | null): void {
    this.fecha = fecha;
    this.emitir();
  }

  onHora(hora: string): void {
    this.hora = hora;
    this.emitir();
  }

  limpiar(): void {
    this.fecha = null;
    this.hora = FechaHoraPickerComponent.HORA_DEFAULT;
    this.emitir();
  }

  private emitir(): void {
    const f = this.fecha;
    this.onChange(f ? `${f.getFullYear()}-${pad(f.getMonth() + 1)}-${pad(f.getDate())}T${this.hora}` : '');
    this.onTouched();
  }
}
