using Tindy.Application.DTOs.Ai;
using Tindy.Application.Interfaces.Services;

namespace Tindy.Infrastructure.Ai;

public sealed partial class BedrockProfileExtractionProvider
{
    private readonly object _capacityLock = new();

    private void Reserve(int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_capacityLock)
        {
            if (_slots.CurrentCount < count)
                throw new Tindy.Application.Exceptions.AiException(Tindy.Application.Exceptions.AiFailure.Busy);
            for (var index = 0; index < count; index++) _slots.Wait(0);
        }
    }

    private void Release(int count)
    {
        lock (_capacityLock) _slots.Release(count);
    }

    public IAiExtractionBatchSession BeginBatch(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();
        Reserve(2, cancellationToken);
        return new Batch(this, cancellationToken);
    }

    private sealed class Batch(BedrockProfileExtractionProvider owner, CancellationToken token) : IAiExtractionBatchSession
    {
        private readonly CancellationTokenSource _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        private readonly object _sync = new();
        private Task? _profile;
        private Task? _lists;
        private Task? _dispose;
        private bool _disposed;

        public Task<AiScalarProfileModelOutput> ExtractProfileScalarsAsync(AiProfileInput input, IReadOnlyList<AiRoleReference> roles)
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_profile is not null) throw new InvalidOperationException("The profile stage has already started.");
                var task = RunProfile(input, roles);
                _profile = task;
                return task;
            }
        }

        private async Task<AiScalarProfileModelOutput> RunProfile(AiProfileInput input, IReadOnlyList<AiRoleReference> roles)
        {
            var result = await owner.SendAsync(input, () => owner.CreateScalarRequest(input, roles),
                ScalarProfileExtractionPrompt.ToolName, _cancellation.Token, reserved: true);
            return new(result.RawJson, result.Diagnostics);
        }

        public Task<AiListModelOutput> ExtractListsAsync(AiProfileInput input, IReadOnlyList<AiSkillReference> skills, IReadOnlyList<AiRoleReference> roles)
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_lists is not null) throw new InvalidOperationException("The list stage has already started.");
                var task = owner.SendAsync(input, () => owner.CreateListRequest(input, skills, roles), ListExtractionPrompt.ToolName,
                    _cancellation.Token, reserved: true);
                _lists = task;
                return task;
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                if (_dispose is not null) return new(_dispose);
                _disposed = true;
                _dispose = DrainAsync();
                return new(_dispose);
            }
        }

        private async Task DrainAsync()
        {
            try
            {
                await _cancellation.CancelAsync();
                try { await Task.WhenAll(_profile ?? Task.CompletedTask, _lists ?? Task.CompletedTask); }
                catch { /* Callers observe the stage tasks; disposal only drains them. */ }
            }
            finally
            {
                _cancellation.Dispose();
                owner.Release(2);
            }
        }
    }
}
