namespace JobAppManager.Core.Entities;

/// <summary>An individual at the company who was emailed or messaged about this application.</summary>
public class Contact
{
    public int Id { get; set; }

    public int ApplicationId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Role { get; set; }

    public DateOnly? DateContacted { get; set; }

    public Application? Application { get; set; }
}
