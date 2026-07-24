# Tests

Dos niveles de test en el backend, mas los de componentes en el frontend.

## Tests unitarios (`FileStore.UnitTests`)

Prueban logica pura, sin base ni red: reglas de nombres, generadores de claves y
API Keys, hashing, validadores de comandos. Corren en menos de un segundo.

```bash
dotnet test backend/tests/FileStore.UnitTests
```

No requieren ninguna preparacion.

## Tests de integracion (`FileStore.IntegrationTests`)

Levantan la API completa en memoria (`WebApplicationFactory`) contra una base
PostgreSQL real. Verifican el aislamiento entre clientes, el flujo de subida, la
reserva atomica de cuota bajo concurrencia y la descarga.

Se usa una base real y no una en memoria a proposito: la reserva de cuota, las
transacciones y los `jsonb` dependen del comportamiento de PostgreSQL, que una
base en memoria no ejecuta.

### Preparacion (una sola vez)

Crear la base y el rol de test:

```sql
-- Como superusuario (postgres)
CREATE ROLE filestore_test WITH LOGIN PASSWORD 'test_local_only';
CREATE DATABASE filestore_test OWNER filestore_test;
```

El rol y su password solo dan acceso a `filestore_test`, por eso pueden vivir en
el codigo. Las migraciones las aplica el propio test la primera vez que corre.

Para un Postgres en otro host o puerto, sobreescribir con la variable de entorno
`FILESTORE_TEST_CONNECTION`.

```bash
dotnet test backend/tests/FileStore.IntegrationTests
```

## Frontend (`vitest`)

```bash
cd frontend
npm test
```
