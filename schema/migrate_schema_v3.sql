-- ===========================================================================
-- migrate_schema_v3.sql
-- ---------------------------------------------------------------------------
-- Cambia el UNIQUE de `pagos_procesados.codigo_autorizacion_banco` (que era
-- por columna sola) por uno compuesto sobre (codigo_autorizacion_banco,
-- id_recibo).
--
-- Razón: un solo pago bancario se distribuye FIFO entre todos los recibos
-- pendientes del contador (lo hace AplicarPagoAsync en EnergiaService.cs).
-- Eso genera N filas en `pagos_procesados` con el MISMO
-- codigo_autorizacion_banco (el `idDebito` del banco) pero distinto
-- id_recibo. El UNIQUE anterior bloqueaba la 2.ª, 3.ª, … filas con error
-- 1062 "Duplicate entry", la transacción de EF hacía rollback y el callback
-- del banco terminaba respondiendo 500. El banco silenciaba esa excepción
-- (`notificacionEnviada=false`) y el portal acababa cobrando al cliente sin
-- actualizar el saldo en Energía.
--
-- Con el UNIQUE compuesto:
--   * Sigue siendo idempotente por recibo (el mismo `idDebito` no se puede
--     aplicar dos veces sobre el mismo recibo).
--   * Permite múltiples filas con la misma autorización ligadas a recibos
--     distintos, que es lo que el código ya hacía y la BD rechazaba.
--   * El chequeo de duplicado en ProcesarPagoExternoAsync
--     (`Pagos.FindAsync(p => p.CodigoAutorizacionBanco == referenciaBanco).Any()`)
--     sigue funcionando: detecta cualquier fila con esa autorización.
--
-- USO:
--   mysql -h <host> -u <user> -p api_energia < migrate_schema_v3.sql
--
-- IMPORTANTE: Hacer backup antes de correr este script en producción.
-- ===========================================================================

USE `api_energia`;

SET @sql_safe_updates_anterior := @@SQL_SAFE_UPDATES;
SET SQL_SAFE_UPDATES = 0;

-- ---------------------------------------------------------------------------
-- 1. Quitar el UNIQUE viejo si todavía existe.
-- ---------------------------------------------------------------------------
SET @ix_viejo := (
    SELECT COUNT(*) FROM information_schema.STATISTICS
     WHERE TABLE_SCHEMA = 'api_energia'
       AND TABLE_NAME   = 'pagos_procesados'
       AND INDEX_NAME   = 'uk_pagos_procesados_codigo_autorizacion'
);
SET @sql := IF(@ix_viejo > 0,
    'ALTER TABLE `pagos_procesados` DROP INDEX `uk_pagos_procesados_codigo_autorizacion`',
    'SELECT "uk_pagos_procesados_codigo_autorizacion ya no existe" AS info'
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------------
-- 2. Crear el UNIQUE nuevo (compuesto) si todavía no existe.
-- ---------------------------------------------------------------------------
SET @ix_nuevo := (
    SELECT COUNT(*) FROM information_schema.STATISTICS
     WHERE TABLE_SCHEMA = 'api_energia'
       AND TABLE_NAME   = 'pagos_procesados'
       AND INDEX_NAME   = 'uk_pagos_procesados_autorizacion_recibo'
);
SET @sql := IF(@ix_nuevo = 0,
    'ALTER TABLE `pagos_procesados` ADD UNIQUE KEY `uk_pagos_procesados_autorizacion_recibo` (`codigo_autorizacion_banco`, `id_recibo`)',
    'SELECT "uk_pagos_procesados_autorizacion_recibo ya existe" AS info'
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------------
-- 3. Índice secundario simple sobre `codigo_autorizacion_banco` para que las
--    consultas de idempotencia (`WHERE codigo_autorizacion_banco = ?`) sigan
--    siendo O(log n). El UNIQUE compuesto puede usarse por prefijo, pero
--    dejamos uno explícito por claridad.
-- ---------------------------------------------------------------------------
SET @ix_simple := (
    SELECT COUNT(*) FROM information_schema.STATISTICS
     WHERE TABLE_SCHEMA = 'api_energia'
       AND TABLE_NAME   = 'pagos_procesados'
       AND INDEX_NAME   = 'ix_pagos_procesados_codigo_autorizacion'
);
SET @sql := IF(@ix_simple = 0,
    'ALTER TABLE `pagos_procesados` ADD INDEX `ix_pagos_procesados_codigo_autorizacion` (`codigo_autorizacion_banco`)',
    'SELECT "ix_pagos_procesados_codigo_autorizacion ya existe" AS info'
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET SQL_SAFE_UPDATES = @sql_safe_updates_anterior;

-- ---------------------------------------------------------------------------
-- Verificación final
-- ---------------------------------------------------------------------------
SELECT INDEX_NAME, NON_UNIQUE, GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) AS columnas
FROM   information_schema.STATISTICS
WHERE  TABLE_SCHEMA = 'api_energia'
  AND  TABLE_NAME   = 'pagos_procesados'
GROUP  BY INDEX_NAME, NON_UNIQUE
ORDER  BY INDEX_NAME;
