import {
  AfterViewInit,
  Component,
  ElementRef,
  Input,
  ViewChild,
  forwardRef,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDividerModule } from '@angular/material/divider';

// Editor de texto enriquecido minimo: negrita, itálica, subrayado,
// tamaño (via bloques semanticos), alineacion y listas. Nada mas.
//
// Se implemento a mano en vez de meter Quill/TipTap porque el set de
// funciones es chico y el bundle ya viene grande; misma logica que el
// grafico de dona hecho con SVG inline.
//
// Usa document.execCommand: esta deprecado, pero sigue implementado en
// todos los navegadores y es la unica forma razonable de hacer esto sin
// escribir un motor de edicion sobre Range/Selection a mano. Si algun
// dia deja de funcionar, el reemplazo natural es cambiar este
// componente por una libreria: al ser ControlValueAccessor, los
// formularios que lo usan no se enterarian.
//
// Guarda HTML. Quien lo muestre debe usar [innerHTML] (Angular
// sanitiza por default y quita scripts/handlers) o el pipe textoPlano
// si solo quiere una vista previa en texto.
@Component({
  selector: 'app-editor-texto',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatButtonModule, MatTooltipModule, MatDividerModule],
  templateUrl: './editor-texto.component.html',
  styleUrl: './editor-texto.component.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => EditorTextoComponent),
      multi: true,
    },
  ],
})
export class EditorTextoComponent implements ControlValueAccessor, AfterViewInit {
  @Input() label = '';
  @Input() placeholder = '';

  @ViewChild('editor') editorRef!: ElementRef<HTMLDivElement>;

  disabled = signal(false);
  enfocado = signal(false);
  vacio = signal(true);

  // Estado de los botones (que formato tiene el texto donde esta el
  // cursor), para poder resaltarlos en la barra.
  estados = signal<Record<string, boolean>>({});
  bloqueActual = signal<'p' | 'h3' | 'h4'>('p');

  private onChange: (value: string | null) => void = () => {};
  private onTouched: () => void = () => {};

  // writeValue puede llegar antes de que el ViewChild exista (pasa
  // cuando el form ya trae valor al construirse). Se guarda aca y se
  // aplica en ngAfterViewInit, si no el valor inicial se perderia.
  private valorPendiente: string | null = null;

  ngAfterViewInit(): void {
    if (this.valorPendiente !== null) {
      this.editorRef.nativeElement.innerHTML = this.valorPendiente;
      this.valorPendiente = null;
    }
  }

  // --- ControlValueAccessor ---

  writeValue(value: string | null): void {
    const html = value ?? '';

    if (!this.editorRef) {
      this.valorPendiente = html;
    } else if (this.editorRef.nativeElement.innerHTML !== html) {
      // Solo se toca el DOM si el valor de afuera difiere del actual:
      // reasignar innerHTML mientras se escribe manda el cursor al inicio.
      this.editorRef.nativeElement.innerHTML = html;
    }

    this.vacio.set(this.esVacio(html));
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

  // --- Edicion ---

  // preventDefault en mousedown: sin esto el boton roba el foco y se
  // pierde la seleccion sobre la que hay que aplicar el formato.
  onToolbarMouseDown(event: MouseEvent): void {
    event.preventDefault();
  }

  aplicar(comando: string, valor?: string): void {
    if (this.disabled()) return;

    this.editorRef.nativeElement.focus();
    document.execCommand(comando, false, valor);
    this.emitirCambio();
    this.actualizarEstados();
  }

  cambiarBloque(tag: 'p' | 'h3' | 'h4'): void {
    // formatBlock genera HTML semantico (<p>/<h3>/<h4>) en vez de las
    // etiquetas <font> deprecadas que produce el comando fontSize.
    this.aplicar('formatBlock', `<${tag}>`);
    this.bloqueActual.set(tag);
  }

  onInput(): void {
    this.emitirCambio();
  }

  onBlur(): void {
    this.enfocado.set(false);
    this.onTouched();
  }

  onFocus(): void {
    this.enfocado.set(true);
  }

  actualizarEstados(): void {
    if (this.disabled()) return;

    this.estados.set({
      bold: this.queryState('bold'),
      italic: this.queryState('italic'),
      underline: this.queryState('underline'),
      insertUnorderedList: this.queryState('insertUnorderedList'),
      insertOrderedList: this.queryState('insertOrderedList'),
      justifyLeft: this.queryState('justifyLeft'),
      justifyCenter: this.queryState('justifyCenter'),
      justifyRight: this.queryState('justifyRight'),
    });

    const bloque = this.queryValue('formatBlock').toLowerCase();
    this.bloqueActual.set(bloque === 'h3' || bloque === 'h4' ? bloque : 'p');
  }

  private queryState(comando: string): boolean {
    try {
      return document.queryCommandState(comando);
    } catch {
      return false;
    }
  }

  private queryValue(comando: string): string {
    try {
      return document.queryCommandValue(comando) || '';
    } catch {
      return '';
    }
  }

  private emitirCambio(): void {
    const html = this.editorRef.nativeElement.innerHTML;
    const vacio = this.esVacio(html);
    this.vacio.set(vacio);
    // Vacio se reporta como null y no como "<br>" o "<p></p>", que es lo
    // que deja contenteditable al borrar todo: asi el backend recibe
    // null igual que cuando el campo nunca se llenó.
    this.onChange(vacio ? null : html);
  }

  private esVacio(html: string): boolean {
    if (!html) return true;
    const doc = new DOMParser().parseFromString(html, 'text/html');
    return (doc.body.textContent ?? '').trim().length === 0;
  }
}
