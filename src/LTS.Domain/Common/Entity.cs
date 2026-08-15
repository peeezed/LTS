namespace LTS.Domain.Common;

/// <summary>Base class for all persisted domain entities.</summary>
public abstract class Entity
{
    public int Id { get; set; }
}

/// <summary>Applied by the DbContext save interceptor to stamp who changed what and when.</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}

/// <summary>Reference data that can be retired without being deleted.</summary>
public interface IActivatable
{
    bool IsActive { get; set; }
}
