import { Component, Input, forwardRef, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { COLOR_PALETTE, ColorSwatch } from '../../color-palette';

// Selector de color para carpetas/proyectos: paleta curada de swatches
// (misma estetica que los colores fijos de la app) mas un swatch
// "Personalizado" que abre el <input type="color"> nativo del navegador,
// para quien quiera un tono fuera de la paleta. ControlValueAccessor,
// mismo patron que EditorTextoComponent, para poder usarse con
// formControlName o [(ngModel)].
@Component({
  selector: 'app-color-picker',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatTooltipModule],
  templateUrl: './color-picker.component.html',
  styleUrl: './color-picker.component.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => ColorPickerComponent),
      multi: true,
    },
  ],
})
export class ColorPickerComponent implements ControlValueAccessor {
  @Input() label = '';
  // Permite dejar la carpeta/proyecto sin color (Color es nullable).
  @Input() allowNone = true;

  palette = COLOR_PALETTE;

  value = signal<string | null>(null);
  disabled = signal(false);

  // Si el valor actual no esta en la paleta (vino del input nativo, o de
  // una edicion anterior con un hex fuera del set curado), se refleja
  // aca para que el swatch "Personalizado" se pinte con ese tono en vez
  // de aparecer vacio.
  customColor = signal<string | null>(null);

  private onChange: (value: string | null) => void = () => {};
  private onTouched: () => void = () => {};

  get enPaleta(): boolean {
    return this.value() === null || this.palette.some((c: ColorSwatch) => c.hex === this.value());
  }

  // --- ControlValueAccessor ---

  writeValue(value: string | null): void {
    this.value.set(value);
    if (value && !this.palette.some((c) => c.hex === value)) {
      this.customColor.set(value);
    }
  }

  registerOnChange(fn: (value: string | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  // --- Seleccion ---

  seleccionar(hex: string | null): void {
    if (this.disabled()) return;
    this.value.set(hex);
    this.onChange(hex);
    this.onTouched();
  }

  onCustomColorInput(event: Event): void {
    const hex = (event.target as HTMLInputElement).value;
    this.customColor.set(hex);
    this.seleccionar(hex);
  }
}
