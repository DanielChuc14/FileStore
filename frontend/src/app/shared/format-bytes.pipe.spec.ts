import { FormatBytesPipe } from './format-bytes.pipe';

/**
 * El pipe es logica pura: entrada -> salida, sin dependencias. Se testea
 * instanciandolo directamente, sin TestBed. Es el equivalente frontend de un
 * test unitario.
 */
describe('FormatBytesPipe', () => {
  const pipe = new FormatBytesPipe();

  it('formatea cero', () => {
    expect(pipe.transform(0)).toBe('0 B');
  });

  it('usa base binaria (1024), no decimal', () => {
    // 1024 bytes son 1 KB, no 1.02 KB: la convencion binaria es la que espera
    // ver quien mira consumo de disco.
    expect(pipe.transform(1024)).toBe('1.0 KB');
    expect(pipe.transform(1048576)).toBe('1.0 MB');
    expect(pipe.transform(1073741824)).toBe('1.0 GB');
  });

  it('muestra un decimal en unidades mayores', () => {
    expect(pipe.transform(1536)).toBe('1.5 KB');
  });

  it('los bytes crudos van sin decimales', () => {
    expect(pipe.transform(512)).toBe('512 B');
  });

  it('null o undefined dan guion, no rompen', () => {
    // La tabla usa el pipe sobre valores que pueden faltar (carpetas sin
    // tamaño): debe degradar con elegancia, no lanzar.
    expect(pipe.transform(null)).toBe('-');
    expect(pipe.transform(undefined)).toBe('-');
  });
});
