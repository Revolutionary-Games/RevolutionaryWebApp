namespace RevolutionaryWebApp.Client.Shared;

using System;
using RevolutionaryWebApp.Shared;

public class SortHelper
{
    public string SortColumn;
    public SortDirection Direction;

    private readonly SortDirection defaultDirection;

    public SortHelper(string column, SortDirection direction)
    {
        SortColumn = column;
        Direction = direction;
        defaultDirection = direction;
    }

    /// <summary>
    ///   Handle when a column was clicked
    /// </summary>
    /// <param name="column">The new (or current sort column)</param>
    /// <returns>True when the sort column was changed, false otherwise.</returns>
    public bool ColumnClick(string column)
    {
        if (SortColumn == column)
        {
            // Toggle direction
            if (Direction == SortDirection.Ascending)
            {
                Direction = SortDirection.Descending;
            }
            else
            {
                Direction = SortDirection.Ascending;
            }

            return false;
        }

        SortColumn = column;
        Direction = defaultDirection;
        return true;
    }

    /// <summary>
    ///   Returns a css class for showing sort direction in a table
    /// </summary>
    /// <param name="currentColumn">The current column in the table</param>
    /// <returns>The CSS class</returns>
    public string SortClass(string currentColumn)
    {
        if (SortColumn != currentColumn)
            return string.Empty;

        switch (Direction)
        {
            case SortDirection.Ascending:
                return "oi oi-sort-ascending";
            case SortDirection.Descending:
                return "oi oi-sort-descending";
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
