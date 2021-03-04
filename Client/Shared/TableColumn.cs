namespace ThriveDevCenter.Client.Shared
{
    public class TableColumn : ITableColumn
    {
        public string Name { get; set; }
        public bool IsSortable { get; set; }

        public TableColumn(string name, bool sortable)
        {
            Name = name;
            IsSortable = sortable;
        }
    }

    public interface ITableColumn
    {
        string Name { get; }
        bool IsSortable { get; }
    }
}
