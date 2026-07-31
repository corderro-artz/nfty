using Nfty.App.Services;
using Nfty.App.ViewModels;

namespace Nfty.App.Tests;

public sealed class FakeNav : INavigationService
{
    public ViewModelBase? Current { get; private set; }
    public event Action? Changed;
    public void To(ViewModelBase page) { Current = page; Changed?.Invoke(); }
    public int BackCount { get; private set; }
    public void Back() { BackCount++; }
}

public sealed class FakeDialogs : IDialogService
{
    public ViewModelBase? Active { get; private set; }
    public event Action? Changed;
    public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog) { Active = dialog; Changed?.Invoke(); return Task.FromResult<TResult?>(default); }
    public void Close(object? result) { Active = null; Changed?.Invoke(); }
}

public sealed class FakeNotYetWired : INotYetWired
{
    public string? Last { get; private set; }
    public event Action<string>? Reported;
    public void Report(string action) { Last = action; Reported?.Invoke(action); }
}
