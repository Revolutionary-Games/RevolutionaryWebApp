namespace ThriveDevCenter.Client.Shared;

public interface ITableColumn
{
    public string Name { get; }
    public bool IsSortable { get; }
}
