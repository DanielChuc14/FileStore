import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'formatBytes' })
export class FormatBytesPipe implements PipeTransform {
  private static readonly units = ['B', 'KB', 'MB', 'GB', 'TB'];

  transform(bytes: number | null | undefined): string {
    if (bytes === null || bytes === undefined) {
      return '-';
    }
    if (bytes === 0) {
      return '0 B';
    }

    // Base 1024: se usa la convencion binaria, que es la que espera ver
    // alguien mirando consumo de disco.
    const exponent = Math.min(
      Math.floor(Math.log(bytes) / Math.log(1024)),
      FormatBytesPipe.units.length - 1,
    );
    const value = bytes / Math.pow(1024, exponent);

    return `${value.toFixed(exponent === 0 ? 0 : 1)} ${FormatBytesPipe.units[exponent]}`;
  }
}
