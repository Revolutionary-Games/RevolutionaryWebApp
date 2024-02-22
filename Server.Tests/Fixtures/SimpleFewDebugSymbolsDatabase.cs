namespace RevolutionaryWebApp.Server.Tests.Fixtures;

using System.Collections.Generic;
using DevCenterCommunication.Models.Enums;
using Server.Models;

public class SimpleFewDebugSymbolsDatabase : BaseSharedDatabaseFixture
{
    private static readonly object Lock = new();
    private static bool databaseInitialized;

    public SimpleFewDebugSymbolsDatabase() : base("SimpleFewDebugSymbolsDatabase")
    {
        lock (Lock)
        {
            if (!databaseInitialized)
            {
                Seed();
                databaseInitialized = true;
            }
        }
    }

    public long Id1 => 1;
    public string Name1 => "Thrive.sym";

    // ReSharper disable once StringLiteralTypo
    public string Path1 => "Thrive/877A3AD4EEDA4E1C98C167D7C0096D170/Thrive.sym";

    public StorageFile StorageFile1 { get; } = new();

    public long Id2 => 2;
    public string Name2 => "Thrive.exe.sym";
    public string Path2 => "Thrive.exe/05D475BDF37315EF0CE2D76F24B537CA1/Thrive.exe.sym";
    public StorageFile StorageFile2 { get; } = new();

    public long Id3 => 3;
    public string Name3 => "godot.windows.opt.64.mono.sym";

    public string Path3 =>
        "godot.windows.opt.64.mono.pdb/05D475BDF37315EF0CE2D76F24B537CA1/godot.windows.opt.64.mono.sym";

    public StorageFile StorageFile3 { get; } = new();

    public string StoragePrefix => "storage";

    protected sealed override void Seed()
    {
        CreateSymbol(Id1, Name1, Path1, 1, true, StorageFile1);
        CreateSymbol(Id2, Name2, Path2, 2, true, StorageFile2);
        CreateSymbol(Id3, Name3, Path3, 3, false, StorageFile3);

        Database.SaveChanges();
    }

    private void CreateSymbol(long id, string name, string path, long size, bool active, StorageFile file,
        bool uploaded = true)
    {
        var symbol = new DebugSymbol
        {
            Id = id,
            Name = name,
            RelativePath = path,
            Active = active,
            Uploaded = uploaded,
            Size = size,
            StoredInItem = new StorageItem
            {
                AllowParentless = true,
                Name = name,
                Ftype = FileType.File,
                StorageItemVersions = new List<StorageItemVersion>
                {
                    new()
                    {
                        Uploading = !uploaded,
                        StorageFile = file,
                    },
                },
            },
        };

        // Finish the file setup now that we know the info for it
        file.Size = size;
        file.Uploading = !uploaded;
        file.StoragePath = $"{StoragePrefix}/{path}";

        Database.DebugSymbols.Add(symbol);
    }
}
