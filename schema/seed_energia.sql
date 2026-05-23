-- ===========================================================================
-- seed_energia.sql
-- ---------------------------------------------------------------------------
-- Datos iniciales para api_energia. Idempotente (ON DUPLICATE KEY UPDATE).
-- Los passwords son hashes BCrypt (work factor 11), generados con la misma
-- librería que la API en runtime.
--
-- Cuentas de prueba que deja sembradas:
--   * agencia        / Admin123*    (rol ADMIN_AGENCIA)
--   * 2200000000101  / Cliente123*  (rol CLIENTE)
--
-- Datos transaccionales que NO se siembran aquí (los crea la API durante el
-- flujo de prueba): lectura_contador, recibo_luz, pagos_procesados.
-- ===========================================================================

USE `api_energia`;

-- ── cliente_luz ────────────────────────────────────────────────────────────
INSERT INTO `cliente_luz` (`id_cliente`, `dpi`, `nombre`, `apellido`, `correo`)
VALUES
    (1,   '2200000000101', 'Carlos',  'Gómez',   'carlos.gomez@example.com'),
    -- Cliente "interno" para usuarios ADMIN_AGENCIA. La columna id_cliente
    -- es NOT NULL con FK a cliente_luz; ADMIN no representa a un
    -- cuentahabiente real, así que vive bajo este registro técnico.
    (100, '0000000000000', 'Empresa', 'Energía', 'agencia@energia.local')
AS nuevo
ON DUPLICATE KEY UPDATE
    `nombre`   = nuevo.`nombre`,
    `apellido` = nuevo.`apellido`,
    `correo`   = nuevo.`correo`;

-- ── contador_energia ───────────────────────────────────────────────────────
INSERT INTO `contador_energia`
    (`numero_contador`, `id_cliente`, `direccion_inmueble`, `fecha_instalacion`, `estado`)
VALUES
    ('CTR0001', 1, '5a Avenida 10-20, Zona 1', '2025-01-15 09:00:00', 'ACTIVO')
AS nuevo
ON DUPLICATE KEY UPDATE
    `direccion_inmueble` = nuevo.`direccion_inmueble`,
    `estado`             = nuevo.`estado`;

-- ── usuario_acceso_energia ─────────────────────────────────────────────────
-- Hashes pre-calculados con BCrypt (workFactor=11):
--   agencia       / Admin123*    => $2a$11$lQpsLq1CE.OPfqvgySp.VOlhLoSIrqnCsHBfDEefJYIrCTBKKx3p.
--   2200000000101 / Cliente123*  => $2a$11$kDQntXhhvTlVcULofIXYmOI3ljyHxPqwptGPwxbMKs6r/i0dmYFmS
INSERT INTO `usuario_acceso_energia`
    (`id_usuario`, `id_cliente`, `nombre_usuario`, `password_hash`, `rol`, `fecha_creacion`)
VALUES
    (1, 100, 'agencia',
        '$2a$11$lQpsLq1CE.OPfqvgySp.VOlhLoSIrqnCsHBfDEefJYIrCTBKKx3p.',
        'ADMIN_AGENCIA', NOW()),
    (2, 1,   '2200000000101',
        '$2a$11$kDQntXhhvTlVcULofIXYmOI3ljyHxPqwptGPwxbMKs6r/i0dmYFmS',
        'CLIENTE', NOW())
AS nuevo
ON DUPLICATE KEY UPDATE
    `id_cliente`    = nuevo.`id_cliente`,
    `password_hash` = nuevo.`password_hash`,
    `rol`           = nuevo.`rol`;
