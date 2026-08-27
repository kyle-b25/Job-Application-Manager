using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobAppManager.App.Converters;
using JobAppManager.App.Services;
using JobAppManager.Core.Entities;
using JobAppManager.Core.Enums;

namespace JobAppManager.App.ViewModels;

/// <summary>One editable "what I sent them" row.</summary>
public partial class SubmittedItemRow : ObservableObject
{
    public int Id { get; init; }

    [ObservableProperty]
    private SubmissionKind _kind = SubmissionKind.Resume;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private DateOnly? _submittedOn;
}

/// <summary>One editable "who I talked to" row.</summary>
public partial class ContactRow : ObservableObject
{
    public int Id { get; init; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string? _role;

    [ObservableProperty]
    private DateOnly? _dateContacted;
}

/// <summary>One read-only entry in the status timeline.</summary>
public record StatusHistoryRow(ApplicationStatus Status, DateTime ChangedLocal, string? Note)
{
    public string DisplayName => EnumDisplayNameConverter.Humanize(Status);

    public string WhenText => ChangedLocal.ToString("d MMM yyyy, h:mm tt");
}

/// <summary>Add and Edit are the same form, so they are the same ViewModel. The only difference
/// is whether <see cref="_applicationId"/> is set, which decides insert vs update on save.</summary>
public partial class ApplicationEditorViewModel : ObservableValidator
{
    private readonly RepositoryFactory _repositories;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly TimeProvider _timeProvider;

    private int? _applicationId;

    /// <summary>What the status was when the form was loaded, so save can tell a real transition
    /// from "the user never touched the dropdown".</summary>
    private ApplicationStatus _statusOnLoad;

    public ApplicationEditorViewModel(
        RepositoryFactory repositories,
        INavigationService navigation,
        IDialogService dialogs,
        TimeProvider? timeProvider = null)
    {
        _repositories = repositories;
        _navigation = navigation;
        _dialogs = dialogs;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dateApplied = Today;

        Statuses = Enum.GetValues<ApplicationStatus>();
        InterestLevels = Enum.GetValues<InterestLevel>();
        SubmissionKinds = Enum.GetValues<SubmissionKind>();
    }

    public IReadOnlyList<ApplicationStatus> Statuses { get; }

    public IReadOnlyList<InterestLevel> InterestLevels { get; }

    public IReadOnlyList<SubmissionKind> SubmissionKinds { get; }

    public ObservableCollection<SubmittedItemRow> SubmittedItems { get; } = new();

    public ObservableCollection<ContactRow> Contacts { get; } = new();

    public ObservableCollection<StatusHistoryRow> StatusHistory { get; } = new();

    public bool IsNew => _applicationId is null;

    public string Title => IsNew ? "Add application" : "Edit application";

    public string Subtitle => IsNew
        ? "Company and job title are all you need to start; the rest can come later."
        : $"{CompanyName} - {JobTitle}";

    public string SaveLabel => IsNew ? "Add application" : "Save changes";

    // ---------------- Fields ----------------

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Company is required.")]
    [MaxLength(200, ErrorMessage = "Company must be 200 characters or fewer.")]
    private string _companyName = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Job title is required.")]
    [MaxLength(200, ErrorMessage = "Job title must be 200 characters or fewer.")]
    private string _jobTitle = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [MaxLength(200, ErrorMessage = "Location must be 200 characters or fewer.")]
    private string _location = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(ApplicationEditorViewModel), nameof(ValidateJobUrl))]
    private string? _jobUrl;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [MaxLength(100, ErrorMessage = "Salary range must be 100 characters or fewer.")]
    private string? _salaryRange;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(ApplicationEditorViewModel), nameof(ValidateDateApplied))]
    private DateOnly _dateApplied;

    [ObservableProperty]
    private ApplicationStatus _status = ApplicationStatus.Applied;

    [ObservableProperty]
    private InterestLevel _interestLevel = InterestLevel.Yellow;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 20, ErrorMessage = "Interview round must be between 1 and 20.")]
    private int? _interviewRound;

    [ObservableProperty]
    private bool _fromJobFair;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [MaxLength(4000, ErrorMessage = "Notes must be 4000 characters or fewer.")]
    private string? _notes;

    /// <summary>Optional note attached to a status change made from this form.</summary>
    [ObservableProperty]
    private string? _statusChangeNote;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Round numbering is only meaningful inside Interview, matching the rule the
    /// repository enforces on every transition.</summary>
    public bool ShowsInterviewRound => Status == ApplicationStatus.Interview;

    /// <summary>The status-change note only earns its space when the status is actually moving.</summary>
    public bool IsStatusChanging => !IsNew && Status != _statusOnLoad;

    public bool HasStatusHistory => StatusHistory.Count > 0;

    partial void OnStatusChanged(ApplicationStatus value)
    {
        if (value != ApplicationStatus.Interview)
        {
            InterviewRound = null;
        }

        OnPropertyChanged(nameof(ShowsInterviewRound));
        OnPropertyChanged(nameof(IsStatusChanging));
    }

    partial void OnCompanyNameChanged(string value) => OnPropertyChanged(nameof(Subtitle));

    partial void OnJobTitleChanged(string value) => OnPropertyChanged(nameof(Subtitle));

    // ---------------- Validation ----------------

    public static ValidationResult? ValidateJobUrl(string? url, ValidationContext _)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return ValidationResult.Success;
        }

        if (url.Length > 2000)
        {
            return new ValidationResult("Job URL must be 2000 characters or fewer.");
        }

        // Absolute-only: a bare "acme.com/jobs" is not something the Open button can launch.
        var wellFormed = Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                         && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

        return wellFormed
            ? ValidationResult.Success
            : new ValidationResult("Enter a full http:// or https:// link, or leave this blank.");
    }

    /// <summary>Today, on this ViewModel's clock.</summary>
    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

    /// <summary>[CustomValidation] requires a static method, but the clock is per-instance - so
    /// it comes back off the ValidationContext rather than from a mutable static, which would be
    /// shared state across parallel tests.</summary>
    public static ValidationResult? ValidateDateApplied(DateOnly date, ValidationContext context) =>
        context.ObjectInstance is ApplicationEditorViewModel vm && date > vm.Today
            ? new ValidationResult("The date applied cannot be in the future.")
            : ValidationResult.Success;

    // ---------------- Loading ----------------

    /// <summary>Resets the form to a blank application.</summary>
    public void LoadNew()
    {
        _applicationId = null;
        _statusOnLoad = ApplicationStatus.Applied;

        CompanyName = string.Empty;
        JobTitle = string.Empty;
        Location = string.Empty;
        JobUrl = null;
        SalaryRange = null;
        DateApplied = Today;
        Status = ApplicationStatus.Applied;
        InterestLevel = InterestLevel.Yellow;
        InterviewRound = null;
        FromJobFair = false;
        Notes = null;
        StatusChangeNote = null;

        SubmittedItems.Clear();
        Contacts.Clear();
        StatusHistory.Clear();

        ClearErrors();
        NotifyModeChanged();
    }

    /// <summary>Copies an existing application into the form. Fields are copied rather than bound
    /// through, because the entities are plain POCOs with no change notification of their own.</summary>
    public async Task LoadAsync(int applicationId)
    {
        await using var scope = _repositories.Create();
        var application = await scope.Repository.GetByIdAsync(applicationId);

        if (application is null)
        {
            // Deleted from another screen between the click and the load.
            _dialogs.ShowError("Not found", "That application no longer exists.");
            _navigation.GoToApplications();
            return;
        }

        _applicationId = application.Id;
        _statusOnLoad = application.Status;

        CompanyName = application.CompanyName;
        JobTitle = application.JobTitle;
        Location = application.Location;
        JobUrl = application.JobUrl;
        SalaryRange = application.SalaryRange;
        DateApplied = application.DateApplied;
        Status = application.Status;
        InterestLevel = application.InterestLevel;
        InterviewRound = application.InterviewRound;
        FromJobFair = application.FromJobFair;
        Notes = application.Notes;
        StatusChangeNote = null;

        SubmittedItems.Clear();

        foreach (var item in application.SubmittedItems)
        {
            SubmittedItems.Add(new SubmittedItemRow
            {
                Id = item.Id,
                Kind = item.Kind,
                Name = item.Name,
                SubmittedOn = item.SubmittedOn
            });
        }

        Contacts.Clear();

        foreach (var contact in application.Contacts)
        {
            Contacts.Add(new ContactRow
            {
                Id = contact.Id,
                Name = contact.Name,
                Email = contact.Email,
                Role = contact.Role,
                DateContacted = contact.DateContacted
            });
        }

        StatusHistory.Clear();

        foreach (var change in application.StatusHistory.OrderByDescending(s => s.ChangedUtc))
        {
            StatusHistory.Add(new StatusHistoryRow(
                change.Status,
                change.ChangedUtc.ToLocalTime(),
                change.Note));
        }

        ClearErrors();
        NotifyModeChanged();
    }

    private void NotifyModeChanged()
    {
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(SaveLabel));
        OnPropertyChanged(nameof(ShowsInterviewRound));
        OnPropertyChanged(nameof(IsStatusChanging));
        OnPropertyChanged(nameof(HasStatusHistory));
    }

    // ---------------- Child rows ----------------

    [RelayCommand]
    private void AddSubmittedItem() => SubmittedItems.Add(new SubmittedItemRow
    {
        SubmittedOn = DateApplied
    });

    [RelayCommand]
    private void RemoveSubmittedItem(SubmittedItemRow? row)
    {
        if (row is not null)
        {
            SubmittedItems.Remove(row);
        }
    }

    [RelayCommand]
    private void AddContact() => Contacts.Add(new ContactRow());

    [RelayCommand]
    private void RemoveContact(ContactRow? row)
    {
        if (row is not null)
        {
            Contacts.Remove(row);
        }
    }

    // ---------------- Save / cancel ----------------

    [RelayCommand]
    private async Task SaveAsync()
    {
        ValidateAllProperties();

        if (HasErrors)
        {
            return;
        }

        // Blank rows are what a user leaves behind after clicking Add and changing their mind;
        // silently dropping them beats rejecting the whole save over one empty line.
        var submittedItems = SubmittedItems
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .ToList();

        var contacts = Contacts
            .Where(c => !string.IsNullOrWhiteSpace(c.Name) || !string.IsNullOrWhiteSpace(c.Email))
            .ToList();

        var missingEmail = contacts.FirstOrDefault(c => string.IsNullOrWhiteSpace(c.Email));

        if (missingEmail is not null)
        {
            _dialogs.ShowError(
                "Contact needs an email",
                $"Add an email address for {(string.IsNullOrWhiteSpace(missingEmail.Name) ? "the contact" : missingEmail.Name)}, or remove the row.");
            return;
        }

        IsBusy = true;

        try
        {
            await using var scope = _repositories.Create();

            if (_applicationId is { } id)
            {
                var existing = await scope.Repository.GetByIdAsync(id);

                if (existing is null)
                {
                    _dialogs.ShowError("Not found", "That application no longer exists.");
                    _navigation.GoToApplications();
                    return;
                }

                ApplyTo(existing, submittedItems, contacts);

                // Status deliberately excluded from ApplyTo: moving through the pipeline has to
                // go through the repository so the change lands in the history too.
                await scope.Repository.UpdateAsync(existing);

                if (Status != _statusOnLoad)
                {
                    await scope.Repository.ChangeStatusAsync(
                        id,
                        Status,
                        string.IsNullOrWhiteSpace(StatusChangeNote) ? null : StatusChangeNote.Trim());

                    // Round number is set on the entity, but ChangeStatusAsync clears it when
                    // moving away from Interview - so re-apply it for a move *into* Interview.
                    if (Status == ApplicationStatus.Interview && InterviewRound is not null)
                    {
                        var refreshed = await scope.Repository.GetByIdAsync(id);

                        if (refreshed is not null)
                        {
                            refreshed.InterviewRound = InterviewRound;
                            await scope.Repository.UpdateAsync(refreshed);
                        }
                    }
                }
            }
            else
            {
                var application = new Application { Status = Status };
                ApplyTo(application, submittedItems, contacts);
                await scope.Repository.AddAsync(application);
            }
        }
        finally
        {
            IsBusy = false;
        }

        _navigation.GoToApplications();
    }

    private void ApplyTo(
        Application application,
        IReadOnlyList<SubmittedItemRow> submittedItems,
        IReadOnlyList<ContactRow> contacts)
    {
        application.CompanyName = CompanyName.Trim();
        application.JobTitle = JobTitle.Trim();
        application.Location = Location?.Trim() ?? string.Empty;
        application.JobUrl = string.IsNullOrWhiteSpace(JobUrl) ? null : JobUrl.Trim();
        application.SalaryRange = string.IsNullOrWhiteSpace(SalaryRange) ? null : SalaryRange.Trim();
        application.DateApplied = DateApplied;
        application.InterestLevel = InterestLevel;
        application.InterviewRound = Status == ApplicationStatus.Interview ? InterviewRound : null;
        application.FromJobFair = FromJobFair;
        application.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

        // Replacing the collections wholesale lets EF work out the inserts, updates, and cascade
        // deletes; matching by Id keeps rows the user only edited from being deleted and re-added.
        SyncChildren(
            application.SubmittedItems,
            submittedItems,
            row => row.Id,
            entity => entity.Id,
            (entity, row) =>
            {
                entity.Kind = row.Kind;
                entity.Name = row.Name.Trim();
                entity.SubmittedOn = row.SubmittedOn;
            });

        SyncChildren(
            application.Contacts,
            contacts,
            row => row.Id,
            entity => entity.Id,
            (entity, row) =>
            {
                entity.Name = row.Name.Trim();
                entity.Email = row.Email.Trim();
                entity.Role = string.IsNullOrWhiteSpace(row.Role) ? null : row.Role.Trim();
                entity.DateContacted = row.DateContacted;
            });
    }

    /// <summary>Reconciles an entity collection against the rows the form is holding: updates
    /// what matches by id, adds what is new, removes what the user deleted.</summary>
    private static void SyncChildren<TEntity, TRow>(
        ICollection<TEntity> entities,
        IReadOnlyList<TRow> rows,
        Func<TRow, int> rowId,
        Func<TEntity, int> entityId,
        Action<TEntity, TRow> copy)
        where TEntity : class, new()
    {
        var keptIds = rows.Select(rowId).Where(id => id != 0).ToHashSet();

        foreach (var removed in entities.Where(e => !keptIds.Contains(entityId(e))).ToList())
        {
            entities.Remove(removed);
        }

        foreach (var row in rows)
        {
            var id = rowId(row);
            var entity = id == 0 ? null : entities.FirstOrDefault(e => entityId(e) == id);

            if (entity is null)
            {
                entity = new TEntity();
                entities.Add(entity);
            }

            copy(entity, row);
        }
    }

    [RelayCommand]
    private void Cancel() => _navigation.GoToApplications();
}
