namespace RevolutionaryWebApp.Server.Utilities;

using System.Linq;
using System.Text.Json.Serialization;
using DiffPlex.Model;

/// <summary>
///   Data from a diff operation. A separate class to be fully in control of the resulting JSON.
/// </summary>
public class DiffData
{
    // Data for blocks to reference
    public readonly string[] Old;
    public readonly string[] New;

    public readonly Block[] Blocks;

    public DiffData(DiffResult dataToConvert)
    {
        Old = dataToConvert.PiecesOld;
        New = dataToConvert.PiecesNew;

        Blocks = dataToConvert.DiffBlocks.Select(b => new Block(b)).ToArray();
    }

    [JsonConstructor]
    public DiffData(string[] old, string[] @new, Block[] blocks)
    {
        Old = old;
        New = @new;
        Blocks = blocks;
    }

    public readonly struct Block
    {
        /// <inheritdoc cref="DiffBlock.DeleteStartA"/>
        [JsonPropertyName("AStart")]
        public readonly int DeleteStartA;

        /// <inheritdoc cref="DiffBlock.DeleteCountA"/>
        [JsonPropertyName("ACount")]
        public readonly int DeleteCountA;

        /// <inheritdoc cref="DiffBlock.InsertStartB"/>
        [JsonPropertyName("BStart")]
        public readonly int InsertStartB;

        /// <inheritdoc cref="DiffBlock.InsertCountB"/>
        [JsonPropertyName("BCount")]
        public readonly int InsertCountB;

        public Block(DiffBlock dataToConvert) : this(dataToConvert.DeleteStartA, dataToConvert.DeleteCountA,
            dataToConvert.InsertStartB, dataToConvert.InsertCountB)
        {
        }

        [JsonConstructor]
        public Block(int aStart, int aCount, int bStart, int bCount)
        {
            DeleteStartA = aStart;
            DeleteCountA = aCount;
            InsertStartB = bStart;
            InsertCountB = bCount;
        }
    }
}
