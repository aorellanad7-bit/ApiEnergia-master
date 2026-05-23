-- ===========================================================================
-- cleanup_energia.sql
-- ---------------------------------------------------------------------------
-- Vacía datos transaccionales y maestros para repetir flujos completos.
-- Mantiene la estructura de tablas (DROP TABLE NO se ejecuta).
--
-- Orden de TRUNCATE pensado para FK; FOREIGN_KEY_CHECKS=0 lo refuerza.
-- ¡USAR SOLO EN AMBIENTES DE PRUEBA!
-- ===========================================================================

USE `api_energia`;

SET FOREIGN_KEY_CHECKS = 0;

-- Datos transaccionales
TRUNCATE TABLE `pagos_procesados`;
TRUNCATE TABLE `recibo_luz`;
TRUNCATE TABLE `lectura_contador`;

-- Datos maestros (los re-creamos vía seed_energia.sql)
TRUNCATE TABLE `usuario_acceso_energia`;
TRUNCATE TABLE `contador_energia`;
TRUNCATE TABLE `cliente_luz`;

SET FOREIGN_KEY_CHECKS = 1;

-- Después de este script:
--   1) Si la BD es nueva o quieres asegurar el schema → schema_energia.sql
--   2) Insertar datos de prueba                       → seed_energia.sql
