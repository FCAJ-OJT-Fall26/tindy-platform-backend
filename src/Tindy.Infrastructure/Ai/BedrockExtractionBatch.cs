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
        private AiProfileInput? _source;
        private Task<AiProfileInput>? _prepared;
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
            input = await PrepareAsync(input);
            var result = await owner.SendAsync(input, prepared => owner.CreateScalarRequest(prepared, roles),
                root => ReadToolOutput(root, ScalarProfileExtractionPrompt.ToolName), _cancellation.Token, reserved: true);
            return new(result.RawJson, result.Diagnostics);
        }

        public Task<AiListModelOutput> ExtractListsAsync(AiProfileInput input, IReadOnlyList<AiSkillReference> skills, IReadOnlyList<AiRoleReference> roles)
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_lists is not null) throw new InvalidOperationException("The list stage has already started.");
                var task = RunLists(input, skills, roles);
                _lists = task;
                return task;
            }
        }

        private async Task<AiListModelOutput> RunLists(AiProfileInput input, IReadOnlyList<AiSkillReference> skills, IReadOnlyList<AiRoleReference> roles)
        {
            input = await PrepareAsync(input);
            return await owner.SendAsync(input, prepared => owner.CreateListRequest(prepared, skills, roles), ReadListOutput,
                _cancellation.Token, reserved: true);
        }

        private Task<AiProfileInput> PrepareAsync(AiProfileInput input)
        {
            lock (_sync)
            {
                if (_source is not null && _source != input) throw new InvalidOperationException("Both stages must use the same source input.");
                _source = input;
                return _prepared ??= owner.PrepareInputAsync(input, _cancellation.Token);
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
