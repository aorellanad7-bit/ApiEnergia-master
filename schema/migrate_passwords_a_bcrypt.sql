-- ===========================================================================
-- migrate_passwords_a_bcrypt.sql
-- ---------------------------------------------------------------------------
-- Migra los passwords almacenados en plaintext en `usuario_acceso_energia`
-- a hashes BCrypt (work factor 11).
--
-- IMPORTANTE:
--   * Solo se actualizan los usuarios cuyo password actual coincide con uno
--     de los valores de prueba conocidos. Los registros creados dinámicamente
--     por la API (CrearClienteConContador) tienen un password único por
--     usuario y NO se pueden migrar desde SQL → la API los re-hashea
--     automáticamente la próxima vez que ese usuario inicia sesión
--     (ver AuthController.Login → camino "legacy").
--   * Los hashes incluidos abajo fueron generados con BCrypt.Net-Next 4.2.0
--     usando el mismo work factor (11) que la API en producción.
-- ---------------------------------------------------------------------------
-- USO:
--   mysql -h <host> -u <user> -p api_energia < migrate_passwords_a_bcrypt.sql
-- ===========================================================================

USE `api_energia`;

-- ---- Hashes pre-calculados (BCrypt, work factor 11) ----
-- 1234        => $2a$11$F0VoGP4BvubXPuMOMvTbV.ku6MVGymAqStq96YAvMiBFJjTtAJ792
-- Admin123*   => $2a$11$lQpsLq1CE.OPfqvgySp.VOlhLoSIrqnCsHBfDEefJYIrCTBKKx3p.
-- Cliente123* => $2a$11$kDQntXhhvTlVcULofIXYmOI3ljyHxPqwptGPwxbMKs6r/i0dmYFmS
-- Energia123* => $2a$11$/ZbWTv.HERl2eI8Jpo41NONe8.FSfpszzTObYjNdQBtPHc27.Q7XW

UPDATE `usuario_acceso_energia`
   SET `password_hash` = '$2a$11$F0VoGP4BvubXPuMOMvTbV.ku6MVGymAqStq96YAvMiBFJjTtAJ792'
 WHERE `password_hash` = '1234';

UPDATE `usuario_acceso_energia`
   SET `password_hash` = '$2a$11$lQpsLq1CE.OPfqvgySp.VOlhLoSIrqnCsHBfDEefJYIrCTBKKx3p.'
 WHERE `password_hash` = 'Admin123*';

UPDATE `usuario_acceso_energia`
   SET `password_hash` = '$2a$11$kDQntXhhvTlVcULofIXYmOI3ljyHxPqwptGPwxbMKs6r/i0dmYFmS'
 WHERE `password_hash` = 'Cliente123*';

UPDATE `usuario_acceso_energia`
   SET `password_hash` = '$2a$11$/ZbWTv.HERl2eI8Jpo41NONe8.FSfpszzTObYjNdQBtPHc27.Q7XW'
 WHERE `password_hash` = 'Energia123*';

-- ---- Reporte de pendientes (los que aún están en plaintext) ----
-- Estos se auto-migrarán al hacer login a través de la API.
SELECT
    `id_usuario`,
    `nombre_usuario`,
    `rol`,
    CASE
        WHEN `password_hash` LIKE '$2a$%' OR `password_hash` LIKE '$2b$%' OR `password_hash` LIKE '$2y$%'
            THEN 'BCrypt OK'
        ELSE 'PLAINTEXT — se migrará en próximo login'
    END AS estado_password
FROM `usuario_acceso_energia`
ORDER BY `id_usuario`;
