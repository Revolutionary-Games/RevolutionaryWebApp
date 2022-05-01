namespace ThriveDevCenter.Server.Tests.Services.Tests;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Moq;
using Server.Services;
using Utilities;
using Xunit;
using Xunit.Abstractions;

public class StackwalkSymbolPreparerTests : IClassFixture<SimpleFewDebugSymbolsDatabase>
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
        var downloadUrlsMock = new Mock<IGeneralRemoteDownloadUrls>();
        downloadUrlsMock.SetupGet(d => d.Configured).Returns(true).Verifiable();
        downloadUrlsMock.Setup(d => d.CreateDownloadFor(symbolsDatabase.StorageFile1, It.IsAny<TimeSpan>()))
            .Returns(DownloadUrl1).Verifiable();
        downloadUrlsMock.Setup(d => d.CreateDownloadFor(symbolsDatabase.StorageFile2, It.IsAny<TimeSpan>()))
            .Returns(DownloadUrl2).Verifiable();

        var downloaderMock = new Mock<IFileDownloader>();
        downloaderMock.Setup(d => d.DownloadFile(DownloadUrl1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>(WriteDummyFile).Verifiable();
        downloaderMock.Setup(d => d.DownloadFile(DownloadUrl2, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>(WriteDummyFile).Verifiable();

        var symbolPreparer = new StackwalkSymbolPreparer(logger, symbolsDatabase.Database, downloadUrlsMock.Object,
            downloaderMock.Object);

        var folder = "stackwalk_symbol_test_right_structure";

        if (Directory.Exists(folder))
            Directory.Delete(folder, true);

        Directory.CreateDirectory(folder);

        var dummyFile = Path.Join(folder, "extra_dummy_file.txt");

        File.WriteAllLines(dummyFile, new[] { "dummy file that should be deleted" });

        Assert.True(File.Exists(dummyFile));

        await symbolPreparer.PrepareSymbolsInFolder(folder, CancellationToken.None);

        Assert.False(File.Exists(dummyFile));

        downloadUrlsMock.Verify();
        downloadUrlsMock.VerifyNoOtherCalls();

        downloaderMock.Verify();
        downloaderMock.VerifyNoOtherCalls();

        Assert.True(File.Exists(Path.Join(folder, symbolsDatabase.Path1)));
        Assert.True(File.Exists(Path.Join(folder, symbolsDatabase.Path2)));
        Assert.False(File.Exists(Path.Join(folder, symbolsDatabase.Path3)));
    }

    private static Task WriteDummyFile(string url, string file, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(Path.GetDirectoryName(file) ?? throw new Exception());

        using var writer = File.OpenWrite(file);
        writer.Write(Encoding.UTF8.GetBytes(url));
        return Task.CompletedTask;
    }
}
