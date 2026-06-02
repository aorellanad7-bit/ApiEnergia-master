-- Una sola lectura por contador y periodo (año + mes).
-- Ejecutar en MySQL de Azure/local si la tabla ya existía sin estas columnas.

ALTER TABLE `lectura_contador`
    ADD COLUMN `periodo_anio` INT NOT NULL DEFAULT 0 AFTER `fecha_lectura`,
    ADD COLUMN `periodo_mes` INT NOT NULL DEFAULT 0 AFTER `periodo_anio`;

UPDATE `lectura_contador`
SET
    `periodo_anio` = YEAR(`fecha_lectura`),
    `periodo_mes` = MONTH(`fecha_lectura`)
WHERE `periodo_anio` = 0 OR `periodo_mes` = 0;

-- Eliminar duplicados previos (conserva la lectura con id más bajo por contador/periodo).
DELETE l1 FROM `lectura_contador` l1
INNER JOIN `lectura_contador` l2
    ON l1.`numero_contador` = l2.`numero_contador`
   AND l1.`periodo_anio` = l2.`periodo_anio`
   AND l1.`periodo_mes` = l2.`periodo_mes`
   AND l1.`id_lectura` > l2.`id_lectura`;

ALTER TABLE `lectura_contador`
    ADD UNIQUE KEY `ux_lectura_contador_periodo` (`numero_contador`, `periodo_anio`, `periodo_mes`);
