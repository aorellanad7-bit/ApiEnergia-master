-- Portal cliente: marcar usuarios que deben cambiar contraseña temporal.
USE api_energia;

ALTER TABLE `usuario_acceso_energia`
    ADD COLUMN `debe_cambiar_password` TINYINT(1) NOT NULL DEFAULT 0
    AFTER `rol`;
