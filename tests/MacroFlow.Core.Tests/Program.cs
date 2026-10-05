using System.Collections.Concurrent;
using MacroFlow.Core.Engine;
using MacroFlow.Core.Models;
using MacroFlow.Core.Recording;

var tests = new (string Name, Func<Task> Run)[]
{
    ("PauseController bloquea y reanuda", PauseControllerBlocksAndResumes),
    ("MacroEngine ejecuta una secuencia", EngineExecutesSequence),
    ("Pausar libera entradas mantenidas", PauseReleasesHeldInputs),
    ("Grabadora conserva tiempos y combinaciones", RecorderPreservesTimingAndChords),
    ("Grabadora elimina la hotkey final", RecorderTrimsStopHotkey)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"FAIL  {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"\n{tests.Length - failed}/{tests.Length} pruebas correctas.");
return failed == 0 ? 0 : 1;

static async Task PauseControllerBlocksAndResumes()
{
    var pause = new PauseController();
    pause.SetPaused("Prueba", true);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var waiting = pause.WaitWhilePausedAsync(timeout.Token);
    await Task.Delay(30, timeout.Token);
    Assert(!waiting.IsCompleted, "La espera terminó mientras todavía estaba pausada.");
    pause.SetPaused("Prueba", false);
    await waiting;
}

static async Task EngineExecutesSequence()
{
    var input = new FakeInputEmitter();
    await using var engine = new MacroEngine(input);
    var profile = new MacroProfile
    {
        Repeat = false,
        Actions = [new MacroAction { Kind = MacroActionKind.KeyPress, Value = "1", DurationMs = 1 }]
    };

    Assert(engine.Start(profile), "El motor no comenzó.");
    await WaitUntil(() => engine.State == MacroEngineState.Stopped);
    Assert(input.Events.Contains("down:1"), "Falta KeyDown.");
    Assert(input.Events.Contains("up:1"), "Falta KeyUp.");
}

static async Task PauseReleasesHeldInputs()
{
    var input = new FakeInputEmitter();
    await using var engine = new MacroEngine(input);
    var profile = new MacroProfile
    {
        Repeat = true,
        Actions =
        [
            new MacroAction { Kind = MacroActionKind.KeyDown, Value = "A" },
            new MacroAction { Kind = MacroActionKind.Delay, DurationMs = 1_000 }
        ]
    };

    engine.Start(profile);
    await WaitUntil(() => input.Events.Contains("down:A"));
    await engine.SetPauseAsync("Seguridad", true);
    Assert(input.ReleaseCount > 0, "La pausa no liberó las entradas.");
    await engine.StopAsync();
}

static Task RecorderPreservesTimingAndChords()
{
    var recorder = new MacroRecorder();
    recorder.Start(1_000);
    recorder.RecordKey("CTRL", true, 1_010);
    recorder.RecordKey("1", true, 1_060);
    recorder.RecordKey("1", false, 1_130);
    recorder.RecordKey("CTRL", false, 1_150);
    var actions = recorder.Stop(1_160);

    Assert(actions.Count == 5, $"Se esperaban 5 acciones y se obtuvieron {actions.Count}.");
    Assert(actions[0].Kind == MacroActionKind.KeyDown && actions[0].Value == "CTRL", "Falta Ctrl abajo.");
    Assert(actions[1].Kind == MacroActionKind.Delay && actions[1].DurationMs == 50, "Intervalo inicial incorrecto.");
    Assert(actions[2].Kind == MacroActionKind.KeyPress && actions[2].Value == "1" && actions[2].DurationMs == 70,
        "La pulsación simple no se compactó correctamente.");
    Assert(actions[3].Kind == MacroActionKind.Delay && actions[3].DurationMs == 20, "Intervalo final incorrecto.");
    Assert(actions[4].Kind == MacroActionKind.KeyUp && actions[4].Value == "CTRL", "Falta Ctrl arriba.");
    return Task.CompletedTask;
}

static Task RecorderTrimsStopHotkey()
{
    var recorder = new MacroRecorder();
    recorder.Start(0);
    recorder.RecordKey("2", true, 10);
    recorder.RecordKey("2", false, 60);
    recorder.RecordKey("CTRL", true, 100);
    var actions = recorder.Stop(110, ["CTRL", "F8"]);

    Assert(actions.All(action => action.Value != "CTRL"), "La combinación para detener quedó grabada.");
    Assert(actions.Any(action => action.Kind == MacroActionKind.KeyPress && action.Value == "2"), "Se perdió la tecla grabada.");
    return Task.CompletedTask;
}

static async Task WaitUntil(Func<bool> predicate)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    while (!predicate())
    {
        await Task.Delay(10, timeout.Token);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

file sealed class FakeInputEmitter : IInputEmitter
{
    public ConcurrentBag<string> Events { get; } = [];
    public int ReleaseCount { get; private set; }
    public Task KeyDownAsync(string key, CancellationToken cancellationToken) { Events.Add($"down:{key}"); return Task.CompletedTask; }
    public Task KeyUpAsync(string key, CancellationToken cancellationToken) { Events.Add($"up:{key}"); return Task.CompletedTask; }
    public Task MouseClickAsync(string button, CancellationToken cancellationToken) { Events.Add($"mouse:{button}"); return Task.CompletedTask; }
    public Task ReleaseAllAsync() { ReleaseCount++; Events.Add("release-all"); return Task.CompletedTask; }
}
