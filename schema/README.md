# Scripts SQL — API Energía

Scripts para administrar la base de datos `api_energia`. La fuente de verdad
para el schema es **`schema_energia.sql`**, alineado 1:1 con
`ApiEnergia.DbContext.EnergiaDbContext`, los modelos de la API y el dump
vigente en producción.

## Archivos

| Archivo | Propósito |
| --- | --- |
| `schema_energia.sql` | **Schema canónico**. CREATE TABLE de toda la base con índices y FKs. Idempotente (`CREATE TABLE IF NOT EXISTS`). Úsalo en una BD vacía. |
| `seed_energia.sql` | Inserta cliente, contador y dos usuarios de prueba (admin + cliente) con passwords BCrypt. Idempotente. |
| `cleanup_energia.sql` | TRUNCATE de todas las tablas. La BD queda vacía pero la estructura permanece. |
| `cleanup_solo_admin.sql` | TRUNCATE + reinserción de **un único admin** (`agencia` / `Admin123*`). Útil para reiniciar el ambiente con credenciales conocidas. |
| `Dump20260528.sql` | Snapshot de la BD productiva (referencia). **No usar para crear bases nuevas** — usar `schema_energia.sql` en su lugar. |

## Cuentas de prueba (tras `seed_energia.sql`)

| Usuario | Password | Rol |
| --- | --- | --- |
| `agencia` | `Admin123*` | `ADMIN_AGENCIA` |
| `2200000000101` | `Cliente123*` | `CLIENTE` |

## Cuenta resultante de `cleanup_solo_admin.sql`

| Usuario | Password | Rol |
| --- | --- | --- |
| `agencia` | `Admin123*` | `ADMIN_AGENCIA` |

> Los passwords están almacenados como hashes BCrypt (work factor 11). El work
> factor coincide con el de la API en runtime, así que login y migración
> automática funcionan sin desfase.

## Flujos típicos

### A) BD nueva (local o un App Service recién creado)

```bash
mysql -h <host> -u <user> -p < schema_energia.sql
mysql -h <host> -u <user> -p api_energia < seed_energia.sql
```

### B) Reiniciar el ambiente con un único admin (tests E2E)

```bash
# Backup primero (siempre)
mysqldump -h <host> -u <user> -p api_energia > backup_$(date +%Y%m%d).sql

# Vaciar BD y dejar solo el admin
mysql -h <host> -u <user> -p api_energia < cleanup_solo_admin.sql
```

Tras ejecutar `cleanup_solo_admin.sql`, ingresa al portal de agencia con
`agencia` / `Admin123*` y crea los clientes y contadores que necesites a
través del API (`POST /api/Energia/Agencia/cliente`).

### C) Pruebas con clientes pre-cargados

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

## Estructura actual de la BD

Las migraciones legacy (`migrate_schema_v2`, `migrate_schema_v3`,
`migrate_passwords_a_bcrypt`) ya quedaron aplicadas en producción y se
removieron del repositorio para evitar confusiones. La estructura vigente
está documentada en `schema_energia.sql` y reflejada en `Dump20260528.sql`.

Cambios destacados respecto al dump original:

* `cliente_luz.correo` (antes `correo_electronico`).
* `lectura_contador.fecha_lectura` y `recibo_luz.fecha_emision` son
  `DATETIME` (antes `DATE`).
* `recibo_luz.estado` ENUM con `Pendiente`, `Pagado`, `Vencido` (antes
  ENUM con MAYÚSCULAS y sin `Vencido`).
* `contador_energia.estado` ENUM con `ACTIVO`, `CORTADO`, `SUSPENDIDO`.
* `pagos_procesados` con UNIQUE compuesto
  `(codigo_autorizacion_banco, id_recibo)` para idempotencia del callback
  bancario.
* Passwords almacenados como hashes BCrypt; el `AuthController` aún acepta
  plaintext legacy y lo re-hashea en el primer login exitoso.
