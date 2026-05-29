-- MySQL dump 10.13  Distrib 8.0.45, for Win64 (x86_64)
--
-- Host: servicio-de-pago.mysql.database.azure.com    Database: api_energia
-- ------------------------------------------------------
-- Server version	8.0.44-azure

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!50503 SET NAMES utf8 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

--
-- Table structure for table `cliente_luz`
--

DROP TABLE IF EXISTS `cliente_luz`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `cliente_luz` (
  `id_cliente` int NOT NULL AUTO_INCREMENT,
  `dpi` varchar(20) NOT NULL,
  `nombre` varchar(100) NOT NULL,
  `apellido` varchar(100) NOT NULL,
  `correo` varchar(150) NOT NULL,
  PRIMARY KEY (`id_cliente`),
  UNIQUE KEY `dpi` (`dpi`)
) ENGINE=InnoDB AUTO_INCREMENT=108 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `contador_energia`
--

DROP TABLE IF EXISTS `contador_energia`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `contador_energia` (
  `numero_contador` varchar(30) NOT NULL,
  `id_cliente` int NOT NULL,
  `direccion_inmueble` varchar(255) NOT NULL,
  `fecha_instalacion` datetime DEFAULT CURRENT_TIMESTAMP,
  `estado` enum('ACTIVO','CORTADO','SUSPENDIDO') NOT NULL DEFAULT 'ACTIVO',
  PRIMARY KEY (`numero_contador`),
  KEY `id_cliente` (`id_cliente`),
  CONSTRAINT `contador_energia_ibfk_1` FOREIGN KEY (`id_cliente`) REFERENCES `cliente_luz` (`id_cliente`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `lectura_contador`
--

DROP TABLE IF EXISTS `lectura_contador`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `lectura_contador` (
  `id_lectura` int NOT NULL AUTO_INCREMENT,
  `numero_contador` varchar(30) NOT NULL,
  `kilovatios_consumidos` int NOT NULL,
  `fecha_lectura` datetime NOT NULL,
  PRIMARY KEY (`id_lectura`),
  KEY `numero_contador` (`numero_contador`),
  KEY `ix_lectura_contador_fecha` (`fecha_lectura`),
  CONSTRAINT `lectura_contador_ibfk_1` FOREIGN KEY (`numero_contador`) REFERENCES `contador_energia` (`numero_contador`)
) ENGINE=InnoDB AUTO_INCREMENT=17 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `pagos_procesados`
--

DROP TABLE IF EXISTS `pagos_procesados`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `pagos_procesados` (
  `id_pago` int NOT NULL AUTO_INCREMENT,
  `numero_contador` varchar(30) NOT NULL,
  `id_recibo` int NOT NULL,
  `monto` decimal(10,2) NOT NULL,
  `fecha_cobro` datetime DEFAULT CURRENT_TIMESTAMP,
  `canal_pago` enum('OFICINA_EMPRESA','SISTEMA_BANCARIO') NOT NULL,
  `codigo_autorizacion_banco` varchar(50) DEFAULT NULL,
  PRIMARY KEY (`id_pago`),
  UNIQUE KEY `uk_pagos_procesados_autorizacion_recibo` (`codigo_autorizacion_banco`,`id_recibo`),
  KEY `numero_contador` (`numero_contador`),
  KEY `id_recibo` (`id_recibo`),
  KEY `ix_pagos_procesados_codigo_autorizacion` (`codigo_autorizacion_banco`),
  CONSTRAINT `pagos_procesados_ibfk_1` FOREIGN KEY (`numero_contador`) REFERENCES `contador_energia` (`numero_contador`),
  CONSTRAINT `pagos_procesados_ibfk_2` FOREIGN KEY (`id_recibo`) REFERENCES `recibo_luz` (`id_recibo`)
) ENGINE=InnoDB AUTO_INCREMENT=28 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `recibo_luz`
--

DROP TABLE IF EXISTS `recibo_luz`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `recibo_luz` (
  `id_recibo` int NOT NULL AUTO_INCREMENT,
  `numero_contador` varchar(30) NOT NULL,
  `id_lectura` int NOT NULL,
  `monto_total` decimal(10,2) NOT NULL,
  `saldo_pendiente` decimal(10,2) NOT NULL DEFAULT '0.00',
  `fecha_emision` datetime NOT NULL,
  `estado` enum('Pendiente','Pagado','Vencido') NOT NULL DEFAULT 'Pendiente',
  PRIMARY KEY (`id_recibo`),
  KEY `numero_contador` (`numero_contador`),
  KEY `id_lectura` (`id_lectura`),
  KEY `ix_recibo_luz_estado` (`estado`),
  CONSTRAINT `recibo_luz_ibfk_1` FOREIGN KEY (`numero_contador`) REFERENCES `contador_energia` (`numero_contador`),
  CONSTRAINT `recibo_luz_ibfk_2` FOREIGN KEY (`id_lectura`) REFERENCES `lectura_contador` (`id_lectura`)
) ENGINE=InnoDB AUTO_INCREMENT=17 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `usuario_acceso_energia`
--

DROP TABLE IF EXISTS `usuario_acceso_energia`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `usuario_acceso_energia` (
  `id_usuario` int NOT NULL AUTO_INCREMENT,
  `id_cliente` int NOT NULL,
  `nombre_usuario` varchar(50) NOT NULL,
  `password_hash` varchar(255) NOT NULL,
  `rol` enum('ADMIN_AGENCIA','CLIENTE') NOT NULL DEFAULT 'CLIENTE',
  `fecha_creacion` datetime DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id_usuario`),
  UNIQUE KEY `nombre_usuario` (`nombre_usuario`),
  KEY `id_cliente` (`id_cliente`),
  CONSTRAINT `usuario_acceso_energia_ibfk_1` FOREIGN KEY (`id_cliente`) REFERENCES `cliente_luz` (`id_cliente`)
) ENGINE=InnoDB AUTO_INCREMENT=10 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

-- Dump completed on 2026-05-28 23:13:03
