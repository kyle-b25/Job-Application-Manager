using CommunityToolkit.Mvvm.ComponentModel;

namespace JobAppManager.App.ViewModels;

/// <summary>A screen in the shell's content area.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    /// <summary>Shown as the page heading.</summary>
    public abstract string Title { get; }

    /// <summary>Shown under the heading. Pages that have nothing useful to say return null.</summary>
    public virtual string? Subtitle => null;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Called every time the page is navigated to, not just once, so a page always
    /// reflects changes made on another screen since it was last seen.</summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;
}
