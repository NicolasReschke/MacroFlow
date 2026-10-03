# MacroFlow

MacroFlow es una aplicación de escritorio para Windows que ejecuta secuencias de teclado y mouse con pausas prioritarias. Está pensada para poder interrumpir una macro inmediatamente al mantener una tecla de parry, alternar una pausa de morph y detener todo con una tecla de emergencia.

> Estado actual: versión de desarrollo. La compilación automática sólo verifica el código y las pruebas; por ahora no genera ni publica un ejecutable portable.

> El usuario es responsable de comprobar las reglas del software o juego donde utilice automatización. MacroFlow no evade anticheat, no modifica procesos y no garantiza que una macro esté permitida.

## Funciones actuales

- Acciones de pulsar, bajar o soltar una tecla, esperar y hacer clic.
- Secuencias repetibles con retardos configurables.
- Hotkey global para iniciar/detener y parada de emergencia.
- Pausa mientras se mantiene una tecla, con retardo de reanudación.
- Pausa alternable para entrar y salir de morph.
- Liberación automática de teclas sintéticas al pausar o detener.
- Restricción opcional al proceso que esté en primer plano.
- Captura directa de teclas físicas, incluyendo Shift/Ctrl izquierdos y derechos, signos y F1–F24.
- Perfiles JSON locales.
- Minimización a la bandeja.
- Sin telemetría, red, drivers ni inyección de procesos.

## Uso inicial

1. Abrí MacroFlow y usá **Capturar** para elegir cada tecla de control.
2. Elegí una acción, capturá su tecla y agregala a la secuencia.
3. Guardá el perfil.
4. Probalo primero en un editor de texto, con una secuencia corta y sin repetición.
5. `F6` inicia/detiene y `F12` ejecuta la parada de emergencia de forma predeterminada.

Los botones especiales del mouse pueden asignarse en G Hub a `F13`–`F24`; luego MacroFlow puede usar esas teclas como controles.

## Captura de teclas

- No es necesario escribir el nombre de una tecla: el botón **Capturar** registra su código de Windows.
- Se distinguen Shift, Ctrl y Alt izquierdos y derechos.
- Los signos dependen de la distribución activa del teclado y se muestran con el nombre provisto por Windows.
- Para una combinación, agregá tres acciones. Ejemplo: **Mantener Ctrl**, **Pulsar 1**, **Soltar Ctrl**.
- Los clics izquierdo, derecho y central se eligen de una lista.

## Desarrollo

Requiere el SDK de .NET 10 en Windows.

```powershell
dotnet restore MacroFlow.slnx
dotnet build MacroFlow.slnx --configuration Release
dotnet run --project tests/MacroFlow.Core.Tests --configuration Release
dotnet run --project src/MacroFlow.App
```

La publicación portable queda reservada para una etapa posterior, cuando la interfaz y el comportamiento estén validados.

## Estructura

```text
src/MacroFlow.Core/       Motor, modelos y persistencia (sin depender de Windows)
src/MacroFlow.App/        Interfaz WPF, hook global y SendInput
tests/MacroFlow.Core.Tests/ Pruebas del motor sin paquetes de terceros
scripts/                  Herramientas de publicación (no se ejecutan automáticamente)
.github/workflows/        Compilación y pruebas automatizadas, sin publicar ejecutables
```

Consulta [PRIVACY.md](PRIVACY.md) y [SECURITY.md](SECURITY.md) para detalles del tratamiento de datos y el modelo de seguridad.
