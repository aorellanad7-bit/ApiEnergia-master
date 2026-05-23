# Scripts SQL — API Energía

Scripts para administrar la base de datos `api_energia`. La fuente de verdad
para el schema es **`schema_energia.sql`**, alineado 1:1 con
`ApiEnergia.DbContext.EnergiaDbContext` y los modelos de la API.

## Archivos

| Archivo | Propósito |
| --- | --- |
| `schema_energia.sql` | **Schema canónico**. CREATE TABLE de toda la base con índices y FKs. Idempotente (`CREATE TABLE IF NOT EXISTS`). Úsalo en una BD vacía. |
| `migrate_schema_v2.sql` | **ALTER TABLE** para llevar una BD legacy (la del dump original `Dump20260522.sql`) al schema canónico, conservando datos. |
| `seed_energia.sql` | Inserta cliente, contador y usuarios de prueba con passwords BCrypt. Idempotente. |
| `cleanup_energia.sql` | TRUNCATE de pagos, recibos, lecturas y datos maestros. Mantiene tablas. |
| `migrate_passwords_a_bcrypt.sql` | Migra passwords plaintext conocidos a hashes BCrypt. |
| `Dump20260522.sql` | Snapshot histórico (legacy). **No usar para crear bases nuevas** — usar `schema_energia.sql` en su lugar. |

## Cuentas de prueba (tras `seed_energia.sql`)

| Usuario | Password | Rol |
| --- | --- | --- |
| `agencia` | `Admin123*` | `ADMIN_AGENCIA` |
| `2200000000101` | `Cliente123*` | `CLIENTE` |

> Los passwords están almacenados como hashes BCrypt (work factor 11). El work
> factor coincide con el de la API en runtime, así que login y migración
> automática funcionan sin desfase.

## Flujos típicos

### A) BD nueva (local o un App Service recién creado)

```bash
mysql -h <host> -u <user> -p < schema_energia.sql
mysql -h <host> -u <user> -p api_energia < seed_energia.sql
```

### B) BD existente con estructura legacy (la que está en Azure hoy)

```bash
# 1) Backup primero (siempre)
mysqldump -h <host> -u <user> -p api_energia > backup_$(date +%Y%m%d).sql

# 2) Migrar schema (renombra correo_electronico→correo, DATE→DATETIME,
#    arregla ENUMs, agrega UNIQUE en codigo_autorizacion_banco, índices)
mysql -h <host> -u <user> -p api_energia < migrate_schema_v2.sql

# 3) Migrar passwords plaintext conocidos a BCrypt
mysql -h <host> -u <user> -p api_energia < migrate_passwords_a_bcrypt.sql
```

### C) Pruebas E2E desde cero

```bash
mysql -h <host> -u <user> -p api_energia < cleanup_energia.sql
mysql -h <host> -u <user> -p api_energia < seed_energia.sql
```

## Convenciones de naming

* Columnas: `snake_case`.
* PK simple → `id_<tabla_singular>`. Ej.: `cliente_luz.id_cliente`.
* FK → `<tabla_referenciada>_<col>` mismo nombre que la PK referenciada.
* Constraints FK → `fk_<tabla_origen>__<tabla_referenciada>`.
* Índices secundarios → `ix_<tabla>_<campos>`.
* Únicos → `uk_<tabla>_<campos>`.
* ENUMs → valores **EXACTAMENTE** como los escribe el código C# (sensible al
  case en MySQL). Cambiar uno requiere cambiar el modelo o un converter.
