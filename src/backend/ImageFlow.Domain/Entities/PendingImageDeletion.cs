namespace ImageFlow;

// Durable filesystem work committed with the deletion of its owning records.
public class PendingImageDeletion
{
    public string FileName { get; set; } = "";
}
