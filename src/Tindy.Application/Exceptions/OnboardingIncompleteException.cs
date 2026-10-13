namespace Tindy.Application.Exceptions;

public sealed class OnboardingIncompleteException : BusinessRuleException
{
    public IReadOnlyList<int> MissingSteps { get; }

    public OnboardingIncompleteException(IReadOnlyList<int> missingSteps)
        : base("Chưa hoàn thành hoặc dữ liệu không hợp lệ ở các bước onboarding bắt buộc.")
    {
        MissingSteps = missingSteps;
    }
}
