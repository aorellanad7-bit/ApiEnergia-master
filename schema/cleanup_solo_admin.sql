-- ===========================================================================
-- cleanup_solo_admin.sql
-- ---------------------------------------------------------------------------
-- Vacía TODA la base `api_energia` y deja un único usuario administrador
-- (rol ADMIN_AGENCIA) listo para iniciar sesión en el portal de agencia.
--
-- Cuenta resultante:
--   * Usuario:  agencia
--   * Password: Admin123*
--   * Rol:      ADMIN_AGENCIA
--   * Cliente:  NULL  ← el admin no representa a un cuentahabiente real
--
-- IMPORTANTE: este script asume el schema actualizado donde
-- `usuario_acceso_energia.id_cliente` es NULLABLE. Si la base aún viene del
-- schema viejo (con id_cliente NOT NULL), el ALTER del paso 1 lo migra
-- automáticamente. Es idempotente.
--
-- ELIMINA todos los clientes, contadores, lecturas, recibos y pagos. Usar
-- solo en ambientes de prueba o en una reinicialización intencional.
--
-- USO:
--   mysql -h <host> -u <user> -p api_energia < cleanup_solo_admin.sql
-- ===========================================================================

USE `api_energia`;

SET FOREIGN_KEY_CHECKS = 0;

-- ── 1. Asegurar que id_cliente sea NULLABLE ───────────────────────────────
-- Idempotente: si la base ya tiene el schema actualizado este ALTER no
-- produce cambios. Permite ejecutar el script sobre bases existentes sin
-- recrearlas desde cero.
ALTER TABLE `usuario_acceso_energia`
    MODIFY COLUMN `id_cliente` INT DEFAULT NULL;

-- ── 2. Vaciar todas las tablas (orden seguro de FKs) ──────────────────────
TRUNCATE TABLE `pagos_procesados`;
TRUNCATE TABLE `recibo_luz`;
TRUNCATE TABLE `lectura_contador`;
TRUNCATE TABLE `usuario_acceso_energia`;
TRUNCATE TABLE `contador_energia`;
TRUNCATE TABLE `cliente_luz`;

-- ── 3. Único usuario ADMIN_AGENCIA ────────────────────────────────────────
-- Hash BCrypt (workFactor=11) pre-calculado para el password "Admin123*",
-- generado con la misma librería (BCrypt.Net-Next) que la API en runtime.
-- id_cliente=NULL porque el admin no es un cuentahabiente: ya no se inserta
-- ningún cliente "interno" y el listado del panel queda limpio.
INSERT INTO `usuario_acceso_energia`
    (`id_usuario`, `id_cliente`, `nombre_usuario`, `password_hash`, `rol`, `fecha_creacion`)
VALUES
    (1, NULL, 'agencia',
        '$2a$11$lQpsLq1CE.OPfqvgySp.VOlhLoSIrqnCsHBfDEefJYIrCTBKKx3p.',
        'ADMIN_AGENCIA', NOW());

SET FOREIGN_KEY_CHECKS = 1;

-- ── 4. Verificación final ─────────────────────────────────────────────────
SELECT 'cliente_luz'             AS tabla, COUNT(*) AS filas FROM `cliente_luz`
UNION ALL SELECT 'contador_energia',       COUNT(*)         FROM `contador_energia`
UNION ALL SELECT 'lectura_contador',       COUNT(*)         FROM `lectura_contador`
UNION ALL SELECT 'recibo_luz',             COUNT(*)         FROM `recibo_luz`
UNION ALL SELECT 'pagos_procesados',       COUNT(*)         FROM `pagos_procesados`
UNION ALL SELECT 'usuario_acceso_energia', COUNT(*)         FROM `usuario_acceso_energia`;

-- Resultado esperado:
--   cliente_luz             0   ← ¡vacío! El admin ya no necesita un cliente.
--   contador_energia        0
--   lectura_contador        0
--   recibo_luz              0
--   pagos_procesados        0
--   usuario_acceso_energia  1
