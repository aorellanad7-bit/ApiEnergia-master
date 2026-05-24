-- ===========================================================================
-- schema_energia.sql
-- ---------------------------------------------------------------------------
-- Schema canónico de la base `api_energia`. Alineado 1:1 con
-- ApiEnergia.DbContext.EnergiaDbContext y los modelos de la API.
--
-- Idempotente: usa CREATE TABLE IF NOT EXISTS, así que se puede correr varias
-- veces sin perder datos. Para limpiar y reconstruir desde cero, ejecuta
-- primero cleanup_energia.sql.
--
-- Para alinear una base ya existente que tenga la estructura legacy
-- (p.ej. `correo_electronico`, `fecha_emision DATE`, ENUM con MAYÚSCULAS),
-- ejecutar migrate_schema_v2.sql en lugar de este archivo.
-- ===========================================================================

CREATE DATABASE IF NOT EXISTS `api_energia`
    DEFAULT CHARACTER SET utf8mb4
    DEFAULT COLLATE utf8mb4_0900_ai_ci;

USE `api_energia`;

SET FOREIGN_KEY_CHECKS = 0;

-- ── cliente_luz ────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `cliente_luz` (
    `id_cliente`   INT          NOT NULL AUTO_INCREMENT,
    `dpi`          VARCHAR(20)  NOT NULL,
    `nombre`       VARCHAR(100) NOT NULL,
    `apellido`     VARCHAR(100) NOT NULL,
    `correo`       VARCHAR(150) NOT NULL,
    PRIMARY KEY (`id_cliente`),
    UNIQUE KEY `uk_cliente_luz_dpi` (`dpi`)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

-- ── contador_energia ───────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `contador_energia` (
    `numero_contador`     VARCHAR(30)             NOT NULL,
    `id_cliente`          INT                     NOT NULL,
    `direccion_inmueble`  VARCHAR(255)            NOT NULL,
    `fecha_instalacion`   DATETIME                NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `estado`              VARCHAR(20)             NOT NULL DEFAULT 'ACTIVO',
    PRIMARY KEY (`numero_contador`),
    KEY `ix_contador_energia_id_cliente` (`id_cliente`),
    CONSTRAINT `fk_contador_energia__cliente_luz`
        FOREIGN KEY (`id_cliente`) REFERENCES `cliente_luz` (`id_cliente`)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

-- ── lectura_contador ───────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `lectura_contador` (
    `id_lectura`            INT         NOT NULL AUTO_INCREMENT,
    `numero_contador`       VARCHAR(30) NOT NULL,
    `kilovatios_consumidos` INT         NOT NULL,
    `fecha_lectura`         DATETIME    NOT NULL,
    PRIMARY KEY (`id_lectura`),
    KEY `ix_lectura_contador_numero_contador` (`numero_contador`),
    KEY `ix_lectura_contador_fecha` (`fecha_lectura`),
    CONSTRAINT `fk_lectura_contador__contador_energia`
        FOREIGN KEY (`numero_contador`) REFERENCES `contador_energia` (`numero_contador`)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

-- ── recibo_luz ─────────────────────────────────────────────────────────────
-- estado almacena el nombre del enum C# (Pendiente / Pagado / Vencido). Si se
-- prefiere usar ENUM nativo en MySQL, los valores deben coincidir EXACTAMENTE
-- con los del enum C# (PascalCase) para que EF Core los grabe sin error.
CREATE TABLE IF NOT EXISTS `recibo_luz` (
    `id_recibo`         INT            NOT NULL AUTO_INCREMENT,
    `numero_contador`   VARCHAR(30)    NOT NULL,
    `id_lectura`        INT            NOT NULL,
    `monto_total`       DECIMAL(10, 2) NOT NULL,
    `saldo_pendiente`   DECIMAL(10, 2) NOT NULL DEFAULT 0.00,
    `fecha_emision`     DATETIME       NOT NULL,
    `estado`            ENUM('Pendiente','Pagado','Vencido') NOT NULL DEFAULT 'Pendiente',
    PRIMARY KEY (`id_recibo`),
    KEY `ix_recibo_luz_numero_contador` (`numero_contador`),
    KEY `ix_recibo_luz_id_lectura` (`id_lectura`),
    KEY `ix_recibo_luz_estado` (`estado`),
    CONSTRAINT `fk_recibo_luz__contador_energia`
        FOREIGN KEY (`numero_contador`) REFERENCES `contador_energia` (`numero_contador`),
    CONSTRAINT `fk_recibo_luz__lectura_contador`
        FOREIGN KEY (`id_lectura`) REFERENCES `lectura_contador` (`id_lectura`)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

-- ── pagos_procesados ───────────────────────────────────────────────────────
-- canal_pago debe coincidir con las constantes en EnergiaService.cs:
--   * SISTEMA_BANCARIO  → pago notificado por el banco
--   * OFICINA_EMPRESA   → pago en efectivo en agencia
CREATE TABLE IF NOT EXISTS `pagos_procesados` (
    `id_pago`                   INT                                          NOT NULL AUTO_INCREMENT,
    `numero_contador`           VARCHAR(30)                                  NOT NULL,
    `id_recibo`                 INT                                          NOT NULL,
    `monto`                     DECIMAL(10, 2)                               NOT NULL,
    `fecha_cobro`               DATETIME                                     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `canal_pago`                ENUM('OFICINA_EMPRESA','SISTEMA_BANCARIO')   NOT NULL,
    `codigo_autorizacion_banco` VARCHAR(50)                                  DEFAULT NULL,
    PRIMARY KEY (`id_pago`),
    KEY `ix_pagos_procesados_numero_contador` (`numero_contador`),
    KEY `ix_pagos_procesados_id_recibo` (`id_recibo`),
    -- Idempotencia: una misma autorización del banco no se puede aplicar dos
    -- veces sobre el MISMO recibo. El par (autorizacion, recibo) es único,
    -- pero la autorización por sí sola puede repetirse porque un solo pago
    -- bancario se distribuye FIFO entre todos los recibos pendientes del
    -- contador, generando N filas con la misma autorización (una por recibo).
    UNIQUE KEY `uk_pagos_procesados_autorizacion_recibo`
        (`codigo_autorizacion_banco`, `id_recibo`),
    KEY `ix_pagos_procesados_codigo_autorizacion` (`codigo_autorizacion_banco`),
    CONSTRAINT `fk_pagos_procesados__contador_energia`
        FOREIGN KEY (`numero_contador`) REFERENCES `contador_energia` (`numero_contador`),
    CONSTRAINT `fk_pagos_procesados__recibo_luz`
        FOREIGN KEY (`id_recibo`) REFERENCES `recibo_luz` (`id_recibo`)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

-- ── usuario_acceso_energia ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `usuario_acceso_energia` (
    `id_usuario`     INT          NOT NULL AUTO_INCREMENT,
    `id_cliente`     INT          NOT NULL,
    `nombre_usuario` VARCHAR(50)  NOT NULL,
    `password_hash`  VARCHAR(255) NOT NULL,
    `rol`            ENUM('ADMIN_AGENCIA','CLIENTE') NOT NULL DEFAULT 'CLIENTE',
    `fecha_creacion` DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id_usuario`),
    UNIQUE KEY `uk_usuario_acceso_nombre_usuario` (`nombre_usuario`),
    KEY `ix_usuario_acceso_id_cliente` (`id_cliente`),
    CONSTRAINT `fk_usuario_acceso__cliente_luz`
        FOREIGN KEY (`id_cliente`) REFERENCES `cliente_luz` (`id_cliente`)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

SET FOREIGN_KEY_CHECKS = 1;

-- =========================================================================
-- Notas de diseño:
--
-- * Todas las claves foráneas tienen índices secundarios explícitos para que
--   los JOINs y los borrados en cascada sean eficientes.
-- * `pagos_procesados.(codigo_autorizacion_banco, id_recibo)` es UNIQUE →
--   garantiza idempotencia por recibo: el mismo `idDebito` del banco no se
--   puede aplicar dos veces sobre el mismo recibo, pero sí puede aparecer
--   varias veces ligado a recibos distintos del mismo contador (cuando el
--   pago bancario salda múltiples recibos pendientes en FIFO).
-- * Las fechas son DATETIME (no DATE) porque la API guarda timestamps con hora.
-- * Los ENUM tienen los valores EXACTOS que escribe el código C# (sensible al
--   case en MySQL).
-- =========================================================================
