# Seguridad

## Diseño

- Se ejecuta con los permisos normales del usuario (`asInvoker`).
- No instala controladores, servicios ni tareas programadas.
- No modifica ni inyecta código en otros procesos.
- Las entradas sintéticas llevan una marca interna y son ignoradas por el hook para evitar bucles.
- Al pausar, detener o cerrar, libera todas las teclas sintéticas mantenidas.
- No incluye secretos, certificados de firma ni telemetría.

## Reportar un problema

Abrí un issue sin incluir credenciales, tokens, rutas privadas ni información personal. Para una distribución pública, cada versión debería firmarse con Authenticode y publicarse con su hash SHA-256.
