# ============================================
# Render.com — Web Service Configuration
# ============================================
# Este archivo le dice a Render cómo construir y ejecutar el servidor Hub.
#
# INSTRUCCIONES DE DEPLOY:
# 1. Ve a https://render.com y crea una cuenta (gratis).
# 2. Click en "New +" → "Web Service".
# 3. Conecta tu repositorio de GitHub: AAD002-Dwg/SYNC-CAD-V2_NEURAL
# 4. Configuración:
#    - Name:           hsync-hub
#    - Region:         Oregon (US West) o el más cercano a tu ubicación
#    - Branch:         main
#    - Root Directory: server
#    - Runtime:        Node
#    - Build Command:  npm install
#    - Start Command:  node hub.js
#    - Plan:           Free (para pruebas)
#
# 5. En la sección "Environment":
#    - Agregar variable: PORT = 10000 (Render asigna este puerto automáticamente)
#
# 6. Click en "Deploy Web Service".
#
# 7. Una vez desplegado, Render te dará una URL como:
#    https://hsync-hub.onrender.com
#
# 8. En el plugin de AutoCAD, cambiar la URL de conexión a:
#    wss://hsync-hub.onrender.com
#    (Nota: Render usa WSS/HTTPS, no WS/HTTP)
#
# NOTAS IMPORTANTES:
# - El plan gratuito de Render "duerme" el servicio tras 15min de inactividad.
#   La primera conexión tras dormir tarda ~30 segundos en despertar.
# - Para producción, usar el plan "Starter" ($7/mes) que mantiene el servicio activo 24/7.
# - El Hub no necesita base de datos para Fase 1 (todo en RAM).
