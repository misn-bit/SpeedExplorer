namespace SpeedExplorer;

public sealed class ImageViewerSortOptions
{
    public ImageViewerSortOptions(SortColumn column, SortDirection direction, bool taggedFilesOnTop)
    {
        Column = column;
        Direction = direction;
        TaggedFilesOnTop = taggedFilesOnTop;
    }

    public SortColumn Column { get; }
    public SortDirection Direction { get; }
    public bool TaggedFilesOnTop { get; }
}
