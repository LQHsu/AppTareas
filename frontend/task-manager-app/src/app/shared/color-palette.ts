// Paleta curada para carpetas/proyectos: tonos bien distinguibles entre
// si y distintos de los --app-*/--estado-* ya fijos en styles.scss (para
// no confundir el color de una carpeta/proyecto con un badge de estado).
// Sin dependencias de Angular: se importa tanto en el color-picker como
// en cualquier card que solo necesite pintar un punto desde el hex
// guardado (no hace falta buscar el nombre para eso).
export interface ColorSwatch {
  name: string;
  hex: string;
}

export const COLOR_PALETTE: ColorSwatch[] = [
  { name: 'Azul', hex: '#3B82F6' },
  { name: 'Verde', hex: '#22A06B' },
  { name: 'Rojo', hex: '#E14B4B' },
  { name: 'Naranja', hex: '#E8792C' },
  { name: 'Amarillo', hex: '#D6A400' },
  { name: 'Morado', hex: '#8B5CF6' },
  { name: 'Rosa', hex: '#EC4899' },
  { name: 'Cian', hex: '#0EA5B7' },
  { name: 'Gris oscuro', hex: '#4B5563' },
  { name: 'Café', hex: '#8A5A34' },
  { name: 'Azul marino', hex: '#1E3A8A' },
  { name: 'Verde lima', hex: '#84B026' },
];

// Para pintar un color como fondo (ej. el titulo de una carpeta/proyecto)
// hace falta saber si el texto encima debe ir blanco o oscuro segun que
// tan claro/oscuro sea ese fondo - formula estandar de luminancia
// relativa (misma que usa WCAG para contraste).
// Color de fondo del banner superior de una card (carpeta/proyecto,
// estilo Google Classroom): si no se eligio ninguno, cae en el azul de
// marca de la app en vez de dejar la franja sin color - la idea es que
// la card SIEMPRE tenga un encabezado con color, elegido o no.
export function bannerColor(hex: string | null): string {
  return hex ?? '#2e5966';
}

export function contrastTextColor(hex: string): string {
  const r = parseInt(hex.slice(1, 3), 16) / 255;
  const g = parseInt(hex.slice(3, 5), 16) / 255;
  const b = parseInt(hex.slice(5, 7), 16) / 255;
  const luminancia = 0.2126 * r + 0.7152 * g + 0.0722 * b;
  return luminancia > 0.6 ? '#1a1f28' : '#ffffff';
}
