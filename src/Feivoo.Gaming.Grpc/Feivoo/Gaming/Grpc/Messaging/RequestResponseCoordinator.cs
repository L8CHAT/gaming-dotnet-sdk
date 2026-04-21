using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Feivoo.Gaming.Grpc.Messaging;

/// <summary>
/// 通过 message_id 关联请求与响应的轻量协调器。
/// </summary>
public sealed class RequestResponseCoordinator<TResponse>
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TResponse>> _pending = new ConcurrentDictionary<string, TaskCompletionSource<TResponse>>();

    public int PendingCount { get { return _pending.Count; } }

    public async Task<TResponse> SendAndWaitAsync(
        string messageId,
        Func<Task> sendAsync,
        TimeSpan timeout,
        CancellationToken cancellationToken = default(CancellationToken))
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("Message id is required.", "messageId");
        }
        if (sendAsync == null)
        {
            throw new ArgumentNullException("sendAsync");
        }

        var waiter = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(messageId, waiter))
        {
            throw new InvalidOperationException("A pending request with id '" + messageId + "' already exists.");
        }

        using (cancellationToken.Register(() => TryFail(messageId, new OperationCanceledException(cancellationToken))))
        {
            try
            {
                await sendAsync().ConfigureAwait(false);

                using (var timeoutCts = new CancellationTokenSource(timeout))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    var completed = await Task.WhenAny(waiter.Task, Task.Delay(Timeout.Infinite, linkedCts.Token)).ConfigureAwait(false);
                    if (completed != waiter.Task)
                    {
                        TaskCompletionSource<TResponse> _;
                        _pending.TryRemove(messageId, out _);
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new TimeoutException("Request '" + messageId + "' timed out after " + timeout + ".");
                    }
                }

                return await waiter.Task.ConfigureAwait(false);
            }
            catch
            {
                TaskCompletionSource<TResponse> _;
                _pending.TryRemove(messageId, out _);
                throw;
            }
        }
    }

    public bool TryComplete(string messageId, TResponse response)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return false;
        }

        TaskCompletionSource<TResponse> waiter;
        if (_pending.TryRemove(messageId, out waiter))
        {
            return waiter.TrySetResult(response);
        }

        return false;
    }

    public bool TryFail(string messageId, Exception exception)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return false;
        }

        TaskCompletionSource<TResponse> waiter;
        if (_pending.TryRemove(messageId, out waiter))
        {
            return waiter.TrySetException(exception);
        }

        return false;
    }

    public void FailAll(Exception exception)
    {
        foreach (var pending in _pending)
        {
            TaskCompletionSource<TResponse> waiter;
            if (_pending.TryRemove(pending.Key, out waiter))
            {
                waiter.TrySetException(exception);
            }
        }
    }
}
