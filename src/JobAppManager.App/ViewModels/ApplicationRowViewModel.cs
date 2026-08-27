using CommunityToolkit.Mvvm.Input;
using JobAppManager.Core.Entities;

namespace JobAppManager.App.ViewModels;

/// <summary>One row in the applications list.
///
/// The row carries its own Edit and Delete commands instead of the template reaching back up to
/// the page with a RelativeSource ancestor binding. Inside an ItemTemplate that lookup resolves
/// to null often enough to be a real hazard - the buttons then hover and press correctly and
/// simply do nothing, which is the worst kind of bug to notice. Holding the commands on the item
/// removes the lookup, and the binding is a plain <c>{Binding EditCommand}</c>.</summary>
public sealed class ApplicationRowViewModel
{
    public ApplicationRowViewModel(Application application, ApplicationsViewModel parent)
    {
        Application = application;
        EditCommand = new RelayCommand(() => parent.Edit(application));
        DeleteCommand = new AsyncRelayCommand(() => parent.DeleteAsync(application));
    }

    /// <summary>The entity itself. Rows are read-only and replaced wholesale on every requery,
    /// so binding straight to the POCO is fine here - it never needs to raise a change.</summary>
    public Application Application { get; }

    public int Id => Application.Id;

    public IRelayCommand EditCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }
}
