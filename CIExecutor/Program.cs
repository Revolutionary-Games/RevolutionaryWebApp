namespace CIExecutor;

using System;
using System.Threading.Tasks;

internal class Program
{
    private static Task Main(string[] args)
    {
        // This fails with an exception because we can't anyway report our failure if we don't have
        // the webhook connection url
        if (args.Length != 1)
            throw new Exception("Expected to be ran with a single argument specifying websocket connect url");

        using var executor = new CIExecutor(args[0]);

        return executor.Run();
    }
}
