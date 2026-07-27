import { readFileSync, readdirSync } from 'node:fs';
import { join, relative } from 'node:path';

/**
 * Guardian de las traducciones.
 *
 * ngx-translate no falla ante una clave inexistente: simplemente no renderiza
 * nada. Un boton se queda sin texto y nadie se entera hasta que alguien lo mira.
 * Ya paso: al quitar el modal de contraseñas del alta de clientes se borraron
 * `clients.copy` y `clients.passwordSaved`, que la pantalla de API Keys estaba
 * reutilizando, y sus dos botones quedaron vacios durante horas.
 *
 * Ni el build ni los tests de componente detectan eso, porque no es un error de
 * compilacion ni de comportamiento. Este spec si.
 */
describe('claves de i18n', () => {
  const projectRoot = process.cwd();
  const appDir = join(projectRoot, 'src', 'app');
  const translationsPath = join(projectRoot, 'public', 'i18n', 'es.json');

  /** Aplana el JSON anidado a claves con punto: 'clients.errors.emailTaken'. */
  function flatten(obj: Record<string, unknown>, prefix = ''): Set<string> {
    const keys = new Set<string>();

    for (const [key, value] of Object.entries(obj)) {
      const full = `${prefix}${key}`;

      if (value !== null && typeof value === 'object') {
        for (const nested of flatten(value as Record<string, unknown>, `${full}.`)) {
          keys.add(nested);
        }
      } else {
        keys.add(full);
      }
    }

    return keys;
  }

  function htmlFiles(dir: string): string[] {
    return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
      const path = join(dir, entry.name);
      if (entry.isDirectory()) return htmlFiles(path);
      return entry.name.endsWith('.html') ? [path] : [];
    });
  }

  /**
   * Solo se detectan las claves literales (`'algo.otro' | translate`). Una clave
   * armada en tiempo de ejecucion no se puede verificar de forma estatica; por
   * eso las que dependen del estado se resuelven con literales completos en la
   * plantilla, y no concatenando.
   */
  const usagePattern = /'([a-zA-Z][\w.]*)'\s*\|\s*translate/g;

  const available = flatten(JSON.parse(readFileSync(translationsPath, 'utf-8')));

  it('el archivo de traducciones tiene contenido', () => {
    expect(available.size).toBeGreaterThan(50);
  });

  it('toda clave usada en una plantilla existe en es.json', () => {
    const missing: string[] = [];

    for (const file of htmlFiles(appDir)) {
      const content = readFileSync(file, 'utf-8');

      for (const [, key] of content.matchAll(usagePattern)) {
        if (!available.has(key)) {
          missing.push(`${relative(projectRoot, file)} -> ${key}`);
        }
      }
    }

    expect(missing, `Claves usadas que no existen en es.json:\n${missing.join('\n')}`).toEqual([]);
  });

  it('ninguna traduccion esta vacia', () => {
    // Una cadena vacia se renderiza igual que una clave inexistente, asi que el
    // sintoma es el mismo aunque la causa sea otra.
    const translations = JSON.parse(readFileSync(translationsPath, 'utf-8'));
    const empty: string[] = [];

    function walk(obj: Record<string, unknown>, prefix = ''): void {
      for (const [key, value] of Object.entries(obj)) {
        const full = `${prefix}${key}`;

        if (value !== null && typeof value === 'object') {
          walk(value as Record<string, unknown>, `${full}.`);
        } else if (typeof value !== 'string' || value.trim() === '') {
          empty.push(full);
        }
      }
    }

    walk(translations);

    expect(empty, `Traducciones vacias:\n${empty.join('\n')}`).toEqual([]);
  });
});
