-- ===========================================================================
-- cleanup_solo_admin.sql
-- ---------------------------------------------------------------------------
-- Vacía TODA la base `api_energia` y deja un único usuario administrador
-- (rol ADMIN_AGENCIA) listo para iniciar sesión en el portal de agencia.
--
-- A diferencia de cleanup_energia.sql (que solo trunca y deja la BD vacía)
-- este script:
--   1. Trunca todas las tablas en orden seguro de FK.
--   2. Inserta un cliente "interno" (id_cliente=100, dpi='0000000000000')
--      que sirve como soporte de la FK obligatoria de usuario_acceso_energia
--      (rol ADMIN_AGENCIA no representa un cuentahabiente real).
--   3. Inserta el único usuario administrador.
--
-- Cuenta resultante:
--   * Usuario:  agencia
--   * Password: Admin123*
--   * Rol:      ADMIN_AGENCIA
--
-- IMPORTANTE: Este script ELIMINA todos los clientes, contadores, lecturas,
-- recibos y pagos. ¡Usar solo en ambientes de prueba o en una reinicialización
-- intencional!
--
-- USO:
--   mysql -h <host> -u <user> -p api_energia < cleanup_solo_admin.sql
-- ===========================================================================

USE `api_energia`;

SET FOREIGN_KEY_CHECKS = 0;

-- ── 1. Vaciar todas las tablas ────────────────────────────────────────────
-- Orden: primero las tablas hijas (con FKs salientes), luego las padre.
TRUNCATE TABLE `pagos_procesados`;
TRUNCATE TABLE `recibo_luz`;
TRUNCATE TABLE `lectura_contador`;
TRUNCATE TABLE `usuario_acceso_energia`;
TRUNCATE TABLE `contador_energia`;
TRUNCATE TABLE `cliente_luz`;

-- ── 2. Cliente "interno" para soportar el FK del admin ───────────────────
-- usuario_acceso_energia.id_cliente es NOT NULL con FK a cliente_luz, así
-- que aunque el admin no representa a un cuentahabiente real, necesita un
-- registro en cliente_luz al cual referirse.
INSERT INTO `cliente_luz`
    (`id_cliente`, `dpi`, `nombre`, `apellido`, `correo`)
VALUES
    (100, '0000000000000', 'Empresa', 'Energía', 'agencia@energia.local');

-- ── 3. Único usuario ADMIN_AGENCIA ────────────────────────────────────────
-- Hash BCrypt (workFactor=11) pre-calculado para el password "Admin123*",
-- generado con la misma librería (BCrypt.Net-Next) que la API en runtime.
-- Coincide con la API tras los cambios:
--   * AuthController acepta este hash directamente (camino BCrypt).
--   * AgenciaLocalController exige rol ADMIN_AGENCIA en todos sus endpoints.
INSERT INTO `usuario_acceso_energia`
    (`id_usuario`, `id_cliente`, `nombre_usuario`, `password_hash`, `rol`, `fecha_creacion`)
VALUES
    (1, 100, 'agencia',
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
--   cliente_luz             1
--   contador_energia        0
--   lectura_contador        0
--   recibo_luz              0
--   pagos_procesados        0
--   usuario_acceso_energia  1
