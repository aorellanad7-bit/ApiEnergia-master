-- ===========================================================================
-- migrate_schema_v2.sql
-- ---------------------------------------------------------------------------
-- Lleva una base `api_energia` con la estructura LEGACY (la del
-- Dump20260522.sql original) al schema canónico que espera el DbContext
-- actualizado. Conserva los datos existentes.
--
-- Cambios cubiertos:
--   1. cliente_luz.correo_electronico  → cliente_luz.correo VARCHAR(150)
--   2. lectura_contador.fecha_lectura  DATE → DATETIME
--   3. recibo_luz.fecha_emision        DATE → DATETIME
--   4. recibo_luz.estado               ENUM('PENDIENTE','PAGADO','VENCIDO')
--                                       → ENUM('Pendiente','Pagado','Vencido')
--   5. pagos_procesados.codigo_autorizacion_banco → UNIQUE (idempotencia)
--   6. Índices secundarios faltantes
--
-- USO:
--   mysql -h <host> -u <user> -p api_energia < migrate_schema_v2.sql
--
-- IMPORTANTE: Hacer backup antes de correr este script en producción.
-- ===========================================================================

USE `api_energia`;

-- ---------------------------------------------------------------------------
-- 1. Renombrar cliente_luz.correo_electronico → correo (y ampliar a 150)
-- ---------------------------------------------------------------------------
SET @col_existe := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
     WHERE TABLE_SCHEMA = 'api_energia'
       AND TABLE_NAME   = 'cliente_luz'
       AND COLUMN_NAME  = 'correo_electronico'
);
SET @sql := IF(@col_existe > 0,
    'ALTER TABLE `cliente_luz` CHANGE COLUMN `correo_electronico` `correo` VARCHAR(150) NOT NULL',
    'SELECT "cliente_luz.correo_electronico ya migrado, se omite" AS info'
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- Si la columna ya se llamaba `correo` pero con menor longitud, la ampliamos
ALTER TABLE `cliente_luz`
    MODIFY COLUMN `correo` VARCHAR(150) NOT NULL;

-- ---------------------------------------------------------------------------
-- 2. lectura_contador.fecha_lectura  DATE → DATETIME
-- ---------------------------------------------------------------------------
ALTER TABLE `lectura_contador`
    MODIFY COLUMN `fecha_lectura` DATETIME NOT NULL;

-- ---------------------------------------------------------------------------
-- 3. recibo_luz.fecha_emision  DATE → DATETIME
-- ---------------------------------------------------------------------------
ALTER TABLE `recibo_luz`
    MODIFY COLUMN `fecha_emision` DATETIME NOT NULL;

-- ---------------------------------------------------------------------------
-- 4. recibo_luz.estado: convertir valores existentes y migrar el ENUM
-- ---------------------------------------------------------------------------
-- Paso 4a: relajar a VARCHAR para poder reescribir los valores
ALTER TABLE `recibo_luz`
    MODIFY COLUMN `estado` VARCHAR(20) NOT NULL DEFAULT 'Pendiente';

-- Paso 4b: pasar a PascalCase
UPDATE `recibo_luz` SET `estado` = 'Pendiente' WHERE `estado` IN ('PENDIENTE','pendiente');
UPDATE `recibo_luz` SET `estado` = 'Pagado'    WHERE `estado` IN ('PAGADO','pagado');
UPDATE `recibo_luz` SET `estado` = 'Vencido'   WHERE `estado` IN ('VENCIDO','vencido');

-- Paso 4c: volver a ENUM con los valores que espera EF Core
ALTER TABLE `recibo_luz`
    MODIFY COLUMN `estado`
    ENUM('Pendiente','Pagado','Vencido') NOT NULL DEFAULT 'Pendiente';

-- ---------------------------------------------------------------------------
-- 5. pagos_procesados.codigo_autorizacion_banco UNIQUE
-- ---------------------------------------------------------------------------
SET @ix_existe := (
    SELECT COUNT(*) FROM information_schema.STATISTICS
     WHERE TABLE_SCHEMA = 'api_energia'
       AND TABLE_NAME   = 'pagos_procesados'
       AND INDEX_NAME   = 'uk_pagos_procesados_codigo_autorizacion'
);
SET @sql := IF(@ix_existe = 0,
    'ALTER TABLE `pagos_procesados` ADD UNIQUE KEY `uk_pagos_procesados_codigo_autorizacion` (`codigo_autorizacion_banco`)',
    'SELECT "pagos_procesados unique de autorización ya existe" AS info'
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------------
-- 6. Índices secundarios faltantes (idempotente)
-- ---------------------------------------------------------------------------
-- Helper: solo crea el índice si no existe
DROP PROCEDURE IF EXISTS `__crear_index_si_falta`;
DELIMITER //
CREATE PROCEDURE `__crear_index_si_falta`(
    IN p_tabla VARCHAR(64),
    IN p_index VARCHAR(64),
    IN p_cols  VARCHAR(255)
)
BEGIN
    DECLARE existe INT DEFAULT 0;
    SELECT COUNT(*) INTO existe
      FROM information_schema.STATISTICS
     WHERE TABLE_SCHEMA = 'api_energia'
       AND TABLE_NAME   = p_tabla
       AND INDEX_NAME   = p_index;

    IF existe = 0 THEN
        SET @sql := CONCAT('CREATE INDEX `', p_index, '` ON `', p_tabla, '` (', p_cols, ')');
        PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
    END IF;
END //
DELIMITER ;

CALL `__crear_index_si_falta`('lectura_contador',  'ix_lectura_contador_fecha',           '`fecha_lectura`');
CALL `__crear_index_si_falta`('recibo_luz',        'ix_recibo_luz_estado',                '`estado`');

DROP PROCEDURE `__crear_index_si_falta`;

-- ---------------------------------------------------------------------------
-- Verificación final
-- ---------------------------------------------------------------------------
SELECT 'Migración v2 aplicada. Verificar columnas y enums:' AS resultado;

SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE
  FROM information_schema.COLUMNS
 WHERE TABLE_SCHEMA = 'api_energia'
   AND ((TABLE_NAME = 'cliente_luz'      AND COLUMN_NAME LIKE 'correo%')
     OR (TABLE_NAME = 'lectura_contador' AND COLUMN_NAME = 'fecha_lectura')
     OR (TABLE_NAME = 'recibo_luz'       AND COLUMN_NAME IN ('fecha_emision','estado')))
 ORDER BY TABLE_NAME, COLUMN_NAME;
