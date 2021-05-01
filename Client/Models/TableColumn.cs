namespace ThriveDevCenter.Client.Shared
{
    public class TableColumn : ITableColumn
    {
        public string Name { get; set; }

        public string ColumnName { get; set; }

        public bool IsSortable { get; set; }

        public string SortName => ColumnName ?? Name;

        public TableColumn(string name, bool sortable, string sortName = null)
        {
            Name = name;
            IsSortable = sortable;
            ColumnName = sortName;
        }
    }

    public interface ITableColumn
    {
        string Name { get; }
        bool IsSortable { get; }
    }
}
