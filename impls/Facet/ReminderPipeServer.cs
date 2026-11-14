using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Facet.Integration;

namespace Facet;

internal sealed class ReminderPipeServer : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private bool disposed;

    internal ReminderPipeServer(string root, Dispatcher dispatcher, Func<ReminderCommand, ReminderReply> handle)
    {
        worker = Task.Run(async () => {
            while (!stop.IsCancellationRequested) {
                try {
                    using var pipe = new NamedPipeServerStream(ReminderProtocol.PipeName(root), PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stop.Token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    ReminderReply reply;
                    try {
                        var command = await ReminderProtocol.ReadAsync<ReminderCommand>(pipe, 16384, timeout.Token);
                        reply = await dispatcher.InvokeAsync(() => handle(command), DispatcherPriority.Normal, timeout.Token).Task;
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidDataException or JsonException) {
                        reply = new(false, "invalid_request", ex.Message);
                    }
                    await ReminderProtocol.WriteAsync(pipe, reply, 8 * 1024 * 1024, timeout.Token);
                }
                catch (OperationCanceledException) { }
                catch (IOException) { if (!stop.IsCancellationRequested) await Task.Delay(100); }
            }
        });
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true; stop.Cancel();
        _ = worker.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
