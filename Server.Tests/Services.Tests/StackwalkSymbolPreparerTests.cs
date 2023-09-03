namespace ThriveDevCenter.Server.Tests.Services.Tests;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using NSubstitute;
using NSubstitute.Core;
using Server.Services;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class StackwalkSymbolPreparerTests : IClassFixture<SimpleFewDebugSymbolsDatabase>, IDisposable
{
    private const string DownloadUrl1 = "https://example.com/test/1";
    private const string DownloadUrl2 = "https://example.com/test/2";
    private const string DownloadUrl3 = "https://example.com/test/3";

    private readonly XunitLogger<StackwalkSymbolPreparer> logger;
    private readonly SimpleFewDebugSymbolsDatabase symbolsDatabase;

    public StackwalkSymbolPreparerTests(ITestOutputHelper output, SimpleFewDebugSymbolsDatabase symbolsDatabase)
    {
        logger = new XunitLogger<StackwalkSymbolPreparer>(output);
        this.symbolsDatabase = symbolsDatabase;
    }

    [Fact]
    public async void SymbolPreparer_CreatesRightFolderStructure()
    {
        var downloadUrlsMock = Substitute.For<IGeneralRemoteDownloadUrls>();
        downloadUrlsMock.Configured.Returns(true);
        downloadUrlsMock.CreateDownloadFor(symbolsDatabase.StorageFile1, Arg.Any<TimeSpan>())
            .Returns(DownloadUrl1);
        downloadUrlsMock.CreateDownloadFor(symbolsDatabase.StorageFile2, Arg.Any<TimeSpan>())
            .Returns(DownloadUrl2);

        var downloaderMock = Substitute.For<IFileDownloader>();
        downloaderMock.DownloadFile(DownloadUrl1, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(WriteDummyFile);
        downloaderMock.DownloadFile(DownloadUrl2, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(WriteDummyFile);

        var symbolPreparer = new StackwalkSymbolPreparer(logger, symbolsDatabase.Database, downloadUrlsMock,
            downloaderMock);

        var folder = "stackwalk_symbol_test_right_structure";

        if (Directory.Exists(folder))
            Directory.Delete(folder, true);

        Directory.CreateDirectory(folder);

        var dummyFile = Path.Join(folder, "extra_dummy_file.txt");

        File.WriteAllLines(dummyFile, new[] { "dummy file that should be deleted" });

        Assert.True(File.Exists(dummyFile));

        await symbolPreparer.PrepareSymbolsInFolder(folder, CancellationToken.None);

        Assert.False(File.Exists(dummyFile));

        downloadUrlsMock.Received().CreateDownloadFor(symbolsDatabase.StorageFile1, Arg.Any<TimeSpan>());
        downloadUrlsMock.Received().CreateDownloadFor(symbolsDatabase.StorageFile2, Arg.Any<TimeSpan>());
        _ = downloadUrlsMock.Received().Configured;

        await downloaderMock.Received().DownloadFile(DownloadUrl1, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await downloaderMock.Received().DownloadFile(DownloadUrl2, Arg.Any<string>(), Arg.Any<CancellationToken>());

        Assert.True(File.Exists(Path.Join(folder, symbolsDatabase.Path1)));
        Assert.True(File.Exists(Path.Join(folder, symbolsDatabase.Path2)));
        Assert.False(File.Exists(Path.Join(folder, symbolsDatabase.Path3)));
    }

    public void Dispose()
    {
        logger.Dispose();
        symbolsDatabase.Dispose();
    }

    private static Task WriteDummyFile(CallInfo x)
    {
        x.Arg<CancellationToken>().ThrowIfCancellationRequested();

        Directory.CreateDirectory(Path.GetDirectoryName(x.ArgAt<string>(1)) ?? throw new Exception());

        using var writer = File.Open(x.ArgAt<string>(1), FileMode.Create);
        writer.Write(Encoding.UTF8.GetBytes(x.ArgAt<string>(0)));
        return Task.CompletedTask;
    }
}
