using NoticeMemo.Core.Models;

namespace NoticeMemo.Core.Services;

public readonly record struct ReminderDecision(bool ShouldRemind, bool IsOverdue);

public static class ReminderPolicy
{
    private static readonly TimeSpan MaximumOverdueWindow = TimeSpan.FromHours(24);

    public static ReminderDecision Evaluate(Memo memo, int leadMinutes, DateTimeOffset now)
    {
        if (memo.Status == MemoStatus.Done ||
            memo.DueAt is null ||
            memo.ReminderSentAt is not null)
        {
            return new ReminderDecision(false, false);
        }

        var dueAt = memo.DueAt.Value;
        var reminderAt = dueAt.AddMinutes(-Math.Max(0, leadMinutes));
        if (now < reminderAt || now > dueAt.Add(MaximumOverdueWindow))
        {
            return new ReminderDecision(false, false);
        }

        return new ReminderDecision(true, now >= dueAt);
    }
}
