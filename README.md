# MacroFlow

MacroFlow es una aplicación portable para Windows que ejecuta secuencias de teclado y mouse con pausas prioritarias. Está pensada para poder interrumpir una macro inmediatamente al mantener una tecla de parry, alternar una pausa de morph y detener todo con una tecla de emergencia.

> El usuario es responsable de comprobar las reglas del software o juego donde utilice automatización. MacroFlow no evade anticheat, no modifica procesos y no garantiza que una macro esté permitida.

## Funciones actuales

- Acciones de pulsar, bajar o soltar una tecla, esperar y hacer clic.
- Secuencias repetibles con retardos configurables.
- Hotkey global para iniciar/detener y parada de emergencia.
- Pausa mientras se mantiene una tecla, con retardo de reanudación.
- Pausa alternable para entrar y salir de morph.
- Liberación automática de teclas sintéticas al pausar o detener.
- Restricción opcional al proceso que esté en primer plano.
- Perfiles JSON portables.
- Minimización a la bandeja.
- Sin telemetría, red, drivers ni inyección de procesos.

## Uso inicial

1. Abrí MacroFlow y elegí las teclas de control.
2. Agregá las acciones en el orden deseado.
3. Guardá el perfil.
4. Probalo primero en un editor de texto, con una secuencia corta y sin repetición.
5. `F6` inicia/detiene y `F12` ejecuta la parada de emergencia de forma predeterminada.

Los botones especiales del mouse pueden asignarse en G Hub a `F13`–`F24`; luego MacroFlow puede usar esas teclas como controles.

## Teclas reconocidas

- `A`–`Z`, `0`–`9` y `F1`–`F24`.
- `Space`, `Tab`, `Enter`, `Escape`, `Shift`, `Ctrl`, `Alt`.
- Navegación: `Up`, `Down`, `Left`, `Right`, `Home`, `End`, `PageUp`, `PageDown`.
- Mouse: `left`, `right`, `middle`.

## Desarrollo

Requiere el SDK de .NET 10 en Windows.

```powershell
dotnet restore MacroFlow.slnx
dotnet build MacroFlow.slnx --configuration Release
dotnet run --project tests/MacroFlow.Core.Tests --configuration Release
dotnet run --project src/MacroFlow.App
```

Para crear un portable autosuficiente:

```powershell
.\scripts\publish-portable.ps1
```

## Estructura

```text
src/MacroFlow.Core/       Motor, modelos y persistencia (sin depender de Windows)
src/MacroFlow.App/        Interfaz WPF, hook global y SendInput
tests/MacroFlow.Core.Tests/ Pruebas del motor sin paquetes de terceros
scripts/                  Publicación portable
.github/workflows/        Compilación y pruebas automatizadas
```

Consulta [PRIVACY.md](PRIVACY.md) y [SECURITY.md](SECURITY.md) para detalles del tratamiento de datos y el modelo de seguridad.
