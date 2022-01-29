namespace ThriveDevCenter.Shared.Tests.Utilities.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class EnumerableChunkingTests
{
    [Fact]
    public void ListChunking_ExpectedUploadConcurrencyListCount()
    {
        for (int i = 1; i < 1000; ++i)
        {
            var list = Enumerable.Range(1, i).ToList();

            var chunked = list.AsEnumerable().Chunk(
                (int)Math.Ceiling(list.Count / (float)AppInfo.MultipartSimultaneousUploads)).ToList();

            Assert.InRange(chunked.Count, 1, AppInfo.MultipartSimultaneousUploads);

            if (list.Count > AppInfo.MultipartSimultaneousUploads + 1)
                Assert.Equal(AppInfo.MultipartSimultaneousUploads, chunked.Count);
        }
    }

    [Fact]
    public void ListChunking_ChunkSplitInExpectedWay()
    {
        var list = new List<int>() { 1, 2, 3, 4, 5 };

        var chunked = list.AsEnumerable().Chunk(
            (int)Math.Ceiling(list.Count / (float)AppInfo.MultipartSimultaneousUploads)).ToList();

        Assert.Equal(AppInfo.MultipartSimultaneousUploads, chunked.Count);
        Assert.Equal(new List<int>() { 1, 2 }, chunked[0]);
        Assert.Equal(new List<int>() { 3, 4 }, chunked[1]);
        Assert.Equal(new List<int>() { 5 }, chunked[2]);
    }
}
