using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Weed.App;

public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listenerTask;
    private int _disposed;

    public SingleInstanceCoordinator(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        var safeId = string.Concat(instanceId.Select(character => char.IsLetterOrDigit(character) ? character : '.'));
        _pipeName = $"Weed.{safeId}.{Process.GetCurrentProcess().SessionId}.Activation";
        // The named object is used as an existence lease, not as a thread-affine lock.
        _mutex = new Mutex(false, $@"Local\Weed.{safeId}.SingleInstance", out var createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public event Action? ActivationRequested;

    public void StartListening()
    {
        if (!IsPrimary || _listenerTask is not null) return;
        _listenerTask = ListenAsync(_shutdown.Token);
    }

    public async Task NotifyPrimaryAsync(CancellationToken cancellationToken = default)
    {
        if (IsPrimary) return;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(timeout.Token);
            var processId = new byte[sizeof(int)];
            await client.ReadExactlyAsync(processId, timeout.Token);
            AllowSetForegroundWindow(BitConverter.ToInt32(processId));
            await client.WriteAsync(Encoding.UTF8.GetBytes("activate"), timeout.Token);
            await client.FlushAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The primary process may still be starting. The second process must still exit.
        }
        catch (IOException)
        {
            // A disappearing primary should not allow this launch to continue as a second instance.
        }
        catch (UnauthorizedAccessException)
        {
            // Do not start another instance if the primary pipe cannot be opened at this integrity level.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            await _shutdown.CancelAsync();
            if (_listenerTask is not null)
            {
                try { await _listenerTask; }
                catch (OperationCanceledException) { }
                catch (IOException) { }
            }
        }
        finally
        {
            _mutex.Dispose();
            _shutdown.Dispose();
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
                using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectionTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                await server.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), connectionTimeout.Token);
                await server.FlushAsync(connectionTimeout.Token);
                var buffer = new byte[16];
                var bytesRead = await server.ReadAsync(buffer, connectionTimeout.Token);
                if (bytesRead > 0)
                {
                    try { ActivationRequested?.Invoke(); }
                    catch { }
                }
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                // A client can disconnect mid-request; keep serving future launches.
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A stalled client must not monopolize the activation pipe.
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
