import { Pipe, PipeTransform } from '@angular/core';

// Convierte el HTML que guarda el editor de texto en texto plano, para
// vistas previas donde renderizar titulos/listas rompería el layout
// (ej. las tarjetas de la lista de proyectos).
//
// Usa DOMParser y no un div temporal: el documento que crea es inerte
// (no ejecuta scripts ni carga recursos como <img src> con onerror),
// que importa porque este HTML lo escribio otro usuario.
@Pipe({ name: 'textoPlano', standalone: true })
export class TextoPlanoPipe implements PipeTransform {
  transform(html: string | null | undefined): string {
    if (!html) return '';

    const doc = new DOMParser().parseFromString(html, 'text/html');
    return (doc.body.textContent ?? '').replace(/\s+/g, ' ').trim();
  }
}
